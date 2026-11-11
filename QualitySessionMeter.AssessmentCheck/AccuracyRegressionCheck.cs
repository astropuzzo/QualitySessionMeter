using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Sequencer;
using NINA.Plugin.QualitySessionMeter.Testing;

internal static class AccuracyRegressionCheck {
    public static void Run(Action<bool,string> check) {
        var settings = new QualitySettings(new InMemoryPluginOptionsAccessor()) { ImageEvidenceEnabled=true, MonitorOnly=true };
        var baseline = new BaselineSnapshot { StarsReady=true, BackgroundReady=true, StarMedian=1000, BackgroundMedian=1000, StarSamples=8, BackgroundSamples=8 };
        ImageEvidence Evidence(double flux) => new() { Attempted=true, Available=true, Stars=100, AxisRatio=1.02,
            TailStrength=0, DoublePeakStrength=0, ElongatedFraction=0, RelativeFlux=flux, RelativeFluxUpperBound=flux+.01,
            MatchedStars=100, ReferenceFrames=4, PhotometryState=PhotometryState.Reliable,
            PhotometryCoverageReliable=true, RegionalSignalUpperMinimum=flux+.01, PhotometryRegionsMeasured=5, PhotometryRegionsExpected=5 };
        FrameQualityResult Assess(double flux, int stars=1000, double background=1000, ImageEvidence image=null) => new QualityEngine().Evaluate(new FrameQualityInput {
            StarCount=stars, BackgroundMedian=background, ExposureSeconds=120, Baseline=baseline, ImageEvidence=image ?? Evidence(flux),
            Guide=new GuideExposureMetrics {HasData=true, Samples=60, RmsArcsec=.5, MaxExcursionArcsec=1, MaxSustainedExcursionSeconds=0}
        }, settings);

        var clean=Assess(1); var darker=Assess(1,background:720);
        check(darker.Status==FrameStatus.Accepted && darker.ConfidenceThresholdSeparation==clean.ConfidenceThresholdSeparation,
            "a darker sky, which no rule penalises, does not weaken the evidence index");
        var marginal=Assess(.48); var severe=Assess(.10);
        check(marginal.RejectReasons.Contains("STELLAR_FLUX_LOSS") && severe.RejectReasons.Contains("STELLAR_FLUX_LOSS")
            && marginal.ConfidenceThresholdSeparation<70 && severe.ConfidenceThresholdSeparation>=95,
            "a signal-loss rejection reports its own distance from the limit");
        check(marginal.ConfidenceAgreement==75 && marginal.ConfidenceScore<severe.ConfidenceScore,
            "signal loss counts as an abnormal channel and a marginal rejection scores below a severe one");
        var shape=Assess(1,image:Evidence(1) with {AxisRatio=1.26,ElongatedFraction=.62});
        var badShape=Assess(1,image:Evidence(1) with {AxisRatio=1.60,ElongatedFraction=.90});
        check(shape.RejectReasons.Contains("STAR_SHAPE_CONFIRMED") && shape.ConfidenceThresholdSeparation<70
            && badShape.ConfidenceThresholdSeparation>=95 && shape.ConfidenceAgreement==75,
            "a star-shape rejection reports its own distance from the limit");
        var nearShape=Assess(1,image:Evidence(1) with {AxisRatio=1.22,ElongatedFraction=.2});
        check(nearShape.Status!=FrameStatus.Rejected && nearShape.ConfidenceThresholdSeparation<clean.ConfidenceThresholdSeparation,
            "retained stars close to the shape limit lower the evidence index");
        check(Assess(1,image:new ImageEvidence {Attempted=true,Available=true,Stars=100,AxisRatio=1.02}).ConfidenceThresholdSeparation==100,
            "unmeasured tail and secondary-peak values are not treated as near-limit evidence");

        FrameQualityResult Frame(int index,double starDeviation,double backgroundDeviation,string side="East") => new() {
            FrameIndex=index, Status=FrameStatus.Accepted, Target="M31", Filter="L", ExposureSeconds=120, Gain=100, BinX=1, BinY=1, Camera="Cam",
            CameraOffset=50, ReadoutModeIndex=0, ImageWidth=6248, ImageHeight=4176, PierSide=side,
            StarDeviationPercent=starDeviation, BackgroundDeviationPercent=backgroundDeviation, OverallQuality=95 };
        var predictive=new PredictiveDegradationEngine();
        var falling=Enumerable.Range(1,5).Select(i=>Frame(i,0,-5.0*i)).ToList();
        check(!predictive.Evaluate(falling,Frame(6,0,-29),settings).Warning, "a darkening sky raises no predictive warning");
        var rising=Enumerable.Range(1,5).Select(i=>Frame(i,0,5.0*i)).ToList();
        var high=predictive.Evaluate(rising,Frame(6,0,29),settings);
        check(high.Warning && high.Channel=="BACKGROUND HIGH" && high.Message.Contains("review limit") && !high.Message.Contains("reject"),
            "a brightening sky is forecast against the review limit it can actually reach");
        var east=Enumerable.Range(1,5).Select(i=>Frame(i,-6.0*i,0)).ToList();
        check(predictive.Evaluate(east,Frame(6,-33,0),settings).Channel=="STARS", "a star-count decline inside one context is still forecast");
        check(!predictive.Evaluate(east,Frame(6,-33,0,"West"),settings).Warning, "a trend is never extended across a meridian flip");
        settings.EnableStarCount=false;
        check(!predictive.Evaluate(east,Frame(6,-33,0),settings).Warning, "a disabled star-count rule raises no predictive warning");
        settings.EnableStarCount=true;

        var trend=new BaselineSnapshot { StarsReady=true, BackgroundReady=true, StarMedian=1050, BackgroundMedian=1000, StarSamples=8, BackgroundSamples=8,
            StarTrendUsable=true, StarTrendExpectedNext=1100, StarTrendR2=.95, StarTrendPercentPerFrame=2 };
        var trendDrop=new QualityEngine().Evaluate(new FrameQualityInput {StarCount=700, BackgroundMedian=1000, ExposureSeconds=120, Baseline=trend,
            Guide=new GuideExposureMetrics {HasData=true, Samples=60, RmsArcsec=.5, MaxExcursionArcsec=1}},settings);
        check(trendDrop.StarDeviationPercent < -settings.MaxStarLossPercent && ExposureAssessment.IsCountDropCandidate(700,trend,settings),
            "the count-drop verification uses the same expected count as the count rule");
        check(!ExposureAssessment.IsCountDropCandidate(720,trend,settings) && !ExposureAssessment.IsCountDropCandidate(-1,trend,settings)
            && !ExposureAssessment.IsCountDropCandidate(100,new BaselineSnapshot(),settings),
            "a count inside the limit, a missing count or an unready reference requests no count verification");

        var none=new QualityEngine().Evaluate(new FrameQualityInput {StarCount=-1, BackgroundMedian=double.NaN, ExposureSeconds=120, Baseline=baseline},settings);
        check(none.Status==FrameStatus.Error && none.OverallQuality==0 && none.ConfidenceScore==0,
            "a frame with nothing measured is not assessed and never reports a perfect quality score");
        var unguided=new QualityEngine().Evaluate(new FrameQualityInput {StarCount=1000, BackgroundMedian=1000, ExposureSeconds=120, Baseline=baseline, ImageEvidence=Evidence(1)},settings);
        check(unguided.Status==FrameStatus.Warning && unguided.OverallQuality==100,
            "a frame missing only guide data keeps the score of the channels that were measured");

        var runStart=new DateTime(2026,11,1,22,0,0,DateTimeKind.Utc);
        var declining=new[]{1400,1370,1340,1310}.Select((stars,i)=>new ReferenceValidation.Candidate(i+1,stars,1000,runStart.AddMinutes(3*i))).ToArray();
        var reference=ReferenceValidation.Build(declining,settings);
        check(reference.StarTrend && Math.Abs(reference.ToSnapshot(runStart.AddMinutes(6)).StarMedian-1340)<1,
            "a trending reference is evaluated at each frame's own time inside the run");
        check(Math.Abs(reference.ToSnapshot(runStart.AddMinutes(-60)).StarMedian-1400)<1 && Math.Abs(reference.ToSnapshot(runStart.AddHours(5)).StarMedian-1310)<1,
            "a trending reference is never extrapolated before its first or after its last frame");
        var risingRun=ReferenceValidation.Build(new[]{1250,1290,1330,1370}.Select((stars,i)=>new ReferenceValidation.Candidate(i+1,stars,1000,runStart.AddMinutes(3*i))).ToArray(),settings);
        check(risingRun.ToSnapshot(runStart.AddHours(-3)).StarsReady && Math.Abs(risingRun.ToSnapshot(runStart.AddHours(-3)).StarMedian-1250)<1,
            "an earlier frame keeps a usable reference when the replacement run was rising");

        settings.EnableBackground=false;
        var skyRise=Assess(.85,background:1100);
        check(double.IsFinite(skyRise.BackgroundDeviationPercent) && skyRise.ReviewReasons.Contains("TRANSPARENCY_CHANGE") && !skyRise.BackgroundQuality.HasValue,
            "a brighter sky still corroborates signal loss when the standalone background rule is disabled");
        check(Assess(1,background:2000).Status==FrameStatus.Accepted && Assess(1,background:500).OverallQuality==Assess(1).OverallQuality,
            "a disabled background rule neither flags nor scores the sky level");
        settings.EnableBackground=true;

        var corner=new StellarRegionEvidence {RegionId=1,Width=512,Height=512,ShapeAvailable=true,ShapeCompromised=true,Eccentricity=.66,TailStrength=0,DoublePeakStrength=0,
            ShapeReferenceFrames=3,ShapeReferenceEccentricity=.65,ShapeReferenceScatter=.01,StableOpticalElongation=true};
        var live=Evidence(1) with {ExtendedAttempted=true,ExtendedAvailable=true,GuideSearchCovered=true,VerifiedRegions=5,RawCompromisedRegions=1,CompromisedRegions=0,
            StableOpticalRegions=1,ShapeReferenceVerified=true,Regions=new[]{new StellarRegionEvidence {RegionId=0,Width=1024,Height=1024,ShapeAvailable=true,Eccentricity=.2},corner}};
        var recompared=new StellarAnalysisPipeline().CompareReference(new BaselineKey("M31","L",120,100,1,1,"Cam"),live,runStart,"East");
        var restored=recompared.WithShapeVerdictFrom(live);
        check(recompared.CompromisedRegions==1 && restored.CompromisedRegions==0 && restored.ShapeReferenceVerified && restored.StableOpticalRegions==1
            && restored.Regions.Single(r=>r.RegionId==1).StableOpticalElongation && restored.Regions.Single(r=>r.RegionId==1).ShapeReferenceFrames==3,
            "a retrospective signal comparison keeps the shape verdict measured at capture");

        var time=new DateTime(2026,11,1,20,0,0,DateTimeKind.Utc);
        FrameQualityResult Counted(int index,FrameStatus status,int minute=0)=>new() {FrameIndex=index,Status=status,TimestampUtc=time.AddMinutes(index+minute)};
        var progress=new ValidFrameProgressTracker.Snapshot();
        var counted=new Dictionary<int,ValidFrameProgressTracker.Counted>();
        foreach(var frame in new[]{Counted(1,FrameStatus.Accepted),Counted(2,FrameStatus.Warning),Counted(3,FrameStatus.Learning),Counted(4,FrameStatus.Rejected)}) {
            progress=ValidFrameProgressTracker.Account(progress,frame);
            counted[frame.FrameIndex]=new ValidFrameProgressTracker.Counted(frame.Status,frame.TimestampUtc);
        }
        progress=ValidFrameProgressTracker.Reconcile(progress,counted,new[]{Counted(1,FrameStatus.Rejected),Counted(2,FrameStatus.Rejected),Counted(4,FrameStatus.Accepted)});
        check(progress.Valid==1 && progress.Rejected==2 && progress.Warning==0 && progress.Learning==1 && progress.Captured==4,
            "accepted frames revised by a rebuilt reference leave the valid count, and recovered ones enter it");
        var settled=ValidFrameProgressTracker.Reconcile(progress,counted,new[]{Counted(1,FrameStatus.Rejected),Counted(3,FrameStatus.Accepted)});
        check(settled.Valid==2 && settled.Learning==0 && ValidFrameProgressTracker.Reconcile(settled,counted,new[]{Counted(3,FrameStatus.Accepted)})==settled,
            "reconciling the same verdict twice never counts a frame twice");
        check(ValidFrameProgressTracker.Reconcile(settled,counted,new[]{Counted(3,FrameStatus.Rejected,minute:90)})==settled,
            "a frame reusing an index in a new session never revises an earlier count");
        check(ValidFrameProgressTracker.SessionFloor(40,0)==0 && ValidFrameProgressTracker.SessionFloor(40,3)==3 && ValidFrameProgressTracker.SessionFloor(5,9)==5,
            "a progress floor saved in an earlier session cannot hide the frames of a new one");
    }
}
