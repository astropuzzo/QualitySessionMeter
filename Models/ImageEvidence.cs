using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Models;

public sealed class ImageEvidence {
    public bool Attempted { get; init; }
    public bool Available { get; init; }
    public int Stars { get; init; }
    public double AxisRatio { get; init; } = double.NaN;
    public double ElongatedFraction { get; init; } = double.NaN;
    public double TailStrength { get; init; } = double.NaN;
    public double DoublePeakStrength { get; init; } = double.NaN;
    public double MedianFlux { get; init; } = double.NaN;
    public double ElapsedMilliseconds { get; init; }
    public StarShapeLimits Limits { get; init; } = new();
    public string PreviewPngBase64 { get; init; } = "";
    public string Detail { get; init; } = "Stellar second pass not run: no guide rejection to verify.";
    public double Eccentricity => AxisRatio >= 1 ? Math.Sqrt(1 - 1 / (AxisRatio * AxisRatio)) : double.NaN;
    public bool Compromised => Available && ((Eccentricity >= Limits.MaxEccentricity && ElongatedFraction >= Limits.DeformedFraction)
        || TailStrength >= Limits.MaxTailPercent / 100 || DoublePeakStrength >= Limits.MaxDoublePeakPercent / 100);
    public string Summary => !Available ? "NOT VERIFIED" : Compromised ? "SHAPE LIMIT EXCEEDED" : "SHAPES WITHIN LIMITS";
    public string PreviewCaption => !Available ? "" : "Left: median of measured stars. Right: six individual stars. Same display stretch; tails are enhanced. Measurements use linear pixels.";
    [JsonIgnore]
    public BitmapSource Preview {
        get {
            if (string.IsNullOrEmpty(PreviewPngBase64)) return null;
            try {
                using var stream = new MemoryStream(Convert.FromBase64String(PreviewPngBase64));
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            } catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException) { return null; }
        }
    }
    public double Quality => !Available ? double.NaN : Math.Clamp(100 - Math.Max(
        Math.Max(0, AxisRatio - 1.10) * 180, Math.Max(0, TailStrength - 0.003) * 2200), 0, 100);
}

public sealed record ImageSample(float[] Pixels, int Width, int Height);

public sealed record StarShapeLimits {
    public int TargetStars { get; init; } = 100;
    public int MinimumStars { get; init; } = 20;
    public double MaxEccentricity { get; init; } = 0.60;
    public double DeformedFraction { get; init; } = 0.60;
    public double MaxTailPercent { get; init; } = 2;
    public double MaxDoublePeakPercent { get; init; } = 8;
}
