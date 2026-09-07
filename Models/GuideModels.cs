using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Models;

public sealed class GuideSample {
    public DateTime TimestampUtc { get; init; }
    public double RaPixels { get; init; }
    public double DecPixels { get; init; }
    public double PixelScaleArcsecPerPixel { get; init; }

    public bool HasScale =>
        PixelScaleArcsecPerPixel > 0 &&
        !double.IsNaN(PixelScaleArcsecPerPixel) &&
        !double.IsInfinity(PixelScaleArcsecPerPixel);

    public double RaArcsec => HasScale ? RaPixels * PixelScaleArcsecPerPixel : double.NaN;
    public double DecArcsec => HasScale ? DecPixels * PixelScaleArcsecPerPixel : double.NaN;
    public double TotalArcsec => HasScale
        ? Math.Sqrt(RaPixels * RaPixels + DecPixels * DecPixels) * PixelScaleArcsecPerPixel
        : double.NaN;
}

public sealed class GuideExposureMetrics {
    public bool HasData { get; init; }
    public int Samples { get; init; }
    public double RmsArcsec { get; init; } = double.NaN;
    public double MaxExcursionArcsec { get; init; } = double.NaN;
    public double MaxSustainedExcursionSeconds { get; init; } = 0;

    /// <summary>
    /// Timestamped samples already clipped to the actual exposure interval. Kept only in the
    /// transient analysis object; FrameQualityResult persists compact pattern diagnostics instead.
    /// </summary>
    public IReadOnlyList<GuideSample> Series { get; init; } = Array.Empty<GuideSample>();
}

public enum GuidePatternKind {
    Unavailable,
    Stable,
    IsolatedSpike,
    SustainedExcursion,
    Oscillation,
    Drift,
    WindLike,
    Irregular
}

public sealed class GuidePatternAnalysis {
    public GuidePatternKind Pattern { get; init; } = GuidePatternKind.Unavailable;
    public double Confidence { get; init; }
    public double DriftArcsecPerMinute { get; init; } = double.NaN;
    public double OscillationRangeArcsec { get; init; } = double.NaN;
    public int SignChanges { get; init; }
    public double Burstiness { get; init; } = double.NaN;
    public string Detail { get; init; } = "";
}
