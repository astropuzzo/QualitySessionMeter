using NINA.Equipment.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Optional read-only HTTP bridge for trusted LAN/Tailscale companions such as OpenAstro Control.
/// It is disabled unless QSM_REMOTE_TOKEN is configured. No write/control routes exist in V1.
/// </summary>
public sealed class QualitySessionHttpBridge : IDisposable {
    private const int DefaultPort = 18973;
    private const int MaxRequestLine = 4096;
    private const int MaxHeaderLines = 64;
    private const int PreviewMaxWidth = 1280;
    private const int PreviewJpegQuality = 82;

    private readonly string token;
    private readonly TcpListener listener;
    private readonly IImageSaveMediator imageSaveMediator;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task acceptLoop;
    private readonly object previewSync = new();
    private byte[] previewJpeg = Array.Empty<byte>();
    private DateTimeOffset previewUtc;
    private string previewImageId = "";
    private bool disposed;

    public bool Enabled { get; }
    public int Port { get; }

    public QualitySessionHttpBridge(IImageSaveMediator imageSaveMediator) {
        this.imageSaveMediator = imageSaveMediator;
        token = (Environment.GetEnvironmentVariable("QSM_REMOTE_TOKEN") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token)) {
            Enabled = false;
            return;
        }

        Port = ParsePort(Environment.GetEnvironmentVariable("QSM_REMOTE_PORT"));
        var bindAddress = ParseBindAddress(Environment.GetEnvironmentVariable("QSM_REMOTE_BIND"));
        listener = new TcpListener(bindAddress, Port);
        listener.Start();
        imageSaveMediator.ImageSaved += ImageSaved;
        Enabled = true;
        acceptLoop = Task.Run(() => AcceptLoop(cancellation.Token));
    }

    private static int ParsePort(string raw) {
        return int.TryParse(raw, out var port) && port is >= 1024 and <= 65535 ? port : DefaultPort;
    }

    private static IPAddress ParseBindAddress(string raw) {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*") return IPAddress.Any;
        return IPAddress.TryParse(raw.Trim(), out var parsed) ? parsed : IPAddress.Any;
    }

    private void ImageSaved(object sender, ImageSavedEventArgs e) {
        if (disposed || e?.Image == null || e.MetaData?.Image == null) return;
        if (e.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;

        try {
            BitmapSource rendered = e.Image;
            if (rendered.PixelWidth <= 0 || rendered.PixelHeight <= 0) return;

            double scale = Math.Min(1.0, PreviewMaxWidth / (double)rendered.PixelWidth);
            if (scale < 1.0) {
                rendered = new TransformedBitmap(rendered, new ScaleTransform(scale, scale));
            }
            if (rendered.Format != PixelFormats.Bgr24) {
                rendered = new FormatConvertedBitmap(rendered, PixelFormats.Bgr24, null, 0);
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = PreviewJpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            var bytes = memory.ToArray();
            if (bytes.Length == 0) return;

            lock (previewSync) {
                previewJpeg = bytes;
                previewUtc = DateTimeOffset.UtcNow;
                previewImageId = e.MetaData.Image.Id?.ToString() ?? previewUtc.ToUnixTimeMilliseconds().ToString();
            }
        } catch {
            // Preview generation is best-effort and must never affect image saving or QSM classification.
        }
    }

    private async Task AcceptLoop(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            TcpClient client = null;
            try {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClient(client, cancellationToken), cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                client?.Dispose();
                break;
            } catch {
                client?.Dispose();
                if (cancellationToken.IsCancellationRequested) break;
                await Task.Delay(150, cancellationToken);
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken cancellationToken) {
        using (client) {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);

            string requestLine;
            try {
                requestLine = await reader.ReadLineAsync(cancellationToken);
            } catch {
                return;
            }
            if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > MaxRequestLine) {
                await WriteJson(stream, 400, new { ok = false, error = "invalid request" }, cancellationToken);
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < MaxHeaderLines; i++) {
                string line;
                try {
                    line = await reader.ReadLineAsync(cancellationToken);
                } catch {
                    return;
                }
                if (string.IsNullOrEmpty(line)) break;
                var separator = line.IndexOf(':');
                if (separator <= 0) continue;
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase)) {
                await WriteJson(stream, 405, new { ok = false, error = "read-only GET service" }, cancellationToken);
                return;
            }

            var path = parts[1].Split('?', 2)[0];
            if (path == "/healthz") {
                byte[] preview;
                DateTimeOffset previewTime;
                lock (previewSync) { preview = previewJpeg; previewTime = previewUtc; }
                await WriteJson(stream, 200, new {
                    ok = true,
                    service = "QualitySessionMeter",
                    contract = "quality-session-meter/http-v1",
                    readOnly = true,
                    previewAvailable = preview.Length > 0,
                    previewUtc = previewTime == default ? null : previewTime.ToString("O")
                }, cancellationToken);
                return;
            }

            if (!Authorized(headers)) {
                await WriteJson(stream, 401, new { ok = false, error = "unauthorized" }, cancellationToken);
                return;
            }

            if (path == "/api/v1/snapshot") {
                await WriteJson(stream, 200, QualitySessionMobileBridge.BuildSnapshot(), cancellationToken);
                return;
            }

            if (path == "/api/v1/preview.jpg") {
                byte[] preview;
                DateTimeOffset previewTime;
                string imageId;
                lock (previewSync) {
                    preview = previewJpeg;
                    previewTime = previewUtc;
                    imageId = previewImageId;
                }
                if (preview.Length == 0) {
                    await WriteJson(stream, 404, new { ok = false, error = "preview not available" }, cancellationToken);
                    return;
                }
                await WriteBytes(stream, 200, "image/jpeg", preview, cancellationToken, new Dictionary<string, string> {
                    ["X-QSM-Preview-Utc"] = previewTime.ToString("O"),
                    ["X-QSM-Image-Id"] = imageId
                });
                return;
            }

            await WriteJson(stream, 404, new { ok = false, error = "not found" }, cancellationToken);
        }
    }

    private bool Authorized(IDictionary<string, string> headers) {
        if (headers.TryGetValue("X-QSM-Token", out var headerToken) && FixedEquals(headerToken, token)) return true;
        if (headers.TryGetValue("Authorization", out var authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            return FixedEquals(authorization[7..].Trim(), token);
        }
        return false;
    }

    private static bool FixedEquals(string left, string right) {
        if (left == null || right == null) return false;
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async Task WriteJson(NetworkStream stream, int statusCode, object payload, CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        await WriteBytes(stream, statusCode, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cancellationToken);
    }

    private static async Task WriteBytes(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken cancellationToken, IDictionary<string, string> extraHeaders = null) {
        var reason = statusCode switch {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error"
        };
        var builder = new StringBuilder()
            .Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n");
        if (extraHeaders != null) {
            foreach (var pair in extraHeaders) {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Contains('\r') || pair.Value.Contains('\n')) continue;
                builder.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
            }
        }
        builder.Append("\r\n");
        var header = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(header, cancellationToken);
        if (body?.Length > 0) await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        if (Enabled && imageSaveMediator != null) imageSaveMediator.ImageSaved -= ImageSaved;
        cancellation.Cancel();
        try { listener?.Stop(); } catch { }
        try { acceptLoop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        cancellation.Dispose();
    }
}
