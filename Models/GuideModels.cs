using System;

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
}
