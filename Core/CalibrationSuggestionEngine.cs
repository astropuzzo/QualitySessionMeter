using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class CalibrationSuggestionEngine {
    public CalibrationSuggestion Evaluate(IReadOnlyList<FrameQualityResult> frames, QualitySettings settings) {
        if (frames == null || frames.Count == 0) return new CalibrationSuggestion();

        var last = frames.LastOrDefault(x => x.Status is FrameStatus.Accepted or FrameStatus.Warning);
        if (last == null) return new CalibrationSuggestion();

        var stable = frames
            .Where(x => SameContext(x, last))
            .Where(x => x.Status == FrameStatus.Accepted)
            .Where(x => x.OverallQuality >= 80)
            .Where(x => double.IsNaN(x.ConfidenceScore) || x.ConfidenceScore >= 70)
            .TakeLast(Math.Max(settings.CalibrationWindow, 8))
            .ToArray();

        if (stable.Length < settings.CalibrationWindow) {
            return new CalibrationSuggestion {
                Available = false,
                Context = ContextText(last),
                SampleCount = stable.Length,
                Reason = $"Need {settings.CalibrationWindow} stable accepted frames; {stable.Length} available."
            };
        }

        var guideRms = Finite(stable.Select(x => x.GuideRmsArcsec)).ToArray();
        var excursions = Finite(stable.Select(x => x.MaxGuideExcursionArcsec)).ToArray();
        var starLoss = Finite(stable.Select(x => Math.Max(0, -x.StarDeviationPercent))).ToArray();
        var bgHigh = Finite(stable.Select(x => Math.Max(0, x.BackgroundDeviationPercent))).ToArray();
        var bgLow = Finite(stable.Select(x => Math.Max(0, -x.BackgroundDeviationPercent))).ToArray();

        double guideMedian = Median(guideRms);
        double guideMad = Mad(guideRms, guideMedian);
        double suggestedRms = double.IsNaN(guideMedian)
            ? settings.MaxGuideRms
            : Clamp(guideMedian + Math.Max(0.25, 4 * guideMad), 0.60, settings.AutoSafetyMaxGuideRms);

        double p95Excursion = Percentile(excursions, 0.95);
        double suggestedExcursion = double.IsNaN(p95Excursion)
            ? settings.ExcursionThreshold
            : Clamp(Math.Max(1.50, p95Excursion * 1.50), 1.0, settings.AutoSafetyMaxExcursion);
        double suggestedHard = Clamp(
            Math.Max(suggestedExcursion * 2.0, double.IsNaN(p95Excursion) ? settings.HardExcursionThreshold : p95Excursion * 3.0),
            suggestedExcursion,
            settings.AutoSafetyMaxHardExcursion);

        double suggestedStars = SuggestedPercent(starLoss, 15, settings.AutoSafetyMaxStarLossPercent);
        double suggestedBgHigh = SuggestedPercent(bgHigh, 15, settings.AutoSafetyMaxBackgroundPercent);
        double suggestedBgLow = SuggestedPercent(bgLow, 15, settings.AutoSafetyMaxBackgroundPercent);

        return new CalibrationSuggestion {
            Available = true,
            Context = ContextText(last),
            SampleCount = stable.Length,
            SuggestedMaxGuideRms = suggestedRms,
            SuggestedExcursionThreshold = suggestedExcursion,
            SuggestedHardExcursionThreshold = suggestedHard,
            SuggestedMaxStarLossPercent = suggestedStars,
            SuggestedBackgroundIncreasePercent = suggestedBgHigh,
            SuggestedBackgroundDecreasePercent = suggestedBgLow,
            Reason = "Derived only from high-quality ACCEPTED frames in the same target/filter/exposure/camera context; rejected/warning frames do not train calibration."
        };
    }

    private static double SuggestedPercent(double[] values, double floor, double cap) {
        double p95 = Percentile(values, 0.95);
        if (double.IsNaN(p95)) return floor;
        return Clamp(Math.Max(floor, p95 * 3.0 + 3.0), floor, cap);
    }

    private static bool SameContext(FrameQualityResult a, FrameQualityResult b) =>
        string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Filter, b.Filter, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(a.ExposureSeconds - b.ExposureSeconds) < 0.001 &&
        a.Gain == b.Gain && a.BinX == b.BinX && a.BinY == b.BinY &&
        string.Equals(a.Camera, b.Camera, StringComparison.OrdinalIgnoreCase);

    private static string ContextText(FrameQualityResult x) =>
        $"{x.Target} · {x.Filter} · {x.ExposureSeconds:0.###}s · gain {x.Gain} · {x.BinX}x{x.BinY} · {x.Camera}";

    private static IEnumerable<double> Finite(IEnumerable<double> values) =>
        values.Where(x => !double.IsNaN(x) && !double.IsInfinity(x) && x >= 0);

    private static double Median(double[] values) => Percentile(values, 0.5);

    private static double Mad(double[] values, double median) {
        if (values.Length == 0 || double.IsNaN(median)) return 0;
        return Median(values.Select(x => Math.Abs(x - median)).ToArray());
    }

    private static double Percentile(double[] values, double p) {
        if (values == null || values.Length == 0) return double.NaN;
        var sorted = values.OrderBy(x => x).ToArray();
        double index = Math.Clamp(p, 0, 1) * (sorted.Length - 1);
        int lo = (int)Math.Floor(index);
        int hi = (int)Math.Ceiling(index);
        if (lo == hi) return sorted[lo];
        double t = index - lo;
        return sorted[lo] * (1 - t) + sorted[hi] * t;
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
