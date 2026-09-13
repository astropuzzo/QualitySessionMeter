using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using System;
using System.Collections.Generic;
using System.Linq;

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

public sealed record SyntheticSessionDefinition(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<SyntheticFrameDefinition> Frames) {

    public int LearningCount => Frames.Count(x => x.ExpectedStatus == FrameStatus.Learning);
    public int AcceptedCount => Frames.Count(x => x.ExpectedStatus == FrameStatus.Accepted);
    public int WarningCount => Frames.Count(x => x.ExpectedStatus == FrameStatus.Warning);
    public int RejectedCount => Frames.Count(x => x.ExpectedStatus == FrameStatus.Rejected);
    public int ErrorCount => Frames.Count(x => x.ExpectedStatus == FrameStatus.Error);

    public string ExpectedSummary =>
        $"{Frames.Count} frames — expected: {AcceptedCount} accepted, {WarningCount} warning, " +
        $"{RejectedCount} rejected, {LearningCount} learning, {ErrorCount} error";
}

public static class SyntheticSessionGenerator {
    public const string CanonicalScenarioId = "canonical";

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
        ImageEvidenceEnabled = false, LegacyAssessmentForRegression = true, // Historical V1/V2/V3 contract; independent 1.4 suite covers production.
        RejectedFileAction = RejectedFileAction.KeepInPlace
    };

    public static IReadOnlyList<SyntheticSessionDefinition> GetScenarios() => new[] {
        new SyntheticSessionDefinition(
            CanonicalScenarioId,
            "Canonical regression",
            "Exact deterministic regression suite for every V1 rule, baseline isolation and missing-data behavior.",
            BuildCanonicalNight()),
        new SyntheticSessionDefinition(
            "excellent",
            "Excellent night",
            "Stable transparent sky and strong guiding. After learning, every frame should be accepted with high Quality.",
            BuildExcellentNight()),
        new SyntheticSessionDefinition(
            "average",
            "Average night",
            "Mostly usable data with moderate guiding/transparency/background variation, two warnings and a small number of rejects.",
            BuildAverageNight()),
        new SyntheticSessionDefinition(
            "poor",
            "Poor night",
            "Frequent wind/cloud/background problems. Usable frames still exist, but rejects dominate a substantial part of the session.",
            BuildPoorNight()),
        new SyntheticSessionDefinition(
            "disaster",
            "Severe / disaster night",
            "Intentionally brutal conditions: repeated hard guiding, cloud and background failures with only minimal usable recovery.",
            BuildDisasterNight()),
        new SyntheticSessionDefinition(
            "deterioration-recovery",
            "Deterioration + recovery",
            "A clean session gradually degrades into warning/reject territory and then recovers. Useful now and later for V2 temporal analysis.",
            BuildDeteriorationRecoveryNight())
    };

    public static SyntheticSessionDefinition GetScenario(string id) =>
        GetScenarios().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? GetScenarios().First(x => x.Id == CanonicalScenarioId);

    public static IReadOnlyList<SyntheticFrameDefinition> BuildCanonicalNight() {
        var frames = new List<SyntheticFrameDefinition>();

        AddLearning(frames, "L baseline", "M101", "L", 120, 1000, 1000);
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

        AddLearning(frames, "Ha baseline", "M101", "Ha", 120, 420, 760);
        frames.Add(new("Ha baseline ready", "M101", "Ha", 120, 420, 760, NormalGuide, FrameStatus.Accepted,
            ExpectedStarBaseline: 420, ExpectedBackgroundBaseline: 760));
        frames.Add(new("Ha star loss", "M101", "Ha", 120, 250, 760, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP" }));

        AddLearning(frames, "L 60s baseline", "M101", "L", 60, 720, 820);
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

    private static IReadOnlyList<SyntheticFrameDefinition> BuildExcellentNight() {
        var frames = new List<SyntheticFrameDefinition>();
        AddLearning(frames, "excellent learning", "NGC7000", "L", 180, 1000, 1000);

        frames.Add(new("excellent baseline ready", "NGC7000", "L", 180, 1000, 1000, GuideAt(0.45), FrameStatus.Accepted));
        frames.Add(new("excellent stable 01", "NGC7000", "L", 180, 1005, 1005, GuideAt(0.48), FrameStatus.Accepted));
        frames.Add(new("excellent stable 02", "NGC7000", "L", 180, 990, 995, GuideAt(0.52), FrameStatus.Accepted));
        frames.Add(new("excellent stable 03", "NGC7000", "L", 180, 1015, 1010, GuideAt(0.55), FrameStatus.Accepted));
        frames.Add(new("excellent stable 04", "NGC7000", "L", 180, 985, 1002, GuideAt(0.58), FrameStatus.Accepted));
        frames.Add(new("excellent stable 05", "NGC7000", "L", 180, 1008, 990, GuideAt(0.50), FrameStatus.Accepted));
        frames.Add(new("excellent stable 06", "NGC7000", "L", 180, 995, 1008, GuideAt(0.62), FrameStatus.Accepted));
        frames.Add(new("excellent stable 07", "NGC7000", "L", 180, 1020, 1012, GuideAt(0.56), FrameStatus.Accepted));
        frames.Add(new("excellent stable 08", "NGC7000", "L", 180, 992, 997, GuideAt(0.60), FrameStatus.Accepted));
        frames.Add(new("excellent stable 09", "NGC7000", "L", 180, 1003, 1004, GuideAt(0.47), FrameStatus.Accepted));
        frames.Add(new("excellent stable 10", "NGC7000", "L", 180, 998, 1001, GuideAt(0.53), FrameStatus.Accepted));
        frames.Add(new("excellent stable 11", "NGC7000", "L", 180, 1010, 994, GuideAt(0.59), FrameStatus.Accepted));
        frames.Add(new("excellent stable 12", "NGC7000", "L", 180, 995, 1006, GuideAt(0.51), FrameStatus.Accepted));
        return frames;
    }

    private static IReadOnlyList<SyntheticFrameDefinition> BuildAverageNight() {
        var frames = new List<SyntheticFrameDefinition>();
        AddLearning(frames, "average learning", "M51", "L", 180, 1000, 1000);

        frames.Add(new("average baseline ready", "M51", "L", 180, 1000, 1000, NormalGuide, FrameStatus.Accepted));
        frames.Add(new("moderate guide RMS", "M51", "L", 180, 950, 1100, GuideAt(0.90), FrameStatus.Accepted));
        frames.Add(new("moderate transparency/background", "M51", "L", 180, 850, 1180, GuideAt(1.10), FrameStatus.Accepted));
        frames.Add(new("near background limit warning", "M51", "L", 180, 1000, 1290, NormalGuide, FrameStatus.Warning));
        frames.Add(new("thin cloud reject", "M51", "L", 180, 620, 1000, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP" }));
        frames.Add(new("average clear recovery", "M51", "L", 180, 980, 1050, NormalGuide, FrameStatus.Accepted));
        frames.Add(new("average wind burst", "M51", "L", 180, 1000, 1000, SustainedGuide(2.6, 40, 44), FrameStatus.Rejected,
            new[] { "SUSTAINED_GUIDE_EXCURSION" }));
        frames.Add(new("mediocre but usable", "M51", "L", 180, 900, 1200, GuideAt(1.20), FrameStatus.Accepted));
        frames.Add(new("high RMS warning", "M51", "L", 180, 1000, 1000, GuideAt(1.45), FrameStatus.Warning));
        frames.Add(new("average clean frame", "M51", "L", 180, 1010, 990, NormalGuide, FrameStatus.Accepted));
        frames.Add(new("average moderate frame", "M51", "L", 180, 920, 1150, GuideAt(1.00), FrameStatus.Accepted));
        return frames;
    }

    private static IReadOnlyList<SyntheticFrameDefinition> BuildPoorNight() {
        var frames = new List<SyntheticFrameDefinition>();
        AddLearning(frames, "poor learning", "M81", "L", 180, 1000, 1000);

        frames.Add(new("poor baseline ready", "M81", "L", 180, 1000, 1000, NormalGuide, FrameStatus.Accepted));
        frames.Add(new("weak but usable", "M81", "L", 180, 800, 1200, GuideAt(1.20), FrameStatus.Accepted));
        frames.Add(new("poor thin cloud", "M81", "L", 180, 600, 1000, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP" }));
        frames.Add(new("poor bright haze", "M81", "L", 180, 1000, 1400, NormalGuide, FrameStatus.Rejected,
            new[] { "BACKGROUND_HIGH" }));
        frames.Add(new("poor sustained wind", "M81", "L", 180, 1000, 1000, SustainedGuide(2.8, 30, 36), FrameStatus.Rejected,
            new[] { "SUSTAINED_GUIDE_EXCURSION" }));
        frames.Add(new("poor near background warning", "M81", "L", 180, 1000, 1290, NormalGuide, FrameStatus.Warning));
        frames.Add(new("poor marginal warning", "M81", "L", 180, 750, 1250, GuideAt(1.35), FrameStatus.Warning));
        frames.Add(new("poor RMS failure", "M81", "L", 180, 1000, 1000, GuideAt(1.80), FrameStatus.Rejected,
            new[] { "GUIDE_RMS" }));
        frames.Add(new("poor hard gust", "M81", "L", 180, 1000, 1000, SingleSpikeGuide(6.0), FrameStatus.Rejected,
            new[] { "HARD_GUIDE_EXCURSION" }));
        frames.Add(new("poor cloud plus bright sky", "M81", "L", 180, 600, 1400, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP", "BACKGROUND_HIGH" }));
        frames.Add(new("poor dark background", "M81", "L", 180, 1000, 650, NormalGuide, FrameStatus.Rejected,
            new[] { "BACKGROUND_LOW" }));
        frames.Add(new("poor recovery", "M81", "L", 180, 980, 1030, NormalGuide, FrameStatus.Accepted));
        frames.Add(new("poor usable finish", "M81", "L", 180, 850, 1180, GuideAt(1.10), FrameStatus.Accepted));
        return frames;
    }

    private static IReadOnlyList<SyntheticFrameDefinition> BuildDisasterNight() {
        var frames = new List<SyntheticFrameDefinition>();
        AddLearning(frames, "disaster learning", "IC1396", "L", 180, 1000, 1000);

        frames.Add(new("disaster baseline ready", "IC1396", "L", 180, 1000, 1000, NormalGuide, FrameStatus.Accepted));
        frames.Add(new("continuous bad guiding", "IC1396", "L", 180, 1000, 1000, GuideAt(2.20), FrameStatus.Rejected,
            new[] { "GUIDE_RMS", "SUSTAINED_GUIDE_EXCURSION" }));
        frames.Add(new("disaster hard gust", "IC1396", "L", 180, 1000, 1000, SingleSpikeGuide(6.0), FrameStatus.Rejected,
            new[] { "HARD_GUIDE_EXCURSION" }));
        frames.Add(new("disaster opaque cloud", "IC1396", "L", 180, 400, 1000, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP" }));
        frames.Add(new("disaster bright cloud", "IC1396", "L", 180, 1000, 1700, NormalGuide, FrameStatus.Rejected,
            new[] { "BACKGROUND_HIGH" }));
        frames.Add(new("disaster dark transparency collapse", "IC1396", "L", 180, 1000, 500, NormalGuide, FrameStatus.Rejected,
            new[] { "BACKGROUND_LOW" }));
        frames.Add(new("disaster mixed cloud and guiding", "IC1396", "L", 180, 400, 1600, GuideAt(1.80), FrameStatus.Rejected,
            new[] { "GUIDE_RMS", "STAR_COUNT_DROP", "BACKGROUND_HIGH" }));
        frames.Add(new("disaster sustained wind", "IC1396", "L", 180, 1000, 1000, SustainedGuide(3.2, 20, 32), FrameStatus.Rejected,
            new[] { "SUSTAINED_GUIDE_EXCURSION" }));
        frames.Add(new("disaster cloud wall", "IC1396", "L", 180, 500, 1500, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP", "BACKGROUND_HIGH" }));
        frames.Add(new("disaster all-guide failure", "IC1396", "L", 180, 1000, 1000, GuideAt(5.50), FrameStatus.Rejected,
            new[] { "GUIDE_RMS", "SUSTAINED_GUIDE_EXCURSION", "HARD_GUIDE_EXCURSION" }));
        frames.Add(new("disaster near-limit warning", "IC1396", "L", 180, 1000, 1290, NormalGuide, FrameStatus.Warning));
        frames.Add(new("disaster late recovery", "IC1396", "L", 180, 990, 1010, NormalGuide, FrameStatus.Accepted));
        return frames;
    }

    private static IReadOnlyList<SyntheticFrameDefinition> BuildDeteriorationRecoveryNight() {
        var frames = new List<SyntheticFrameDefinition>();
        AddLearning(frames, "trend learning", "M33", "L", 180, 1000, 1000);

        frames.Add(new("trend baseline ready", "M33", "L", 180, 1000, 1000, GuideAt(0.50), FrameStatus.Accepted));
        frames.Add(new("trend mild degradation 1", "M33", "L", 180, 960, 1030, GuideAt(0.65), FrameStatus.Accepted));
        frames.Add(new("trend mild degradation 2", "M33", "L", 180, 920, 1060, GuideAt(0.80), FrameStatus.Accepted));
        frames.Add(new("trend moderate degradation 3", "M33", "L", 180, 880, 1100, GuideAt(1.00), FrameStatus.Accepted));
        frames.Add(new("trend moderate degradation 4", "M33", "L", 180, 840, 1140, GuideAt(1.20), FrameStatus.Accepted));
        frames.Add(new("trend warning before collapse", "M33", "L", 180, 850, 1250, GuideAt(1.45), FrameStatus.Warning));
        frames.Add(new("trend cloud collapse", "M33", "L", 180, 580, 1500, NormalGuide, FrameStatus.Rejected,
            new[] { "STAR_COUNT_DROP", "BACKGROUND_HIGH" }));
        frames.Add(new("trend mixed collapse", "M33", "L", 180, 650, 1400, GuideAt(1.80), FrameStatus.Rejected,
            new[] { "GUIDE_RMS", "BACKGROUND_HIGH" }));
        frames.Add(new("trend recovery 1", "M33", "L", 180, 970, 1040, GuideAt(0.60), FrameStatus.Accepted));
        frames.Add(new("trend recovery 2", "M33", "L", 180, 990, 1010, GuideAt(0.50), FrameStatus.Accepted));
        return frames;
    }

    private static void AddLearning(
        List<SyntheticFrameDefinition> frames,
        string prefix,
        string target,
        string filter,
        double exposureSeconds,
        int stars,
        double background) {

        for (int i = 1; i <= 4; i++) {
            frames.Add(new($"{prefix} {i}", target, filter, exposureSeconds, stars, background, NormalGuide, FrameStatus.Learning));
        }
    }

    private static IReadOnlyList<GuideSample> NoGuide(DateTime start, double duration) => Array.Empty<GuideSample>();

    private static IReadOnlyList<GuideSample> NormalGuide(DateTime start, double duration) =>
        BuildGuide(start, duration, _ => 0.50);

    private static Func<DateTime, double, IReadOnlyList<GuideSample>> GuideAt(double arcsec) =>
        (start, duration) => BuildGuide(start, duration, _ => arcsec);

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
