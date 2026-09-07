using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Synthetic;

public static class SyntheticGuidePatternOracle {
    public static IReadOnlyList<string> Run() {
        var failures = new List<string>();
        var settings = SyntheticSessionGenerator.CreateCanonicalSettings();
        var analyzer = new GuidePatternAnalyzer();
        var start = new DateTime(2026, 9, 7, 20, 0, 0, DateTimeKind.Utc);
        const double duration = 120;

        Check("stable", GuidePatternKind.Stable,
            Build(start, duration, s => 0.40 + 0.04 * Math.Sin(s / 8.0), s => 0.10 * Math.Sin(s / 13.0)));

        Check("isolated spike", GuidePatternKind.IsolatedSpike,
            Build(start, duration, s => s == 30 ? 3.0 : 0.40, _ => 0));

        Check("sustained excursion", GuidePatternKind.SustainedExcursion,
            Build(start, duration, s => s >= 30 && s <= 34 ? 2.6 : 0.40, _ => 0));

        Check("linear drift", GuidePatternKind.Drift,
            Build(start, duration, s => 0.20 + 1.60 * s / duration, _ => 0));

        Check("oscillation", GuidePatternKind.Oscillation,
            Build(start, duration, s => 1.10 * Math.Sin(2 * Math.PI * s / 12.0), _ => 0));

        var burstSeconds = new HashSet<int> { 9, 19, 31, 44, 58, 73, 89, 106, 117 };
        Check("wind-like bursts", GuidePatternKind.WindLike,
            Build(start, duration, s => burstSeconds.Contains(s) ? 3.2 + (s % 3) * 0.2 : 0.40, _ => 0));

        return failures;

        void Check(string name, GuidePatternKind expected, IReadOnlyList<GuideSample> samples) {
            var metrics = GuideMetricsCalculator.Calculate(samples, start, duration, settings.ExcursionThreshold);
            var actual = analyzer.Analyze(metrics, settings);
            if (actual.Pattern != expected) {
                failures.Add($"Guide pattern oracle '{name}': expected {expected}, got {actual.Pattern} ({actual.Detail})");
            }
            if (actual.Confidence < 0 || actual.Confidence > 100 || double.IsNaN(actual.Confidence)) {
                failures.Add($"Guide pattern oracle '{name}': invalid confidence {actual.Confidence}");
            }
            if (string.IsNullOrWhiteSpace(actual.Detail)) {
                failures.Add($"Guide pattern oracle '{name}': empty detail");
            }
        }
    }

    private static IReadOnlyList<GuideSample> Build(
        DateTime start,
        double durationSeconds,
        Func<int, double> raArcsec,
        Func<int, double> decArcsec) {

        var samples = new List<GuideSample>();
        int seconds = (int)Math.Floor(durationSeconds);
        for (int second = 0; second <= seconds; second++) {
            samples.Add(new GuideSample {
                TimestampUtc = start.AddSeconds(second),
                RaPixels = raArcsec(second),
                DecPixels = decArcsec(second),
                PixelScaleArcsecPerPixel = 1.0
            });
        }
        return samples;
    }
}
