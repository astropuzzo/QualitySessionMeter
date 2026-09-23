using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Validates the frames that form a star-count/background reference. Frames collected while a
/// context is still LEARNING are provisional: they cannot be compared with a reference that does not
/// exist yet, so they must not define "normal" or count as valid until they agree with one another.
/// </summary>
public static class ReferenceValidation {
    public readonly record struct Candidate(int FrameIndex, int Stars, double Background);

    public readonly record struct Reference(double Stars, double Background, int Samples) {
        public BaselineSnapshot ToSnapshot() => new() {
            StarSamples = Samples,
            BackgroundSamples = Samples,
            StarsReady = true,
            BackgroundReady = true,
            StarMedian = Stars,
            BackgroundMedian = Background
        };
    }

    /// <summary>
    /// Clouds and haze only remove stars, so the star reference is the median of the better half of
    /// the candidates. A minority of degraded frames cannot pull it down. The background reference is
    /// the median of the frames that pass the star-count limit against that level.
    /// </summary>
    public static Reference Build(IReadOnlyList<Candidate> candidates, QualitySettings settings) {
        if (candidates == null || candidates.Count == 0) return new Reference(double.NaN, double.NaN, 0);

        double starRef = double.NaN;
        var withStars = candidates.Where(c => c.Stars > 0).Select(c => (double)c.Stars).OrderByDescending(x => x).ToArray();
        if (settings.EnableStarCount && withStars.Length > 0) {
            starRef = Median(withStars.Take((withStars.Length + 1) / 2));
        }

        double minimumStars = double.IsFinite(starRef) ? starRef * (1 - settings.MaxStarLossPercent / 100.0) : double.NegativeInfinity;
        var backgrounds = candidates
            .Where(c => double.IsFinite(c.Background) && c.Background > 0 && (!double.IsFinite(starRef) || c.Stars >= minimumStars))
            .Select(c => c.Background)
            .ToArray();
        double backgroundRef = backgrounds.Length > 0 ? Median(backgrounds) : double.NaN;
        return new Reference(starRef, backgroundRef, candidates.Count);
    }

    /// <summary>
    /// True when the validated reference would itself fail the star-count rule against this frame:
    /// the frame has so many more stars, without a brighter sky, that the reference must have been
    /// formed in worse conditions.
    /// </summary>
    public static bool ShowsBetterConditions(FrameQualityResult result, Reference reference, QualitySettings settings) {
        if (!settings.EnableStarCount || result == null || result.StarCount <= 0) return false;
        if (!double.IsFinite(reference.Stars) || reference.Stars <= 0) return false;
        if (result.Status is FrameStatus.Learning or FrameStatus.Error) return false;
        // A darker sky is consistent with better transparency; every other failed rule disqualifies the frame.
        if (result.RejectReasons.Any(r => r != "BACKGROUND_LOW")) return false;
        if (double.IsFinite(reference.Background) && double.IsFinite(result.BackgroundMedian)
            && result.BackgroundMedian > reference.Background * (1 + settings.MaxBackgroundIncreasePercent / 100.0)) return false;
        return reference.Stars < result.StarCount * (1 - settings.MaxStarLossPercent / 100.0);
    }

    private static double Median(IEnumerable<double> values) {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return double.NaN;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
