using System;

namespace NINA.Plugin.QualitySessionMeter.Models;

public sealed class FrameQualityInput {
    public int FrameIndex { get; init; }
    public DateTime TimestampUtc { get; init; }
    public string OriginalPath { get; init; }
    public string Target { get; init; }
    public string Filter { get; init; }
    public double ExposureSeconds { get; init; }
    public int Gain { get; init; }
    public int BinX { get; init; }
    public int BinY { get; init; }
    public string Camera { get; init; }
    public int StarCount { get; init; } = -1;
    public double BackgroundMedian { get; init; } = double.NaN;
    public BaselineSnapshot Baseline { get; init; }
    public GuideExposureMetrics Guide { get; init; }
    public ImageEvidence ImageEvidence { get; init; }

    public FrameQualityInput WithBaseline(BaselineSnapshot baseline) => new() {
        FrameIndex = FrameIndex, TimestampUtc = TimestampUtc, OriginalPath = OriginalPath, Target = Target, Filter = Filter,
        ExposureSeconds = ExposureSeconds, Gain = Gain, BinX = BinX, BinY = BinY, Camera = Camera, StarCount = StarCount,
        BackgroundMedian = BackgroundMedian, Baseline = baseline, Guide = Guide, ImageEvidence = ImageEvidence
    };
}
