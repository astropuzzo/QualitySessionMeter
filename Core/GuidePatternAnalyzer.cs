using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Diagnostic-only V2 guide time-series classifier. It never changes the V1 hard rejection state.
/// Labels describe the observed mathematical pattern, not a guaranteed physical cause.
/// </summary>
public sealed class GuidePatternAnalyzer {
    public GuidePatternAnalysis Analyze(GuideExposureMetrics guide, QualitySettings settings) {
        if (guide?.HasData != true || guide.Series == null || guide.Series.Count < 3) {
            return new GuidePatternAnalysis {
                Pattern = GuidePatternKind.Unavailable,
                Confidence = 0,
                Detail = "Insufficient timestamped guide samples for pattern analysis."
            };
        }

        var series = guide.Series
            .Where(x => x.HasScale && !double.IsNaN(x.RaArcsec) && !double.IsNaN(x.DecArcsec))
            .OrderBy(x => x.TimestampUtc)
            .ToArray();
        if (series.Length < 3) {
            return new GuidePatternAnalysis {
                Pattern = GuidePatternKind.Unavailable,
                Confidence = 0,
                Detail = "Insufficient valid scaled guide samples for pattern analysis."
            };
        }

        var ra = series.Select(x => x.RaArcsec).ToArray();
        var dec = series.Select(x => x.DecArcsec).ToArray();
        var axis = Variance(ra) >= Variance(dec) ? ra : dec;
        string axisName = ReferenceEquals(axis, ra) ? "RA" : "DEC";
        var timesMin = series.Select(x => (x.TimestampUtc - series[0].TimestampUtc).TotalMinutes).ToArray();

        var regression = LinearRegression(timesMin, axis);
        double durationMin = Math.Max(0, timesMin[^1] - timesMin[0]);
        double projectedDrift = Math.Abs(regression.Slope) * durationMin;
        double range = axis.Max() - axis.Min();

        double median = Median(axis);
        double deadband = Math.Max(0.08, Math.Abs(range) * 0.08);
        int signChanges = CountSignChanges(axis.Select(x => x - median).ToArray(), deadband);

        var deltas = new List<double>();
        for (int i = 1; i < axis.Length; i++) deltas.Add(Math.Abs(axis[i] - axis[i - 1]));
        double medianDelta = deltas.Count == 0 ? 0 : Median(deltas.ToArray());
        double p90Delta = deltas.Count == 0 ? 0 : Percentile(deltas, 0.90);
        double burstiness = p90Delta / Math.Max(0.05, medianDelta);

        int aboveExcursion = series.Count(x => x.TotalArcsec > settings.ExcursionThreshold);

        if (aboveExcursion == 1 && guide.MaxSustainedExcursionSeconds <= 0) {
            double c = Clamp(75 + 25 * Math.Min(1.0, Math.Max(0, guide.MaxExcursionArcsec / Math.Max(0.1, settings.ExcursionThreshold) - 1.0)));
            return Result(GuidePatternKind.IsolatedSpike, c, regression.Slope, range, signChanges, burstiness,
                $"One isolated guide excursion dominates the exposure; no sustained run was detected ({axisName} dominant axis)." );
        }

        if (series.Length >= 8 && regression.R2 >= 0.82 &&
            projectedDrift >= Math.Max(0.8, settings.ExcursionThreshold * 0.55) &&
            Math.Abs(regression.Slope) >= 0.20) {
            double c = Clamp(65 + 25 * regression.R2 + 10 * Math.Min(1.0, projectedDrift / Math.Max(1.0, settings.ExcursionThreshold)));
            return Result(GuidePatternKind.Drift, c, regression.Slope, range, signChanges, burstiness,
                $"Strong monotonic {axisName} trend (R² {regression.R2:0.00}, {regression.Slope:+0.00;-0.00;0.00} arcsec/min)." );
        }

        int oscillationChangesNeeded = Math.Max(4, series.Length / 10);
        if (series.Length >= 10 && signChanges >= oscillationChangesNeeded &&
            range >= Math.Max(0.8, settings.ExcursionThreshold * 0.50)) {
            double changeStrength = Math.Min(1.0, signChanges / (double)Math.Max(1, oscillationChangesNeeded * 2));
            double rangeStrength = Math.Min(1.0, range / Math.Max(1.0, settings.ExcursionThreshold));
            double c = Clamp(65 + 20 * changeStrength + 15 * rangeStrength);
            return Result(GuidePatternKind.Oscillation, c, regression.Slope, range, signChanges, burstiness,
                $"Repeated sign reversals on the {axisName} axis ({signChanges} centered crossings; range {range:0.00} arcsec)." );
        }

        if (guide.MaxSustainedExcursionSeconds >= settings.ExcursionMinimumDuration &&
            guide.MaxExcursionArcsec > settings.ExcursionThreshold) {
            double durationStrength = Math.Min(1.0, guide.MaxSustainedExcursionSeconds / Math.Max(0.5, settings.ExcursionMinimumDuration * 3));
            double c = Clamp(75 + 25 * durationStrength);
            return Result(GuidePatternKind.SustainedExcursion, c, regression.Slope, range, signChanges, burstiness,
                $"Guide error remained above the configured excursion threshold for {guide.MaxSustainedExcursionSeconds:0.0} s." );
        }

        if (series.Length >= 8 && guide.MaxExcursionArcsec > settings.ExcursionThreshold && burstiness >= 3.0) {
            double c = Clamp(65 + 15 * Math.Min(1.0, (burstiness - 3.0) / 4.0) +
                             20 * Math.Min(1.0, guide.MaxExcursionArcsec / Math.Max(0.1, settings.ExcursionThreshold * 2)));
            return Result(GuidePatternKind.WindLike, c, regression.Slope, range, signChanges, burstiness,
                $"Irregular high-derivative guide bursts were detected (burstiness {burstiness:0.0}); this is compatible with gust/vibration-like disturbance." );
        }

        if (guide.MaxExcursionArcsec <= settings.ExcursionThreshold && guide.RmsArcsec <= settings.MaxGuideRms) {
            double c = series.Length >= 10 ? 90 : 75;
            return Result(GuidePatternKind.Stable, c, regression.Slope, range, signChanges, burstiness,
                "No strong drift, oscillation, sustained excursion or burst pattern was detected." );
        }

        return Result(GuidePatternKind.Irregular, 60, regression.Slope, range, signChanges, burstiness,
            "Guide data is abnormal but does not match a stronger deterministic pattern signature." );
    }

