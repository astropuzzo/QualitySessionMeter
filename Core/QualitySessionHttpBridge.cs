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

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Optional read-only HTTP bridge for trusted LAN/Tailscale companions such as OpenAstro Control.
/// It is disabled unless QSM_REMOTE_TOKEN is configured. No write/control routes exist in V1.
/// </summary>
public sealed class QualitySessionHttpBridge : IDisposable {
    private const int DefaultPort = 18973;
    private const int MaxRequestLine = 4096;
    private const int MaxHeaderLines = 64;

    private readonly string token;
    private readonly TcpListener listener;
    private readonly LatestLightPreviewCache previewCache;
    private readonly bool ownsPreviewCache;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task acceptLoop;
    private bool disposed;

    public bool Enabled { get; }
    public int Port { get; }

    // Compatibility constructor retained for callers outside the main plugin composition path.
    // The plugin itself uses the shared-cache overload below so OpenAstro and the universal
    // dashboard always serve the exact same preview bytes.
    public QualitySessionHttpBridge(IImageSaveMediator imageSaveMediator)
        : this(new LatestLightPreviewCache(imageSaveMediator), ownsPreviewCache: true) {
    }

    public QualitySessionHttpBridge(LatestLightPreviewCache previewCache)
        : this(previewCache, ownsPreviewCache: false) {
    }

    private QualitySessionHttpBridge(LatestLightPreviewCache previewCache, bool ownsPreviewCache) {
        this.previewCache = previewCache ?? throw new ArgumentNullException(nameof(previewCache));
        this.ownsPreviewCache = ownsPreviewCache;

        token = (Environment.GetEnvironmentVariable("QSM_REMOTE_TOKEN") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token)) {
            Enabled = false;
            return;
        }

        Port = ParsePort(Environment.GetEnvironmentVariable("QSM_REMOTE_PORT"));
        var bindAddress = ParseBindAddress(Environment.GetEnvironmentVariable("QSM_REMOTE_BIND"));
        listener = new TcpListener(bindAddress, Port);
        listener.Start();
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
                var preview = previewCache.Snapshot();
                await WriteJson(stream, 200, new {
                    ok = true,
                    service = "QualitySessionMeter",
                    contract = "quality-session-meter/http-v1",
                    readOnly = true,
                    previewAvailable = preview.Available,
                    previewUtc = preview.CapturedUtc == default ? null : preview.CapturedUtc.ToString("O")
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
                var preview = previewCache.Snapshot();
                if (!preview.Available) {
                    await WriteJson(stream, 404, new { ok = false, error = "preview not available" }, cancellationToken);
                    return;
                }
                await WriteBytes(stream, 200, "image/jpeg", preview.Jpeg, cancellationToken, new Dictionary<string, string> {
                    ["X-QSM-Preview-Utc"] = preview.CapturedUtc.ToString("O"),
                    ["X-QSM-Image-Id"] = preview.ImageId,
                    ["ETag"] = preview.EntityTag
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
        cancellation.Cancel();
        try { listener?.Stop(); } catch { }
        try { acceptLoop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        cancellation.Dispose();
        if (ownsPreviewCache) previewCache?.Dispose();
    }
}
