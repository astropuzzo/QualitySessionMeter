using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// V2 confidence model. Confidence answers "how strongly does the available evidence support
/// this classification?" and is deliberately independent from both OverallQuality and the hard
/// ACCEPT/REJECT rules.
/// </summary>
public sealed class ConfidenceEngine {
    public void Apply(FrameQualityInput input, FrameQualityResult result, QualitySettings settings) {
        if (result == null) return;

        if (result.Status == FrameStatus.Error) {
            result.ConfidenceScore = 0;
            result.ConfidenceDataCompleteness = 0;
            result.ConfidenceBaselineMaturity = ComputeBaselineMaturity(input, settings);
            result.ConfidenceThresholdSeparation = 0;
            result.ConfidenceAgreement = 0;
            result.ConfidenceReason = "Required analysis data is unavailable; no confident quality judgment can be made.";
            return;
        }

        double dataCompleteness = ComputeDataCompleteness(input, settings);
        double baselineMaturity = ComputeBaselineMaturity(input, settings);

        var allBoundaryStrengths = new List<double>();
        var failedBoundaryStrengths = new List<double>();

        AddGuideRmsEvidence(input, result, settings, allBoundaryStrengths, failedBoundaryStrengths);
        AddGuideStabilityEvidence(input, result, settings, allBoundaryStrengths, failedBoundaryStrengths);
        AddStarEvidence(input, result, settings, allBoundaryStrengths, failedBoundaryStrengths);
        AddBackgroundEvidence(result, settings, allBoundaryStrengths, failedBoundaryStrengths);

        double thresholdSeparation;
        if (result.Status == FrameStatus.Rejected && failedBoundaryStrengths.Count > 0) {
            // A hard rejection is supported by the strongest actual failed rule. Agreement between
            // independent channels is handled separately below.
            thresholdSeparation = failedBoundaryStrengths.Max();
        } else if (allBoundaryStrengths.Count > 0) {
            // For a non-rejected frame, the metric closest to a decision boundary is the weak link.
            thresholdSeparation = allBoundaryStrengths.Min();
        } else {
            thresholdSeparation = 50;
        }

        int abnormalChannels = CountIndependentAbnormalChannels(result);
        double agreement = ComputeAgreement(result.Status, abnormalChannels);

        double confidence =
            dataCompleteness * 0.20 +
            baselineMaturity * 0.20 +
            thresholdSeparation * 0.50 +
            agreement * 0.10;

        // During LEARNING there is no mature adaptive reference yet. Even perfect guide data must
        // not produce a misleadingly high whole-frame confidence score.
        if (result.Status == FrameStatus.Learning) {
            double learningProgress = ComputeLearningProgress(input, settings);
            double learningCap = 25.0 + 40.0 * learningProgress;
            confidence = Math.Min(confidence, learningCap);
        }

        // WARNING represents near-boundary/low-quality evidence by design. It should not be
        // advertised as VERY HIGH confidence until V2 has stronger temporal context.
        if (result.Status == FrameStatus.Warning) {
            confidence = Math.Min(confidence, 72.0);
        }

        result.ConfidenceScore = Clamp(confidence);
        result.ConfidenceDataCompleteness = Clamp(dataCompleteness);
        result.ConfidenceBaselineMaturity = Clamp(baselineMaturity);
        result.ConfidenceThresholdSeparation = Clamp(thresholdSeparation);
        result.ConfidenceAgreement = Clamp(agreement);
        result.ConfidenceReason = BuildReason(input, result, settings, abnormalChannels);
    }

    private static double ComputeDataCompleteness(FrameQualityInput input, QualitySettings settings) {
        var components = new List<double>();

        bool guideRequired = settings.EnableGuideRms || settings.EnableSustainedExcursion || settings.EnableHardExcursion;
        if (guideRequired) {
            if (input.Guide?.HasData != true) {
                components.Add(0);
            } else if (input.Guide.Samples >= 5) {
                components.Add(100);
            } else if (input.Guide.Samples >= 2) {
                components.Add(85);
            } else {
                components.Add(60);
            }
        }

        if (settings.EnableStarCount) {
            components.Add(input.StarCount >= 0 ? 100 : 0);
        }

        if (settings.EnableBackground) {
            bool valid = !double.IsNaN(input.BackgroundMedian) && !double.IsInfinity(input.BackgroundMedian);
            components.Add(valid ? 100 : 0);
        }

        return components.Count == 0 ? 100 : components.Average();
    }

