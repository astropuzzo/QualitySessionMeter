using System;
using System.Globalization;

namespace NINA.Plugin.QualitySessionMeter.Models;

public readonly record struct BaselineKey(
    string Target,
    string Filter,
    double ExposureSeconds,
    int Gain,
    int BinX,
    int BinY,
    string Camera,
    int CameraOffset = -1,
    int ReadoutModeIndex = -1,
    int ImageWidth = 0,
    int ImageHeight = 0,
    int SampleStep = 1,
    string PierSide = ""
) {
    public override string ToString() =>
        string.Join("|",
            Normalize(Target),
            Normalize(Filter),
            ExposureSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            Gain.ToString(CultureInfo.InvariantCulture),
            $"{BinX}x{BinY}",
            Normalize(Camera),
            CameraOffset.ToString(CultureInfo.InvariantCulture),
            ReadoutModeIndex.ToString(CultureInfo.InvariantCulture),
            $"{ImageWidth}x{ImageHeight}/{SampleStep}",
            Normalize(PierSide));

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
}

public sealed class BaselineSnapshot {
    public int StarSamples { get; init; }
    public int BackgroundSamples { get; init; }
    public bool StarsReady { get; init; }
    public bool BackgroundReady { get; init; }
    public double StarMedian { get; init; } = double.NaN;
    public double BackgroundMedian { get; init; } = double.NaN;

    // Predictions use independently eligible measurements from this imaging context.
    public bool StarTrendUsable { get; init; }
    public bool BackgroundTrendUsable { get; init; }
    public double StarTrendExpectedNext { get; init; } = double.NaN;
    public double BackgroundTrendExpectedNext { get; init; } = double.NaN;
    public double StarTrendPercentPerFrame { get; init; } = double.NaN;
    public double BackgroundTrendPercentPerFrame { get; init; } = double.NaN;
    public double StarTrendR2 { get; init; } = double.NaN;
    public double BackgroundTrendR2 { get; init; } = double.NaN;
}
