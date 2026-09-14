using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public static class ExposureAssessment {
    public static bool CanTrainBaseline(FrameQualityResult result) => (result.Status is FrameStatus.Accepted or FrameStatus.Learning) && !result.ReviewReasons.Contains("TRANSPARENCY_CHANGE");

    public static bool IsGuideRejectCandidate(GuideExposureMetrics guide, QualitySettings settings) => guide?.HasData == true && (
        (settings.EnableGuideRms && guide.RmsArcsec > settings.MaxGuideRms)
        || (settings.EnableHardExcursion && guide.MaxExcursionArcsec >= settings.HardExcursionThreshold)
        || (settings.EnableSustainedExcursion && guide.MaxExcursionArcsec > settings.ExcursionThreshold && guide.MaxSustainedExcursionSeconds >= settings.ExcursionMinimumDuration));

    public static void Apply(FrameQualityInput input, FrameQualityResult result, QualitySettings settings) {
        var image = input.ImageEvidence ?? new ImageEvidence();
        result.ImageEvidence = image;
        result.AssessmentVersion = "1.4.1.1";
        result.GuideEvidence = input.Guide?.Series?.Take(512).ToList() ?? new();
        result.ThresholdsUsed = $"RMS>{settings.MaxGuideRms:R}; peak>={settings.HardExcursionThreshold:R}; sustained>{settings.ExcursionThreshold:R} for {settings.ExcursionMinimumDuration:R}s; stars -{settings.MaxStarLossPercent:R}%; background +{settings.MaxBackgroundIncreasePercent:R}/-{settings.MaxBackgroundDecreasePercent:R}%";
        bool guideRule = result.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool extreme = (settings.EnableHardExcursion && result.MaxGuideExcursionArcsec >= Math.Max(6, settings.HardExcursionThreshold * 2))
            || (settings.EnableSustainedExcursion && result.SustainedGuideExcursionSeconds >= Math.Max(settings.ExcursionMinimumDuration, input.ExposureSeconds * 0.10))
            || (settings.EnableGuideRms && result.GuideRmsArcsec > settings.MaxGuideRms);
        bool confirmedShape = settings.ImageEvidenceEnabled && image.Compromised;
        bool extendedRecovery = image.HasExtendedRescueEvidence
            && (!settings.EnableGuideRms || result.GuideRmsArcsec <= settings.MaxGuideRms*3)
            && (!settings.EnableSustainedExcursion || result.SustainedGuideExcursionSeconds < Math.Max(settings.ExcursionMinimumDuration,input.ExposureSeconds*.25));
        if (guideRule && settings.ImageEvidenceEnabled && image.HasRescueMargin && image.CompromisedRegions==0 && (!image.ExtendedAttempted || image.ExtendedAvailable)
            && (!extreme || extendedRecovery)) {
            result.GuideFalsePositive = true;
            result.ReviewReasons.AddRange(result.RejectReasons.Where(x => x.Contains("GUIDE", StringComparison.Ordinal)));
            result.RejectReasons.RemoveAll(x => x.Contains("GUIDE", StringComparison.Ordinal));
        }
        if (confirmedShape) result.RejectReasons.Add("STAR_SHAPE_CONFIRMED");
        if (settings.ImageEvidenceEnabled && settings.EnableStarCount && settings.VerifyStarCountWithFlux && image.PhotometryAvailable) {
            if (result.RejectReasons.Contains("STAR_COUNT_DROP") && image.RelativeFlux < 1-settings.MaxMeasuredFluxLossPercent/100) result.RejectReasons.Add("STELLAR_FLUX_LOSS");
            else if (result.RejectReasons.Contains("STAR_COUNT_DROP") && image.HasRescueMargin && image.ExtendedAvailable && image.VerifiedRegions>=4 && image.CompromisedRegions==0
                && (!settings.RejectSignalDegradation || !double.IsFinite(result.BackgroundDeviationPercent) || result.BackgroundDeviationPercent < 3)
                && image.RelativeFlux >= 1-Math.Min(20,settings.MaxMeasuredFluxLossPercent-5)/100) {
                result.RejectReasons.Remove("STAR_COUNT_DROP");result.ReviewReasons.Add("STAR_COUNT_DROP");result.StarCountFalsePositive=true;
            }
        }
        if (settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && image.PhotometryAvailable) {
            double loss = Math.Max(0, (1-image.RelativeFlux)*100);
            bool fewerStars = double.IsFinite(result.StarDeviationPercent) && result.StarDeviationPercent <= -Math.Max(10,settings.MaxStarLossPercent/2);
            bool brighterSky = double.IsFinite(result.BackgroundDeviationPercent) && result.BackgroundDeviationPercent >= 3;
            if (loss > settings.MaxMeasuredFluxLossPercent) {
                if (!result.RejectReasons.Contains("STELLAR_FLUX_LOSS")) result.RejectReasons.Add("STELLAR_FLUX_LOSS");
            }
            else if (loss >= settings.MaxCloudSignalLossPercent && fewerStars && brighterSky) result.RejectReasons.Add("SKY_SIGNAL_LOSS");
            // A modest measured loss is kept for review but must not teach the next baseline.
            if ((loss >= 10
                && ((double.IsFinite(result.StarDeviationPercent) && result.StarDeviationPercent <= -10)
                    || (double.IsFinite(result.BackgroundDeviationPercent) && result.BackgroundDeviationPercent >= 2)))
                || (brighterSky && result.StarDeviationPercent <= -10))
                result.ReviewReasons.Add("TRANSPARENCY_CHANGE");
        }
        if(settings.ImageEvidenceEnabled && !image.Available) result.ReviewReasons.Add("STELLAR_CHECK_UNAVAILABLE");
        if(settings.ImageEvidenceEnabled && image.Available && !image.Compromised && !image.HasRescueMargin)result.ReviewReasons.Add("BORDERLINE_STAR_SHAPE");

        // Duration is relative to the shutter interval. A lone peak must not zero image quality.
        if (input.Guide?.HasData == true && (settings.EnableSustainedExcursion || settings.EnableHardExcursion)) {
            double affected = input.ExposureSeconds > 0 ? result.SustainedGuideExcursionSeconds / input.ExposureSeconds : 0;
            result.StabilityQuality = Math.Clamp(100 - 300 * affected, 0, 100);
        }
        var scores = new[] { result.GuideFalsePositive ? null : result.GuidingQuality, result.GuideFalsePositive ? null : result.StabilityQuality,
                result.StarCountFalsePositive ? null : result.TransparencyQuality, result.BackgroundQuality }
            .Where(x => x.HasValue).Select(x => x.Value).ToArray();
        if (image.Available) scores = scores.Append(image.Quality).ToArray();
        if (settings.ImageEvidenceEnabled && image.PhotometryAvailable) {
            scores=scores.Append(Math.Clamp(image.RelativeFlux*100,0,100)).ToArray();
            if(double.IsFinite(image.FwhmRatio))scores=scores.Append(Math.Clamp(100-Math.Max(0,image.FwhmRatio-1.1)*100,0,100)).ToArray();
        }
        result.OverallQuality = scores.Length > 0 ? scores.Average() * 0.75 + scores.Min() * 0.25 : 100;
        if (result.RejectReasons.Count > 0) result.Status = FrameStatus.Rejected;
        else if (result.Status != FrameStatus.Learning) result.Status = result.ReviewReasons.Count > 0 || result.OverallQuality < 65 ? FrameStatus.Warning : FrameStatus.Accepted;
        // A rejected pre-baseline frame that is now retained is still learning, never fully accepted.
        if (result.RejectReasons.Count == 0 && ((settings.EnableStarCount && input.Baseline?.StarsReady != true)
            || (settings.EnableBackground && input.Baseline?.BackgroundReady != true))) result.Status = FrameStatus.Learning;

        result.DecisionSummary = result.Status == FrameStatus.Rejected
            ? confirmedShape ? image.RemotePeakConfirmed ? "Rejected: repeated distant stellar image." : "Rejected: star-shape limit exceeded."
                : result.RejectReasons.Contains("SKY_SIGNAL_LOSS") ? "Rejected: brighter sky with fewer and fainter stars."
                : result.RejectReasons.Contains("STELLAR_FLUX_LOSS") ? "Rejected: measured stellar signal loss."
                : guideRule && !result.GuideFalsePositive && extreme ? "Rejected: guide limit exceeded; extended stellar recovery not confirmed."
                : guideRule && !result.GuideFalsePositive && settings.ImageEvidenceEnabled && image.Available && !image.HasRescueMargin ? $"Rejected: borderline stellar shape. Eccentricity {image.Eccentricity:0.00} must be below {image.Limits.RescueMaxEccentricity:0.00} for automatic rescue; original guide rejection retained."
                : guideRule && !result.GuideFalsePositive ? settings.ImageEvidenceEnabled ? "Rejected: stellar check inconclusive; original guide limit retained." : "Rejected: guide limit exceeded; stellar analysis is disabled."
                : result.GuideFalsePositive ? "Guide false positive cleared, but an independent star-count or background rule still rejects this frame."
                : "Rejected: star-count or background limit exceeded."
            : result.GuideFalsePositive ? "Kept for review: stellar verification cleared the guide flag."
            : result.StarCountFalsePositive ? "Kept for review: matched stellar signal is within recovery limits."
            : result.Status == FrameStatus.Learning ? "Learning: building the reference for this target and imaging setup."
            : result.ReviewReasons.Contains("TRANSPARENCY_CHANGE") ? "Kept for review: changing sky or stellar signal; reference unchanged."
            : result.ReviewReasons.Contains("STELLAR_CHECK_UNAVAILABLE") ? "Kept for review: stellar check unavailable."
            : result.ReviewReasons.Contains("BORDERLINE_STAR_SHAPE") ? "Kept for review: borderline stellar shape."
            : result.Status == FrameStatus.Warning ? "Kept for review: reduced image quality."
            : "Kept: all active checks passed.";
        result.ThresholdsUsed += $"; secondPass={settings.ImageEvidenceEnabled}; eccentricity>={image.Limits.MaxEccentricity:R} in >={image.Limits.DeformedFraction:P0} stars; tail>={image.Limits.MaxTailPercent:R}%; doublePeak>={image.Limits.MaxDoublePeakPercent:R}%; minStars={image.Limits.MinimumStars}; targetStars={image.Limits.TargetStars}; rescue requires RMS<={settings.MaxGuideRms:R} when enabled, peak<{Math.Max(6, settings.HardExcursionThreshold*2):R} when enabled, sustained<{Math.Max(settings.ExcursionMinimumDuration,input.ExposureSeconds*.1):R}s when enabled";
        result.ThresholdsUsed += $"; rescueEccentricity<{image.Limits.RescueMaxEccentricity:R} (0.05 margin)";
        result.ThresholdsUsed += $"; signalReject={settings.RejectSignalDegradation}; directFluxLoss>{settings.MaxMeasuredFluxLossPercent:R}%; combinedFluxLoss>={settings.MaxCloudSignalLossPercent:R}% with starLoss>={Math.Max(10,settings.MaxStarLossPercent/2):R}% and backgroundRise>=3%; reference protection at >=10% flux loss plus stars -10% or background +2%, or stars -10% with background +3%; count rescue requires background rise<3% when signal rejection enabled; referenceAge<=120min";
        result.ThresholdsUsed += $"; extendedRecovery requires >=4/5 regions, covered guide peak, RMS<=3x limit, sustained<25% exposure, tail<half limit; remotePeak>={image.Limits.MaxRemotePeakPercent:R}% in >=60% stars; measuredFluxEnabled={settings.VerifyStarCountWithFlux}; measuredFluxLoss>{settings.MaxMeasuredFluxLossPercent:R}%; countRescueFluxLoss<={Math.Min(20,settings.MaxMeasuredFluxLossPercent-5):R}%";
    }
}
