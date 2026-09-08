using NINA.Equipment.Model;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Optional, self-contained and read-only browser dashboard for the user's LAN/VPN.
/// It is intentionally independent from QualitySessionHttpBridge, which remains the
/// tokenized OpenAstro/companion API for backward compatibility.
///
/// Security boundary:
/// - disabled by default;
/// - no router/UPnP changes and no outbound network connections;
/// - dashboard routes expose read-only snapshot/preview data only;
/// - optional password sessions use HttpOnly + SameSite=Strict cookies;
/// - direct Internet exposure is not a supported deployment model (use VPN/HTTPS proxy).
/// </summary>
public sealed class QualitySessionWebServer : IDisposable {
    private const int MaxRequestLine = 4096;
    private const int MaxHeaderLines = 64;
    private const int MaxHeaderLine = 8192;
    private const int MaxBodyBytes = 8192;
    private const int PreviewMaxWidth = 1280;
    private const int PreviewJpegQuality = 82;
    private const int MaxConcurrentClients = 24;
    private const int MaxFailedLoginsPerWindow = 8;
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(1);

    private readonly IImageSaveMediator imageSaveMediator;
    private readonly QualitySettings settings;
    private readonly object lifecycleSync = new();
    private readonly object previewSync = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LoginFailureState> loginFailures = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim clientSlots = new(MaxConcurrentClients, MaxConcurrentClients);

    private TcpListener listener;
    private CancellationTokenSource listenerCancellation;
    private Task acceptLoop;
    private byte[] previewJpeg = Array.Empty<byte>();
    private DateTimeOffset previewUtc;
    private string previewImageId = "";
    private bool disposed;

    public bool Enabled { get; private set; }
    public int Port { get; private set; }
    public string LastError { get; private set; } = "";

    public QualitySessionWebServer(IImageSaveMediator imageSaveMediator, QualitySettings settings) {
        this.imageSaveMediator = imageSaveMediator ?? throw new ArgumentNullException(nameof(imageSaveMediator));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        imageSaveMediator.ImageSaved += ImageSaved;
        settings.PropertyChanged += SettingsChanged;
        Reconfigure();
    }

    public string AccessUrl => LanAddressResolver.BuildDashboardUrl(Port > 0 ? Port : settings.WebDashboardPort);

