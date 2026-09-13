using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public static class ExposureAssessment {
    public static bool IsGuideRejectCandidate(GuideExposureMetrics guide, QualitySettings settings) => guide?.HasData == true && (
        (settings.EnableGuideRms && guide.RmsArcsec > settings.MaxGuideRms)
        || (settings.EnableHardExcursion && guide.MaxExcursionArcsec >= settings.HardExcursionThreshold)
        || (settings.EnableSustainedExcursion && guide.MaxExcursionArcsec > settings.ExcursionThreshold && guide.MaxSustainedExcursionSeconds >= settings.ExcursionMinimumDuration));

    public static void Apply(FrameQualityInput input, FrameQualityResult result, QualitySettings settings) {
        var image = input.ImageEvidence ?? new ImageEvidence();
        result.ImageEvidence = image;
        result.AssessmentVersion = "1.4";
        result.GuideEvidence = input.Guide?.Series?.Take(512).ToList() ?? new();
        result.ThresholdsUsed = $"RMS>{settings.MaxGuideRms:R}; peak>={settings.HardExcursionThreshold:R}; sustained>{settings.ExcursionThreshold:R} for {settings.ExcursionMinimumDuration:R}s; stars -{settings.MaxStarLossPercent:R}%; background +{settings.MaxBackgroundIncreasePercent:R}/-{settings.MaxBackgroundDecreasePercent:R}%";
        bool guideRule = result.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool extreme = (settings.EnableHardExcursion && result.MaxGuideExcursionArcsec >= Math.Max(6, settings.HardExcursionThreshold * 2))
            || (settings.EnableSustainedExcursion && result.SustainedGuideExcursionSeconds >= Math.Max(settings.ExcursionMinimumDuration, input.ExposureSeconds * 0.10))
            || (settings.EnableGuideRms && result.GuideRmsArcsec > settings.MaxGuideRms);
        bool confirmedShape = settings.ImageEvidenceEnabled && image.Compromised;
        if (guideRule && settings.ImageEvidenceEnabled && image.Available && !extreme && !confirmedShape) {
            result.GuideFalsePositive = true;
            result.ReviewReasons.AddRange(result.RejectReasons.Where(x => x.Contains("GUIDE", StringComparison.Ordinal)));
            result.RejectReasons.RemoveAll(x => x.Contains("GUIDE", StringComparison.Ordinal));
        }
        if (confirmedShape && guideRule) result.RejectReasons.Add("STAR_SHAPE_CONFIRMED");

        // Duration is relative to the shutter interval. A lone peak must not zero image quality.
        if (input.Guide?.HasData == true && (settings.EnableSustainedExcursion || settings.EnableHardExcursion)) {
            double affected = input.ExposureSeconds > 0 ? result.SustainedGuideExcursionSeconds / input.ExposureSeconds : 0;
            result.StabilityQuality = Math.Clamp(100 - 300 * affected, 0, 100);
        }
        var scores = new[] { result.GuidingQuality, result.StabilityQuality, result.TransparencyQuality, result.BackgroundQuality }
            .Where(x => x.HasValue).Select(x => x.Value).ToArray();
        if (image.Available) scores = scores.Append(image.Quality).ToArray();
        result.OverallQuality = scores.Length > 0 ? scores.Average() * 0.75 + scores.Min() * 0.25 : 100;
        if (result.RejectReasons.Count > 0) result.Status = FrameStatus.Rejected;
        else if (result.Status != FrameStatus.Learning) result.Status = result.ReviewReasons.Count > 0 || result.OverallQuality < 65 ? FrameStatus.Warning : FrameStatus.Accepted;
        // A rejected pre-baseline frame that is now retained is still learning, never fully accepted.
        if (result.RejectReasons.Count == 0 && ((settings.EnableStarCount && input.Baseline?.StarsReady != true)
            || (settings.EnableBackground && input.Baseline?.BackgroundReady != true))) result.Status = FrameStatus.Learning;

        result.DecisionSummary = result.Status == FrameStatus.Rejected
            ? confirmedShape && guideRule ? "Rejected: guide failure confirmed by star shapes outside your tolerances."
                : guideRule && extreme ? "Rejected: exposure RMS, prolonged disturbance or extreme peak exceeds the rescue safety limits; central stars cannot certify the whole exposure."
                : guideRule ? settings.ImageEvidenceEnabled ? "Rejected: stellar check inconclusive; original guide limit retained." : "Rejected: guide limit exceeded; stellar second pass is disabled."
                : result.GuideFalsePositive ? "Guide false positive cleared, but an independent star-count or background rule still rejects this frame."
                : "Rejected: star-count or background limit exceeded."
            : result.GuideFalsePositive ? "Kept for review: guide false positive. Measured stars are within tolerance; no BAD_ prefix or move is applied."
            : result.Status == FrameStatus.Learning ? "Learning: building the reference for this target and imaging setup."
            : result.Status == FrameStatus.Warning ? "Kept for review: low diagnostic score; no enabled rejection rule failed."
            : "Kept: no enabled rejection rule failed. Stellar second pass was not needed.";
        result.ThresholdsUsed += $"; secondPass={settings.ImageEvidenceEnabled}; eccentricity>={image.Limits.MaxEccentricity:R} in >={image.Limits.DeformedFraction:P0} stars; tail>={image.Limits.MaxTailPercent:R}%; doublePeak>={image.Limits.MaxDoublePeakPercent:R}%; minStars={image.Limits.MinimumStars}; targetStars={image.Limits.TargetStars}; rescue requires RMS<={settings.MaxGuideRms:R} when enabled, peak<{Math.Max(6, settings.HardExcursionThreshold*2):R} when enabled, sustained<{Math.Max(settings.ExcursionMinimumDuration,input.ExposureSeconds*.1):R}s when enabled";
    }
}
