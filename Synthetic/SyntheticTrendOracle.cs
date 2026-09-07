using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Synthetic;

public static class SyntheticTrendOracle {
    public static IReadOnlyList<string> Run() {
        var failures = new List<string>();
        var settings = SyntheticSessionGenerator.CreateCanonicalSettings();
        var baseline = new BaselineEngine();
        var key = new BaselineKey("TrendTarget", "L", 180, 100, 1, 1, "SyntheticCam");

        // Coherent, modest evolution: star count slowly falls while background slowly rises.
        var stars = new[] { 1000, 980, 960, 940, 920, 900, 880, 860 };
        var backgrounds = new[] { 1000d, 1020, 1040, 1060, 1080, 1100, 1120, 1140 };
        for (int i = 0; i < stars.Length; i++) {
            baseline.AddAccepted(key, stars[i], backgrounds[i], settings.BaselineWindow);
        }

        var snapshot = baseline.GetSnapshot(key, settings.MinimumLearningFrames);
        if (!snapshot.StarTrendUsable) failures.Add("Trend oracle: coherent slow star trend should be usable");
        if (!snapshot.BackgroundTrendUsable) failures.Add("Trend oracle: coherent slow background trend should be usable");
        if (snapshot.StarTrendExpectedNext < 825 || snapshot.StarTrendExpectedNext > 855) {
            failures.Add($"Trend oracle: expected next star count near 840, got {snapshot.StarTrendExpectedNext:0.##}");
        }
        if (snapshot.BackgroundTrendExpectedNext < 1150 || snapshot.BackgroundTrendExpectedNext > 1170) {
            failures.Add($"Trend oracle: expected next background near 1160, got {snapshot.BackgroundTrendExpectedNext:0.##}");
        }
        if (snapshot.StarTrendR2 < 0.95 || snapshot.BackgroundTrendR2 < 0.95) {
            failures.Add("Trend oracle: linear slow trend should have high R²");
        }

        // The next expected slow frame must be interpreted as gradual, not abrupt.
        var engine = new QualityEngine();
        var start = new DateTime(2026, 9, 7, 20, 0, 0, DateTimeKind.Utc);
        var guide = GuideMetricsCalculator.Calculate(NormalGuide(start, 180), start, 180, settings.ExcursionThreshold);
        var gradual = engine.Evaluate(new FrameQualityInput {
            FrameIndex = 9,
            TimestampUtc = start,
            OriginalPath = "SYNTHETIC://trend/gradual.fits",
            Target = "TrendTarget",
            Filter = "L",
            ExposureSeconds = 180,
            Gain = 100,
            BinX = 1,
            BinY = 1,
            Camera = "SyntheticCam",
            StarCount = 840,
            BackgroundMedian = 1160,
            Baseline = snapshot,
            Guide = guide
        }, settings);

        if (gradual.StarTrendKind != TrendInterpretationKind.GradualChange ||
            gradual.BackgroundTrendKind != TrendInterpretationKind.GradualChange) {
            failures.Add($"Trend oracle: expected GradualChange/GradualChange, got {gradual.StarTrendKind}/{gradual.BackgroundTrendKind}");
        }

        // A sudden collapse against that trend must remain an abrupt anomaly and must still obey V1
        // hard median-based rejection. Trend diagnostics are not allowed to normalize it away.
        var abrupt = engine.Evaluate(new FrameQualityInput {
            FrameIndex = 10,
            TimestampUtc = start.AddMinutes(4),
            OriginalPath = "SYNTHETIC://trend/abrupt.fits",
            Target = "TrendTarget",
            Filter = "L",
            ExposureSeconds = 180,
            Gain = 100,
            BinX = 1,
            BinY = 1,
            Camera = "SyntheticCam",
            StarCount = 500,
            BackgroundMedian = 1600,
            Baseline = snapshot,
            Guide = guide
        }, settings);

        if (abrupt.Status != FrameStatus.Rejected) failures.Add($"Trend oracle: abrupt collapse must remain REJECTED, got {abrupt.Status}");
        if (abrupt.StarTrendKind != TrendInterpretationKind.AbruptAnomaly ||
            abrupt.BackgroundTrendKind != TrendInterpretationKind.AbruptAnomaly) {
            failures.Add($"Trend oracle: abrupt collapse should be classified AbruptAnomaly on both image channels, got {abrupt.StarTrendKind}/{abrupt.BackgroundTrendKind}");
        }

        // A much steeper historical change is deliberately NOT normalized as a benign trend.
        var steep = new BaselineEngine();
        var steepKey = new BaselineKey("Steep", "L", 180, 100, 1, 1, "SyntheticCam");
        foreach (var value in new[] { 1000, 900, 800, 700, 600, 500 }) steep.AddAccepted(steepKey, value, 1000, settings.BaselineWindow);
        var steepSnapshot = steep.GetSnapshot(steepKey, settings.MinimumLearningFrames);
        if (steepSnapshot.StarTrendUsable) failures.Add("Trend oracle: >20% coherent historical degradation must not be normalized as benign slow trend");

        return failures;
    }

    private static IReadOnlyList<GuideSample> NormalGuide(DateTime start, double duration) {
        var list = new List<GuideSample>();
        for (int s = 0; s <= (int)duration; s++) {
            list.Add(new GuideSample {
                TimestampUtc = start.AddSeconds(s),
                RaPixels = 0.45,
                DecPixels = 0,
                PixelScaleArcsecPerPixel = 1.0
            });
        }
        return list;
    }
}