    private void SettingsChanged(object sender, PropertyChangedEventArgs e) {
        if (e == null || string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardEnabled) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardPort) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardRequirePassword) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardPasswordConfigured)) {
            Reconfigure();
        }
    }

    private void Reconfigure() {
        lock (lifecycleSync) {
            StopListener();
            LastError = "";
            Port = settings.WebDashboardPort;
            if (disposed || !settings.WebDashboardEnabled) return;

            try {
                listenerCancellation = new CancellationTokenSource();
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                Enabled = true;
                acceptLoop = Task.Run(() => AcceptLoop(listenerCancellation.Token));
            } catch (Exception ex) {
                Enabled = false;
                LastError = ex.Message;
                try { listener?.Stop(); } catch { }
                listener = null;
                listenerCancellation?.Dispose();
                listenerCancellation = null;
                acceptLoop = null;
            }
        }
    }

    private void StopListener() {
        Enabled = false;
        try { listenerCancellation?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { acceptLoop?.Wait(TimeSpan.FromMilliseconds(750)); } catch { }
        listener = null;
        acceptLoop = null;
        if (listenerCancellation != null) {
            listenerCancellation.Dispose();
            listenerCancellation = null;
        }
    }

    private void ImageSaved(object sender, ImageSavedEventArgs e) {
        if (disposed || e?.Image == null || e.MetaData?.Image == null) return;
        if (e.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;

        try {
            BitmapSource rendered = e.Image;
            if (rendered.PixelWidth <= 0 || rendered.PixelHeight <= 0) return;
            var scale = Math.Min(1.0, PreviewMaxWidth / (double)rendered.PixelWidth);
            if (scale < 1.0) rendered = new TransformedBitmap(rendered, new ScaleTransform(scale, scale));
            if (rendered.Format != PixelFormats.Bgr24) rendered = new FormatConvertedBitmap(rendered, PixelFormats.Bgr24, null, 0);

            var encoder = new JpegBitmapEncoder { QualityLevel = PreviewJpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            var bytes = memory.ToArray();
            if (bytes.Length == 0) return;

            lock (previewSync) {
                previewJpeg = bytes;
                previewUtc = DateTimeOffset.UtcNow;
                previewImageId = e.MetaData.Image.Id.ToString();
            }
        } catch {
            // Display-only preview generation must never affect acquisition.
        }
    }

    private async Task AcceptLoop(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            TcpClient client = null;
            try {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                await clientSlots.WaitAsync(cancellationToken);
                var accepted = client;
                client = null;
                _ = Task.Run(async () => {
                    try {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(RequestLifetime);
                        await HandleClient(accepted, timeout.Token);
                    } catch (OperationCanceledException) { }
                    catch { accepted?.Dispose(); }
                    finally { clientSlots.Release(); }
                });
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                client?.Dispose();
                break;
            } catch {
                client?.Dispose();
                if (cancellationToken.IsCancellationRequested) break;
                try { await Task.Delay(150, cancellationToken); } catch { break; }
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken cancellationToken) {
        using (client) {
            client.NoDelay = true;
            client.ReceiveTimeout = (int)RequestLifetime.TotalMilliseconds;
            client.SendTimeout = (int)RequestLifetime.TotalMilliseconds;
            var remoteKey = (client.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? "unknown";

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

            string requestLine;
            try { requestLine = await reader.ReadLineAsync(cancellationToken); }
            catch { return; }

            if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > MaxRequestLine) {
                await WriteJson(stream, 400, new { ok = false, error = "invalid request" }, cancellationToken);
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var headersComplete = false;
            for (var i = 0; i < MaxHeaderLines; i++) {
                string line;
                try { line = await reader.ReadLineAsync(cancellationToken); }
                catch { return; }
                if (line == null || line.Length > MaxHeaderLine) {
                    await WriteJson(stream, 431, new { ok = false, error = "request headers too large" }, cancellationToken);
                    return;
                }
                if (line.Length == 0) {
                    headersComplete = true;
                    break;
                }
                var separator = line.IndexOf(':');
                if (separator > 0) headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
            if (!headersComplete) {
                await WriteJson(stream, 431, new { ok = false, error = "too many request headers" }, cancellationToken);
                return;
            }

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                await WriteJson(stream, 400, new { ok = false, error = "invalid request" }, cancellationToken);
                return;
            }

            var method = parts[0].ToUpperInvariant();
            var path = parts[1].Split('?', 2)[0];

            string body = "";
            if (method == "POST") {
                var contentLength = 0;
                if (headers.TryGetValue("Content-Length", out var rawLength) && !int.TryParse(rawLength, out contentLength)) {
                    await WriteJson(stream, 400, new { ok = false, error = "invalid content length" }, cancellationToken);
                    return;
                }
                if (contentLength < 0 || contentLength > MaxBodyBytes) {
                    await WriteJson(stream, 413, new { ok = false, error = "request too large" }, cancellationToken);
                    return;
                }
                if (contentLength > 0) {
                    var buffer = new char[contentLength];
                    var read = 0;
                    while (read < buffer.Length) {
                        var count = await reader.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
                        if (count <= 0) break;
                        read += count;
                    }
                    if (read != contentLength) {
                        await WriteJson(stream, 400, new { ok = false, error = "incomplete request body" }, cancellationToken);
                        return;
                    }
                    body = new string(buffer);
                }
            }

            if (method == "GET" && path == "/dashboard/healthz") {
                await WriteJson(stream, 200, new {
                    ok = true,
                    service = "QualitySessionMeter Web Dashboard",
                    readOnly = true,
                    passwordRequired = settings.WebDashboardRequirePassword,
                    passwordConfigured = settings.WebDashboardPasswordConfigured
                }, cancellationToken);
                return;
            }

            if (method == "POST" && path == "/dashboard/login") {
                await HandleLogin(stream, headers, body, remoteKey, cancellationToken);
                return;
            }

            if (method == "POST" && path == "/dashboard/logout") {
                RevokeSession(headers);
                await WriteJson(stream, 200, new { ok = true }, cancellationToken,
                    new Dictionary<string, string> { ["Set-Cookie"] = "qsm_session=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict" });
                return;
            }

            if (method != "GET") {
                await WriteJson(stream, 405, new { ok = false, error = "method not allowed" }, cancellationToken);
                return;
            }

            if (path == "/" || path == "/index.html") {
                if (!settings.WebDashboardEnabled) {
                    await WriteHtml(stream, 404, DisabledHtml, cancellationToken);
                    return;
                }
                if (settings.WebDashboardRequirePassword && !settings.WebDashboardPasswordConfigured) {
                    await WriteHtml(stream, 200, PasswordNotConfiguredHtml, cancellationToken);
                    return;
                }
                if (!DashboardAuthorized(headers)) {
                    await WriteHtml(stream, 200, LoginHtml, cancellationToken);
                    return;
                }
                await WriteHtml(stream, 200, WebDashboardPage.Html, cancellationToken);
                return;
            }

            if (path == "/dashboard/api/snapshot") {
                if (!DashboardAuthorized(headers)) {
                    await WriteJson(stream, 401, new { ok = false, error = "unauthorized" }, cancellationToken);
                    return;
                }
                await WriteJson(stream, 200, QualitySessionMobileBridge.BuildSnapshot(), cancellationToken);
                return;
            }

            if (path == "/dashboard/api/preview.jpg") {
                if (!DashboardAuthorized(headers)) {
                    await WriteJson(stream, 401, new { ok = false, error = "unauthorized" }, cancellationToken);
                    return;
                }
                byte[] preview;
                DateTimeOffset time;
                string imageId;
                lock (previewSync) {
                    preview = previewJpeg;
                    time = previewUtc;
                    imageId = previewImageId;
                }
                if (preview.Length == 0) {
                    await WriteJson(stream, 404, new { ok = false, error = "preview not available" }, cancellationToken);
                    return;
                }
                await WriteBytes(stream, 200, "image/jpeg", preview, cancellationToken, new Dictionary<string, string> {
                    ["X-QSM-Preview-Utc"] = time.ToString("O"),
                    ["X-QSM-Image-Id"] = imageId
                });
                return;
            }

            await WriteJson(stream, 404, new { ok = false, error = "not found" }, cancellationToken);
        }
    }

    private async Task HandleLogin(NetworkStream stream, IDictionary<string, string> headers, string body, string remoteKey, CancellationToken cancellationToken) {
        if (!settings.WebDashboardEnabled) {
            await WriteJson(stream, 404, new { ok = false, error = "dashboard disabled" }, cancellationToken);
            return;
        }
        if (!settings.WebDashboardRequirePassword) {
            await WriteJson(stream, 200, new { ok = true }, cancellationToken);
            return;
        }
        if (!settings.WebDashboardPasswordConfigured) {
            await WriteJson(stream, 409, new { ok = false, error = "password not configured" }, cancellationToken);
            return;
        }

        if (!LoginAttemptAllowed(remoteKey, out var retryAfter)) {
            await WriteJson(stream, 429, new { ok = false, error = "too many login attempts", retryAfterSeconds = retryAfter }, cancellationToken,
                new Dictionary<string, string> { ["Retry-After"] = retryAfter.ToString() });
            return;
        }

        var password = ParseFormValue(body, "password");
        if (!settings.VerifyWebDashboardPassword(password)) {
            RegisterLoginFailure(remoteKey);
            // Small fixed delay makes rapid password guessing more expensive without making the UI annoying.
            try { await Task.Delay(250, cancellationToken); } catch { }
            await WriteJson(stream, 401, new { ok = false, error = "invalid credentials" }, cancellationToken);
            return;
        }

        loginFailures.TryRemove(remoteKey, out _);
        CleanupSessions();
        var token = RandomToken(32);
        sessions[token] = DateTimeOffset.UtcNow.Add(SessionLifetime);
        await WriteJson(stream, 200, new { ok = true }, cancellationToken,
            new Dictionary<string, string> { ["Set-Cookie"] = $"qsm_session={token}; Path=/; Max-Age={(int)SessionLifetime.TotalSeconds}; HttpOnly; SameSite=Strict" });
    }

    private bool LoginAttemptAllowed(string remoteKey, out int retryAfterSeconds) {
        retryAfterSeconds = 0;
        if (!loginFailures.TryGetValue(remoteKey, out var state)) return true;
        lock (state.Sync) {
            var now = DateTimeOffset.UtcNow;
            if (now - state.WindowStart >= LoginFailureWindow) {
                state.WindowStart = now;
                state.Failures = 0;
                return true;
            }
            if (state.Failures < MaxFailedLoginsPerWindow) return true;
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((LoginFailureWindow - (now - state.WindowStart)).TotalSeconds));
            return false;
        }
    }

    private void RegisterLoginFailure(string remoteKey) {
        var state = loginFailures.GetOrAdd(remoteKey, _ => new LoginFailureState { WindowStart = DateTimeOffset.UtcNow });
        lock (state.Sync) {
            var now = DateTimeOffset.UtcNow;
            if (now - state.WindowStart >= LoginFailureWindow) {
                state.WindowStart = now;
                state.Failures = 1;
            } else {
                state.Failures++;
            }
        }
    }

    private bool DashboardAuthorized(IDictionary<string, string> headers) {
        if (!settings.WebDashboardEnabled) return false;
        if (!settings.WebDashboardRequirePassword) return true;
        if (!settings.WebDashboardPasswordConfigured) return false;
        var token = SessionToken(headers);
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!sessions.TryGetValue(token, out var expires)) return false;
        if (expires <= DateTimeOffset.UtcNow) {
            sessions.TryRemove(token, out _);
            return false;
        }
        sessions[token] = DateTimeOffset.UtcNow.Add(SessionLifetime);
        return true;
    }

    private void RevokeSession(IDictionary<string, string> headers) {
        var token = SessionToken(headers);
        if (!string.IsNullOrWhiteSpace(token)) sessions.TryRemove(token, out _);
    }

    private static string SessionToken(IDictionary<string, string> headers) {
        if (!headers.TryGetValue("Cookie", out var cookie)) return null;
        foreach (var part in cookie.Split(';')) {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], "qsm_session", StringComparison.Ordinal)) return pair[1];
        }
        return null;
    }

    private void CleanupSessions() {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in sessions) if (pair.Value <= now) sessions.TryRemove(pair.Key, out _);
    }

    private static string ParseFormValue(string body, string key) {
        if (string.IsNullOrEmpty(body)) return "";
        foreach (var pair in body.Split('&')) {
            var item = pair.Split('=', 2);
            if (item.Length != 2) continue;
            var name = Uri.UnescapeDataString(item[0].Replace('+', ' '));
            if (!string.Equals(name, key, StringComparison.Ordinal)) continue;
            return Uri.UnescapeDataString(item[1].Replace('+', ' '));
        }
        return "";
    }

    private static string RandomToken(int bytes) {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
        return value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static async Task WriteJson(NetworkStream stream, int statusCode, object payload, CancellationToken cancellationToken, IDictionary<string, string> extraHeaders = null) {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        await WriteBytes(stream, statusCode, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cancellationToken, extraHeaders);
    }

    private static Task WriteHtml(NetworkStream stream, int statusCode, string html, CancellationToken cancellationToken) =>
        WriteBytes(stream, statusCode, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), cancellationToken);

    private static async Task WriteBytes(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken cancellationToken, IDictionary<string, string> extraHeaders = null) {
        var reason = statusCode switch {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            409 => "Conflict",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
            431 => "Request Header Fields Too Large",
            _ => "Error"
        };
        var builder = new StringBuilder()
            .Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("X-Frame-Options: DENY\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            .Append("Cross-Origin-Resource-Policy: same-origin\r\n")
            .Append("Permissions-Policy: camera=(), microphone=(), geolocation=()\r\n")
            .Append("Content-Security-Policy: default-src 'self'; connect-src 'self'; img-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'\r\n");
        if (extraHeaders != null) {
            foreach (var pair in extraHeaders) {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Contains('\r') || pair.Value.Contains('\n')) continue;
                builder.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
            }
        }
        builder.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken);
        if (body?.Length > 0) await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        settings.PropertyChanged -= SettingsChanged;
        imageSaveMediator.ImageSaved -= ImageSaved;
        lock (lifecycleSync) StopListener();
        sessions.Clear();
        loginFailures.Clear();
        clientSlots.Dispose();
    }

    private sealed class LoginFailureState {
        public object Sync { get; } = new();
        public DateTimeOffset WindowStart { get; set; }
        public int Failures { get; set; }
    }

    private const string DisabledHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>QSM</title></head><body style="font-family:system-ui;background:#101114;color:#eee;padding:32px"><h2>QualitySessionMeter</h2><p>Web Dashboard is disabled in N.I.N.A. plugin settings.</p></body></html>
