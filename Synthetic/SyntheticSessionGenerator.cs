using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Synthetic;

public sealed record SyntheticFrameDefinition(
    string Name,
    string Target,
    string Filter,
    double ExposureSeconds,
    int StarCount,
    double BackgroundMedian,
    Func<DateTime, double, IReadOnlyList<GuideSample>> GuideFactory,
    FrameStatus ExpectedStatus,
    string[] ExpectedReasons = null,
    string ExpectedErrorContains = null,
    double? ExpectedStarBaseline = null,
    double? ExpectedBackgroundBaseline = null);

public static class SyntheticSessionGenerator {
    public static QualitySettings CreateCanonicalSettings() => new(new InMemoryPluginOptionsAccessor()) {
        Enabled = true,
        MonitorOnly = true,
        EnableGuideRms = true,
        MaxGuideRms = 1.50,
        EnableSustainedExcursion = true,
        ExcursionThreshold = 2.00,
        ExcursionMinimumDuration = 2.0,
        EnableHardExcursion = true,
        HardExcursionThreshold = 5.0,
        EnableStarCount = true,
        MaxStarLossPercent = 35.0,
        EnableBackground = true,
        MaxBackgroundIncreasePercent = 30.0,
        MaxBackgroundDecreasePercent = 30.0,
        BaselineWindow = 8,
        MinimumLearningFrames = 4,
        WorstMetricWeight = 0.70,
        RejectedFileAction = RejectedFileAction.KeepInPlace
    };

    public static IReadOnlyList<SyntheticFrameDefinition> BuildCanonicalNight() {
        var frames = new List<SyntheticFrameDefinition>();

        for (int i = 1; i <= 4; i++) {
            frames.Add(new($"L baseline learning {i}", "M101", "L", 120, 1000, 1000, NormalGuide, FrameStatus.Learning));
        }
        frames.Add(new("L baseline ready", "M101", "L", 120, 1000, 1000, NormalGuide, FrameStatus.Accepted,
            ExpectedStarBaseline: 1000, ExpectedBackgroundBaseline: 1000));

        frames.Add(new("single 3 arcsec wind spike", "M101", "L", 120, 1000, 1000, SingleSpikeGuide(3.0), FrameStatus.Accepted));
        frames.Add(new("sustained wind excursion", "M101", "L", 120, 1000, 1000, SustainedGuide(2.6, 30, 33), FrameStatus.Rejected,
            new[] { "SUSTAINED_GUIDE_EXCURSION" }));
        frames.Add(new("single hard 6 arcsec excursion", "M101", "L", 120, 1000, 1000, SingleSpikeGuide(6.0), FrameStatus.Rejected,
            new[] { "HARD_GUIDE_EXCURSION" }));

        frames.Add(new("40 percent star loss", "M101", "L", 120, 600, 1000, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP" }));
        frames.Add(new("background plus 40 percent", "M101", "L", 120, 1000, 1400, NormalGuide, FrameStatus.Rejected,
            new[] { "BACKGROUND_HIGH" }));
        frames.Add(new("background minus 35 percent", "M101", "L", 120, 1000, 650, NormalGuide, FrameStatus.Rejected,
            new[] { "BACKGROUND_LOW" }));
        frames.Add(new("cloud plus bright background", "M101", "L", 120, 600, 1400, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP", "BACKGROUND_HIGH" }));

        frames.Add(new("near background limit warning", "M101", "L", 120, 1000, 1290, NormalGuide, FrameStatus.Warning));
        frames.Add(new("clean recovery after rejected frames", "M101", "L", 120, 1000, 1000, NormalGuide, FrameStatus.Accepted,
            ExpectedStarBaseline: 1000, ExpectedBackgroundBaseline: 1000));

        for (int i = 1; i <= 4; i++) {
            frames.Add(new($"Ha baseline learning {i}", "M101", "Ha", 120, 420, 760, NormalGuide, FrameStatus.Learning));
        }
        frames.Add(new("Ha baseline ready", "M101", "Ha", 120, 420, 760, NormalGuide, FrameStatus.Accepted,
            ExpectedStarBaseline: 420, ExpectedBackgroundBaseline: 760));
        frames.Add(new("Ha star loss", "M101", "Ha", 120, 250, 760, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP" }));

        for (int i = 1; i <= 4; i++) {
            frames.Add(new($"L 60s baseline learning {i}", "M101", "L", 60, 720, 820, NormalGuide, FrameStatus.Learning));
        }
        frames.Add(new("L 60s baseline ready", "M101", "L", 60, 720, 820, NormalGuide, FrameStatus.Accepted,
            ExpectedStarBaseline: 720, ExpectedBackgroundBaseline: 820));

        frames.Add(new("missing guide data", "M101", "L", 120, 1000, 1000, NoGuide, FrameStatus.Error,
            ExpectedErrorContains: "GUIDE_DATA_UNAVAILABLE"));
        frames.Add(new("missing star data", "M101", "L", 120, -1, 1000, NormalGuide, FrameStatus.Error,
            ExpectedErrorContains: "STAR_COUNT_UNAVAILABLE"));
        frames.Add(new("missing background data", "M101", "L", 120, 1000, double.NaN, NormalGuide, FrameStatus.Error,
            ExpectedErrorContains: "BACKGROUND_UNAVAILABLE"));

        frames.Add(new("new target starts independent learning", "M31", "L", 120, 900, 950, NormalGuide, FrameStatus.Learning));

        return frames;
    }

    private static IReadOnlyList<GuideSample> NoGuide(DateTime start, double duration) => Array.Empty<GuideSample>();

    private static IReadOnlyList<GuideSample> NormalGuide(DateTime start, double duration) =>
        BuildGuide(start, duration, _ => 0.50);

    private static Func<DateTime, double, IReadOnlyList<GuideSample>> SingleSpikeGuide(double spikeArcsec) =>
        (start, duration) => BuildGuide(start, duration, second => second == 30 ? spikeArcsec : 0.50);

    private static Func<DateTime, double, IReadOnlyList<GuideSample>> SustainedGuide(double excursionArcsec, int firstSecond, int lastSecond) =>
        (start, duration) => BuildGuide(start, duration,
            second => second >= firstSecond && second <= lastSecond ? excursionArcsec : 0.50);

    private static IReadOnlyList<GuideSample> BuildGuide(DateTime start, double duration, Func<int, double> totalArcsecAtSecond) {
        var samples = new List<GuideSample>();
        int seconds = Math.Max(1, (int)Math.Floor(duration));
        for (int second = 0; second <= seconds; second++) {
            samples.Add(new GuideSample {
                TimestampUtc = start.AddSeconds(second),
                RaPixels = totalArcsecAtSecond(second),
                DecPixels = 0,
                PixelScaleArcsecPerPixel = 1.0
            });
        }
        return samples;
    }
}
