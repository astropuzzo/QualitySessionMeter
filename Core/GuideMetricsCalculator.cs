using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Pure guide-metric calculation shared by the live collector and the synthetic-session harness.
/// Keeping this logic free of N.I.N.A. mediators makes the exact production algorithm deterministic
/// and directly testable in CI.
/// </summary>
public static class GuideMetricsCalculator {
    public static GuideExposureMetrics Calculate(
        IEnumerable<GuideSample> samples,
        DateTime exposureStartUtc,
        double durationSeconds,
        double excursionThresholdArcsec) {

        if (exposureStartUtc == DateTime.MinValue || durationSeconds <= 0) {
            return new GuideExposureMetrics();
        }

        var end = exposureStartUtc.AddSeconds(durationSeconds);
        var window = (samples ?? Array.Empty<GuideSample>())
            .Where(x => x.TimestampUtc >= exposureStartUtc && x.TimestampUtc <= end)
            .Where(x => x.HasScale && !double.IsNaN(x.TotalArcsec) && !double.IsInfinity(x.TotalArcsec))
            .OrderBy(x => x.TimestampUtc)
            .ToArray();

        if (window.Length == 0) return new GuideExposureMetrics();

        var errors = window.Select(x => x.TotalArcsec).ToArray();
        double rms = Math.Sqrt(errors.Select(x => x * x).Average());
        double max = errors.Max();

        double typicalCadence = 0;
        if (window.Length > 1) {
            var cadences = new List<double>();
            for (int i = 1; i < window.Length; i++) {
                var dt = (window[i].TimestampUtc - window[i - 1].TimestampUtc).TotalSeconds;
                if (dt > 0 && dt < 30) cadences.Add(dt);
            }

            if (cadences.Count > 0) {
                var ordered = cadences.OrderBy(x => x).ToArray();
                typicalCadence = ordered[ordered.Length / 2];
            }
        }

        // Sustained duration is based only on confirmed consecutive samples above threshold.
        // A single spike has duration 0 and cannot satisfy a duration-based rejection rule.
        double longestRun = 0;
        DateTime? runStart = null;
        DateTime? runLast = null;
        double maxAllowedGap = typicalCadence > 0
            ? Math.Max(1.0, typicalCadence * 2.5)
            : 30.0;

        for (int i = 0; i < window.Length; i++) {
            if (errors[i] > excursionThresholdArcsec) {
                var timestamp = window[i].TimestampUtc;

                if (!runStart.HasValue) {
                    runStart = timestamp;
                    runLast = timestamp;
                } else {
                    var gap = (timestamp - runLast.Value).TotalSeconds;
                    if (gap <= 0 || gap > maxAllowedGap) {
                        runStart = timestamp;
                        runLast = timestamp;
                    } else {
                        runLast = timestamp;
                        longestRun = Math.Max(longestRun, (runLast.Value - runStart.Value).TotalSeconds);
                    }
                }
            } else {
                runStart = null;
                runLast = null;
            }
        }

        return new GuideExposureMetrics {
            HasData = true,
            Samples = window.Length,
            RmsArcsec = rms,
            MaxExcursionArcsec = max,
            MaxSustainedExcursionSeconds = longestRun,
            Series = window
        };
    }
}