""";

    private const string PasswordNotConfiguredHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>QSM</title></head><body style="font-family:system-ui;background:#101114;color:#eee;padding:32px"><h2>QualitySessionMeter</h2><p>Password protection is enabled, but no password has been configured yet. Set one in N.I.N.A. → Plugins → QualitySessionMeter.</p></body></html>
""";

    private const string LoginHtml = """
<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>QSM Login</title>
<style>body{margin:0;background:#0d0f12;color:#f3f5f7;font:15px system-ui;display:grid;place-items:center;min-height:100vh}.card{width:min(390px,calc(100vw - 40px));background:#15181d;border:1px solid #3b4551;border-radius:16px;padding:28px;box-shadow:0 18px 55px #0008}h1{font-size:22px;margin:0 0 6px}p{color:#b2bbc7;margin:0 0 22px}input{box-sizing:border-box;width:100%;padding:13px 14px;border-radius:9px;border:1px solid #4a5664;background:#0f1216;color:#fff;font-size:16px;outline:none}input:focus{border-color:#8ab4f8}button{width:100%;margin-top:12px;padding:12px;border:0;border-radius:9px;background:#8ab4f8;color:#10141b;font-weight:700;font-size:15px}.err{min-height:20px;color:#f28b82;margin-top:10px;font-size:13px}</style></head>
<body><div class="card"><h1>QualitySessionMeter</h1><p>Enter the optional dashboard password configured in N.I.N.A.</p><form id="f"><input id="p" type="password" autocomplete="current-password" autofocus maxlength="512" placeholder="Password"><button>Open dashboard</button><div class="err" id="e" role="status"></div></form></div>
<script>document.getElementById('f').addEventListener('submit',async(e)=>{e.preventDefault();const out=document.getElementById('e');out.textContent='';try{const r=await fetch('/dashboard/login',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'password='+encodeURIComponent(document.getElementById('p').value)});if(r.ok){location.reload();return}if(r.status===429){const d=await r.json();out.textContent='Too many attempts. Try again in '+(d.retryAfterSeconds||60)+' seconds.';return}out.textContent='Invalid password.'}catch{out.textContent='Dashboard connection error.'}})</script></body></html>
""";
}
