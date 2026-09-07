using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

public static class HtmlReportWriter {
    public static async Task WriteAsync(
        string path,
        IReadOnlyList<FrameQualityResult> frames,
        IReadOnlyList<SessionEvent> events,
        DateTime createdUtc) {

        frames ??= Array.Empty<FrameQualityResult>();
        events ??= Array.Empty<SessionEvent>();

        var accepted = frames.Where(x => x.Status == FrameStatus.Accepted).ToArray();
        var usable = frames.Where(x => x.IsUsable).ToArray();
        var best = accepted.OrderByDescending(x => x.OverallQuality).ThenByDescending(x => x.ConfidenceScore).Take(8).ToArray();
        var worst = accepted.OrderBy(x => x.OverallQuality).ThenBy(x => x.ConfidenceScore).Take(8).ToArray();
        int rejected = frames.Count(x => x.Status == FrameStatus.Rejected);
        int warning = frames.Count(x => x.Status == FrameStatus.Warning);
        int learning = frames.Count(x => x.Status == FrameStatus.Learning);
        int errors = frames.Count(x => x.Status == FrameStatus.Error);
        double acceptance = usable.Length + rejected == 0 ? 0 : usable.Length * 100.0 / (usable.Length + rejected);
        double quality = usable.Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
        double confidence = usable.Where(x => IsFinite(x.ConfidenceScore)).Select(x => x.ConfidenceScore).DefaultIfEmpty(0).Average();

        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>QualitySessionMeter — Session report</title><style>");
        sb.Append("*{box-sizing:border-box}body{margin:0;background:#0f1115;color:#f1f3f4;font:14px 'Segoe UI',Arial,sans-serif}main{max-width:1440px;margin:auto;padding:24px}.top{display:flex;justify-content:space-between;align-items:flex-end;gap:24px;margin-bottom:18px}h1{font-size:27px;margin:0}h2{font-size:17px;margin:0 0 12px}h3{font-size:13px;color:#9aa0a6;text-transform:uppercase;letter-spacing:.06em;margin:0 0 8px}.muted{color:#9aa0a6}.grid{display:grid;grid-template-columns:repeat(6,minmax(120px,1fr));gap:10px}.card{background:#171a20;border:1px solid #2a3039;border-radius:10px;padding:14px}.big{font-size:28px;font-weight:650}.good{color:#81c995}.warn{color:#fdd663}.bad{color:#f28b82}.accent{color:#8ab4f8}.section{margin-top:14px}.chart{width:100%;overflow:auto}.legend{display:flex;flex-wrap:wrap;gap:14px;margin:8px 0;color:#bdc1c6}.dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:5px}.two{display:grid;grid-template-columns:1fr 1fr;gap:10px}.rank{display:grid;grid-template-columns:56px 1fr 72px 82px;gap:5px 8px;align-items:center;border-top:1px solid #2a3039;padding:6px 0}.rank:first-of-type{border-top:0}table{width:100%;border-collapse:collapse;font-size:12px}th{position:sticky;top:0;background:#171a20;color:#9aa0a6;text-align:left;padding:8px;border-bottom:1px solid #353b45}td{padding:7px 8px;border-bottom:1px solid #242933;white-space:nowrap}tr:hover td{background:#1d222a}.tablewrap{max-height:590px;overflow:auto;border:1px solid #2a3039;border-radius:8px}.tools{display:flex;gap:8px;flex-wrap:wrap;margin:0 0 10px}button,input,select{background:#11151b;border:1px solid #343b46;color:#f1f3f4;border-radius:6px;padding:7px 9px}button{cursor:pointer}button.active{border-color:#8ab4f8;color:#8ab4f8}.pill{border:1px solid #343b46;border-radius:999px;padding:2px 8px;font-size:11px}.event{display:grid;grid-template-columns:62px 150px 1fr 80px 80px 86px;gap:8px;padding:9px 0;border-bottom:1px solid #2a3039;align-items:center}.causebar{display:grid;grid-template-columns:minmax(160px,280px) 1fr 50px;gap:10px;align-items:center;margin:6px 0}.bar{height:8px;background:#252b34;border-radius:5px;overflow:hidden}.fill{height:100%;background:#8ab4f8}.footer{margin:18px 0;color:#737a84;font-size:12px}@media(max-width:900px){.grid{grid-template-columns:repeat(2,1fr)}.two{grid-template-columns:1fr}.event{grid-template-columns:60px 1fr 70px}.event .hide-sm{display:none}}"]);
        sb.Append("</style></head><body><main>");

