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
        double acceptance = usable.Length + rejected == 0 ? 0 : usable.Length * 100.0 / (usable.Length + rejected);
        double meanQuality = usable.Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
        double meanConfidence = usable.Where(x => Finite(x.ConfidenceScore)).Select(x => x.ConfidenceScore).DefaultIfEmpty(0).Average();

        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        sb.AppendLine("<title>QualitySessionMeter — Session report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("*{box-sizing:border-box}body{margin:0;background:#0f1115;color:#f1f3f4;font:14px 'Segoe UI',Arial,sans-serif}main{max-width:1440px;margin:auto;padding:24px}h1{margin:0;font-size:28px}h2{font-size:17px;margin:0 0 12px}h3{font-size:11px;color:#9aa0a6;text-transform:uppercase;letter-spacing:.06em;margin:0 0 7px}.muted{color:#9aa0a6}.top{display:flex;justify-content:space-between;align-items:flex-end;gap:20px;margin-bottom:18px}.grid{display:grid;grid-template-columns:repeat(6,minmax(120px,1fr));gap:10px}.card{background:#171a20;border:1px solid #2a3039;border-radius:10px;padding:14px}.section{margin-top:12px}.big{font-size:28px;font-weight:650}.good{color:#81c995}.bad{color:#f28b82}.accent{color:#8ab4f8}.two{display:grid;grid-template-columns:1fr 1fr;gap:10px}.legend{display:flex;gap:14px;flex-wrap:wrap;color:#bdc1c6;margin:0 0 8px}.dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:5px}.rank{display:grid;grid-template-columns:50px 1fr 58px 80px;gap:8px;padding:7px 0;border-top:1px solid #2a3039}.rank:first-of-type{border-top:0}.event{display:grid;grid-template-columns:55px 170px 1fr 78px 72px;gap:8px;padding:8px 0;border-bottom:1px solid #2a3039}.pill{border:1px solid #353b45;border-radius:999px;padding:2px 8px;font-size:11px}.tools{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:9px}input,select,button{background:#11151b;color:#f1f3f4;border:1px solid #343b46;border-radius:6px;padding:7px 9px}button{cursor:pointer}.tablewrap{max-height:600px;overflow:auto;border:1px solid #2a3039;border-radius:8px}table{width:100%;border-collapse:collapse;font-size:12px}th{position:sticky;top:0;background:#171a20;color:#9aa0a6;text-align:left;padding:8px;border-bottom:1px solid #353b45}td{padding:7px 8px;border-bottom:1px solid #242933;white-space:nowrap}tr:hover td{background:#1d222a}.cause{display:grid;grid-template-columns:minmax(150px,260px) 1fr 40px;gap:8px;align-items:center;margin:7px 0}.bar{height:8px;background:#252b34;border-radius:5px;overflow:hidden}.fill{height:100%;background:#8ab4f8}.footer{margin-top:16px;color:#737a84;font-size:12px}@media(max-width:900px){.grid{grid-template-columns:repeat(2,1fr)}.two{grid-template-columns:1fr}.event{grid-template-columns:55px 1fr 70px}.event .optional{display:none}}");
        sb.AppendLine("</style></head><body><main>");

        sb.Append("<div class='top'><div><h1>QualitySessionMeter</h1><div class='muted'>V2 Smart Quality Analysis — session report</div></div><div class='muted'>")
          .Append(H((createdUtc == default ? DateTime.UtcNow : createdUtc).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)))
          .AppendLine("</div></div>");

        sb.AppendLine("<div class='grid'>");
        Metric(sb, "Captured", frames.Count.ToString(CultureInfo.InvariantCulture), "");
        Metric(sb, "Accepted", accepted.Length.ToString(CultureInfo.InvariantCulture), "good");
        Metric(sb, "Rejected", rejected.ToString(CultureInfo.InvariantCulture), rejected > 0 ? "bad" : "good");
        Metric(sb, "Acceptance", acceptance.ToString("0.0", CultureInfo.InvariantCulture) + "%", "accent");
        Metric(sb, "Session quality", meanQuality.ToString("0", CultureInfo.InvariantCulture), "accent");
        Metric(sb, "Mean confidence", meanConfidence.ToString("0", CultureInfo.InvariantCulture) + "%", "accent");
        sb.AppendLine("</div>");

        sb.AppendLine("<section class='card section'><h2>Multichannel timeline</h2>");
        sb.AppendLine("<div class='legend'><span><i class='dot' style='background:#8ab4f8'></i>Quality</span><span><i class='dot' style='background:#c58af9'></i>Confidence</span><span><i class='dot' style='background:#81c995'></i>Guide RMS</span><span><i class='dot' style='background:#fdd663'></i>Stars Δ</span><span><i class='dot' style='background:#f28b82'></i>Background Δ</span></div>");
        sb.AppendLine(BuildTimeline(frames));
        sb.AppendLine("</section>");

        sb.AppendLine("<div class='two section'>");
        sb.AppendLine("<section class='card'><h2>Best accepted frames</h2>");
        Ranking(sb, best);
        sb.AppendLine("</section><section class='card'><h2>Worst accepted frames</h2>");
        Ranking(sb, worst);
        sb.AppendLine("</section></div>");

        sb.AppendLine("<div class='two section'>");
        sb.AppendLine("<section class='card'><h2>Session events</h2>");
        if (events.Count == 0) sb.AppendLine("<div class='muted'>No warning/rejection/error event detected.</div>");
        foreach (var e in events) {
            sb.Append("<div class='event'><b>").Append(H(e.Id)).Append("</b><span>").Append(H(e.TypeText)).Append("</span><span class='optional'>")
              .Append(H(e.PrimaryCause)).Append("</span><span>").Append(e.AffectedFrames).Append(" frames</span><span class='pill'>")
              .Append(H(e.SeverityText)).AppendLine("</span></div>");
        }
        sb.AppendLine("</section><section class='card'><h2>Rejection / warning causes</h2>");
        Causes(sb, frames);
        sb.AppendLine("</section></div>");

        sb.AppendLine("<section class='card section'><h2>Frame history</h2>");
        sb.AppendLine("<div class='tools'><input id='q' placeholder='Search filename / cause / pattern…' oninput='filterRows()'><select id='status' onchange='filterRows()'><option value=''>All statuses</option><option>ACCEPTED</option><option>WARNING</option><option>WOULD REJECT</option><option>REJECTED</option><option>LEARNING</option><option>ERROR</option></select><button onclick=\"sortRows('frame')\">Frame order</button><button onclick=\"sortRows('quality')\">Quality ↓</button></div>");
        sb.AppendLine("<div class='tablewrap'><table id='frames'><thead><tr><th>#</th><th>File</th><th>Status</th><th>Quality</th><th>Confidence</th><th>RMS</th><th>Guide pattern</th><th>Trend</th><th>Stars Δ</th><th>BG Δ</th><th>Cause</th></tr></thead><tbody>");
        foreach (var f in frames) {
            string search = $"{f.FileName} {f.StatusText} {f.ProbableCause} {f.GuidePatternText} {f.TrendText}".ToLowerInvariant();
            sb.Append("<tr data-frame='").Append(f.FrameIndex).Append("' data-quality='").Append(N(f.OverallQuality)).Append("' data-status='").Append(A(f.StatusText)).Append("' data-search='").Append(A(search)).Append("'>")
              .Append("<td>").Append(f.FrameIndex).Append("</td><td>").Append(H(f.FileName)).Append("</td><td>").Append(H(f.StatusText)).Append("</td><td>").Append(H(f.QualityText)).Append("</td><td>").Append(H(f.ConfidenceText)).Append("</td><td>").Append(H(f.GuideRmsText)).Append("</td><td title='").Append(A(f.GuidePatternDetail)).Append("'>").Append(H(f.GuidePatternText)).Append("</td><td>").Append(H(f.TrendText)).Append("</td><td>").Append(H(f.StarDeltaText)).Append("</td><td>").Append(H(f.BackgroundDeltaText)).Append("</td><td>").Append(H(f.ProbableCause)).AppendLine("</td></tr>");
        }
        sb.AppendLine("</tbody></table></div></section>");
        sb.AppendLine("<div class='footer'>Guide pattern, trend and probable-cause labels are diagnostic interpretations of measured data; they are not guaranteed physical-cause identifications.</div>");
        sb.AppendLine("<script>function filterRows(){const q=document.getElementById('q').value.toLowerCase(),s=document.getElementById('status').value;document.querySelectorAll('#frames tbody tr').forEach(r=>r.style.display=(!q||r.dataset.search.includes(q))&&(!s||r.dataset.status===s)?'':'none')}function sortRows(k){const b=document.querySelector('#frames tbody');[...b.rows].sort((a,c)=>k==='quality'?Number(c.dataset.quality)-Number(a.dataset.quality):Number(a.dataset.frame)-Number(c.dataset.frame)).forEach(r=>b.appendChild(r))}</script>");
        sb.AppendLine("</main></body></html>");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string BuildTimeline(IReadOnlyList<FrameQualityResult> frames) {
        const int width = 1320, height = 390, left = 46, right = 14, top = 22, band = 105, gap = 16;
        int plotW = width - left - right;
        var sb = new StringBuilder();
        sb.Append($"<svg viewBox='0 0 {width} {height}' style='min-width:820px;width:100%;height:auto;background:#101318;border-radius:8px'>");
        Band(sb, left, top, plotW, band, "Quality / Confidence");
        Band(sb, left, top + band + gap, plotW, band, "Guide RMS");
        Band(sb, left, top + 2 * (band + gap), plotW, band, "Stars Δ / Background Δ");
        if (frames.Count > 0) {
            double guideMax = Math.Max(2.0, frames.Where(x => Finite(x.GuideRmsArcsec)).Select(x => x.GuideRmsArcsec).DefaultIfEmpty(2).Max() * 1.1);
            Line(sb, frames, left, top, plotW, band, x => x.OverallQuality, 0, 100, "#8ab4f8", false);
            Line(sb, frames, left, top, plotW, band, x => x.ConfidenceScore, 0, 100, "#c58af9", true);
            Line(sb, frames, left, top + band + gap, plotW, band, x => x.GuideRmsArcsec, 0, guideMax, "#81c995", false);
            Line(sb, frames, left, top + 2 * (band + gap), plotW, band, x => x.StarDeviationPercent, -50, 50, "#fdd663", false);
            Line(sb, frames, left, top + 2 * (band + gap), plotW, band, x => x.BackgroundDeviationPercent, -50, 50, "#f28b82", false);
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Band(StringBuilder sb, int left, int top, int width, int height, string title) {
        sb.Append($"<rect x='{left}' y='{top}' width='{width}' height='{height}' fill='#12161c' stroke='#2a3039'/><line x1='{left}' y1='{top + height / 2}' x2='{left + width}' y2='{top + height / 2}' stroke='#30363d'/><text x='{left + 6}' y='{top + 14}' fill='#9aa0a6' font-size='11' font-family='Segoe UI'>{H(title)}</text>");
    }

    private static void Line(StringBuilder sb, IReadOnlyList<FrameQualityResult> frames, double left, double top, double width, double height, Func<FrameQualityResult,double> selector, double min, double max, string color, bool dash) {
        var segment = new List<string>();
        void Flush() { if (segment.Count > 0) { sb.Append($"<polyline points='{string.Join(" ", segment)}' fill='none' stroke='{color}' stroke-width='2' {(dash ? "stroke-dasharray='5 4'" : "")}/>"); segment.Clear(); } }
        for (int i = 0; i < frames.Count; i++) {
            double v = selector(frames[i]);
            if (!Finite(v)) { Flush(); continue; }
            v = Math.Clamp(v, min, max);
            double x = frames.Count == 1 ? left + width / 2 : left + i * width / (frames.Count - 1.0);
            double y = top + height * (1 - (v - min) / Math.Max(0.000001, max - min));
            segment.Add($"{x:0.##},{y:0.##}");
        }
        Flush();
    }

    private static void Ranking(StringBuilder sb, IReadOnlyList<FrameQualityResult> frames) {
        if (frames.Count == 0) { sb.AppendLine("<div class='muted'>No accepted frame available.</div>"); return; }
        foreach (var f in frames) sb.Append("<div class='rank'><span>#").Append(f.FrameIndex).Append("</span><span>").Append(H(f.FileName)).Append("</span><b>").Append(H(f.QualityText)).Append(" Q</b><span class='muted'>").Append(H(f.ConfidenceText)).AppendLine(" conf</span></div>");
    }

    private static void Causes(StringBuilder sb, IReadOnlyList<FrameQualityResult> frames) {
        var groups = frames.Where(x => x.Status is FrameStatus.Warning or FrameStatus.Rejected or FrameStatus.Error)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.ProbableCause) ? "UNKNOWN" : x.ProbableCause)
            .Select(x => (Name: x.Key, Count: x.Count())).OrderByDescending(x => x.Count).ToArray();
        if (groups.Length == 0) { sb.AppendLine("<div class='muted'>No abnormal-frame causes.</div>"); return; }
        int max = groups.Max(x => x.Count);
        foreach (var g in groups) sb.Append("<div class='cause'><span>").Append(H(g.Name)).Append("</span><div class='bar'><div class='fill' style='width:").Append((100.0 * g.Count / max).ToString("0.0", CultureInfo.InvariantCulture)).Append("%'></div></div><b>").Append(g.Count).AppendLine("</b></div>");
    }

    private static void Metric(StringBuilder sb, string title, string value, string cls) => sb.Append("<div class='card'><h3>").Append(H(title)).Append("</h3><div class='big ").Append(cls).Append("'>").Append(H(value)).AppendLine("</div></div>");
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    private static string N(double v) => Finite(v) ? v.ToString("0.####", CultureInfo.InvariantCulture) : "";
    private static string H(string? v) => WebUtility.HtmlEncode(v ?? "");
    private static string A(string? v) => H(v).Replace("'", "&#39;");
}
