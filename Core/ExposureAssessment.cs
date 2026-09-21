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
        var image = (input.ImageEvidence ?? new ImageEvidence()) with {MinimumSessionSignalPercent=settings.MinimumSessionSignalPercent};
        result.ImageEvidence = image;
        result.AssessmentVersion = "1.4.2.1";
        result.GuideEvidence = input.Guide?.Series?.Take(512).ToList() ?? new();
        result.ThresholdsUsed = $"RMS>{settings.MaxGuideRms:R}; peak>={settings.HardExcursionThreshold:R}; sustained>{settings.ExcursionThreshold:R} for {settings.ExcursionMinimumDuration:R}s; stars -{settings.MaxStarLossPercent:R}%; background +{settings.MaxBackgroundIncreasePercent:R}/-{settings.MaxBackgroundDecreasePercent:R}%";
        bool guideRule = result.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool skyRise = double.IsFinite(result.BackgroundDeviationPercent) && result.BackgroundDeviationPercent >= 3;
        // The rolling median lags behind a steadily darkening sky. A measured rise above a reliable
        // local trend can corroborate cloud loss, but never rejects on its own.
        bool skyRiseFromTrend = result.BackgroundTrendUsable && result.BackgroundTrendR2 >= .85
            && result.BackgroundTrendPercentPerFrame < 0 && double.IsFinite(result.BackgroundTrendResidualPercent)
            && result.BackgroundTrendResidualPercent >= 3;
        double guardedFlux = double.IsFinite(image.RelativeFluxUpperBound) ? Math.Max(image.RelativeFlux,image.RelativeFluxUpperBound) : image.RelativeFlux;
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
            if (result.RejectReasons.Contains("STAR_COUNT_DROP") && guardedFlux < 1-settings.MaxMeasuredFluxLossPercent/100) result.RejectReasons.Add("STELLAR_FLUX_LOSS");
            else if (result.RejectReasons.Contains("STAR_COUNT_DROP") && image.HasRescueMargin && image.ExtendedAvailable && image.VerifiedRegions>=4 && image.CompromisedRegions==0
                && (!double.IsFinite(image.ReferenceAgeMinutes) || image.ReferenceAgeMinutes <= 120)
                && (!settings.RejectSignalDegradation || (!skyRise && !skyRiseFromTrend))
                && image.RelativeFlux >= 1-Math.Min(20,settings.MaxMeasuredFluxLossPercent-5)/100) {
                result.RejectReasons.Remove("STAR_COUNT_DROP");result.ReviewReasons.Add("STAR_COUNT_DROP");result.StarCountFalsePositive=true;
            }
        }
        if (settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && image.PhotometryAvailable) {
            double loss = Math.Max(0, (1-image.RelativeFlux)*100);
            double guardedLoss = Math.Max(0, (1-guardedFlux)*100);
            bool fewerStars = double.IsFinite(result.StarDeviationPercent) && result.StarDeviationPercent <= -Math.Max(10,settings.MaxStarLossPercent/2);
            if (guardedLoss > settings.MaxMeasuredFluxLossPercent) {
                if (!result.RejectReasons.Contains("STELLAR_FLUX_LOSS")) result.RejectReasons.Add("STELLAR_FLUX_LOSS");
            }
            else if (guardedLoss >= settings.MaxCloudSignalLossPercent && fewerStars) result.RejectReasons.Add("SKY_SIGNAL_LOSS");
            if(double.IsFinite(image.SessionFluxUpperBound) && image.SessionFluxUpperBound*100 < settings.MinimumSessionSignalPercent)
                result.RejectReasons.Add("LOW_SESSION_SIGNAL");
            // Only an unexpected change freezes learning. Smooth motion along the forecast trains it.
            if (loss >= Math.Min(20,settings.MaxCloudSignalLossPercent*.75) && (result.StarDeviationPercent <= -10 || skyRise || skyRiseFromTrend))
                result.ReviewReasons.Add("TRANSPARENCY_CHANGE");
        }
        if(settings.ImageEvidenceEnabled && !image.Available) result.ReviewReasons.Add("STELLAR_CHECK_UNAVAILABLE");
        if(settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && !image.PhotometryAvailable
            && (result.StarDeviationPercent <= -10 || skyRise || skyRiseFromTrend))
            result.ReviewReasons.Add("SIGNAL_REFERENCE_UNAVAILABLE");
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
                : result.RejectReasons.Contains("LOW_SESSION_SIGNAL") ? "Rejected: stellar signal below the session minimum."
                : result.RejectReasons.Contains("SKY_SIGNAL_LOSS") ? "Rejected: sudden loss of stellar signal and star detections."
                : result.RejectReasons.Contains("STELLAR_FLUX_LOSS") ? "Rejected: sudden stellar signal loss beyond the limit."
                : guideRule && !result.GuideFalsePositive && settings.ImageEvidenceEnabled && image.Available && !image.HasRescueMargin ? $"Rejected: borderline stellar shape. Eccentricity {image.Eccentricity:0.00} must be below {image.Limits.RescueMaxEccentricity:0.00} for automatic rescue; original guide rejection retained."
                : guideRule && !result.GuideFalsePositive && image.ExtendedAttempted ? $"Guide rejection retained: {image.VerifiedRegions}/5 regions verified; {image.CompromisedRegions} outside shape limits. Stellar recovery not established."
                : guideRule && !result.GuideFalsePositive && extreme ? "Guide rejection retained: safety limit exceeded; stellar recovery not established."
                : guideRule && !result.GuideFalsePositive ? settings.ImageEvidenceEnabled ? "Rejected: stellar check inconclusive; original guide limit retained." : "Rejected: guide limit exceeded; stellar analysis is disabled."
                : result.GuideFalsePositive ? "Guide false positive cleared, but an independent star-count or background rule still rejects this frame."
                : "Rejected: star-count or background limit exceeded."
            : result.GuideFalsePositive ? "Kept for review: stellar verification cleared the guide flag."
            : result.StarCountFalsePositive ? "Kept for review: matched stellar signal is within recovery limits."
            : result.Status == FrameStatus.Learning ? "Learning: building the reference for this target and imaging setup."
            : result.ReviewReasons.Contains("TRANSPARENCY_CHANGE") ? "Kept for review: unexpected signal change; reference unchanged."
            : result.ReviewReasons.Contains("STELLAR_CHECK_UNAVAILABLE") ? "Kept for review: stellar check unavailable."
            : result.ReviewReasons.Contains("SIGNAL_REFERENCE_UNAVAILABLE") ? "Kept for review: insufficient matched signal references."
            : result.ReviewReasons.Contains("BORDERLINE_STAR_SHAPE") ? "Kept for review: borderline stellar shape."
            : result.Status == FrameStatus.Warning ? "Kept for review: reduced image quality."
            : settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && !image.PhotometryAvailable ? "Kept: available checks passed; signal references are learning."
            : image.SignalTrendUsed ? "Kept: stellar signal follows the gradual session trend."
            : "Kept: all active checks passed.";
        result.ThresholdsUsed += $"; secondPass={settings.ImageEvidenceEnabled}; eccentricity>={image.Limits.MaxEccentricity:R} in >={image.Limits.DeformedFraction:P0} stars; tail>={image.Limits.MaxTailPercent:R}%; doublePeak>={image.Limits.MaxDoublePeakPercent:R}%; minStars={image.Limits.MinimumStars}; targetStars={image.Limits.TargetStars}; rescue requires RMS<={settings.MaxGuideRms:R} when enabled, peak<{Math.Max(6, settings.HardExcursionThreshold*2):R} when enabled, sustained<{Math.Max(settings.ExcursionMinimumDuration,input.ExposureSeconds*.1):R}s when enabled";
        result.ThresholdsUsed += $"; rescueEccentricity<{image.Limits.RescueMaxEccentricity:R} (0.05 margin)";
        result.ThresholdsUsed += $"; signalReject={settings.RejectSignalDegradation}; unexpectedFluxLoss>{settings.MaxMeasuredFluxLossPercent:R}%; combinedUnexpectedLoss>={settings.MaxCloudSignalLossPercent:R}% with starLoss>={Math.Max(10,settings.MaxStarLossPercent/2):R}%; minimumSessionSignal={settings.MinimumSessionSignalPercent:R}%; gradual matched-signal trend follows past clean frames; unexpected loss >={Math.Min(20,settings.MaxCloudSignalLossPercent*.75):R}% with count loss>=10% or sky rise>=3% freezes reference learning; referenceAge<=360min for loss; <=120min for count rescue";
        result.ThresholdsUsed += $"; extendedRecovery requires >=4/5 regions, covered guide peak, RMS<=3x limit, sustained<25% exposure, tail<half limit; remotePeak>={image.Limits.MaxRemotePeakPercent:R}% in >=60% stars; measuredFluxEnabled={settings.VerifyStarCountWithFlux}; measuredFluxLoss>{settings.MaxMeasuredFluxLossPercent:R}%; countRescueFluxLoss<={Math.Min(20,settings.MaxMeasuredFluxLossPercent-5):R}%";
    }
}