    private static double ComputeBaselineMaturity(FrameQualityInput input, QualitySettings settings) {
        var components = new List<double>();
        int window = Math.Max(1, settings.BaselineWindow);

        if (settings.EnableStarCount) {
            components.Add(100.0 * Math.Min(window, Math.Max(0, input.Baseline?.StarSamples ?? 0)) / window);
        }

        if (settings.EnableBackground) {
            components.Add(100.0 * Math.Min(window, Math.Max(0, input.Baseline?.BackgroundSamples ?? 0)) / window);
        }

        return components.Count == 0 ? 100 : components.Average();
    }

    private static double ComputeLearningProgress(FrameQualityInput input, QualitySettings settings) {
        var progress = new List<double>();
        int minimum = Math.Max(1, settings.MinimumLearningFrames);

        if (settings.EnableStarCount) {
            progress.Add(Math.Min(1.0, Math.Max(0, input.Baseline?.StarSamples ?? 0) / (double)minimum));
        }

        if (settings.EnableBackground) {
            progress.Add(Math.Min(1.0, Math.Max(0, input.Baseline?.BackgroundSamples ?? 0) / (double)minimum));
        }

        return progress.Count == 0 ? 1.0 : progress.Min();
    }

    private static void AddGuideRmsEvidence(
        FrameQualityInput input,
        FrameQualityResult result,
        QualitySettings settings,
        List<double> all,
        List<double> failed) {

        if (!settings.EnableGuideRms || input.Guide?.HasData != true) return;

        double strength = BoundarySeparation(input.Guide.RmsArcsec, settings.MaxGuideRms);
        all.Add(strength);
        if (result.RejectReasons.Contains("GUIDE_RMS", StringComparer.Ordinal)) failed.Add(strength);
    }

    private static void AddGuideStabilityEvidence(
        FrameQualityInput input,
        FrameQualityResult result,
        QualitySettings settings,
        List<double> all,
        List<double> failed) {

        if (input.Guide?.HasData != true) return;

        if (settings.EnableSustainedExcursion) {
            double strength = input.Guide.MaxExcursionArcsec <= settings.ExcursionThreshold
                ? 100
                : BoundarySeparation(input.Guide.MaxSustainedExcursionSeconds, settings.ExcursionMinimumDuration);
            all.Add(strength);
            if (result.RejectReasons.Contains("SUSTAINED_GUIDE_EXCURSION", StringComparer.Ordinal)) failed.Add(strength);
        }

        if (settings.EnableHardExcursion) {
            double strength = BoundarySeparation(input.Guide.MaxExcursionArcsec, settings.HardExcursionThreshold);
            all.Add(strength);
            if (result.RejectReasons.Contains("HARD_GUIDE_EXCURSION", StringComparer.Ordinal)) failed.Add(strength);
        }
    }

    private static void AddStarEvidence(
        FrameQualityInput input,
        FrameQualityResult result,
        QualitySettings settings,
        List<double> all,
        List<double> failed) {

        if (!settings.EnableStarCount || input.Baseline?.StarsReady != true ||
            double.IsNaN(result.StarDeviationPercent)) return;

        double loss = Math.Max(0, -result.StarDeviationPercent);
        double strength = BoundarySeparation(loss, settings.MaxStarLossPercent);
        all.Add(strength);
        if (result.RejectReasons.Contains("STAR_COUNT_DROP", StringComparer.Ordinal)) failed.Add(strength);
    }

