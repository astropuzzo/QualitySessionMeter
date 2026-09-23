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
    private readonly Func<FrameQualityResult, string> directoryResolver;
    private string sessionFolder;
    private DateTime sessionCreatedUtc;

    public SessionStore(string baseDirectoryOverride = null, Func<FrameQualityResult, string> directoryResolver = null) {
        this.baseDirectoryOverride = baseDirectoryOverride;
        this.directoryResolver = directoryResolver;
    }

    public string StorageWarning { get; private set; } = "";

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
            StorageWarning = "";
        }
        eventGrouping.Reset();
    }

    public async Task AppendAsync(FrameQualityResult result) {
        lock (sync) results.Add(result);
        eventGrouping.Add(result);

        await ioLock.WaitAsync();
        try {
            EnsureSession(result);
            await AppendCsvAsync(result);
            await WriteSummaryAsync();
            await WriteEventsCsvAsync();
            await WriteSvgAsync();
            await WriteHtmlReportAsync();
        } finally {
            ioLock.Release();
        }
    }

    /// <summary>
    /// Replaces earlier results after a retrospective reference decision. Every artifact, including
    /// frames.csv, is rewritten so the files never disagree with the live session.
    /// </summary>
    public async Task ReplaceAsync(IReadOnlyCollection<FrameQualityResult> updated) {
        if (updated == null || updated.Count == 0) return;
        FrameQualityResult[] copy;
        lock (sync) {
            foreach (var frame in updated) {
                int index = results.FindIndex(x => x.FrameIndex == frame.FrameIndex);
                if (index >= 0) results[index] = frame;
            }
            copy = results.ToArray();
        }
        eventGrouping.Reset();
        foreach (var frame in copy) eventGrouping.Add(frame);

        if (string.IsNullOrWhiteSpace(SessionFolder)) return;
        await ioLock.WaitAsync();
        try {
            await RewriteCsvAsync(copy);
            await WriteSummaryAsync();
            await WriteEventsCsvAsync();
            await WriteSvgAsync();
            await WriteHtmlReportAsync();
        } finally {
            ioLock.Release();
        }
    }

    public async Task RefreshArtifactsAsync() {
        if (string.IsNullOrWhiteSpace(SessionFolder)) return;
        await ioLock.WaitAsync();
        try {
            await WriteSummaryAsync();
            await WriteEventsCsvAsync();
            await WriteSvgAsync();
            await WriteHtmlReportAsync();
        } finally {
            ioLock.Release();
        }
    }

    private void EnsureSession(FrameQualityResult result) {
        lock (sync) {
            if (!string.IsNullOrWhiteSpace(sessionFolder)) return;

            var baseDir = baseDirectoryOverride;
            try {
                if (string.IsNullOrWhiteSpace(baseDir)) baseDir = directoryResolver?.Invoke(result) ?? SessionPathResolver.DefaultDirectory;
                Directory.CreateDirectory(baseDir);
                // Test actual write access; an existing read-only directory can pass CreateDirectory.
                string probe = Path.Combine(baseDir, ".qsm-write-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            } catch (Exception ex) when (directoryResolver != null && ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
                StorageWarning = "Selected session folder unavailable. Reports are saved in the default local folder. " + ex.Message;
                baseDir = SessionPathResolver.DefaultDirectory;
                Directory.CreateDirectory(baseDir);
            }
            sessionCreatedUtc = DateTime.UtcNow;
            string stem = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff", CultureInfo.InvariantCulture);
            string candidate = Path.Combine(baseDir, stem);
            int suffix = 1;
            while (Directory.Exists(candidate)) candidate = Path.Combine(baseDir, $"{stem}_{suffix++}");
            Directory.CreateDirectory(candidate);
            sessionFolder = candidate;
        }
    }

    private const string CsvHeader =
                "Frame,TimestampUtc,Filename,Target,Filter,Exposure,Gain,BinX,BinY," +
                "SourceKind,SequenceTitle,QsmControlled,ProvenanceFrozen,FileActionEligible," +
                "Stars,StarsBaseline,StarsDeltaPct,StarTrendUsable,StarTrendExpected,StarTrendPctPerFrame,StarTrendR2,StarTrendResidualPct,StarTrendKind," +
                "Background,BackgroundBaseline,BackgroundDeltaPct,BackgroundTrendUsable,BackgroundTrendExpected,BackgroundTrendPctPerFrame,BackgroundTrendR2,BackgroundTrendResidualPct,BackgroundTrendKind," +
                "GuideSamples,GuideRMS,MaxGuideExcursion,SustainedExcursionSeconds,GuidePattern,GuidePatternConfidence,GuideDriftArcsecPerMinute,GuideOscillationRangeArcsec,GuidePatternSignChanges,GuidePatternBurstiness,GuidePatternDetail," +
                "GuidingQuality,StabilityQuality,TransparencyQuality,BackgroundQuality,OverallQuality," +
                "Confidence,ConfidenceLabel,ConfidenceDataCompleteness,ConfidenceBaselineMaturity,ConfidenceThresholdSeparation,ConfidenceAgreement,ConfidenceReason," +
                "PredictiveWarning,PredictiveConfidence,PredictiveChannel,PredictiveMessage,PredictiveFramesToThreshold," +
                "EnvironmentAvailable,CloudCover,Humidity,WindSpeed,WindGust,SkyQuality,AmbientTemperature,DewPoint,EnvironmentalHint," +
                "Status,RejectReasons,ProbableCause,ErrorMessage,MonitorOnly,AssessmentVersion,ReviewReasons,DecisionSummary,ImageEvidenceAvailable,ImageStars,StarAxisRatio,StarTailStrength,ThresholdsUsed,GuideFalsePositive,StarEccentricity,StarDoublePeak,StarMedianFlux,ShapeAnalysisMs,StarCountFalsePositive,ExtendedAvailable,VerifiedRegions,RemotePeakStrength,RemotePeakSupport,SearchRadiusArcsec,RelativeStellarFlux,MatchedStars,StellarReferenceFrames,StarFwhmPixels,StarFwhmRatio,StellarReferenceAgeMinutes,SessionRelativeStellarFlux,StellarSignalTrendUsed,StellarSignalTrendPercentPerHour,MinimumSessionSignalPercent";

    private async Task AppendCsvAsync(FrameQualityResult r) {
        var path = Path.Combine(SessionFolder, "frames.csv");
        bool exists = File.Exists(path);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        if (!exists) await writer.WriteLineAsync(CsvHeader);
        await writer.WriteLineAsync(CsvLine(r));
    }

    private async Task RewriteCsvAsync(IEnumerable<FrameQualityResult> frames) {
        var path = Path.Combine(SessionFolder, "frames.csv");
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync(CsvHeader);
        foreach (var frame in frames) await writer.WriteLineAsync(CsvLine(frame));
    }

    private static string CsvLine(FrameQualityResult r) {
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
            r.Status.ToString(), Csv(string.Join(", ", r.RejectReasons)), Csv(r.ProbableCause), Csv(r.ErrorMessage), Bool(r.MonitorOnly), Csv(r.AssessmentVersion), Csv(string.Join(", ", r.ReviewReasons)),
            Csv(r.DecisionSummary), Bool(r.ImageEvidence.Available), r.ImageEvidence.Stars.ToString(CultureInfo.InvariantCulture),
            Num(r.ImageEvidence.AxisRatio), Num(r.ImageEvidence.TailStrength), Csv(r.ThresholdsUsed), Bool(r.GuideFalsePositive), Num(r.ImageEvidence.Eccentricity), Num(r.ImageEvidence.DoublePeakStrength), Num(r.ImageEvidence.MedianFlux), Num(r.ImageEvidence.ElapsedMilliseconds),
            Bool(r.StarCountFalsePositive),Bool(r.ImageEvidence.ExtendedAvailable),Num(r.ImageEvidence.VerifiedRegions),Num(r.ImageEvidence.RemotePeakStrength),Num(r.ImageEvidence.RemotePeakSupport),
            Num(r.ImageEvidence.SearchRadiusArcsec),Num(r.ImageEvidence.RelativeFlux),Num(r.ImageEvidence.MatchedStars),Num(r.ImageEvidence.ReferenceFrames),Num(r.ImageEvidence.FwhmPixels),Num(r.ImageEvidence.FwhmRatio),Num(r.ImageEvidence.ReferenceAgeMinutes),Num(r.ImageEvidence.SessionRelativeFlux),Bool(r.ImageEvidence.SignalTrendUsed),Num(r.ImageEvidence.SignalTrendPercentPerHour),Num(r.ImageEvidence.MinimumSessionSignalPercent)
        };
        return string.Join(",", fields);
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

}
