using NINA.Equipment.Model;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
/// Optional self-contained read-only dashboard for browsers on the user's LAN/VPN.
/// It is intentionally independent from QualitySessionHttpBridge, which remains the
/// tokenized OpenAstro/companion API and therefore stays backward compatible.
/// </summary>
public sealed class QualitySessionWebServer : IDisposable {
    private const int MaxRequestLine = 4096;
    private const int MaxHeaderLines = 64;
    private const int MaxBodyBytes = 8192;
    private const int PreviewMaxWidth = 1280;
    private const int PreviewJpegQuality = 82;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    private readonly IImageSaveMediator imageSaveMediator;
    private readonly QualitySettings settings;
    private readonly object lifecycleSync = new();
    private readonly object previewSync = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> sessions = new(StringComparer.Ordinal);

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

    public string AccessUrl {
        get {
            var port = Port > 0 ? Port : settings.WebDashboardPort;
            try {
                var address = Dns.GetHostAddresses(Dns.GetHostName())
                    .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
                return address == null ? $"http://<NINA-PC-IP>:{port}/" : $"http://{address}:{port}/";
            } catch {
                return $"http://<NINA-PC-IP>:{port}/";
            }
        }
    }

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
                _ = Task.Run(() => HandleClient(client, cancellationToken), cancellationToken);
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
            for (var i = 0; i < MaxHeaderLines; i++) {
                string line;
                try { line = await reader.ReadLineAsync(cancellationToken); }
                catch { return; }
                if (string.IsNullOrEmpty(line)) break;
                var separator = line.IndexOf(':');
                if (separator > 0) headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
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
                if (headers.TryGetValue("Content-Length", out var rawLength)) int.TryParse(rawLength, out contentLength);
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
                    body = new string(buffer, 0, read);
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
                await HandleLogin(stream, headers, body, cancellationToken);
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
                await WriteHtml(stream, 200, DashboardHtml, cancellationToken);
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

    private async Task HandleLogin(NetworkStream stream, IDictionary<string, string> headers, string body, CancellationToken cancellationToken) {
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

        var password = ParseFormValue(body, "password");
        if (!settings.VerifyWebDashboardPassword(password)) {
            await WriteJson(stream, 401, new { ok = false, error = "wrong password" }, cancellationToken);
            return;
        }

        CleanupSessions();
        var token = RandomToken(32);
        sessions[token] = DateTimeOffset.UtcNow.Add(SessionLifetime);
        await WriteJson(stream, 200, new { ok = true }, cancellationToken,
            new Dictionary<string, string> { ["Set-Cookie"] = $"qsm_session={token}; Path=/; Max-Age={(int)SessionLifetime.TotalSeconds}; HttpOnly; SameSite=Strict" });
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
            .Append("Referrer-Policy: no-referrer\r\n");
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
    }

    private const string DisabledHtml = """
<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><title>QSM</title></head><body style="font-family:system-ui;background:#101114;color:#eee;padding:32px"><h2>QualitySessionMeter</h2><p>Web Dashboard is disabled in N.I.N.A. plugin settings.</p></body></html>
""";

    private const string PasswordNotConfiguredHtml = """
<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><title>QSM</title></head><body style="font-family:system-ui;background:#101114;color:#eee;padding:32px"><h2>QualitySessionMeter</h2><p>Password protection is enabled, but no password has been configured yet. Set one in N.I.N.A. → Plugins → QualitySessionMeter.</p></body></html>
""";

    private const string LoginHtml = """
<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>QSM Login</title>
<style>body{margin:0;background:#0d0f12;color:#f3f5f7;font:15px system-ui;display:grid;place-items:center;min-height:100vh}.card{width:min(390px,calc(100vw - 40px));background:#15181d;border:1px solid #2a2f37;border-radius:18px;padding:28px;box-shadow:0 18px 55px #0008}h1{font-size:22px;margin:0 0 6px}p{color:#9aa4b2;margin:0 0 22px}input{box-sizing:border-box;width:100%;padding:13px 14px;border-radius:10px;border:1px solid #343b46;background:#0f1216;color:#fff;font-size:16px;outline:none}button{width:100%;margin-top:12px;padding:12px;border:0;border-radius:10px;background:#8ab4f8;color:#10141b;font-weight:700;font-size:15px}.err{min-height:20px;color:#f28b82;margin-top:10px;font-size:13px}</style></head>
<body><div class="card"><h1>QualitySessionMeter</h1><p>Enter the dashboard password configured in N.I.N.A.</p><form id="f"><input id="p" type="password" autocomplete="current-password" autofocus placeholder="Password"><button>Open dashboard</button><div class="err" id="e"></div></form></div>
<script>document.getElementById('f').addEventListener('submit',async(e)=>{e.preventDefault();const r=await fetch('/dashboard/login',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'password='+encodeURIComponent(document.getElementById('p').value)});if(r.ok){location.reload()}else{document.getElementById('e').textContent='Wrong password'}})</script></body></html>
""";

    private const string DashboardHtml = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><meta name="theme-color" content="#0b0d10"><title>QualitySessionMeter</title>
<style>
:root{color-scheme:dark;--bg:#0b0d10;--card:#13171c;--card2:#171c22;--line:#252c35;--muted:#8d98a6;--text:#eef2f6;--blue:#8ab4f8;--green:#81c995;--yellow:#fdd663;--red:#f28b82;--purple:#c58af9}*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at 15% -10%,#182133 0,transparent 34%),var(--bg);color:var(--text);font:14px/1.45 Inter,ui-sans-serif,system-ui,-apple-system,Segoe UI,sans-serif}.wrap{width:min(1240px,100%);margin:auto;padding:18px}.top{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:16px}.brand h1{margin:0;font-size:21px;letter-spacing:-.02em}.brand div{color:var(--muted);font-size:12px}.live{display:flex;align-items:center;gap:7px;color:var(--green);font-size:12px}.dot{width:8px;height:8px;border-radius:50%;background:currentColor;box-shadow:0 0 12px currentColor}.grid{display:grid;grid-template-columns:repeat(12,1fr);gap:12px}.card{background:linear-gradient(180deg,#15191f,#11151a);border:1px solid var(--line);border-radius:15px;padding:15px;min-width:0}.summary{grid-column:span 2}.summary .n{font-size:27px;font-weight:760;letter-spacing:-.04em}.label{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.08em}.current{grid-column:span 5}.preview{grid-column:span 7;min-height:280px;display:flex;flex-direction:column}.guide{grid-column:span 5}.timeline{grid-column:span 7}.history{grid-column:1/-1}.row{display:flex;justify-content:space-between;gap:14px;padding:6px 0;border-bottom:1px solid #20262e}.row:last-child{border:0}.row span:first-child{color:var(--muted)}.status{display:inline-flex;padding:5px 9px;border-radius:999px;font-weight:700;font-size:11px;letter-spacing:.04em;background:#232a33}.accepted{color:var(--green);background:#18271f}.rejected{color:var(--red);background:#301d1f}.warning{color:var(--yellow);background:#2d2819}.learning{color:var(--blue);background:#192536}.error{color:var(--red)}#preview{width:100%;height:100%;min-height:240px;object-fit:contain;border-radius:11px;background:#090b0e;margin-top:10px}canvas{display:block;width:100%;height:210px;margin-top:8px}table{width:100%;border-collapse:collapse;font-size:12px}th{color:var(--muted);font-weight:600;text-align:left;padding:8px 7px;border-bottom:1px solid var(--line)}td{padding:8px 7px;border-bottom:1px solid #20262e;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:230px}.section-title{font-size:14px;font-weight:700;margin-bottom:9px}.muted{color:var(--muted)}@media(max-width:850px){.wrap{padding:12px}.summary{grid-column:span 4}.current,.preview,.guide,.timeline{grid-column:1/-1}.preview{min-height:220px}.history{overflow:auto}.top{align-items:flex-start}.brand h1{font-size:19px}}@media(max-width:520px){.summary{grid-column:span 6}.summary .n{font-size:23px}.card{border-radius:13px;padding:12px}}
</style></head><body><div class="wrap"><div class="top"><div class="brand"><h1>QualitySessionMeter</h1><div id="sub">Waiting for N.I.N.A. session data…</div></div><div class="live"><span class="dot"></span><span id="liveText">LOCAL</span></div></div>
<div class="grid"><div class="card summary"><div class="label">Captured</div><div class="n" id="captured">—</div></div><div class="card summary"><div class="label">Usable</div><div class="n" id="usable">—</div></div><div class="card summary"><div class="label">Rejected</div><div class="n" id="rejected">—</div></div><div class="card summary"><div class="label">Acceptance</div><div class="n" id="acceptance">—</div></div><div class="card summary"><div class="label">Session Q</div><div class="n" id="sessionQ">—</div></div><div class="card summary"><div class="label">Confidence</div><div class="n" id="sessionC">—</div></div>
<div class="card current"><div class="section-title">Current frame <span id="status" class="status">—</span></div><div class="row"><span>Quality</span><strong id="quality">—</strong></div><div class="row"><span>Target / Filter</span><strong id="target">—</strong></div><div class="row"><span>Guide RMS</span><strong id="rms">—</strong></div><div class="row"><span>Stars Δ</span><strong id="stars">—</strong></div><div class="row"><span>Background Δ</span><strong id="background">—</strong></div><div class="row"><span>Cause</span><strong id="cause">—</strong></div><div class="row"><span>File</span><strong id="file">—</strong></div></div>
<div class="card preview"><div class="section-title">Latest LIGHT preview</div><div class="muted" id="previewState">Waiting for an image…</div><img id="preview" alt="Latest LIGHT preview"></div>
<div class="card guide"><div class="section-title">Live guiding · last 20 s</div><div class="row"><span>Total RMS</span><strong id="grms">—</strong></div><div class="row"><span>RA / DEC RMS</span><strong id="axes">—</strong></div><div class="row"><span>Max excursion</span><strong id="gmax">—</strong></div><canvas id="guideCanvas"></canvas></div>
<div class="card timeline"><div class="section-title">Quality timeline</div><canvas id="qualityCanvas"></canvas></div>
<div class="card history"><div class="section-title">Recent frames</div><div style="overflow:auto"><table><thead><tr><th>#</th><th>Status</th><th>Quality</th><th>Confidence</th><th>RMS</th><th>Stars Δ</th><th>BG Δ</th><th>Filter</th><th>File</th><th>Cause</th></tr></thead><tbody id="frames"></tbody></table></div></div></div></div>
<script>
const $=id=>document.getElementById(id),num=(v,d=1)=>v==null?'—':Number(v).toFixed(d),pct=v=>v==null?'—':num(v,1)+'%',arc=v=>v==null?'—':num(v,2)+'″';
function statusClass(v){return 'status '+String(v||'').toLowerCase().replace(/\s+/g,'-')}
function drawSeries(canvas,items,getter,min,max,stroke){const dpr=devicePixelRatio||1,w=canvas.clientWidth||300,h=canvas.clientHeight||180;canvas.width=w*dpr;canvas.height=h*dpr;const c=canvas.getContext('2d');c.scale(dpr,dpr);c.clearRect(0,0,w,h);c.strokeStyle='#252c35';c.lineWidth=1;for(let i=0;i<4;i++){const y=12+(h-24)*i/3;c.beginPath();c.moveTo(0,y);c.lineTo(w,y);c.stroke()}const pts=items.map((x,i)=>({x:items.length<2?w/2:i*w/(items.length-1),v:getter(x)})).filter(p=>p.v!=null&&Number.isFinite(Number(p.v)));if(!pts.length)return;c.strokeStyle=stroke;c.lineWidth=2;c.beginPath();pts.forEach((p,i)=>{const y=10+(h-20)*(1-(Number(p.v)-min)/(max-min));i?c.lineTo(p.x,y):c.moveTo(p.x,y)});c.stroke()}
function render(s){const q=s.summary||{},f=s.currentFrame||{},g=s.guidingLive||{},frames=s.frames||[];$('captured').textContent=q.captured??0;$('usable').textContent=q.usable??0;$('rejected').textContent=q.rejected??0;$('acceptance').textContent=pct(q.acceptanceRate);$('sessionQ').textContent=num(q.sessionQuality,0);$('sessionC').textContent=num(q.sessionConfidence,0);$('status').textContent=f.status||'NO FRAME';$('status').className=statusClass(f.status);$('quality').textContent=f.quality==null?'—':num(f.quality,0)+' / 100';$('target').textContent=[f.target,f.filter].filter(Boolean).join(' · ')||'—';$('rms').textContent=arc(f.guideRmsArcsec);$('stars').textContent=pct(f.starDeltaPercent);$('background').textContent=pct(f.backgroundDeltaPercent);$('cause').textContent=f.probableCause||f.reason||'—';$('file').textContent=f.finalFileName||f.fileName||'—';$('grms').textContent=arc(g.rmsTotalArcsec);$('axes').textContent=arc(g.rmsRaArcsec)+' / '+arc(g.rmsDecArcsec);$('gmax').textContent=arc(g.maxExcursionArcsec);$('sub').textContent=(s.mode?.enabled?'QSM enabled':'QSM disabled')+' · '+(s.mode?.monitorOnly?'Monitor only':'Active file handling');$('frames').innerHTML=frames.slice().reverse().slice(0,30).map(x=>`<tr><td>${x.frameIndex??''}</td><td><span class="${statusClass(x.status)}">${x.status||''}</span></td><td>${num(x.quality,0)}</td><td>${num(x.confidence,0)}</td><td>${arc(x.guideRmsArcsec)}</td><td>${pct(x.starDeltaPercent)}</td><td>${pct(x.backgroundDeltaPercent)}</td><td>${x.filter||''}</td><td title="${x.finalFileName||x.fileName||''}">${x.finalFileName||x.fileName||''}</td><td title="${x.probableCause||x.reason||''}">${x.probableCause||x.reason||''}</td></tr>`).join('');drawSeries($('qualityCanvas'),frames,x=>x.quality,0,100,'#8ab4f8');const gs=g.series||[];const abs=Math.max(1,...gs.flatMap(x=>[Math.abs(Number(x.raArcsec)||0),Math.abs(Number(x.decArcsec)||0)]));drawSeries($('guideCanvas'),gs,x=>x.totalArcsec,0,Math.max(1,abs*1.25),'#81c995')}
async function refresh(){try{const r=await fetch('/dashboard/api/snapshot',{cache:'no-store'});if(r.status===401){location.reload();return}if(!r.ok)throw 0;render(await r.json());$('liveText').textContent='LIVE'}catch{$('liveText').textContent='RECONNECTING'}}
function preview(){const img=$('preview');img.src='/dashboard/api/preview.jpg?t='+Date.now();img.onload=()=>{$('previewState').textContent='Latest saved LIGHT'};img.onerror=()=>{$('previewState').textContent='Preview not available yet'}}
refresh();preview();setInterval(refresh,2000);setInterval(preview,5000);addEventListener('resize',()=>refresh());
</script></body></html>
""";
}
