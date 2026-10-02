using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;

internal static class GuardrailRegressionCheck {
    public static void Run(Action<bool,string> check) {
        var options = new InMemoryPluginOptionsAccessor();
        var settings = new QualitySettings(options) { ImageEvidenceEnabled=true, MonitorOnly=true };
        var baseline = new BaselineSnapshot { StarsReady=true, BackgroundReady=true, StarMedian=1000, BackgroundMedian=1000, StarSamples=8, BackgroundSamples=8 };
        ImageEvidence Evidence(double flux) => new() { Attempted=true, Available=true, Stars=100, AxisRatio=1.02,
            TailStrength=0, DoublePeakStrength=0, ElongatedFraction=0, RelativeFlux=flux, RelativeFluxUpperBound=flux+.01,
            MatchedStars=100, ReferenceFrames=4, PhotometryState=PhotometryState.Reliable,
            PhotometryCoverageReliable=true, RegionalSignalUpperMinimum=flux+.01, PhotometryRegionsMeasured=5, PhotometryRegionsExpected=5 };
        FrameQualityResult Assess(double flux, int stars=1000, double background=1000, GuideExposureMetrics guide=null) => new QualityEngine().Evaluate(new FrameQualityInput {
            StarCount=stars, BackgroundMedian=background, ExposureSeconds=120, Baseline=baseline, ImageEvidence=Evidence(flux),
            Guide=guide ?? new GuideExposureMetrics {HasData=true, Samples=60, RmsArcsec=.5, MaxExcursionArcsec=1, MaxSustainedExcursionSeconds=0}
        }, settings);
        check(Assess(.4,background:double.NaN).RejectReasons.Contains("STELLAR_FLUX_LOSS"), "missing background cannot cancel measured severe stellar loss");
        settings.VerifyStarCountWithFlux=false;
        check(Assess(1,stars:500,background:double.NaN).RejectReasons.Contains("STAR_COUNT_DROP"), "missing background cannot cancel a separate star-count rejection");
        settings.VerifyStarCountWithFlux=true;
        check(Assess(1,background:0).ReviewReasons.Contains("BACKGROUND_UNAVAILABLE"), "zero background is unavailable rather than a dark-sky measurement");
        check(Assess(1,background:500).IsUsable && ExposureAssessment.CanTrainBackground(Assess(1,background:500)), "healthy darker skies remain usable and update the sky reference");
        foreach(double signal in new[]{.66,.70,.80}) {
            var result=Assess(signal,stars:750,background:1600);
            check(result.Status==FrameStatus.Warning && !ExposureAssessment.CanTrainBaseline(result), $"signal {signal:P0} with elevated sky stays reviewable without reference contamination");
        }
        check(Assess(.2,stars:700,background:1300).Status==FrameStatus.Rejected, "severe opacity is detected despite a pedestal-diluted sky percentage");
        settings.EnableStarCount=false;
        check(Assess(.6,stars:750).RejectReasons.Contains("SKY_SIGNAL_LOSS"), "count measurement still corroborates signal loss when the standalone count rule is disabled");
        settings.EnableStarCount=true;
        options.SetValueDouble(nameof(QualitySettings.MaxBackgroundIncreasePercent), double.NaN);
        settings.MaxGuideRms=double.PositiveInfinity;
        check(double.IsFinite(settings.MaxBackgroundIncreasePercent) && settings.MaxGuideRms==1.5, "invalid persisted limits fall back to finite defaults");
        check(Assess(.2,stars:700,background:6000).Status==FrameStatus.Rejected && double.IsFinite(Assess(.2,stars:700,background:6000).OverallQuality), "invalid limits cannot disable rejection or produce a NaN quality score");
        var invalidRms = new GuideExposureMetrics {HasData=true, Samples=60, RmsArcsec=double.NaN, MaxExcursionArcsec=15, MaxSustainedExcursionSeconds=0};
        check(Assess(1,guide:invalidRms).RejectReasons.Contains("HARD_GUIDE_EXCURSION"), "missing RMS cannot cancel an independently valid hard guide peak");
        var warning=Assess(1,background:1500);
        check(!warning.RejectReasons.Contains("BACKGROUND_HIGH") && warning.ReviewReasons.Contains("BACKGROUND_HIGH"), "sky brightening without stellar loss is reviewed rather than rejected");
        settings.WorstMetricWeight=.5;
        var lowInfluence=Assess(1,background:1200).OverallQuality;
        settings.WorstMetricWeight=.95;
        check(Assess(1,background:1200).OverallQuality<lowInfluence, "worst-channel influence changes the reported quality score");
        var originalKey=new BaselineKey("Mago","HOO",600,100,1,1,"Camera",50,0,6248,4176,2,"East");
        var engine=new BaselineEngine();engine.AddAccepted(originalKey,1000,1000,8,DateTime.UtcNow);
        foreach(var other in new[]{originalKey with{PierSide="West"}, originalKey with{CameraOffset=60}, originalKey with{ReadoutModeIndex=1}, originalKey with{ImageWidth=2048}, originalKey with{SampleStep=1}})
            check(engine.GetSnapshot(other,1).StarSamples==0, "references are isolated after a camera or geometry change: "+other);
        var partial=Evidence(1) with{ExtendedAvailable=true, VerifiedRegions=5, PhotometryCoverageReliable=false, RegionalSignalUpperMinimum=.2};
        var partialResult=new QualityEngine().Evaluate(new FrameQualityInput {StarCount=500, BackgroundMedian=1000, Baseline=baseline, ImageEvidence=partial,
            Guide=new GuideExposureMetrics {HasData=true,RmsArcsec=.5,MaxExcursionArcsec=1}},settings);
        check(partialResult.Status==FrameStatus.Rejected && !partialResult.StarCountFalsePositive, "a healthy center cannot rescue star-count loss with unreliable outer photometry");
        var moderate=partial with{RelativeFlux=.73,RelativeFluxUpperBound=.74,RegionalSignalUpperMinimum=.72,
            PhotometryRegionsMeasured=3,PhotometryRegionsExpected=5,SpatialSignalInconsistent=true};
        var moderateResult=new QualityEngine().Evaluate(new FrameQualityInput {StarCount=469, BackgroundMedian=868,
            Baseline=baseline, ImageEvidence=moderate, Guide=new GuideExposureMetrics {HasData=true,RmsArcsec=.78,MaxExcursionArcsec=2.5}},settings);
        check(moderateResult.Status==FrameStatus.Warning && !moderateResult.StarCountFalsePositive
            && !ExposureAssessment.CanTrainBaseline(moderateResult) && !ExposureAssessment.CanTrainPhotometry(moderateResult),
            "partial moderate stellar evidence keeps a count-drop frame for review without claiming recovery or training");
        var shape=Assess(1);shape.Status=FrameStatus.Warning;shape.ReviewReasons.Add("BORDERLINE_STAR_SHAPE");
        check(ExposureAssessment.CanTrainBackground(shape), "a borderline shape does not freeze an independent healthy sky measurement");
        shape.ReviewReasons.Add("TRANSPARENCY_CHANGE");
        check(!ExposureAssessment.CanTrainBackground(shape), "a suspected transparency change protects the sky reference");
        var rescued=Assess(1,stars:1000);rescued.StarCount=500;rescued.StarCountFalsePositive=true;rescued.Status=FrameStatus.Warning;
        rescued.ReviewReasons.Add("STAR_COUNT_DROP");
        check(!ExposureAssessment.CanTrainStarCount(rescued) && ExposureAssessment.CanTrainBackground(rescued) && ExposureAssessment.CanTrainPhotometry(rescued),
            "a rescued count drop cannot train the unreliable count but preserves independent healthy channels");
        var now=DateTime.UtcNow;var aged=new BaselineEngine();
        for(int i=0;i<8;i++)aged.AddAccepted(originalKey,1000,1000,8,now.AddHours(-7).AddMinutes(i));
        check(!aged.GetSnapshot(originalKey,4,now).StarsReady && !aged.GetSnapshot(originalKey,4,now).BackgroundReady,
            "scalar references expire with photometry after a long observing gap");
        options.SetValueDouble(nameof(QualitySettings.AutoSafetyMaxGuideRms), double.NaN);
        check(settings.AutoSafetyMaxGuideRms==2.5,"invalid automatic-calibration safety caps remain finite");
    }
}
