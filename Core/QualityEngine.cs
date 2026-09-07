using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class QualityEngine {
    public FrameQualityResult Evaluate(FrameQualityInput input, QualitySettings settings) {
        var result = new FrameQualityResult {
            FrameIndex = input.FrameIndex,
            TimestampUtc = input.TimestampUtc,
            OriginalPath = input.OriginalPath,
            FinalPath = input.OriginalPath,
            Target = input.Target,
            Filter = input.Filter,
            ExposureSeconds = input.ExposureSeconds,
            Gain = input.Gain,
            BinX = input.BinX,
            BinY = input.BinY,
            Camera = input.Camera,
            StarCount = input.StarCount,
            BackgroundMedian = input.BackgroundMedian,
            GuideSamples = input.Guide?.Samples ?? 0,
            GuideRmsArcsec = input.Guide?.RmsArcsec ?? double.NaN,
            MaxGuideExcursionArcsec = input.Guide?.MaxExcursionArcsec ?? double.NaN,
            SustainedGuideExcursionSeconds = input.Guide?.MaxSustainedExcursionSeconds ?? double.NaN,
            StarBaseline = input.Baseline?.StarMedian ?? double.NaN,
            BackgroundBaseline = input.Baseline?.BackgroundMedian ?? double.NaN,
            MonitorOnly = settings.MonitorOnly
        };

        var reasons = new List<string>();
        var dataErrors = new List<string>();

        bool guideRequired = settings.EnableGuideRms || settings.EnableSustainedExcursion || settings.EnableHardExcursion;
        bool guideAvailable = input.Guide?.HasData == true;
        bool starDataAvailable = input.StarCount >= 0;
        bool backgroundDataAvailable = !double.IsNaN(input.BackgroundMedian) && !double.IsInfinity(input.BackgroundMedian);

        if (guideRequired && !guideAvailable) dataErrors.Add("GUIDE_DATA_UNAVAILABLE");
        if (settings.EnableStarCount && !starDataAvailable) dataErrors.Add("STAR_COUNT_UNAVAILABLE");
        if (settings.EnableBackground && !backgroundDataAvailable) dataErrors.Add("BACKGROUND_UNAVAILABLE");

        if (settings.EnableGuideRms && guideAvailable) {
            result.GuidingQuality = ScoreUpper(input.Guide.RmsArcsec, settings.MaxGuideRms);
            if (input.Guide.RmsArcsec > settings.MaxGuideRms) reasons.Add("GUIDE_RMS");
        }

        if (guideAvailable && (settings.EnableSustainedExcursion || settings.EnableHardExcursion)) {
            var peakScore = ScoreBetween(input.Guide.MaxExcursionArcsec, settings.ExcursionThreshold, settings.HardExcursionThreshold);
            var durationScore = settings.EnableSustainedExcursion
                ? ScoreUpper(input.Guide.MaxSustainedExcursionSeconds, settings.ExcursionMinimumDuration)
                : 100.0;
            result.StabilityQuality = Math.Min(peakScore, durationScore);

            if (settings.EnableSustainedExcursion &&
                input.Guide.MaxSustainedExcursionSeconds >= settings.ExcursionMinimumDuration &&
                input.Guide.MaxExcursionArcsec > settings.ExcursionThreshold) {
                reasons.Add("SUSTAINED_GUIDE_EXCURSION");
            }

            if (settings.EnableHardExcursion &&
                input.Guide.MaxExcursionArcsec >= settings.HardExcursionThreshold) {
                reasons.Add("HARD_GUIDE_EXCURSION");
            }
        }

        bool starsReady = !settings.EnableStarCount || input.Baseline?.StarsReady == true;
        if (settings.EnableStarCount && starsReady && starDataAvailable && input.Baseline.StarMedian > 0) {
            result.StarDeviationPercent = ((input.StarCount - input.Baseline.StarMedian) / input.Baseline.StarMedian) * 100.0;
            var loss = Math.Max(0, -result.StarDeviationPercent);
            result.TransparencyQuality = ScoreUpper(loss, settings.MaxStarLossPercent);
            if (loss > settings.MaxStarLossPercent) reasons.Add("STAR_COUNT_DROP");
        }

        bool backgroundReady = !settings.EnableBackground || input.Baseline?.BackgroundReady == true;
        if (settings.EnableBackground && backgroundReady && backgroundDataAvailable && input.Baseline.BackgroundMedian > 0) {
            result.BackgroundDeviationPercent =
                ((input.BackgroundMedian - input.Baseline.BackgroundMedian) / input.Baseline.BackgroundMedian) * 100.0;

            double threshold = result.BackgroundDeviationPercent >= 0
                ? settings.MaxBackgroundIncreasePercent
                : settings.MaxBackgroundDecreasePercent;

            result.BackgroundQuality = ScoreUpper(Math.Abs(result.BackgroundDeviationPercent), threshold);

            if (result.BackgroundDeviationPercent > settings.MaxBackgroundIncreasePercent) reasons.Add("BACKGROUND_HIGH");
            if (result.BackgroundDeviationPercent < -settings.MaxBackgroundDecreasePercent) reasons.Add("BACKGROUND_LOW");
        }

        var scores = new[] {
            result.GuidingQuality,
            result.StabilityQuality,
            result.TransparencyQuality,
            result.BackgroundQuality
        }.Where(x => x.HasValue).Select(x => x.Value).ToArray();

        result.OverallQuality = scores.Length == 0
            ? (dataErrors.Count > 0 ? 0.0 : 100.0)
            : Combine(scores, settings.WorstMetricWeight);

        result.RejectReasons = reasons.Distinct().ToList();

        if (dataErrors.Count > 0) {
            result.Status = FrameStatus.Error;
            result.ErrorMessage = string.Join(", ", dataErrors);
            result.ProbableCause = "ANALYSIS DATA UNAVAILABLE";
            return result;
        }

        bool learning = !starsReady || !backgroundReady;
        if (reasons.Count > 0) {
            result.Status = FrameStatus.Rejected;
        } else if (learning) {
            result.Status = FrameStatus.Learning;
        } else if (result.OverallQuality < 65) {
            result.Status = FrameStatus.Warning;
        } else {
            result.Status = FrameStatus.Accepted;
        }

        result.ProbableCause = ClassifyCause(result);
        return result;
    }

    private static double Combine(double[] scores, double worstWeight) {
        var worst = scores.Min();
        var average = scores.Average();
        return Clamp(worst * worstWeight + average * (1.0 - worstWeight), 0, 100);
    }

    private static double ScoreUpper(double value, double threshold) {
        if (double.IsNaN(value) || threshold <= 0) return 100;
        if (value <= threshold * 0.5) return 100;
        if (value <= threshold) {
            return 100 - 50 * ((value - threshold * 0.5) / (threshold * 0.5));
        }
        if (value >= threshold * 2) return 0;
        return 50 - 50 * ((value - threshold) / threshold);
    }

    private static double ScoreBetween(double value, double normalThreshold, double hardThreshold) {
        if (double.IsNaN(value)) return 100;
        if (value <= normalThreshold) return 100;
        if (hardThreshold <= normalThreshold) return value >= hardThreshold ? 0 : 100;
        if (value >= hardThreshold) return 0;
        return 100 * (1 - ((value - normalThreshold) / (hardThreshold - normalThreshold)));
    }

    private static string ClassifyCause(FrameQualityResult result) {
        bool guide = result.RejectReasons.Any(x => x.Contains("GUIDE"));
        bool stars = result.RejectReasons.Contains("STAR_COUNT_DROP");
        bool bright = result.RejectReasons.Contains("BACKGROUND_HIGH");
        bool dark = result.RejectReasons.Contains("BACKGROUND_LOW");

        if (guide && !stars && !bright && !dark) return "WIND / GUIDING DISTURBANCE";
        if (stars && bright && !guide) return "CLOUD / BRIGHT SKY EVENT";
        if (stars && dark && !guide) return "CLOUD / TRANSPARENCY LOSS";
        if (stars && !guide) return "CLOUD / TRANSPARENCY LOSS";
        if ((bright || dark) && !guide && !stars) return "BACKGROUND / HAZE EVENT";
        if (guide || stars || bright || dark) return "MIXED CONDITIONS";
        return "NORMAL";
    }

    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
}
