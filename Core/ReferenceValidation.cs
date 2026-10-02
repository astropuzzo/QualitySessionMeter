using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Validates coherent startup levels without interpolating between cloudy and clear groups.</summary>
public static class ReferenceValidation {
    public readonly record struct Candidate(int FrameIndex, int Stars, double Background, DateTime Time = default);

    public readonly record struct Reference(double Stars, double Background, int Samples) {
        public DateTime Origin { get; init; }
        public double StarAtOrigin { get; init; }
        public double BackgroundAtOrigin { get; init; }
        public double StarSlope { get; init; }
        public double BackgroundSlope { get; init; }
        public bool StarTrend { get; init; }
        public bool BackgroundTrend { get; init; }

        public BaselineSnapshot ToSnapshot(DateTime? time = null) {
            double minutes = time.HasValue && Origin != default ? (time.Value - Origin).TotalMinutes : 0;
            double stars = StarTrend && time.HasValue ? StarAtOrigin + StarSlope * minutes : Stars;
            double background = BackgroundTrend && time.HasValue ? BackgroundAtOrigin + BackgroundSlope * minutes : Background;
            return new BaselineSnapshot {
                StarSamples = Samples, BackgroundSamples = Samples,
                StarsReady = double.IsFinite(stars) && stars > 0,
                BackgroundReady = double.IsFinite(background) && background > 0,
                StarMedian = stars, BackgroundMedian = background
            };
        }
    }

    /// <summary>
    /// Select a supported high-count band. An isolated high frame cannot validate a new level, and
    /// a midpoint between separated populations is not an observed reference. A coherent temporal
    /// drift keeps all its samples and is evaluated at their original times.
    /// </summary>
    public static Candidate[] SelectReferenceCandidates(IReadOnlyList<Candidate> candidates, QualitySettings settings) {
        if (candidates == null || candidates.Count == 0) return Array.Empty<Candidate>();
        var ordered = candidates.OrderBy(c => c.Time == default ? DateTime.MinValue : c.Time).ThenBy(c => c.FrameIndex).ToArray();
        if (!settings.EnableStarCount) return ordered;
        var valid = ordered.Where(c => c.Stars > 0).ToArray();
        if (valid.Length == 0) return ordered;
        if (FitGradual(valid, c => c.Stars).Usable) return valid;

        var bands = new List<List<Candidate>>();
        double separation = Math.Min(.25, Math.Max(.10, settings.MaxStarLossPercent / 100));
        foreach (var candidate in valid.OrderByDescending(c => c.Stars)) {
            if (bands.Count == 0 || 1 - candidate.Stars / (double)bands[^1][^1].Stars > separation)
                bands.Add(new List<Candidate>());
            bands[^1].Add(candidate);
        }
        var supported = bands.FirstOrDefault(b => b.Count >= Math.Min(2, settings.MinimumLearningFrames)) ?? bands[0];
        return supported.OrderBy(c => c.Time).ThenBy(c => c.FrameIndex).ToArray();
    }

    public static Reference Build(IReadOnlyList<Candidate> candidates, QualitySettings settings) {
        var selected = SelectReferenceCandidates(candidates, settings);
        if (selected.Length == 0) return new Reference(double.NaN, double.NaN, 0);
        double stars = settings.EnableStarCount ? Median(selected.Where(c => c.Stars > 0).Select(c => (double)c.Stars)) : double.NaN;
        double background = Median(selected.Where(c => double.IsFinite(c.Background) && c.Background > 0).Select(c => c.Background));
        var starFit = FitGradual(selected, c => c.Stars);
        var backgroundFit = FitGradual(selected, c => c.Background);
        return new Reference(stars, background, selected.Length) {
            Origin = selected[0].Time,
            StarTrend = starFit.Usable, StarAtOrigin = starFit.Intercept, StarSlope = starFit.Slope,
            BackgroundTrend = backgroundFit.Usable, BackgroundAtOrigin = backgroundFit.Intercept, BackgroundSlope = backgroundFit.Slope
        };
    }

