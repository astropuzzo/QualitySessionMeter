using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Models;

public sealed record ImageEvidence {
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
    public string Detail { get; init; } = "Stellar analysis not run.";
    public double FwhmPixels { get; init; } = double.NaN;
    public double RelativeFlux { get; init; } = double.NaN;
    public int MatchedStars { get; init; }
    public int ReferenceFrames { get; init; }
    public double FwhmRatio { get; init; } = double.NaN;
    public int VerifiedRegions { get; init; }
    public int CompromisedRegions { get; init; }
    public double WorstRegionEccentricity { get; init; } = double.NaN;
    public bool ExtendedAttempted { get; init; }
    public bool ExtendedAvailable { get; init; }
    public bool GuideSearchCovered { get; init; }
    public double SearchRadiusArcsec { get; init; } = double.NaN;
    public double RemotePeakStrength { get; init; } = double.NaN;
    public double RemotePeakSupport { get; init; } = double.NaN;
    public double RemotePeakDistancePixels { get; init; } = double.NaN;
    public string ExtendedPreviewPngBase64 { get; init; } = "";
    [JsonIgnore] public StarObservation[] Catalog { get; init; } = Array.Empty<StarObservation>();
    public bool RemotePeakConfirmed => ExtendedAvailable && RemotePeakStrength >= Limits.MaxRemotePeakPercent / 100 && RemotePeakSupport >= Limits.DeformedFraction;
    public bool PhotometryAvailable => ReferenceFrames >= 2 && MatchedStars >= 20 && double.IsFinite(RelativeFlux) && RelativeFlux > 0;
    public double Eccentricity => AxisRatio >= 1 ? Math.Sqrt(1 - 1 / (AxisRatio * AxisRatio)) : double.NaN;
    public bool Compromised => Available && ((Eccentricity >= Limits.MaxEccentricity && ElongatedFraction >= Limits.DeformedFraction)
        || TailStrength >= Limits.MaxTailPercent / 100 || DoublePeakStrength >= Limits.MaxDoublePeakPercent / 100 || RemotePeakConfirmed);
    public bool HasRescueMargin => Available && !Compromised && Eccentricity < Limits.RescueMaxEccentricity;
    public bool HasExtendedRescueEvidence => HasRescueMargin && ExtendedAvailable && GuideSearchCovered && VerifiedRegions >= 4 && CompromisedRegions == 0
        && WorstRegionEccentricity < Limits.MaxEccentricity && TailStrength < Limits.MaxTailPercent / 200
        && DoublePeakStrength < Limits.MaxDoublePeakPercent / 200;
    public string Summary => !Available ? "NOT VERIFIED" : Compromised ? "SHAPE LIMIT EXCEEDED" : HasRescueMargin ? "SHAPES WITHIN RESCUE LIMITS" : "BORDERLINE STELLAR SHAPE";
    public string ExtendedCaption => ExtendedAvailable ? "Extended profile · enhanced contrast" : "";
    public string PreviewCaption => !Available ? "" : "Median profile (left) · 6 sample stars (right) · enhanced contrast";
    [JsonIgnore]
    public BitmapSource Preview => Decode(PreviewPngBase64);
    [JsonIgnore] public BitmapSource ExtendedPreview => Decode(ExtendedPreviewPngBase64);
    private static BitmapSource Decode(string png) {
            if (string.IsNullOrEmpty(png)) return null;
            try {
                using var stream = new MemoryStream(Convert.FromBase64String(png));
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            } catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException) { return null; }
    }
    public double Quality => !Available ? double.NaN : Math.Clamp(100 - Math.Max(
        Math.Max(0, AxisRatio - 1.10) * 180, Math.Max(0, TailStrength - 0.003) * 2200), 0, 100);
}

public sealed record ImageSample(float[] Pixels, int Width, int Height) {
    public int PixelsPerSample { get; init; } = 1;
    public double ArcsecPerSample { get; init; } = double.NaN;
    public ImageSample[] OuterFields { get; init; } = Array.Empty<ImageSample>();
}

public sealed record StarObservation(double X, double Y, double Flux, double Peak, double Fwhm);

public sealed record StarShapeLimits {
    public int TargetStars { get; init; } = 100;
    public int MinimumStars { get; init; } = 20;
    public double MaxEccentricity { get; init; } = 0.60;
    // Rescue requires stronger evidence than merely not exceeding the damage threshold.
    public double RescueMaxEccentricity => Math.Max(0, MaxEccentricity - 0.05);
    public double DeformedFraction { get; init; } = 0.60;
    public double MaxTailPercent { get; init; } = 2;
    public double MaxDoublePeakPercent { get; init; } = 8;
    public double MaxRemotePeakPercent { get; init; } = .5;
}
