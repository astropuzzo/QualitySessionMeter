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

    public void Reset() {
        lock (sync) {
            results.Clear();
            sessionFolder = null;
            sessionCreatedUtc = default;
        }
    }

    public async Task AppendAsync(FrameQualityResult result) {
        EnsureSession();
        lock (sync) results.Add(result);

        await ioLock.WaitAsync();
        try {
            await AppendCsvAsync(result);
            await WriteSummaryAsync();
            await WriteSvgAsync();
        } finally {
            ioLock.Release();
        }
    }

    private void EnsureSession() {
        lock (sync) {
            if (!string.IsNullOrWhiteSpace(sessionFolder)) return;

            var baseDir = string.IsNullOrWhiteSpace(baseDirectoryOverride)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA",
                    "QualitySessionMeter",
                    "Sessions")
                : baseDirectoryOverride;

            Directory.CreateDirectory(baseDir);

            sessionCreatedUtc = DateTime.UtcNow;
            string stem = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff", CultureInfo.InvariantCulture);
            string candidate = Path.Combine(baseDir, stem);
            int suffix = 1;
            while (Directory.Exists(candidate)) {
                candidate = Path.Combine(baseDir, $"{stem}_{suffix++}");
            }

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
            await writer.WriteLineAsync("Frame,TimestampUtc,Filename,Target,Filter,Exposure,Gain,BinX,BinY,Stars,StarsBaseline,StarsDeltaPct,Background,BackgroundBaseline,BackgroundDeltaPct,GuideSamples,GuideRMS,MaxGuideExcursion,SustainedExcursionSeconds,GuidingQuality,StabilityQuality,TransparencyQuality,BackgroundQuality,OverallQuality,Status,RejectReasons,ProbableCause,ErrorMessage,MonitorOnly");
        }

        string[] fields = {
            r.FrameIndex.ToString(CultureInfo.InvariantCulture),
            r.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            Csv(r.FinalPath ?? r.OriginalPath),
            Csv(r.Target),
            Csv(r.Filter),
            Num(r.ExposureSeconds),
            r.Gain.ToString(CultureInfo.InvariantCulture),
            r.BinX.ToString(CultureInfo.InvariantCulture),
            r.BinY.ToString(CultureInfo.InvariantCulture),
            r.StarCount.ToString(CultureInfo.InvariantCulture),
            Num(r.StarBaseline),
            Num(r.StarDeviationPercent),
            Num(r.BackgroundMedian),
            Num(r.BackgroundBaseline),
            Num(r.BackgroundDeviationPercent),
            r.GuideSamples.ToString(CultureInfo.InvariantCulture),
            Num(r.GuideRmsArcsec),
            Num(r.MaxGuideExcursionArcsec),
            Num(r.SustainedGuideExcursionSeconds),
            Num(r.GuidingQuality),
            Num(r.StabilityQuality),
            Num(r.TransparencyQuality),
            Num(r.BackgroundQuality),
            Num(r.OverallQuality),
            r.Status.ToString(),
            Csv(r.ReasonText),
            Csv(r.ProbableCause),
            Csv(r.ErrorMessage),
            r.MonitorOnly ? "true" : "false"
        };
        await writer.WriteLineAsync(string.Join(",", fields));
    }

    private async Task WriteSummaryAsync() {
        FrameQualityResult[] copy;
        DateTime created;
        lock (sync) {
            copy = results.ToArray();
            created = sessionCreatedUtc;
        }

        int usable = copy.Count(x => x.IsUsable);
        int rejected = copy.Count(x => x.Status == FrameStatus.Rejected);
        int learning = copy.Count(x => x.Status == FrameStatus.Learning);
        int errors = copy.Count(x => x.Status == FrameStatus.Error);
        double acceptedQuality = copy.Where(x => x.IsUsable).Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
        double acceptanceRate = usable + rejected == 0 ? 0 : usable * 100.0 / (usable + rejected);

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
            frames = copy
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FiniteDoubleJsonConverter());
        await File.WriteAllTextAsync(
            Path.Combine(SessionFolder, "session.json"),
            JsonSerializer.Serialize(summary, options),
            new UTF8Encoding(false));
    }

    private async Task WriteSvgAsync() {
        FrameQualityResult[] copy;
        lock (sync) copy = results.ToArray();
        if (copy.Length == 0) return;

        const int width = 1200;
        const int height = 420;
        const int left = 55;
        const int right = 20;
        const int top = 25;
        const int bottom = 45;
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

    private static string Num(double value) => double.IsNaN(value) || double.IsInfinity(value)
        ? ""
        : value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Num(double? value) => !value.HasValue ? "" : Num(value.Value);

    private static string Csv(string value) {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Keeps session.json standards-compliant: unavailable numeric metrics are written as JSON null
    /// instead of non-standard NaN/Infinity tokens. Null reads back as NaN if future tooling deserializes
    /// directly into the runtime model.
    /// </summary>
    private sealed class FiniteDoubleJsonConverter : JsonConverter<double> {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.Null) return double.NaN;
            if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble();
            throw new JsonException($"Unexpected token {reader.TokenType} for double value.");
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) {
            if (double.IsNaN(value) || double.IsInfinity(value)) {
                writer.WriteNullValue();
            } else {
                writer.WriteNumberValue(value);
            }
        }
    }
}
