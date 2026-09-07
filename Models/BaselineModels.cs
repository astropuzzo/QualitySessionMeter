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
    string Camera
) {
    public override string ToString() =>
        string.Join("|",
            Normalize(Target),
            Normalize(Filter),
            ExposureSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            Gain.ToString(CultureInfo.InvariantCulture),
            $"{BinX}x{BinY}",
            Normalize(Camera));

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
}