    /// <summary>A brighter stellar level needs independent evidence of improved transparency.</summary>
    public static bool ShowsBetterConditions(FrameQualityResult result, Reference reference, QualitySettings settings) {
        if (!settings.EnableStarCount || result == null || result.StarCount <= 0) return false;
        if (!double.IsFinite(reference.Stars) || reference.Stars <= 0 || result.Status == FrameStatus.Error) return false;
        if (result.RejectReasons.Any(r => r != "BACKGROUND_LOW")) return false;
        if (result.ReviewReasons.Contains("TRANSPARENCY_CHANGE") || result.ImageEvidence.Compromised) return false;
        if (settings.ImageEvidenceEnabled && result.ImageEvidence.Available && !result.ImageEvidence.HasRescueMargin) return false;
        if (result.StarCount < reference.Stars * 1.20) return false;

        bool darker = double.IsFinite(reference.Background) && reference.Background > 0
            && double.IsFinite(result.BackgroundMedian) && result.BackgroundMedian <= reference.Background * .90;
        bool moreSignal = result.ImageEvidence.PhotometryAvailable && result.ImageEvidence.RelativeFlux >= 1.15;
        bool brighterSky = double.IsFinite(reference.Background) && double.IsFinite(result.BackgroundMedian)
            && result.BackgroundMedian > reference.Background * 1.10;
        return !brighterSky && (darker || moreSignal);
    }

    /// <summary>Do not start an improvement run while measurements merely follow a coherent forecast.</summary>
    public static bool IsUnexpectedImprovement(FrameQualityResult result) {
        if (result.ImageEvidence.PhotometryAvailable && result.ImageEvidence.RelativeFlux >= 1.15) return true;
        if (result.StarTrendUsable && result.StarTrendR2 >= .85 && result.StarTrendExpected > 0
            && result.StarCount <= result.StarTrendExpected * 1.15) return false;
        return true;
    }

    public static bool FollowsGradualContinuation(IReadOnlyList<Candidate> recent) =>
        FitGradual(recent, c => c.Stars).Usable;

    public static bool HasGradualReferenceTrajectory(IReadOnlyList<Candidate> candidates) {
        if (FitGradual(candidates, c => c.Stars).Usable) return true;
        for (int i = 1; i < candidates.Count; i++)
            if (candidates[i - 1].Stars <= 0 || Math.Abs(candidates[i].Stars / (double)candidates[i - 1].Stars - 1) > .10) return false;
        return FitGradual(candidates, c => c.Background).Usable;
    }

    private static (bool Usable, double Intercept, double Slope) FitGradual(IReadOnlyList<Candidate> source, Func<Candidate, double> value) {
        if (source.Count < 3 || source.Any(c => c.Time == default)) return default;
        var ordered = source.OrderBy(c => c.Time).ThenBy(c => c.FrameIndex).ToArray();
        var y = ordered.Select(value).ToArray();
        if (y.Any(v => !double.IsFinite(v) || v <= 0)) return default;
        var x = ordered.Select(c => (c.Time - ordered[0].Time).TotalMinutes).ToArray();
        if (x[^1] <= 0) return default;
        for (int i = 1; i < y.Length; i++) if (Math.Abs(y[i] / y[i - 1] - 1) > .10) return default;
        double mx = x.Average(), my = y.Average();
        double xx = x.Sum(v => (v - mx) * (v - mx));
        if (xx <= 0) return default;
        double slope = x.Select((v, i) => (v - mx) * (y[i] - my)).Sum() / xx;
        double intercept = my - slope * mx;
        double total = y.Sum(v => (v - my) * (v - my));
        double residual = x.Select((v, i) => Math.Pow(y[i] - intercept - slope * v, 2)).Sum();
        bool usable = total > 0 && 1 - residual / total >= .95 && Math.Abs(slope * x[^1] / my) >= .02;
        return (usable, intercept, slope);
    }

    private static double Median(IEnumerable<double> values) {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return double.NaN;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
