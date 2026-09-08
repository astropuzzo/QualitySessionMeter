using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class SessionStore {
    private readonly object sync = new();
    private readonly List<FrameQualityResult> results = new();
    private readonly EventGroupingEngine eventGrouping = new();
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private readonly string baseDirectoryOverride;
    private string sessionFolder;
    private DateTime sessionCreatedUtc;

    public SessionStore(string baseDirectoryOverride = null) {
        this.baseDirectoryOverride = baseDirectoryOverride;
    }

    public string SessionFolder {
        get { lock (sync) return sessionFolder ?? ""; }
    }

    public IReadOnlyList<FrameQualityResult> Results {
        get { lock (sync) return results.ToArray(); }
    }

    public IReadOnlyList<SessionEvent> Events => eventGrouping.Events;

    public void Reset() {
        lock (sync) {
            results.Clear();
            sessionFolder = null;
            sessionCreatedUtc = default;
        }
        eventGrouping.Reset();
    }

    public async Task AppendAsync(FrameQualityResult result) {
        EnsureSession();
        lock (sync) results.Add(result);
        eventGrouping.Add(result);

        await ioLock.WaitAsync();
        try {
            await AppendCsvAsync(result);
            await WriteSummaryAsync();
            await WriteEventsCsvAsync();
            await WriteSvgAsync();
            await WriteHtmlReportAsync();
        } finally {
            ioLock.Release();
        }
    }

    private void EnsureSession() {
        lock (sync) {
            if (!string.IsNullOrWhiteSpace(sessionFolder)) return;

            var baseDir = string.IsNullOrWhiteSpace(baseDirectoryOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "QualitySessionMeter", "Sessions")
                : baseDirectoryOverride;

            Directory.CreateDirectory(baseDir);
            sessionCreatedUtc = DateTime.UtcNow;
            string stem = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff", CultureInfo.InvariantCulture);
            string candidate = Path.Combine(baseDir, stem);
            int suffix = 1;
            while (Directory.Exists(candidate)) candidate = Path.Combine(baseDir, $"{stem}_{suffix++}");
            Directory.CreateDirectory(candidate);
            sessionFolder = candidate;
        }
    }

    private async Task AppendCsvAsync(FrameQualityResult r) {
        var path = Path.Combine(SessionFolder, "frames.csv");
        bool exists = File.Exists(path);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        if (!exists) {
            await writer.WriteLineAsync(
                "Frame,TimestampUtc,Filename,Target,Filter,Exposure,Gain,BinX,BinY," +
                "SourceKind,SequenceTitle,QsmControlled,ProvenanceFrozen,FileActionEligible," +
                "Stars,StarsBaseline,StarsDeltaPct,StarTrendUsable,StarTrendExpected,StarTrendPctPerFrame,StarTrendR2,StarTrendResidualPct,StarTrendKind," +
                "Background,BackgroundBaseline,BackgroundDeltaPct,BackgroundTrendUsable,BackgroundTrendExpected,BackgroundTrendPctPerFrame,BackgroundTrendR2,BackgroundTrendResidualPct,BackgroundTrendKind," +
                "GuideSamples,GuideRMS,MaxGuideExcursion,SustainedExcursionSeconds,GuidePattern,GuidePatternConfidence,GuideDriftArcsecPerMinute,GuideOscillationRangeArcsec,GuidePatternSignChanges,GuidePatternBurstiness,GuidePatternDetail," +
                "GuidingQuality,StabilityQuality,TransparencyQuality,BackgroundQuality,OverallQuality," +
                "Confidence,ConfidenceLabel,ConfidenceDataCompleteness,ConfidenceBaselineMaturity,ConfidenceThresholdSeparation,ConfidenceAgreement,ConfidenceReason," +
                "PredictiveWarning,PredictiveConfidence,PredictiveChannel,PredictiveMessage,PredictiveFramesToThreshold," +
                "EnvironmentAvailable,CloudCover,Humidity,WindSpeed,WindGust,SkyQuality,AmbientTemperature,DewPoint,EnvironmentalHint," +
                "Status,RejectReasons,ProbableCause,ErrorMessage,MonitorOnly");
        }

        string[] fields = {
            r.FrameIndex.ToString(CultureInfo.InvariantCulture),
            r.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            Csv(r.FinalPath ?? r.OriginalPath), Csv(r.Target), Csv(r.Filter), Num(r.ExposureSeconds),
            r.Gain.ToString(CultureInfo.InvariantCulture), r.BinX.ToString(CultureInfo.InvariantCulture), r.BinY.ToString(CultureInfo.InvariantCulture),
            r.SourceKind.ToString(), Csv(r.SequenceTitle), Bool(r.QsmControlled), Bool(r.ProvenanceFrozen), Bool(r.FileActionEligible),

            r.StarCount.ToString(CultureInfo.InvariantCulture), Num(r.StarBaseline), Num(r.StarDeviationPercent), Bool(r.StarTrendUsable),
            Num(r.StarTrendExpected), Num(r.StarTrendPercentPerFrame), Num(r.StarTrendR2), Num(r.StarTrendResidualPercent), r.StarTrendKind.ToString(),

            Num(r.BackgroundMedian), Num(r.BackgroundBaseline), Num(r.BackgroundDeviationPercent), Bool(r.BackgroundTrendUsable),
            Num(r.BackgroundTrendExpected), Num(r.BackgroundTrendPercentPerFrame), Num(r.BackgroundTrendR2), Num(r.BackgroundTrendResidualPercent), r.BackgroundTrendKind.ToString(),

            r.GuideSamples.ToString(CultureInfo.InvariantCulture), Num(r.GuideRmsArcsec), Num(r.MaxGuideExcursionArcsec), Num(r.SustainedGuideExcursionSeconds),
            r.GuidePattern.ToString(), Num(r.GuidePatternConfidence), Num(r.GuideDriftArcsecPerMinute), Num(r.GuideOscillationRangeArcsec),
            r.GuidePatternSignChanges.ToString(CultureInfo.InvariantCulture), Num(r.GuidePatternBurstiness), Csv(r.GuidePatternDetail),

            Num(r.GuidingQuality), Num(r.StabilityQuality), Num(r.TransparencyQuality), Num(r.BackgroundQuality), Num(r.OverallQuality),
            Num(r.ConfidenceScore), Csv(r.ConfidenceLabel), Num(r.ConfidenceDataCompleteness), Num(r.ConfidenceBaselineMaturity),
            Num(r.ConfidenceThresholdSeparation), Num(r.ConfidenceAgreement), Csv(r.ConfidenceReason),
            Bool(r.PredictiveWarning), Num(r.PredictiveConfidence), Csv(r.PredictiveChannel), Csv(r.PredictiveMessage), Num(r.PredictiveFramesToThreshold),
            Bool(r.EnvironmentAvailable), Num(r.CloudCover), Num(r.Humidity), Num(r.WindSpeed), Num(r.WindGust), Num(r.SkyQuality),
            Num(r.AmbientTemperature), Num(r.DewPoint), Csv(r.EnvironmentalHint),
            r.Status.ToString(), Csv(r.ReasonText), Csv(r.ProbableCause), Csv(r.ErrorMessage), Bool(r.MonitorOnly)
        };
        await writer.WriteLineAsync(string.Join(",", fields));
    }

    private async Task WriteSummaryAsync() {
        FrameQualityResult[] copy;
        DateTime created;
        lock (sync) { copy = results.ToArray(); created = sessionCreatedUtc; }
        var eventCopy = eventGrouping.Events;

        int usable = copy.Count(x => x.IsUsable);
        int rejected = copy.Count(x => x.Status == FrameStatus.Rejected);
        int learning = copy.Count(x => x.Status == FrameStatus.Learning);
        int errors = copy.Count(x => x.Status == FrameStatus.Error);
        double acceptedQuality = copy.Where(x => x.IsUsable).Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
        double acceptedConfidence = copy.Where(x => x.IsUsable && Finite(x.ConfidenceScore)).Select(x => x.ConfidenceScore).DefaultIfEmpty(0).Average();
        double acceptanceRate = usable + rejected == 0 ? 0 : usable * 100.0 / (usable + rejected);
        var acceptedOnly = copy.Where(x => x.Status == FrameStatus.Accepted).ToArray();
        var bestAccepted = acceptedOnly.OrderByDescending(x => x.OverallQuality).ThenByDescending(x => x.ConfidenceScore).Take(10).Select(RankProjection).ToArray();
        var worstAccepted = acceptedOnly.OrderBy(x => x.OverallQuality).ThenBy(x => x.ConfidenceScore).Take(10).Select(RankProjection).ToArray();

        var summary = new {
            createdUtc = created,
            updatedUtc = DateTime.UtcNow,
            captured = copy.Length,
            usable,
            rejected,
            learning,
            errors,
            acceptanceRate,
            acceptedQuality,
            acceptedConfidence,
            eventCount = eventCopy.Count,
            bestAccepted,
            worstAccepted,
            events = eventCopy,
            frames = copy
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FiniteDoubleJsonConverter());
        await File.WriteAllTextAsync(Path.Combine(SessionFolder, "session.json"), JsonSerializer.Serialize(summary, options), new UTF8Encoding(false));
    }

    private async Task WriteEventsCsvAsync() {
        var path = Path.Combine(SessionFolder, "events.csv");
        var events = eventGrouping.Events;
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("Event,Type,StartUtc,EndUtc,FirstFrame,LastFrame,AffectedFrames,Rejected,Warning,Errors,MeanConfidence,PeakConfidence,Severity,PrimaryCause,Open,FrameIndices");
        foreach (var e in events) {
            string[] fields = {
                Csv(e.Id), e.Type.ToString(), e.StartUtc.ToString("O", CultureInfo.InvariantCulture), e.EndUtc.ToString("O", CultureInfo.InvariantCulture),
                e.FirstFrameIndex.ToString(CultureInfo.InvariantCulture), e.LastFrameIndex.ToString(CultureInfo.InvariantCulture),
                e.AffectedFrames.ToString(CultureInfo.InvariantCulture), e.RejectedFrames.ToString(CultureInfo.InvariantCulture),
                e.WarningFrames.ToString(CultureInfo.InvariantCulture), e.ErrorFrames.ToString(CultureInfo.InvariantCulture),
                Num(e.MeanConfidence), Num(e.PeakConfidence), Csv(e.SeverityText), Csv(e.PrimaryCause), Bool(e.IsOpen), Csv(string.Join(";", e.FrameIndices))
            };
            await writer.WriteLineAsync(string.Join(",", fields));
        }
    }

    private async Task WriteHtmlReportAsync() {
        FrameQualityResult[] copy;
        DateTime created;
        lock (sync) { copy = results.ToArray(); created = sessionCreatedUtc; }
        await HtmlReportWriter.WriteAsync(Path.Combine(SessionFolder, "report.html"), copy, eventGrouping.Events, created);
    }

    private async Task WriteSvgAsync() {
        FrameQualityResult[] copy;
        lock (sync) copy = results.ToArray();
        if (copy.Length == 0) return;

        const int width = 1200, height = 420, left = 55, right = 20, top = 25, bottom = 45;
        double plotW = width - left - right;
        double plotH = height - top - bottom;
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#101318\"/>");
        sb.AppendLine("<text x=\"55\" y=\"18\" fill=\"#f1f3f4\" font-family=\"Segoe UI, sans-serif\" font-size=\"14\">QualitySessionMeter — Overall Quality</text>");
        foreach (var value in new[] { 0, 25, 50, 65, 80, 90, 100 }) {
            double y = top + plotH * (1.0 - value / 100.0);
            sb.AppendLine($"<line x1=\"{left}\" y1=\"{y:0.##}\" x2=\"{width-right}\" y2=\"{y:0.##}\" stroke=\"#30363d\" stroke-width=\"1\"/>");
            sb.AppendLine($"<text x=\"8\" y=\"{y+4:0.##}\" fill=\"#9aa0a6\" font-family=\"Segoe UI, sans-serif\" font-size=\"11\">{value}</text>");
        }

        var points = new List<string>();
        for (int i = 0; i < copy.Length; i++) {
            double x = copy.Length == 1 ? left : left + plotW * i / (copy.Length - 1.0);
            double y = top + plotH * (1.0 - Math.Clamp(copy[i].OverallQuality, 0, 100) / 100.0);
            points.Add($"{x:0.##},{y:0.##}");
        }
        sb.AppendLine($"<polyline fill=\"none\" stroke=\"#8ab4f8\" stroke-width=\"2\" points=\"{string.Join(" ", points)}\"/>");

        for (int i = 0; i < copy.Length; i++) {
            double x = copy.Length == 1 ? left : left + plotW * i / (copy.Length - 1.0);
            double y = top + plotH * (1.0 - Math.Clamp(copy[i].OverallQuality, 0, 100) / 100.0);
            string color = copy[i].Status == FrameStatus.Rejected ? "#f28b82"
                : copy[i].Status == FrameStatus.Warning ? "#fdd663"
                : copy[i].Status == FrameStatus.Learning ? "#bdc1c6"
                : copy[i].Status == FrameStatus.Error ? "#ff7878"
                : "#81c995";
            sb.AppendLine($"<circle cx=\"{x:0.##}\" cy=\"{y:0.##}\" r=\"4\" fill=\"{color}\"/>");
        }
        sb.AppendLine($"<text x=\"{left}\" y=\"{height-12}\" fill=\"#9aa0a6\" font-family=\"Segoe UI, sans-serif\" font-size=\"11\">Frame 1</text>");
        sb.AppendLine($"<text x=\"{width-right-90}\" y=\"{height-12}\" fill=\"#9aa0a6\" font-family=\"Segoe UI, sans-serif\" font-size=\"11\">Frame {copy.Length}</text>");
        sb.AppendLine("</svg>");
        await File.WriteAllTextAsync(Path.Combine(SessionFolder, "quality.svg"), sb.ToString(), new UTF8Encoding(false));
    }

    private static object RankProjection(FrameQualityResult r) => new { r.FrameIndex, r.FileName, r.OverallQuality, r.ConfidenceScore, r.Filter, r.Target };
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static string Num(double value) => Finite(value) ? value.ToString("0.####", CultureInfo.InvariantCulture) : "";
    private static string Num(double? value) => value.HasValue ? Num(value.Value) : "";
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Csv(string value) { value ??= ""; return "\"" + value.Replace("\"", "\"\"") + "\""; }

    private sealed class FiniteDoubleJsonConverter : JsonConverter<double> {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.Null) return double.NaN;
            if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble();
            throw new JsonException($"Unexpected token {reader.TokenType} for double value.");
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) {
            if (!Finite(value)) writer.WriteNullValue(); else writer.WriteNumberValue(value);
        }
    }
}