        sb.Append("<div class=\"top\"><div><h1>QualitySessionMeter</h1><div class=\"muted\">V2 Smart Quality Analysis — session report</div></div><div class=\"muted\">");
        sb.Append(H(createdUtc == default ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'") : createdUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
        sb.Append("</div></div>");

        sb.Append("<div class=\"grid\">");
        Metric(sb, "Captured", frames.Count.ToString(CultureInfo.InvariantCulture), "");
        Metric(sb, "Accepted", accepted.Length.ToString(CultureInfo.InvariantCulture), "good");
        Metric(sb, "Rejected", rejected.ToString(CultureInfo.InvariantCulture), rejected > 0 ? "bad" : "good");
        Metric(sb, "Acceptance", acceptance.ToString("0.0", CultureInfo.InvariantCulture) + "%", acceptance >= 80 ? "good" : acceptance >= 60 ? "warn" : "bad");
        Metric(sb, "Session quality", quality.ToString("0", CultureInfo.InvariantCulture), "accent");
        Metric(sb, "Mean confidence", confidence.ToString("0", CultureInfo.InvariantCulture) + "%", "accent");
        sb.Append("</div>");

        sb.Append("<section class=\"card section\"><h2>Multichannel timeline</h2><div class=\"legend\"><span><i class=\"dot\" style=\"background:#8ab4f8\"></i>Quality</span><span><i class=\"dot\" style=\"background:#c58af9\"></i>Confidence</span><span><i class=\"dot\" style=\"background:#81c995\"></i>Guide RMS</span><span><i class=\"dot\" style=\"background:#fdd663\"></i>Stars Δ</span><span><i class=\"dot\" style=\"background:#f28b82\"></i>Background Δ</span></div><div class=\"chart\">");
        sb.Append(BuildTimelineSvg(frames));
        sb.Append("</div></section>");

        sb.Append("<div class=\"two section\"><section class=\"card\"><h2>Best accepted frames</h2>");
        AppendRanking(sb, best);
        sb.Append("</section><section class=\"card\"><h2>Worst accepted frames</h2>");
        AppendRanking(sb, worst);
        sb.Append("</section></div>");

        sb.Append("<div class=\"two section\"><section class=\"card\"><h2>Session events</h2>");
        if (events.Count == 0) sb.Append("<div class=\"muted\">No warning/rejection/error event was detected.</div>");
        foreach (var e in events) {
            sb.Append("<div class=\"event\"><b>").Append(H(e.Id)).Append("</b><span>").Append(H(e.TypeText)).Append("</span><span class=\"hide-sm\">").Append(H(e.PrimaryCause)).Append("</span><span>").Append(e.AffectedFrames).Append(" frames</span><span class=\"hide-sm\">").Append(H(e.ConfidenceText)).Append("</span><span class=\"pill\">").Append(H(e.SeverityText)).Append("</span></div>");
        }
        sb.Append("</section><section class=\"card\"><h2>Rejection / warning causes</h2>");
        AppendCauseDistribution(sb, frames);
        sb.Append("</section></div>");

        sb.Append("<section class=\"card section\"><h2>Frame history</h2><div class=\"tools\"><input id=\"search\" placeholder=\"Search filename / cause / pattern…\" oninput=\"filterRows()\"><select id=\"statusFilter\" onchange=\"filterRows()\"><option value=\"\">All statuses</option><option>ACCEPTED</option><option>WARNING</option><option>WOULD REJECT</option><option>REJECTED</option><option>LEARNING</option><option>ERROR</option></select><button class=\"active\" onclick=\"setSort('frame')\">Frame order</button><button onclick=\"setSort('quality')\">Quality</button></div><div class=\"tablewrap\"><table id=\"frames\"><thead><tr><th>#</th><th>File</th><th>Status</th><th>Quality</th><th>Confidence</th><th>RMS</th><th>Guide pattern</th><th>Trend</th><th>Stars Δ</th><th>BG Δ</th><th>Cause</th></tr></thead><tbody>");
        foreach (var f in frames) {
            string search = $"{f.FileName} {f.StatusText} {f.ProbableCause} {f.GuidePatternText} {f.TrendText}".ToLowerInvariant();
            sb.Append("<tr data-frame=\"").Append(f.FrameIndex).Append("\" data-quality=\"").Append(N(f.OverallQuality)).Append("\" data-status=\"").Append(H(f.StatusText)).Append("\" data-search=\"").Append(HA(search)).Append("\"><td>").Append(f.FrameIndex).Append("</td><td>").Append(H(f.FileName)).Append("</td><td>").Append(H(f.StatusText)).Append("</td><td>").Append(f.QualityText).Append("</td><td>").Append(H(f.ConfidenceText)).Append("</td><td>").Append(H(f.GuideRmsText)).Append("</td><td title=\"").Append(HA(f.GuidePatternDetail)).Append("\">").Append(H(f.GuidePatternText)).Append("</td><td>").Append(H(f.TrendText)).Append("</td><td>").Append(H(f.StarDeltaText)).Append("</td><td>").Append(H(f.BackgroundDeltaText)).Append("</td><td>").Append(H(f.ProbableCause)).Append("</td></tr>");
        }
        sb.Append("</tbody></table></div></section>");

        sb.Append("<div class=\"footer\">QualitySessionMeter V2 report. Guide-pattern and probable-cause labels are diagnostic interpretations of measured data, not guaranteed physical-cause identifications.</div>");
        sb.Append("<script>let sort='frame';function filterRows(){const q=document.getElementById('search').value.toLowerCase();const s=document.getElementById('statusFilter').value;document.querySelectorAll('#frames tbody tr').forEach(r=>{r.style.display=(!q||r.dataset.search.includes(q))&&(!s||r.dataset.status===s)?'':'none'})}function setSort(k){sort=k;document.querySelectorAll('.tools button').forEach(b=>b.classList.remove('active'));event.target.classList.add('active');const body=document.querySelector('#frames tbody');[...body.rows].sort((a,b)=>k==='quality'?Number(b.dataset.quality)-Number(a.dataset.quality):Number(a.dataset.frame)-Number(b.dataset.frame)).forEach(r=>body.appendChild(r));}</script>");
        sb.Append("</main></body></html>");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string BuildTimelineSvg(IReadOnlyList<FrameQualityResult> frames) {
        const int width = 1320;
        const int height = 390;
        const int left = 46;
        const int right = 14;
        const int top = 22;
        const int band = 105;
        const int gap = 16;
        int plotW = width - left - right;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {width} {height}' style='min-width:820px;width:100%;height:auto'>");
        sb.Append("<rect width='100%' height='100%' rx='8' fill='#101318'/>");
        BandGrid(sb, left, top, plotW, band, "Quality / Confidence", "0", "100");
        BandGrid(sb, left, top + band + gap, plotW, band, "Guide RMS (arcsec)", "0", "auto");
        BandGrid(sb, left, top + 2 * (band + gap), plotW, band, "Image residuals (%)", "-50", "+50");
        if (frames.Count == 0) { sb.Append("</svg>"); return sb.ToString(); }

        double guideMax = Math.Max(2.0, frames.Where(x => IsFinite(x.GuideRmsArcsec)).Select(x => x.GuideRmsArcsec).DefaultIfEmpty(2).Max() * 1.1);
        Polyline(sb, frames, left, top, plotW, band, f => Math.Clamp(f.OverallQuality, 0, 100), 0, 100, "#8ab4f8", 2.2, false);
        Polyline(sb, frames, left, top, plotW, band, f => IsFinite(f.ConfidenceScore) ? f.ConfidenceScore : double.NaN, 0, 100, "#c58af9", 1.7, true);
        Polyline(sb, frames, left, top + band + gap, plotW, band, f => f.GuideRmsArcsec, 0, guideMax, "#81c995", 1.9, false);
        Polyline(sb, frames, left, top + 2 * (band + gap), plotW, band, f => f.StarDeviationPercent, -50, 50, "#fdd663", 1.7, false);
        Polyline(sb, frames, left, top + 2 * (band + gap), plotW, band, f => f.BackgroundDeviationPercent, -50, 50, "#f28b82", 1.7, false);

        for (int i = 0; i < frames.Count; i++) {
            if (frames[i].Status is not (FrameStatus.Rejected or FrameStatus.Warning or FrameStatus.Error)) continue;
            double x = frames.Count == 1 ? left + plotW / 2.0 : left + i * plotW / (frames.Count - 1.0);
            string c = frames[i].Status == FrameStatus.Rejected ? "#f28b82" : frames[i].Status == FrameStatus.Error ? "#ff7878" : "#fdd663";
            sb.Append($"<line x1='{x:0.##}' y1='{top}' x2='{x:0.##}' y2='{top + 3 * band + 2 * gap}' stroke='{c}' stroke-width='1' opacity='.30'/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void BandGrid(StringBuilder sb, int left, int top, int width, int height, string title, string low, string high) {
        sb.Append($"<rect x='{left}' y='{top}' width='{width}' height='{height}' fill='#12161c' stroke='#2a3039'/>");
        sb.Append($"<line x1='{left}' y1='{top + height / 2}' x2='{left + width}' y2='{top + height / 2}' stroke='#30363d' stroke-width='1'/>");
        sb.Append($"<text x='{left + 6}' y='{top + 14}' fill='#9aa0a6' font-size='11' font-family='Segoe UI'>{H(title)}</text>");
        sb.Append($"<text x='5' y='{top + 12}' fill='#737a84' font-size='10' font-family='Segoe UI'>{H(high)}</text><text x='5' y='{top + height - 3}' fill='#737a84' font-size='10' font-family='Segoe UI'>{H(low)}</text>");
    }

    private static void Polyline(StringBuilder sb, IReadOnlyList<FrameQualityResult> frames, double left, double top, double width, double height, Func<FrameQualityResult,double> selector, double min, double max, string color, double stroke, bool dashed) {
        var segments = new List<List<string>> { new() };
        for (int i = 0; i < frames.Count; i++) {
            double v = selector(frames[i]);
            if (!IsFinite(v)) { if (segments[^1].Count > 0) segments.Add(new()); continue; }
            v = Math.Clamp(v, min, max);
            double x = frames.Count == 1 ? left + width / 2 : left + i * width / (frames.Count - 1.0);
            double y = top + height * (1 - (v - min) / Math.Max(1e-9, max - min));
            segments[^1].Add($"{x:0.##},{y:0.##}");
        }
        foreach (var segment in segments.Where(x => x.Count > 0)) {
            sb.Append($"<polyline points='{string.Join(" ", segment)}' fill='none' stroke='{color}' stroke-width='{stroke:0.0}' {(dashed ? "stroke-dasharray='5 4'" : "")}/>");
        }
    }

    private static void AppendRanking(StringBuilder sb, IReadOnlyList<FrameQualityResult> frames) {
        if (frames.Count == 0) { sb.Append("<div class='muted'>No accepted frame available.</div>"); return; }
        foreach (var f in frames) {
            sb.Append("<div class=\"rank\"><span>#").Append(f.FrameIndex).Append("</span><span title=\"").Append(HA(f.FinalPath ?? f.OriginalPath)).Append("\">").Append(H(f.FileName)).Append("</span><b>").Append(f.QualityText).Append(" Q</b><span class=\"muted\">").Append(H(f.ConfidenceText)).Append(" conf</span></div>");
        }
    }

    private static void AppendCauseDistribution(StringBuilder sb, IReadOnlyList<FrameQualityResult> frames) {
        var abnormal = frames.Where(x => x.Status is FrameStatus.Warning or FrameStatus.Rejected or FrameStatus.Error).ToArray();
        var groups = abnormal.GroupBy(x => string.IsNullOrWhiteSpace(x.ProbableCause) ? "UNKNOWN" : x.ProbableCause).Select(g => (Cause:g.Key, Count:g.Count())).OrderByDescending(x => x.Count).ToArray();
        if (groups.Length == 0) { sb.Append("<div class='muted'>No abnormal-frame cause to summarize.</div>"); return; }
        int max = groups.Max(x => x.Count);
        foreach (var g in groups) {
            double pct = 100.0 * g.Count / Math.Max(1, max);
            sb.Append("<div class=\"causebar\"><span>").Append(H(g.Cause)).Append("</span><div class=\"bar\"><div class=\"fill\" style=\"width:").Append(pct.ToString("0.0",CultureInfo.InvariantCulture)).Append("%\"></div></div><b>").Append(g.Count).Append("</b></div>");
        }
    }

    private static void Metric(StringBuilder sb, string title, string value, string cls) => sb.Append("<div class=\"card\"><h3>").Append(H(title)).Append("</h3><div class=\"big ").Append(cls).Append("\">").Append(H(value)).Append("</div></div>");
    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    private static string N(double v) => IsFinite(v) ? v.ToString("0.####", CultureInfo.InvariantCulture) : "";
    private static string H(string? v) => WebUtility.HtmlEncode(v ?? "");
    private static string HA(string? v) => H(v).Replace("'", "&#39;");
}
