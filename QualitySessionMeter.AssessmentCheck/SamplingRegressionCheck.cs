using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;

internal static class SamplingRegressionCheck {
    public static void Run(Action<bool,string> check) {
        ImageSample Field(double sigma,double phase,double eccentricity=0,bool artifacts=false,double scale=1) {
            const int w=512;var pixels=new float[w*w];var rng=new Random(94);
            for(int i=0;i<pixels.Length;i++)pixels[i]=(float)(1000+20*(rng.NextDouble()-.5));
            if(sigma>0)for(int y=48;y<480;y+=48)for(int x=48;x<480;x+=48) {
                double sx=sigma/Math.Sqrt(1-eccentricity*eccentricity);
                for(int dy=-12;dy<=12;dy++)for(int dx=-12;dx<=12;dx++) {
                    double v=0;
                    // Six-point subpixel truth is independent of the three-point fit quadrature.
                    for(int sy=0;sy<6;sy++)for(int ix=0;ix<6;ix++) {
                        double xx=dx+(ix+.5)/6-.5-phase,yy=dy+(sy+.5)/6-.5-phase;
                        v+=Math.Exp(-.5*(xx*xx/(sx*sx)+yy*yy/(sigma*sigma)));
                    }
                    pixels[(y+dy)*w+x+dx]+=(float)(6000*scale*v/36);
                }
            }
            if(artifacts)for(int y=72;y<470;y+=48)for(int x=72;x<470;x+=48)pixels[y*w+x]+=18000;
            return new ImageSample(pixels,w,w){PixelsPerSample=2,ArcsecPerSample=2,SourceWidth=1024,SourceHeight=1024};
        }
        foreach(double sigma in new[]{.55,.65,.8})foreach(double phase in new[]{0,.2,.45,.7,.9}) {
            var shape=ImageEvidenceAnalyzer.Analyze(Field(sigma,phase),new StarShapeLimits{TargetStars=60});
            check(shape.Available&&shape.Stars>=50&&!shape.Compromised&&shape.HasRescueMargin,
                $"resolved compact stars remain measurable at sigma {sigma}, subpixel phase {phase}");
        }
        var isolatedPixels=ImageEvidenceAnalyzer.Analyze(Field(0,0,artifacts:true));
        check(!isolatedPixels.Available,"isolated bright sensor pixels cannot certify good stellar shapes");
        var contaminated=ImageEvidenceAnalyzer.Analyze(Field(.65,.2,artifacts:true));
        check(contaminated.Available&&!contaminated.Compromised,"sensor artifacts do not invalidate a resolved compact stellar field");
        foreach(double phase in new[]{0,.45,.9}) {
            var elongated=ImageEvidenceAnalyzer.Analyze(Field(.65,phase,eccentricity:.8));
            check(elongated.Available&&elongated.Compromised&&elongated.Eccentricity>=.6,
                $"elongated compact stars retain the absolute damage limit at phase {phase}");
        }
        var settings=new QualitySettings(new InMemoryPluginOptionsAccessor()){Enabled=true,MonitorOnly=true};
        var baseline=new BaselineSnapshot{StarsReady=true,BackgroundReady=true,StarMedian=1000,BackgroundMedian=1000};
        var missingShape=new ImageEvidence{Attempted=true,Available=false,RelativeFlux=.8,RelativeFluxUpperBound=.85,
            MatchedStars=30,ReferenceFrames=3,RegionalSignalUpperMinimum=.8,PhotometryRegionsMeasured=3,PhotometryRegionsExpected=5};
        FrameQualityResult Assess(ImageEvidence e,double peak=1)=>new QualityEngine().Evaluate(new FrameQualityInput{
            StarCount=500,BackgroundMedian=1100,ExposureSeconds=600,Baseline=baseline,ImageEvidence=e,
            Guide=new GuideExposureMetrics{HasData=true,Samples=60,RmsArcsec=.5,MaxExcursionArcsec=peak,MaxSustainedExcursionSeconds=0}},settings);
        var retained=Assess(missingShape);
        check(retained.Status==FrameStatus.Warning&&!retained.StarCountFalsePositive
            &&retained.ReviewReasons.Contains("STELLAR_CHECK_UNAVAILABLE")&&!ExposureAssessment.CanTrainBaseline(retained)
            &&!ExposureAssessment.CanTrainPhotometry(retained),"moderate independent signal can retain an inconclusive shape for review without certifying or training it");
        check(Assess(missingShape,peak:12).RejectReasons.Contains("HARD_GUIDE_EXCURSION"),"an inconclusive shape cannot clear an independent guide rejection");
        check(Assess(missingShape with{RelativeFlux=.3,RelativeFluxUpperBound=.35,RegionalSignalUpperMinimum=.35}).Status==FrameStatus.Rejected,
            "severe stellar loss remains rejected when shape measurement is unavailable");
        var damaged=missingShape with{Available=true,AxisRatio=2,ElongatedFraction=1,TailStrength=0,DoublePeakStrength=0};
        check(Assess(damaged).RejectReasons.Contains("STAR_SHAPE_CONFIRMED"),"moderate photometry cannot clear independently confirmed stellar damage");
        var key=new BaselineKey("sampling","HOO",600,100,1,1,"camera");var time=new DateTime(2026,10,4,20,0,0,DateTimeKind.Utc);
        var guide=new GuideExposureMetrics{HasData=true,Samples=60,RmsArcsec=.5,MaxExcursionArcsec=1};
        var sample=Field(.65,.2) with{OuterFields=new[]{Field(.65,.2),Field(.65,.2),Field(.65,.2),Field(.65,.2)}};
        var pipeline=new StellarAnalysisPipeline();
        var first=pipeline.Analyze(key,sample,guide,baseline,1000,time,"East",settings);
        for(int i=0;i<4;i++)pipeline.AddReference(key,sample,first,FrameStatus.Accepted,time.AddMinutes(i*10),"East",8);
        var measured=pipeline.Analyze(key,sample,guide,baseline,1000,time.AddMinutes(40),"East",settings);
        check(measured.PhotometryCoverageReliable&&measured.PhotometryRegionsMeasured==5,
            "compact stars provide five independently matched photometric regions");
        var cloudy=sample with{OuterFields=new[]{Field(.65,.2,scale:.25),Field(.65,.2,scale:.25),Field(.65,.2),Field(.65,.2)}};
        var cloud=pipeline.Analyze(key,cloudy,guide,baseline,750,time.AddMinutes(50),"East",settings);
        var cloudVerdict=new QualityEngine().Evaluate(new FrameQualityInput{StarCount=750,BackgroundMedian=1200,ExposureSeconds=600,
            Baseline=baseline,ImageEvidence=cloud,Guide=guide},settings);
        check(cloud.PhotometryCoverageReliable&&cloud.SpatialSignalInconsistent&&cloudVerdict.RejectReasons.Contains("SKY_SIGNAL_LOSS"),
            "partial clouds remain rejected with a clear center and compact stars");
    }
}
