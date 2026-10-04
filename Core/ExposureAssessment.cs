using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public static class ExposureAssessment {
    public const string Version = "1.4.3.1";

    public static bool CanTrainBaseline(FrameQualityResult result) => CanTrainStarCount(result) && CanTrainBackground(result);

    private static bool TransparencyUncertain(FrameQualityResult result) => result == null || result.Status == FrameStatus.Error
        || result.RejectReasons.Any(r => r is "STAR_COUNT_DROP" or "STELLAR_FLUX_LOSS" or "SKY_SIGNAL_LOSS" or "LOW_SESSION_SIGNAL" or "BACKGROUND_HIGH")
        || result.ReviewReasons.Any(r => r is "TRANSPARENCY_CHANGE" or "SPATIAL_SIGNAL_VARIATION" or "SIGNAL_COMPARISON_FAILED" or "SIGNAL_REFERENCE_UNAVAILABLE" or "BACKGROUND_HIGH" or "COUNT_SIGNAL_MISMATCH");

    public static bool CanTrainStarCount(FrameQualityResult result) => !TransparencyUncertain(result) && result.StarCount > 0 && !result.StarCountFalsePositive
        && !result.ReviewReasons.Contains("STAR_COUNT_DROP")
        && !result.ImageEvidence.Compromised && !result.RejectReasons.Any(r => r == "STAR_SHAPE_CONFIRMED" || r.Contains("GUIDE", StringComparison.Ordinal));

    public static bool CanTrainBackground(FrameQualityResult result) => !TransparencyUncertain(result)
        && double.IsFinite(result.BackgroundMedian) && result.BackgroundMedian > 0;

    public static bool CanTrainPhotometry(FrameQualityResult result) => !TransparencyUncertain(result) && result.ImageEvidence.Available
        && result.ImageEvidence.HasRescueMargin && !result.ImageEvidence.SpatialSignalInconsistent
        && !result.RejectReasons.Any(r => r == "STAR_SHAPE_CONFIRMED" || r.Contains("GUIDE", StringComparison.Ordinal));

    public static bool IsGuideRejectCandidate(GuideExposureMetrics guide, QualitySettings settings) => guide?.HasData == true && (
        (settings.EnableGuideRms && guide.RmsArcsec > settings.MaxGuideRms)
        || (settings.EnableHardExcursion && guide.MaxExcursionArcsec >= settings.HardExcursionThreshold)
        || (settings.EnableSustainedExcursion && guide.MaxExcursionArcsec > settings.ExcursionThreshold && guide.MaxSustainedExcursionSeconds >= settings.ExcursionMinimumDuration));

    public static void Apply(FrameQualityInput input, FrameQualityResult result, QualitySettings settings) {
        var image = (input.ImageEvidence ?? new ImageEvidence()) with {MinimumSessionSignalPercent=settings.MinimumSessionSignalPercent};
        result.ImageEvidence = image;
        result.AssessmentVersion = Version;
        result.GuideEvidence = input.Guide?.Series?.Take(512).ToList() ?? new();
        result.ThresholdsUsed = $"RMS>{settings.MaxGuideRms:R}; peak>={settings.HardExcursionThreshold:R}; sustained>{settings.ExcursionThreshold:R} for {settings.ExcursionMinimumDuration:R}s; stars -{settings.MaxStarLossPercent:R}%; backgroundReview>+{settings.MaxBackgroundIncreasePercent:R}%";
        bool guideRule = result.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool skyRise = double.IsFinite(result.BackgroundDeviationPercent) && result.BackgroundDeviationPercent >= 3;
        // The rolling median lags behind a steadily darkening sky. A measured rise above a reliable
        // local trend can corroborate cloud loss, but never rejects on its own.
        bool skyRiseFromTrend = result.BackgroundTrendUsable && result.BackgroundTrendR2 >= .85
            && result.BackgroundTrendPercentPerFrame < 0 && double.IsFinite(result.BackgroundTrendResidualPercent)
            && result.BackgroundTrendResidualPercent >= 3;
        double guardedFlux = double.IsFinite(image.RelativeFluxUpperBound) ? Math.Max(image.RelativeFlux,image.RelativeFluxUpperBound) : image.RelativeFlux;
        bool countChange = double.IsFinite(result.StarDeviationPercent) && result.StarDeviationPercent <= -10;
        double guardedLoss = double.IsFinite(guardedFlux) ? Math.Max(0, (1 - guardedFlux) * 100) : 0;
        // A raw background percentage is a sky diagnostic. A darker sky is not damage;
        // an abrupt rise only becomes a rejection when independent stellar measures agree.
        result.RejectReasons.Remove("BACKGROUND_LOW");
        if (result.BackgroundDeviationPercent < 0) result.BackgroundQuality = 100;
        if (result.RejectReasons.Contains("BACKGROUND_HIGH")) {
            result.RejectReasons.Remove("BACKGROUND_HIGH");
            result.ReviewReasons.Add("BACKGROUND_HIGH");
        }
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
                && image.PhotometryCoverageReliable && !image.SpatialSignalInconsistent
                && image.RegionalSignalUpperMinimum >= 1-Math.Min(20,settings.MaxMeasuredFluxLossPercent-5)/100
                && image.RelativeFlux >= 1-Math.Min(20,settings.MaxMeasuredFluxLossPercent-5)/100) {
                result.RejectReasons.Remove("STAR_COUNT_DROP");result.ReviewReasons.Add("STAR_COUNT_DROP");result.StarCountFalsePositive=true;
            }
            else if (result.RejectReasons.Contains("STAR_COUNT_DROP") && !image.Compromised
                && guardedLoss < settings.MaxCloudSignalLossPercent
                && double.IsFinite(image.RegionalSignalUpperMinimum)
                && image.RegionalSignalUpperMinimum >= 1-settings.MaxCloudSignalLossPercent/100) {
                // Photometry can retain a moderate frame even when shape measurement is
                // unavailable. Independent guide/shape rejections remain active, and this
                // branch cannot certify a false positive or authorize reference learning.
                result.RejectReasons.Remove("STAR_COUNT_DROP");
                result.ReviewReasons.Add("STAR_COUNT_DROP");
                result.ReviewReasons.Add("COUNT_SIGNAL_MISMATCH");
            }
        }
        if (settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && image.PhotometryAvailable) {
            bool fewerStars = double.IsFinite(result.StarDeviationPercent) && result.StarDeviationPercent <= -Math.Max(10,settings.MaxStarLossPercent/2);
            if (guardedLoss > settings.MaxMeasuredFluxLossPercent) {
                if (!result.RejectReasons.Contains("STELLAR_FLUX_LOSS")) result.RejectReasons.Add("STELLAR_FLUX_LOSS");
            }
            else if (guardedLoss >= settings.MaxCloudSignalLossPercent && fewerStars) result.RejectReasons.Add("SKY_SIGNAL_LOSS");
            if(double.IsFinite(image.SessionFluxUpperBound) && image.SessionFluxUpperBound*100 < settings.MinimumSessionSignalPercent)
                result.RejectReasons.Add("LOW_SESSION_SIGNAL");
            // Only an unexpected change freezes learning. Smooth motion along the forecast trains it.
            if (guardedLoss >= Math.Min(10,settings.MaxCloudSignalLossPercent*.5) && (countChange || skyRise || skyRiseFromTrend))
                result.ReviewReasons.Add("TRANSPARENCY_CHANGE");
            if (image.SpatialSignalInconsistent) result.ReviewReasons.Add("SPATIAL_SIGNAL_VARIATION");
            int regionsLost = image.Regions.Count(r => r.PhotometryAvailable && double.IsFinite(r.RelativeFluxUpperBound)
                && r.RelativeFluxUpperBound < 1-settings.MaxCloudSignalLossPercent/100);
            if (image.PhotometryCoverageReliable && regionsLost >= 2 && (fewerStars || (countChange && (skyRise || skyRiseFromTrend))))
                result.RejectReasons.Add("SKY_SIGNAL_LOSS");
        }
        if(settings.ImageEvidenceEnabled && !image.Available) result.ReviewReasons.Add("STELLAR_CHECK_UNAVAILABLE");
        if(settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && !image.PhotometryAvailable) {
            if (image.PhotometryState is PhotometryState.InsufficientMatches or PhotometryState.SpatiallyVariable)
                result.ReviewReasons.Add("SIGNAL_COMPARISON_FAILED");
            else if (countChange || skyRise || skyRiseFromTrend) result.ReviewReasons.Add("SIGNAL_REFERENCE_UNAVAILABLE");
        }
        if(settings.ImageEvidenceEnabled && image.Available && !image.Compromised && !image.HasRescueMargin)result.ReviewReasons.Add("BORDERLINE_STAR_SHAPE");

        // Duration is relative to the shutter interval. A lone peak must not zero image quality.
        if (input.Guide?.HasData == true && (settings.EnableSustainedExcursion || settings.EnableHardExcursion)) {
            double affected = input.ExposureSeconds > 0 ? result.SustainedGuideExcursionSeconds / input.ExposureSeconds : 0;
            result.StabilityQuality = Math.Clamp(100 - 300 * affected, 0, 100);
        }
        var scores = new[] { result.GuideFalsePositive ? null : result.GuidingQuality, result.GuideFalsePositive ? null : result.StabilityQuality,
                result.StarCountFalsePositive ? null : result.TransparencyQuality, result.BackgroundQuality }
            .Where(x => x.HasValue && double.IsFinite(x.Value)).Select(x => x.Value).ToArray();
        if (image.Available) scores = scores.Append(image.Quality).ToArray();
        if (settings.ImageEvidenceEnabled && image.PhotometryAvailable) {
            scores=scores.Append(Math.Clamp(image.RelativeFlux*100,0,100)).ToArray();
            if(double.IsFinite(image.FwhmRatio))scores=scores.Append(Math.Clamp(100-Math.Max(0,image.FwhmRatio-1.1)*100,0,100)).ToArray();
        }
        scores = scores.Where(double.IsFinite).ToArray();
        result.OverallQuality = scores.Length > 0
            ? scores.Average() * (1-settings.WorstMetricWeight) + scores.Min() * settings.WorstMetricWeight : 100;
        result.RejectReasons = result.RejectReasons.Distinct().ToList();
        result.ReviewReasons = result.ReviewReasons.Distinct().ToList();
        if (result.RejectReasons.Count > 0) result.Status = FrameStatus.Rejected;
        else if (result.Status != FrameStatus.Learning) result.Status = result.ReviewReasons.Count > 0 || result.OverallQuality < 65 ? FrameStatus.Warning : FrameStatus.Accepted;
        // A rejected pre-baseline frame that is now retained is still learning, never fully accepted.
        if (result.RejectReasons.Count == 0 && ((settings.EnableStarCount && input.Baseline?.StarsReady != true)
            || (settings.EnableBackground && input.Baseline?.BackgroundReady != true))) result.Status = FrameStatus.Learning;

        result.DecisionSummary = Summarize(result, image, settings, guideRule, extreme, confirmedShape);
        result.ThresholdsUsed += $"; secondPass={settings.ImageEvidenceEnabled}; eccentricity>={image.Limits.MaxEccentricity:R} in >={image.Limits.DeformedFraction:P0} stars; tail>={image.Limits.MaxTailPercent:R}%; doublePeak>={image.Limits.MaxDoublePeakPercent:R}%; minStars={image.Limits.MinimumStars}; targetStars={image.Limits.TargetStars}; rescue requires RMS<={settings.MaxGuideRms:R} when enabled, peak<{Math.Max(6, settings.HardExcursionThreshold*2):R} when enabled, sustained<{Math.Max(settings.ExcursionMinimumDuration,input.ExposureSeconds*.1):R}s when enabled";
        result.ThresholdsUsed += $"; rescueEccentricity<{image.Limits.RescueMaxEccentricity:R} (0.05 margin)";
        result.ThresholdsUsed += $"; signalReject={settings.RejectSignalDegradation}; unexpectedFluxLoss>{settings.MaxMeasuredFluxLossPercent:R}%; combinedUnexpectedLoss>={settings.MaxCloudSignalLossPercent:R}% with starLoss>={Math.Max(10,settings.MaxStarLossPercent/2):R}%; minimumSessionSignal={settings.MinimumSessionSignalPercent:R}%; unexpected loss >={Math.Min(10,settings.MaxCloudSignalLossPercent*.5):R}% with count loss>=10% or sky rise>=3% protects references; background changes alone never reject; confirmed count rescue requires reliable regional coverage; moderate measured signal with partial coverage retains a count-drop frame for review without training; referenceAge<=360min for loss; <=120min for confirmed count rescue";
        result.ThresholdsUsed += $"; extendedRecovery requires >=4/5 regions, covered guide peak, RMS<=3x limit, sustained<25% exposure, tail<half limit; remotePeak>={image.Limits.MaxRemotePeakPercent:R}% in >=60% stars; measuredFluxEnabled={settings.VerifyStarCountWithFlux}; measuredFluxLoss>{settings.MaxMeasuredFluxLossPercent:R}%; countRescueFluxLoss<={Math.Min(20,settings.MaxMeasuredFluxLossPercent-5):R}%";
    }

    private static string Summarize(FrameQualityResult result, ImageEvidence image, QualitySettings settings, bool guideRule, bool extreme, bool confirmedShape) {
        var reasons = result.RejectReasons;
        if (result.Status == FrameStatus.Rejected) {
            if (confirmedShape) return image.RemotePeakConfirmed ? "Rejected: repeated secondary star images." : "Rejected: star shapes exceed the distortion limits.";
            if (reasons.Contains("LOW_SESSION_SIGNAL")) return $"Rejected: stellar signal below {settings.MinimumSessionSignalPercent:0}% of the session reference.";
            if (reasons.Contains("SKY_SIGNAL_LOSS")) return "Rejected: sudden loss of stellar signal with fewer stars.";
            if (reasons.Contains("STELLAR_FLUX_LOSS")) return $"Rejected: sudden stellar signal loss above {settings.MaxMeasuredFluxLossPercent:0}%.";
            if (guideRule && !result.GuideFalsePositive) {
                if (settings.ImageEvidenceEnabled && image.Available && !image.HasRescueMargin)
                    return $"Rejected: guiding limit exceeded. Star eccentricity {image.Eccentricity:0.00} is too close to the limit to clear it (below {image.Limits.RescueMaxEccentricity:0.00} required).";
                if (image.ExtendedAttempted)
                    return $"Rejected: guiding limit exceeded. Star analysis verified {image.VerifiedRegions} of 5 regions ({image.CompromisedRegions} outside limits) and could not clear it.";
                if (extreme) return "Rejected: guiding far beyond the limit; star analysis cannot clear it.";
                return settings.ImageEvidenceEnabled ? "Rejected: guiding limit exceeded; star analysis was inconclusive." : "Rejected: guiding limit exceeded.";
            }
            string failed = QualityVocabulary.Labels(reasons.Where(x => !x.Contains("GUIDE", StringComparison.Ordinal)));
            return result.GuideFalsePositive
                ? $"Rejected: {QualityVocabulary.InSentence(failed)}. The guiding flag alone was cleared by star analysis."
                : $"Rejected: {QualityVocabulary.LabelsInSentence(reasons)}.";
        }
        if (result.Status == FrameStatus.Learning) return "Provisional: waiting for enough frames to validate the star-count and background reference.";
        if (result.GuideFalsePositive) return "Accepted for review: guiding flag cleared by star analysis.";
        if (result.StarCountFalsePositive) return "Accepted for review: fewer stars detected, but matched stellar signal is within limits.";
        var review = result.ReviewReasons;
        if (review.Contains("COUNT_SIGNAL_MISMATCH")) return "Accepted for review: fewer stars detected; measured signal loss is moderate. Not used as reference.";
        if (review.Contains("TRANSPARENCY_CHANGE")) return "Accepted for review: sudden transparency change. Not used as reference.";
        if (review.Contains("SPATIAL_SIGNAL_VARIATION")) return "Accepted for review: stellar signal varies across the image.";
        if (review.Contains("SIGNAL_COMPARISON_FAILED")) return "Accepted for review: stellar signal could not be compared with the established reference.";
        if (review.Contains("BACKGROUND_HIGH")) return "Accepted for review: sky background increased; stellar loss is not confirmed.";
        if (review.Contains("STELLAR_CHECK_UNAVAILABLE")) return "Accepted for review: star analysis unavailable for this frame.";
        if (review.Contains("SIGNAL_REFERENCE_UNAVAILABLE")) return "Accepted for review: too few matched frames for a signal comparison.";
        if (review.Contains("BORDERLINE_STAR_SHAPE")) return "Accepted for review: borderline star shapes.";
        if (result.Status == FrameStatus.Warning) return $"Accepted for review: quality score {result.OverallQuality:0} is below 65.";
        if (settings.ImageEvidenceEnabled && settings.RejectSignalDegradation && !image.PhotometryAvailable) return "Accepted: all active checks passed. The signal reference is still being built.";
        if (image.SignalTrendUsed) return "Accepted: stellar signal follows the gradual session trend.";
        return "Accepted: all active checks passed.";
    }
}
