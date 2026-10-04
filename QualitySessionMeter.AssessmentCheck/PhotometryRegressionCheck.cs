using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using System.Text.Json;
using System.Diagnostics;

internal static class PhotometryRegressionCheck {
    public static void Run(Action<bool,string> check) {
        var settings=new QualitySettings(new InMemoryPluginOptionsAccessor()) {Enabled=true,MonitorOnly=true};
        var key=new BaselineKey("synthetic","L",120,100,1,1,"camera");
        var time=new DateTime(2026,10,2,20,0,0,DateTimeKind.Utc);
        var rng=new Random(122);var positions=new List<(double X,double Y)>();
        while(positions.Count<80) {
            double x=35+rng.NextDouble()*440,y=35+rng.NextDouble()*440;
            if(positions.Any(s=>(s.X-x)*(s.X-x)+(s.Y-y)*(s.Y-y)<32*32))continue;
            positions.Add((x,y));
        }
        ImageSample Field(Func<int,double> attenuation=null,double sigma=1.4,double gradient=0,double background=1000,double eccentricity=0,double secondary=0) {
            const int w=512;var pixels=new float[w*w];var noise=new Random(43);
            double sigmaX=sigma/Math.Sqrt(1-eccentricity*eccentricity);
            for(int y=0;y<w;y++)for(int x=0;x<w;x++)pixels[y*w+x]=(float)(background+gradient*(x/(double)w-.5)+(noise.NextDouble()*20-10));
            for(int i=0;i<positions.Count;i++) {
                var s=positions[i];int cx=(int)s.X,cy=(int)s.Y;
                for(int dy=-24;dy<=24;dy++)for(int dx=-24;dx<=24;dx++) {
                    double xx=cx+dx-s.X,yy=cy+dy-s.Y;
                    double main=Math.Exp(-.5*(xx*xx/(sigmaX*sigmaX)+yy*yy/(sigma*sigma)));
                    double extra=secondary*Math.Exp(-.5*((xx-8)*(xx-8)/(sigmaX*sigmaX)+yy*yy/(sigma*sigma)));
                    pixels[(cy+dy)*w+cx+dx]+=(float)(9000*1.4*1.4/(sigmaX*sigma)*(attenuation?.Invoke(i)??1)*(main+extra));
                }
            }
            return new ImageSample(pixels,w,w) {PixelsPerSample=1,ArcsecPerSample=1,SourceWidth=512,SourceHeight=512};
        }
        var clear=Field();var initial=ImageEvidenceAnalyzer.Analyze(clear);
        check(initial.Available&&initial.Catalog.Length>=60,"irregular clean star field provides reliable shape and photometry catalog");
        var engine=new StellarReferenceEngine();
        for(int i=0;i<4;i++)engine.Add(key,clear,initial,FrameStatus.Accepted,time.AddMinutes(i*2),"East",8);
        ImageEvidence Compare(ImageSample image)=>engine.Compare(key,image,ImageEvidenceAnalyzer.Analyze(image),time.AddMinutes(10),"East");

        var faint=Compare(Field(_=>.08));
        check(faint.Available&&faint.PhotometryAvailable&&faint.RelativeFluxUpperBound<.15,
            "high-SNR stars below 1000 ADU retain a measured severe attenuation instead of disabling photometry");
        var minority=Compare(Field(i=>i<16?.2:1));
        check(minority.PhotometryAvailable&&minority.SpatialSignalInconsistent&&minority.AffectedStarFraction>=.15&&minority.RelativeFlux>.9,
            "minority attenuation is explicit while overall usable signal remains high");
        var missing=Compare(Field(i=>i<24?0:1));
        check(missing.PhotometryAvailable&&missing.MissingReferenceStars>=20&&missing.ExpectedReferenceStars>=60
            &&missing.Catalog.Any(s=>s.WasForcedMeasurement)&&missing.SpatialSignalInconsistent,
            "disappeared reference stars receive bounded forced measurements and cannot silently leave the comparison");
        var replayed=engine.CompareReference(key,missing,time.AddMinutes(10),"East");
        check(replayed.PhotometryAvailable&&replayed.SpatialSignalInconsistent&&Math.Abs(replayed.RelativeFlux-missing.RelativeFlux)<.02,
            "historical reference comparison preserves forced measurements without retaining raw pixels");
        var heterogeneous=Compare(Field(i=>i<40?.4:1));
        check(heterogeneous.PhotometryAvailable&&heterogeneous.SpatialSignalInconsistent&&double.IsFinite(heterogeneous.RelativeFlux),
            "nonuniform attenuation remains measured instead of being erased as generic unavailability");
        var broad=Compare(Field(sigma:4));
        check(broad.PhotometryAvailable&&broad.RelativeFlux>.94&&broad.FwhmRatio>2.4,
            "broadened constant-flux stars preserve flux while FWHM still reports degradation");
        var gradient=Compare(Field(gradient:1400));
        check(gradient.PhotometryAvailable&&gradient.RelativeFlux>.94&&gradient.RelativeFlux<1.06,
            "a smooth spatial background gradient does not inflate local noise into a false loss of stellar signal");
        FrameQualityResult Assess(ImageEvidence e,int stars=1000,double sky=1000,double referenceSky=1000)=>new QualityEngine().Evaluate(new FrameQualityInput {
            StarCount=stars,BackgroundMedian=sky,ExposureSeconds=120,ImageEvidence=e,
            Baseline=new BaselineSnapshot {StarMedian=1000,BackgroundMedian=referenceSky,StarsReady=true,BackgroundReady=true,StarSamples=8,BackgroundSamples=8},
            Guide=new GuideExposureMetrics {HasData=true,Samples=50,RmsArcsec=.5,MaxExcursionArcsec=1}
        },settings);
        check(Assess(faint).Status==FrameStatus.Rejected&&Assess(faint).RejectReasons.Contains("STELLAR_FLUX_LOSS"),
            "confirmed severe raw stellar attenuation rejects even when count and sky remain unchanged");
        foreach(double signal in new[]{.65,.70,.80}) {
            var moderate=Compare(Field(_=>signal));
            check(Assess(moderate).IsUsable&&!moderate.SpatialSignalInconsistent,"uniform moderate "+signal.ToString("P0")+" stellar signal remains usable without an uneven-signal flag");
        }
        var weakReference=initial.Catalog.Select((s,i)=>i<24?s with {Flux=1000,FluxUncertainty=100,Peak=500}:s).ToArray();
        var weakCurrent=weakReference.Select((s,i)=>i<24?s with {Flux=0,FluxUncertainty=600,WasForcedMeasurement=true}
            :s with {Flux=s.Flux*.7}).ToArray();
        var weakEngine=new StellarReferenceEngine();
        for(int i=0;i<4;i++)weakEngine.AddEvidenceReference(key,initial with {Catalog=weakReference},FrameStatus.Accepted,time.AddMinutes(i*2),"East",8);
        var uncertainMissing=weakEngine.CompareReference(key,initial with {Catalog=weakCurrent},time.AddMinutes(10),"East");
        check(uncertainMissing.PhotometryAvailable&&uncertainMissing.MissingReferenceStars>=20&&!uncertainMissing.SpatialSignalInconsistent,
            "disappeared weak stars with nonconclusive flux bounds do not label uniform fading as uneven attenuation");
        var lowSky=Field(background:500);var lowSkyInitial=ImageEvidenceAnalyzer.Analyze(lowSky);var pedestalReferences=new StellarReferenceEngine();
        for(int i=0;i<4;i++)pedestalReferences.Add(key,lowSky,lowSkyInitial,FrameStatus.Accepted,time.AddMinutes(i*2),"East",8);
        ImageEvidence PedestalCompare(double signal) {
            var image=Field(_=>signal,background:800);
            return pedestalReferences.Compare(key,image,ImageEvidenceAnalyzer.Analyze(image),time.AddMinutes(10),"East");
        }
        check(PedestalCompare(1).RelativeFlux>.98&&Assess(PedestalCompare(1),sky:800,referenceSky:500).IsUsable,
            "a 60 percent increase of raw background with stable stars cannot independently reject");
        check(Assess(PedestalCompare(.70),sky:800,referenceSky:500).IsUsable,
            "moderate attenuation plus a pedestal-sensitive background percentage cannot independently reject");
        check(Assess(PedestalCompare(.20),sky:800,referenceSky:500).Status==FrameStatus.Rejected,
            "severe stellar loss remains rejectable despite a low-pedestal background context");

        var regions=clear with {OuterFields=new[]{Field(),Field(),Field(),Field()}};
        var pipeline=new StellarAnalysisPipeline();
        var baseline=new BaselineSnapshot {StarMedian=1000,BackgroundMedian=1000,StarsReady=true,BackgroundReady=true};
        var guide=new GuideExposureMetrics {HasData=true,Samples=50,RmsArcsec=.5,MaxExcursionArcsec=1};
        var regionInitial=pipeline.Analyze(key,regions,guide,baseline,1000,time,"East",settings);
        for(int i=0;i<4;i++)pipeline.AddReference(key,regions,regionInitial,FrameStatus.Accepted,time.AddMinutes(i*2),"East",8);
        var cloudy=regions with {OuterFields=new[]{Field(_=>.2),Field(_=>.2),Field(),Field()}};
        var peripheral=pipeline.Analyze(key,cloudy,guide,baseline,800,time.AddMinutes(10),"East",settings);
        check(peripheral.PhotometryRegionsMeasured==5&&peripheral.PhotometryCoverageReliable&&peripheral.SpatialSignalInconsistent
            &&peripheral.RegionalSignalUpperMinimum<.3&&peripheral.RelativeFlux>.9,
            "peripheral clouds are measured independently even when the center is clear and no hard count rule fires");

        var opticalPipeline=new StellarAnalysisPipeline();
        ImageSample Optical(double outerE=.68,double centerE=0,double secondary=0)=>Field(eccentricity:centerE) with {
            OuterFields=new[]{Field(eccentricity:outerE,secondary:secondary),Field(),Field(),Field()}};
        for(int i=0;i<3;i++) {
            var now=time.AddMinutes(i*2);var normal=Optical();
            var cleanOptics=opticalPipeline.Analyze(key,normal,guide,baseline,1000,now,"East",settings);
            opticalPipeline.AddReference(key,normal,cleanOptics,FrameStatus.Accepted,now,"East",8);
        }
        var severeGuide=new GuideExposureMetrics {HasData=true,Samples=50,RmsArcsec=.5,MaxExcursionArcsec=7};
        ImageEvidence OpticalCheck(ImageSample image)=>opticalPipeline.Analyze(key,image,severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        FrameQualityResult AssessGuide(ImageEvidence e)=>new QualityEngine().Evaluate(new FrameQualityInput {
            StarCount=1000,BackgroundMedian=1000,ExposureSeconds=120,ImageEvidence=e,Baseline=baseline,Guide=severeGuide
        },settings);
        var stableOptics=OpticalCheck(Optical());
        check(stableOptics.HasExtendedRescueEvidence&&stableOptics.ShapeReferenceVerified&&stableOptics.StableOpticalRegions==1
            &&stableOptics.WorstRegionEccentricity>.6&&stableOptics.RawCompromisedRegions>=1&&stableOptics.CompromisedRegions==0
            &&AssessGuide(stableOptics).GuideFalsePositive,
            "three prior clean exposures identify stable peripheral optical elongation without raising the absolute shape limit");
        var newlyElongated=OpticalCheck(Optical(outerE:.76));
        check(!newlyElongated.HasExtendedRescueEvidence&&!newlyElongated.ShapeReferenceVerified
            &&AssessGuide(newlyElongated).Status==FrameStatus.Rejected,
            "new peripheral elongation above the learned shape allowance retains the guide rejection");
        var peripheralTail=OpticalCheck(Optical(secondary:.20));
        check(!peripheralTail.HasExtendedRescueEvidence&&!peripheralTail.ShapeReferenceVerified
            &&AssessGuide(peripheralTail).Status==FrameStatus.Rejected,
            "a new repeated peripheral star tail or secondary peak cannot be cleared by the eccentricity reference");
        var borderlineCenter=OpticalCheck(Optical(centerE:.58));
        check(!borderlineCenter.HasRescueMargin&&!borderlineCenter.HasExtendedRescueEvidence
            &&AssessGuide(borderlineCenter).Status==FrameStatus.Rejected,
            "central eccentricity 0.58 keeps the original rescue margin even with stable peripheral optics");
        var twoOpticalReferences=new StellarAnalysisPipeline();
        for(int i=0;i<2;i++) {
            var now=time.AddMinutes(i*2);var image=Optical();
            var evidence=twoOpticalReferences.Analyze(key,image,guide,baseline,1000,now,"East",settings);
            twoOpticalReferences.AddReference(key,image,evidence,FrameStatus.Accepted,now,"East",8);
        }
        var immatureOptics=twoOpticalReferences.Analyze(key,Optical(),severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        check(!immatureOptics.ShapeReferenceVerified&&!immatureOptics.HasExtendedRescueEvidence,
            "two prior shapes are insufficient to clear a peripheral absolute-limit failure");
        var provisionalOpticalReferences=new StellarAnalysisPipeline();
        for(int i=0;i<3;i++) {
            var now=time.AddMinutes(i*2);var image=Optical();
            var evidence=provisionalOpticalReferences.Analyze(key,image,guide,baseline,1000,now,"East",settings);
            provisionalOpticalReferences.AddReference(key,image,evidence,FrameStatus.Learning,now,"East",8);
        }
        var provisionalProof=provisionalOpticalReferences.Analyze(key,Optical(),severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        check(!provisionalProof.ShapeReferenceVerified&&!provisionalProof.HasExtendedRescueEvidence,
            "provisional exposures cannot certify a peripheral optical exception before their acceptance is validated");
        var futureOpticalReferences=new StellarAnalysisPipeline();
        for(int i=0;i<3;i++) {
            var actual=time.AddMinutes(20+i*2);var image=Optical();
            var evidence=futureOpticalReferences.Analyze(key,image,guide,baseline,1000,actual,"East",settings);
            futureOpticalReferences.AddEvidenceReference(key,evidence,FrameStatus.Accepted,time.AddMinutes(i),"East",8);
        }
        var futureProof=futureOpticalReferences.Analyze(key,Optical(),severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        check(!futureProof.ShapeReferenceVerified&&!futureProof.HasExtendedRescueEvidence,
            "historical normalization of photometric dates never turns future exposures into prior optical proof");
        var normalizedFutureProof=futureOpticalReferences.CompareReference(key,futureProof,time.AddMinutes(30),"East");
        check(!normalizedFutureProof.ShapeReferenceVerified&&!normalizedFutureProof.HasExtendedRescueEvidence
            &&AssessGuide(normalizedFutureProof).Status==FrameStatus.Rejected,
            "a normalized future comparison date cannot override the target exposure's real optical cutoff");
        var normalizedExpiredProof=opticalPipeline.CompareReference(key,stableOptics with {ShapeTimestampUtc=time.AddHours(7)},time.AddMinutes(10),"East");
        check(!normalizedExpiredProof.ShapeReferenceVerified&&!normalizedExpiredProof.HasExtendedRescueEvidence
            &&AssessGuide(normalizedExpiredProof).Status==FrameStatus.Rejected,
            "normalized historical dates cannot reuse optical references older than six real hours");
        var duplicateOpticalReferences=new StellarAnalysisPipeline();var oneOpticalImage=Optical();
        var oneOpticalEvidence=duplicateOpticalReferences.Analyze(key,oneOpticalImage,guide,baseline,1000,time,"East",settings);
        for(int i=0;i<3;i++)duplicateOpticalReferences.AddReference(key,oneOpticalImage,oneOpticalEvidence,FrameStatus.Accepted,time.AddMinutes(i*2),"East",8);
        var duplicateProof=duplicateOpticalReferences.Analyze(key,Optical(),severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        check(!duplicateProof.ShapeReferenceVerified&&!duplicateProof.HasExtendedRescueEvidence,
            "repeated catalog copies from one exposure cannot become three independent optical references");
        var changingOpticalReferences=new StellarAnalysisPipeline();var shapes=new[]{.63,.68,.73};
        for(int i=0;i<3;i++) {
            var now=time.AddMinutes(i*2);var image=Optical(outerE:shapes[i]);
            var evidence=changingOpticalReferences.Analyze(key,image,guide,baseline,1000,now,"East",settings);
            changingOpticalReferences.AddReference(key,image,evidence,FrameStatus.Accepted,now,"East",8);
        }
        var changingProof=changingOpticalReferences.Analyze(key,Optical(),severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        check(!changingProof.ShapeReferenceVerified&&!changingProof.HasExtendedRescueEvidence,
            "an unstable peripheral shape history cannot certify an optical exception");
        var settledOpticalReferences=new StellarAnalysisPipeline();var opticalHistory=new[]{.50,.70,.55,.73,.60,.683,.691,.681};
        for(int i=0;i<opticalHistory.Length;i++) {
            var now=time.AddMinutes(i*2);var image=Optical(outerE:opticalHistory[i]);
            var evidence=settledOpticalReferences.Analyze(key,image,guide,baseline,1000,now,"East",settings);
            settledOpticalReferences.AddReference(key,image,evidence,FrameStatus.Accepted,now,"East",8);
        }
        var settledImage=Optical(outerE:.634);
        var settledProof=settledOpticalReferences.Analyze(key,settledImage,severeGuide,baseline,1000,time.AddMinutes(20),"East",settings);
        check(settledProof.ShapeReferenceVerified&&settledProof.HasExtendedRescueEvidence
            &&settledProof.Regions.First(r=>r.RegionId==1).ShapeReferenceFrames==3
            &&AssessGuide(settledProof).GuideFalsePositive,
            "the latest three accepted optical references describe a stable current field despite older shape variation");
        var outlierOpticalReferences=new StellarAnalysisPipeline();var latestOutlier=new[]{.68,.69,.80};
        for(int i=0;i<latestOutlier.Length;i++) {
            var now=time.AddMinutes(i*2);var image=Optical(outerE:latestOutlier[i]);
            var evidence=outlierOpticalReferences.Analyze(key,image,guide,baseline,1000,now,"East",settings);
            outlierOpticalReferences.AddReference(key,image,evidence,FrameStatus.Accepted,now,"East",8);
        }
        var outlierProof=outlierOpticalReferences.Analyze(key,Optical(),severeGuide,baseline,1000,time.AddMinutes(10),"East",settings);
        check(!outlierProof.ShapeReferenceVerified&&!outlierProof.HasExtendedRescueEvidence
            &&AssessGuide(outlierProof).Status==FrameStatus.Rejected,
            "one outlier among the latest three references fails the shape range cap even when the median deviation is small");

        var json=JsonSerializer.Serialize(peripheral,new JsonSerializerOptions {NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals});
        check(!json.Contains("WasForcedMeasurement")&&!json.Contains("\"Catalog\""),"serialized diagnostics contain neither raw pixels nor star catalogs");
        pipeline.ResetReferences(key);
        check(!pipeline.CompareReference(key,peripheral,time.AddMinutes(12),"East").PhotometryAvailable,
            "context reference reset also clears photometric anchors");
        var newGeometry=clear with {SourceWidth=1024};
        check(!engine.Compare(key,newGeometry,initial,time.AddMinutes(10),"East").PhotometryAvailable,
            "a changed sensor geometry cannot reuse the old photometric reference");
        check(!engine.Compare(key,clear,initial,time.AddMinutes(10),"West").PhotometryAvailable,
            "meridian sides keep independent stellar references");
        var blank=clear with {Pixels=Enumerable.Repeat(1000f,512*512).ToArray()};
        var unavailable=engine.Compare(key,blank,ImageEvidenceAnalyzer.Analyze(blank),time.AddMinutes(10),"East");
        check(unavailable.PhotometryState==PhotometryState.InsufficientMatches&&Assess(unavailable).Status==FrameStatus.Warning
            &&!ExposureAssessment.CanTrainPhotometry(Assess(unavailable)),
            "no surviving stars cannot certify clear sky or train a mature photometric reference");
        var nullCatalog=engine.CompareReference(key,initial with {Catalog=Array.Empty<StarObservation>()},time.AddMinutes(10),"East");
        check(nullCatalog.PhotometryState==PhotometryState.InsufficientMatches&&Assess(nullCatalog).Status==FrameStatus.Warning
            &&!ExposureAssessment.CanTrainPhotometry(Assess(nullCatalog)),
            "missing catalog in a mature context cannot falsely certify usable photometric evidence");
        var lostHistoricalCatalog=engine.CompareReference(key,minority with {Catalog=Array.Empty<StarObservation>()},time.AddMinutes(10),"East");
        check(!lostHistoricalCatalog.PhotometryAvailable&&!lostHistoricalCatalog.SpatialSignalInconsistent
            &&double.IsNaN(lostHistoricalCatalog.AffectedStarFraction)&&double.IsNaN(lostHistoricalCatalog.LowerQuartileRelativeFlux)
            &&double.IsNaN(lostHistoricalCatalog.ReferenceAgeMinutes),
            "failed historical comparison clears stale attenuation and age diagnostics instead of reporting old measurements");
        ImageEvidence resumed=null;
        for(int i=0;i<4;i++) {
            var now=time.AddHours(7).AddMinutes(i*2);
            resumed=engine.Compare(key,clear,initial,now,"East");
            var assessed=Assess(resumed);
            check(assessed.IsUsable&&ExposureAssessment.CanTrainPhotometry(assessed),
                "stable context can rebuild expired photometric reference at resumed exposure "+i);
            engine.Add(key,clear,resumed,assessed.Status,now,"East",8);
        }
        check(resumed.PhotometryAvailable&&resumed.PhotometryState==PhotometryState.Reliable,
            "two new clean measurements restore photometry after reference expiry");

        var pixels=Enumerable.Repeat((ushort)1000,512*512).ToArray();
        for(int y=0;y<100;y++)for(int x=0;x<100;x++)pixels[y*512+x]=2000;
        var captured=ImageEvidenceAnalyzer.Capture(pixels,512,512,false);
        check(captured.BackgroundGrid.Length==25&&captured.BackgroundGrid[0].Median>1900&&captured.BackgroundGrid[24].Median==1000,
            "bounded whole-sensor sky grid detects a corner change without copying the full frame");

        const int sensorW=4096,sensorH=3072;var sensor=Enumerable.Repeat((ushort)1000,sensorW*sensorH).ToArray();
        for(int y=40;y<sensorH-40;y+=40)for(int x=40;x<sensorW-40;x+=40)
            for(int dy=-8;dy<=8;dy++)for(int dx=-8;dx<=8;dx++)sensor[(y+dy)*sensorW+x+dx]+=(ushort)(8000*Math.Exp(-.5*(dx*dx+dy*dy)/1.96));
        var watch=Stopwatch.StartNew();var sensorSample=ImageEvidenceAnalyzer.Capture(sensor,sensorW,sensorH,false);
        double captureMs=watch.Elapsed.TotalMilliseconds;var sensorPipeline=new StellarAnalysisPipeline();
        var sensorEvidence=sensorPipeline.Analyze(key,sensorSample,guide,baseline,1000,time,"East",settings);
        double firstMs=watch.Elapsed.TotalMilliseconds-captureMs;
        for(int i=0;i<4;i++)sensorPipeline.AddReference(key,sensorSample,sensorEvidence,FrameStatus.Accepted,time.AddMinutes(i*2),"East",8);
        watch.Restart();var measuredSensor=sensorPipeline.Analyze(key,sensorSample,guide,baseline,1000,time.AddMinutes(10),"East",settings);
        double matureMs=watch.Elapsed.TotalMilliseconds;
        Console.WriteLine($"BENCH 4096x3072 mono; central1024; four{sensorSample.OuterFields[0].Width}; capture={captureMs:0.0}ms initial={firstMs:0.0}ms mature={matureMs:0.0}ms regions={measuredSensor.PhotometryRegionsMeasured}/5");
        check(measuredSensor.PhotometryRegionsMeasured==5&&measuredSensor.PhotometryCoverageReliable&&matureMs<4000,
            "full sensor capture and five bounded stellar fields complete within the declared analysis budget");
    }
}