    private static void AddBackgroundEvidence(
        FrameQualityResult result,
        QualitySettings settings,
        List<double> all,
        List<double> failed) {

        if (!settings.EnableBackground || double.IsNaN(result.BackgroundDeviationPercent)) return;

        double threshold = result.BackgroundDeviationPercent >= 0
            ? settings.MaxBackgroundIncreasePercent
            : settings.MaxBackgroundDecreasePercent;
        double strength = BoundarySeparation(Math.Abs(result.BackgroundDeviationPercent), threshold);
        all.Add(strength);

        if (result.RejectReasons.Contains("BACKGROUND_HIGH", StringComparer.Ordinal) ||
            result.RejectReasons.Contains("BACKGROUND_LOW", StringComparer.Ordinal)) {
            failed.Add(strength);
        }
    }

    /// <summary>
    /// Returns 50 at a configured threshold and approaches 100 as evidence moves clearly away from
    /// the boundary on either side. A failed rule must move farther beyond the threshold to earn high
    /// confidence; a value barely over the line therefore remains moderate evidence.
    /// </summary>
    private static double BoundarySeparation(double value, double threshold) {
        if (double.IsNaN(value) || double.IsInfinity(value) || threshold <= 0) return 50;

        double ratio = Math.Max(0, value) / threshold;
        if (ratio <= 1.0) {
            return 50.0 + 50.0 * Math.Min(1.0, (1.0 - ratio) / 0.50);
        }

        return 50.0 + 50.0 * Math.Min(1.0, (ratio - 1.0) / 0.25);
    }

    private static int CountIndependentAbnormalChannels(FrameQualityResult result) {
        bool guide = result.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool stars = result.RejectReasons.Contains("STAR_COUNT_DROP", StringComparer.Ordinal);
        bool background = result.RejectReasons.Contains("BACKGROUND_HIGH", StringComparer.Ordinal) ||
                          result.RejectReasons.Contains("BACKGROUND_LOW", StringComparer.Ordinal);
        return (guide ? 1 : 0) + (stars ? 1 : 0) + (background ? 1 : 0);
    }

    private static double ComputeAgreement(FrameStatus status, int abnormalChannels) {
        if (status == FrameStatus.Rejected) {
            return abnormalChannels switch {
                >= 3 => 100,
                2 => 95,
                1 => 75,
                _ => 60
            };
        }

        return status switch {
            FrameStatus.Warning => 65,
            FrameStatus.Learning => 70,
            FrameStatus.Accepted => 85,
            _ => 0
        };
    }

    private static string BuildReason(
        FrameQualityInput input,
        FrameQualityResult result,
        QualitySettings settings,
        int abnormalChannels) {

        if (result.Status == FrameStatus.Learning) {
            int minimum = Math.Max(1, settings.MinimumLearningFrames);
            var counts = new List<int>();
            if (settings.EnableStarCount) counts.Add(input.Baseline?.StarSamples ?? 0);
            if (settings.EnableBackground) counts.Add(input.Baseline?.BackgroundSamples ?? 0);
            int current = counts.Count == 0 ? minimum : counts.Min();
            return $"Adaptive baseline is still learning ({Math.Min(current, minimum)}/{minimum} minimum samples); confidence is capped until the reference is ready.";
        }

        if (result.Status == FrameStatus.Warning) {
            return "At least one metric is close to a decision boundary; warning confidence is intentionally limited until temporal evidence accumulates.";
        }

        if (result.Status == FrameStatus.Rejected) {
            if (abnormalChannels >= 2) {
                return $"{abnormalChannels} independent abnormal channels agree, strengthening the rejection diagnosis.";
            }
            if (result.ConfidenceThresholdSeparation >= 90) {
                return "A failed rule is far beyond its configured threshold, providing strong single-channel evidence.";
            }
            if (result.ConfidenceThresholdSeparation < 70) {
                return "The rejection is close to its configured threshold; the hard rule still applies, but cause attribution should be treated cautiously.";
            }
            return "A configured hard rule failed with clear single-channel evidence.";
        }

        if (result.ConfidenceBaselineMaturity < 75) {
            return "The frame is clean, but the adaptive baseline is still maturing; confidence will increase as more accepted frames accumulate.";
        }
        if (result.ConfidenceThresholdSeparation < 70) {
            return "The frame is accepted, but at least one metric is relatively close to a configured threshold.";
        }
        return "Available evidence is complete, mature and well separated from configured decision thresholds.";
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 100.0);
}