    private static GuidePatternAnalysis Result(
        GuidePatternKind kind,
        double confidence,
        double drift,
        double range,
        int signChanges,
        double burstiness,
        string detail) => new() {
            Pattern = kind,
            Confidence = Clamp(confidence),
            DriftArcsecPerMinute = drift,
            OscillationRangeArcsec = range,
            SignChanges = signChanges,
            Burstiness = burstiness,
            Detail = detail
        };

    private static (double Slope, double Intercept, double R2) LinearRegression(double[] x, double[] y) {
        if (x.Length != y.Length || x.Length < 2) return (0, 0, 0);
        double meanX = x.Average();
        double meanY = y.Average();
        double sxx = 0;
        double sxy = 0;
        for (int i = 0; i < x.Length; i++) {
            double dx = x[i] - meanX;
            sxx += dx * dx;
            sxy += dx * (y[i] - meanY);
        }
        if (sxx <= 0) return (0, meanY, 0);
        double slope = sxy / sxx;
        double intercept = meanY - slope * meanX;
        double ssTot = y.Sum(v => (v - meanY) * (v - meanY));
        if (ssTot <= 1e-12) return (slope, intercept, 0);
        double ssRes = 0;
        for (int i = 0; i < x.Length; i++) {
            double residual = y[i] - (intercept + slope * x[i]);
            ssRes += residual * residual;
        }
        return (slope, intercept, Clamp01(1.0 - ssRes / ssTot));
    }

    private static int CountSignChanges(double[] values, double deadband) {
        int last = 0;
        int changes = 0;
        foreach (double value in values) {
            int sign = value > deadband ? 1 : value < -deadband ? -1 : 0;
            if (sign == 0) continue;
            if (last != 0 && sign != last) changes++;
            last = sign;
        }
        return changes;
    }

    private static double Variance(double[] values) {
        if (values.Length == 0) return 0;
        double mean = values.Average();
        return values.Sum(x => (x - mean) * (x - mean)) / values.Length;
    }

    private static double Median(double[] values) {
        if (values.Length == 0) return 0;
        var ordered = values.OrderBy(x => x).ToArray();
        int mid = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2.0 : ordered[mid];
    }

    private static double Percentile(List<double> values, double percentile) {
        if (values.Count == 0) return 0;
        var ordered = values.OrderBy(x => x).ToArray();
        int index = (int)Math.Round((ordered.Length - 1) * Math.Clamp(percentile, 0, 1));
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}
