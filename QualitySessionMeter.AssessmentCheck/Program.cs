using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using NINA.Plugin.QualitySessionMeter.Sequencer;
using System.Text.Json;
using System.Diagnostics;
using System.IO;

if(args.FirstOrDefault()=="--field") { await FieldReplay.Run(args.Skip(1).ToArray()); return; }

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
ImageSample Field(double sx, double sy, bool tail = false) {
    const int w = 512; var a = Enumerable.Repeat(1000f, w * w).ToArray(); var rng = new Random(41);
    for (int i = 0; i < a.Length; i++) a[i] += (float)(rng.NextDouble() * 30 - 15);
    for (int y = 45; y < 480; y += 48) for (int x = 45; x < 480; x += 48)
        for (int dy = -15; dy <= 15; dy++) for (int dx = -15; dx <= 15; dx++)
            a[(y + dy) * w + x + dx] += (float)(6000 * Math.Exp(-0.5 * (dx * dx / (sx * sx) + dy * dy / (sy * sy)))
                + (tail ? 850 * Math.Exp(-0.5 * ((dx + 6) * (dx + 6) / 2.0 + (dy + 6) * (dy + 6) / 2.0)) : 0));
    return new ImageSample(a, w, w);
}
var round = ImageEvidenceAnalyzer.Analyze(Field(1.4, 1.4));
var elongated = ImageEvidenceAnalyzer.Analyze(Field(2.6, 1.1));
var trailed = ImageEvidenceAnalyzer.Analyze(Field(1.4, 1.4, true));
Check(round.Available && !round.Compromised, "round raw stars do not imply damage");
Check(elongated.Available && elongated.Compromised, "elongated raw stars detected");
Check(trailed.Available && trailed.Compromised, "weak off-center tails detected despite round cores");
Check(!ImageEvidenceAnalyzer.Analyze(new ImageSample(new float[512*512],512,512)).Available, "blank image is unavailable, never proof of good stars");
Check(!ImageEvidenceAnalyzer.Analyze(Field(2.5,2.5)).Compromised, "symmetric broad stars do not masquerade as asymmetric trails");
Check(ImageEvidenceAnalyzer.Capture(new ushort[10], 512,512,true)==null, "invalid image dimensions rejected safely");

var settings = new QualitySettings(new InMemoryPluginOptionsAccessor()) {
    Enabled = true, MonitorOnly = true, MaxGuideRms = 1.6, HardExcursionThreshold = 3,
    ExcursionThreshold = 2, ExcursionMinimumDuration = 5, ImageEvidenceEnabled = true
};
FrameQualityResult Assess(ImageEvidence image, double peak, double rms = .9, double duration = 0, int stars = 1000, double background = 1000) => new QualityEngine().Evaluate(new FrameQualityInput {
    ExposureSeconds = 120, StarCount = stars, BackgroundMedian = background, ImageEvidence = image,
    Baseline = new BaselineSnapshot { StarMedian = 1000, BackgroundMedian = 1000, StarsReady = true, BackgroundReady = true, StarSamples=8, BackgroundSamples=8 },
    Guide = new GuideExposureMetrics { HasData=true, Samples=54, RmsArcsec=rms, MaxExcursionArcsec=peak, MaxSustainedExcursionSeconds=duration }
},settings);
var spike = Assess(round,3.03);
ImageEvidence ShapeAt(double eccentricity, double limit=.60) => new() {
    Attempted=true, Available=true, Stars=100, AxisRatio=1/Math.Sqrt(1-eccentricity*eccentricity),
    ElongatedFraction=0, TailStrength=0, DoublePeakStrength=0, Limits=new StarShapeLimits {MaxEccentricity=limit}
};
var borderline = Assess(ShapeAt(.58),3.03);
Check(borderline.Status==FrameStatus.Rejected && !borderline.GuideFalsePositive && !borderline.ImageEvidence.Compromised && borderline.DecisionSummary.Contains("borderline"), "borderline stars retain guide rejection without claiming confirmed shape damage");
Check(Assess(ShapeAt(.54),3.03).GuideFalsePositive, "clear margin still rescues round-enough stars");
Check(Assess(ShapeAt(.56),3.03).Status==FrameStatus.Rejected, "near-limit shape is not rescued merely for passing the damage limit");
Check(Assess(ShapeAt(.58,.65),3.03).GuideFalsePositive, "rescue margin follows the configured eccentricity tolerance");
Check(spike.Status==FrameStatus.Warning && spike.IsUsable && spike.OverallQuality>80, "isolated moderate peak retains a round image for review without score collapse");
Check(Assess(elongated,3.9).Status==FrameStatus.Rejected, "moderate guide failure plus damaged stars still rejects");
Check(Assess(trailed,21,3.9,12).Status==FrameStatus.Rejected, "large guide excursion and stellar tail still reject");
Check(Assess(null,3.03).Status==FrameStatus.Rejected, "missing shape evidence cannot rescue a guide reject");
Check(Assess(null,21,3.9,12).Status==FrameStatus.Rejected, "extreme guide failure remains reject even without image evidence");
Check(Assess(round,1,stars:500).RejectReasons.Contains("STAR_COUNT_DROP"), "cloud-like star loss remains reject");
Check(Assess(round,1,background:1500).RejectReasons.Contains("BACKGROUND_HIGH"), "background failure remains reject");
Check(Assess(round,1,background:500).RejectReasons.Contains("BACKGROUND_LOW"), "background decrease remains reject");
Check(spike.ConfidenceScore<=85 && spike.ConfidenceReason.Contains("not a probability"), "evidence strength is qualified");
settings.EnableGuideRms = false; settings.EnableHardExcursion = false; settings.EnableSustainedExcursion = false;
Check(Assess(round,21,3.9,12).Status == FrameStatus.Accepted, "disabled guide rules cannot silently reject or penalize stability");
settings.EnableGuideRms = true; settings.EnableHardExcursion = true; settings.EnableSustainedExcursion = true;
settings.ImageEvidenceEnabled = false;
Check(Assess(round,3.03).Status == FrameStatus.Rejected, "disabled second pass retains original guide rejection");
Check(Assess(round,3.03).AssessmentVersion == "1.4.2.0", "second-pass switch does not revert to legacy scoring");
settings.ImageEvidenceEnabled = true;
Check(!ExposureAssessment.IsGuideRejectCandidate(new GuideExposureMetrics {HasData=true,RmsArcsec=.8,MaxExcursionArcsec=1},settings), "healthy exposure does not request stellar analysis");
Check(ExposureAssessment.IsGuideRejectCandidate(new GuideExposureMetrics {HasData=true,RmsArcsec=.8,MaxExcursionArcsec=3.1},settings), "guide reject requests second pass");
Check(spike.GuideFalsePositive && spike.ReviewReasons.Contains("HARD_GUIDE_EXCURSION"), "rescue retains original reason for audit");
Check(Assess(round,3.03,stars:500).Status==FrameStatus.Rejected, "stellar rescue cannot clear an independent signal reject");
Check(!ImageEvidenceAnalyzer.Analyze(Field(2.6,1.1),new StarShapeLimits {MaxEccentricity=.95}).Compromised, "eccentricity tolerance changes measured decision");
Check(round.Preview?.IsFrozen==true && round.Preview.PixelWidth==135, "persistent visual proof decodes as dispatcher-safe frozen pixels");
Check(!ImageEvidenceAnalyzer.Analyze(new ImageSample(Enumerable.Repeat(float.NaN,64*64).ToArray(),64,64)).Available, "invalid numeric pixels cannot certify clean stars");
var doubleStars = Field(1.4,1.4,true);
var doubles = ImageEvidenceAnalyzer.Analyze(doubleStars, new StarShapeLimits {MaxTailPercent=100,MaxDoublePeakPercent=5});
Check(doubles.Available && doubles.DoublePeakStrength > .05 && doubles.Compromised, "repeated detached secondary peaks reject independently of tail tolerance");
var settingsRoundtrip = new InMemoryPluginOptionsAccessor();
var firstSettings = new QualitySettings(settingsRoundtrip) {ShapeMaxEccentricity=.68, ShapeTargetStars=70};
var reopenedSettings = new QualitySettings(settingsRoundtrip);
Check(reopenedSettings.ShapeMaxEccentricity==.68 && reopenedSettings.ShapeTargetStars==70, "stellar tolerances persist across plugin reload");
reopenedSettings.ShapeMaxEccentricity=double.NaN;
Check(reopenedSettings.ShapeMaxEccentricity==.6, "invalid tolerance cannot silently disable shape rejection");
var sensor = Enumerable.Repeat((ushort)1234, 6248*4176).ToArray();
var captureTime = Stopwatch.StartNew(); var captured = ImageEvidenceAnalyzer.Capture(sensor,6248,4176,true); captureTime.Stop();
Check(captured.Pixels.Length==1024*1024 && captured.Pixels.All(v=>v==1234), "Bayer central capture has fixed 4 MiB ownership and preserves a uniform field");
Console.WriteLine($"26MP Bayer capture: {captureTime.Elapsed.TotalMilliseconds:0.0} ms; managed copy: {captured.Pixels.Length*4} bytes");
var gate=new QsmSmartRecoveryGate(); var task=gate.Execute(null,new CancellationToken(true));
Check(task.IsCompletedSuccessfully, "retired recovery item never waits, even in old sequences");
Check(!typeof(QsmSmartRecoveryGate).GetCustomAttributes(false).Any(x=>x.GetType().Name=="ExportAttribute"), "retired recovery item is not exported");
var legacyGate = Newtonsoft.Json.JsonConvert.DeserializeObject<QsmSmartRecoveryGate>("{\"Name\":\"QSM Smart Recovery Gate\",\"SmartPauseEnabled\":true,\"SmartPauseSeconds\":120}");
Check(legacyGate.Execute(null,CancellationToken.None).IsCompletedSuccessfully, "saved recovery configuration deserializes and never waits");

var exportedFrame = new FrameQualityResult { Status = FrameStatus.Learning, ImageEvidence = new ImageEvidence {
    Attempted = true, Available = true, AxisRatio = 1.23, Limits = new StarShapeLimits { MaxEccentricity = .68, MaxTailPercent = 3.5 }
}};
var mapFrame = typeof(QualitySessionMobileBridge).GetMethod("MobileFrame", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var exported = (IDictionary<string, object>)mapFrame.Invoke(null, new object[] { exportedFrame });
Check((double)exported["starEccentricityLimit"] == .68 && Math.Abs((double)exported["starRescueEccentricityLimit"]-.63)<1e-10
    && (double)exported["starTailLimitPercent"] == 3.5, "remote analysis exports applied per-frame limits, not current defaults");
Check(exported["quality"] == null && (bool)exported["imageEvidenceAttempted"] && (bool)exported["imageEvidenceAvailable"], "remote learning quality stays null while measured evidence remains available");

var extendedRound = round with {ExtendedAttempted=true,ExtendedAvailable=true,GuideSearchCovered=true,VerifiedRegions=5,WorstRegionEccentricity=.3};
Check(Assess(extendedRound,8.3,2.1,17).GuideFalsePositive,"extended multi-region evidence can clear a guide flag above the old safety limit");
Check(Assess(extendedRound with {GuideSearchCovered=false},8.3,2.1,17).Status==FrameStatus.Rejected,"unknown or insufficient angular coverage cannot clear a large guide error");
Check(Assess(extendedRound with {VerifiedRegions=3},8.3,2.1,17).Status==FrameStatus.Rejected,"too few verified regions cannot clear a large guide error");
Check(Assess(extendedRound with {CompromisedRegions=1},3.2).Status==FrameStatus.Rejected,"damaged outer stars prevent guide recovery even with a round center");
Check(Assess(extendedRound with {ExtendedAvailable=false},3.2).Status==FrameStatus.Rejected,"failed extended check retains the original guide flag");
Check(Assess(extendedRound,8.3,5,17).Status==FrameStatus.Rejected && Assess(extendedRound,8.3,2,35).Status==FrameStatus.Rejected,"extreme RMS and long disturbances remain bounded even with clean stellar evidence");
Check(Assess(elongated,1,.5).RejectReasons.Contains("STAR_SHAPE_CONFIRMED"),"stellar damage rejects independently of a guiding trigger");
var matched = extendedRound with {RelativeFlux=.92,MatchedStars=40,ReferenceFrames=3};
Check(Assess(matched,1,stars:500).StarCountFalsePositive && Assess(matched,1,stars:500).Status==FrameStatus.Warning,"matched stellar signal can clear a misleading count drop");
Check(Assess(matched with {RelativeFlux=.5},1,stars:500).RejectReasons.Contains("STELLAR_FLUX_LOSS"),"real flux loss confirms a star-count rejection");
Check(Assess(matched with {ReferenceFrames=1},1,stars:500).Status==FrameStatus.Rejected,"one reference is insufficient to override star-count rejection");
Check(Assess(matched,1,stars:500,background:1500).Status==FrameStatus.Rejected,"photometric rescue never clears an independent background rule");
settings.VerifyStarCountWithFlux=false;
Check(!Assess(matched,1,stars:500).StarCountFalsePositive,"disabled flux verification cannot clear a count flag");
settings.VerifyStarCountWithFlux=true;
Check(Assess(matched with {RelativeFlux=.5},1).RejectReasons.Contains("STELLAR_FLUX_LOSS"),"measured signal loss rejects without requiring a prior star-count flag");
Check(Assess(matched with {RelativeFlux=.76},1,stars:750,background:1080).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"moderate signal loss plus brighter sky and fewer stars rejects");
Check(Assess(matched with {RelativeFlux=.76},1,stars:750,background:1000).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"abrupt count and signal reduction also rejects without brighter sky");
Check(Assess(matched with {RelativeFlux=.96},1,stars:750,background:1080).Status!=FrameStatus.Rejected,"brighter sky and fewer detections without measured attenuation do not imply cloud rejection");
Check(Assess(matched with {RelativeFlux=.76,ReferenceFrames=1},1,stars:750,background:1080).Status!=FrameStatus.Rejected,"insufficient photometric references cannot trigger combined rejection");
var partialCloud=Assess(matched with {RelativeFlux=.83},1,stars:800,background:1030);
Check(partialCloud.IsUsable && !ExposureAssessment.CanTrainBaseline(partialCloud),"partially attenuated kept frames do not contaminate the baseline");
settings.RejectSignalDegradation=false;
Check(Assess(matched with {RelativeFlux=.5},1).Status!=FrameStatus.Rejected,"disabled signal rejection does not add a new reject");
settings.RejectSignalDegradation=true;
settings.ImageEvidenceEnabled=false;
Check(Assess(matched with {RelativeFlux=.5},1).Status!=FrameStatus.Rejected,"disabled stellar analysis disables measured signal rejection");
settings.ImageEvidenceEnabled=true;

Check(Assess(matched with {RelativeFlux=.75},1,stars:750,background:1040).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"measured attenuation with a modest but corroborating sky rise rejects");
Check(!Assess(matched with {RelativeFlux=.75},1,stars:1000,background:1040).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"sky plus flux without count corroboration stays below combined rejection");
Check(ExposureAssessment.CanTrainBaseline(Assess(matched with {RelativeFlux=1},1,stars:800,background:1100)),"stable measured signal can train through moderate count and background changes");
Check(!Assess(matched,1,stars:500,background:1100).StarCountFalsePositive,"brightening sky prevents count rescue from clean stellar shape alone");

var starCatalog=Enumerable.Range(0,45).Select(i=>new StarObservation(30+(i*73)%400,30+(i*113)%400,50000+i*1000,5000+i*100,3)).ToArray();
var catalogEvidence=round with {Catalog=starCatalog,FwhmPixels=3};
var referenceEngine=new StellarReferenceEngine();var referenceTime=new DateTime(2026,9,13,22,0,0,DateTimeKind.Utc);
var referenceKey=new BaselineKey("A","L",120,100,1,1,"camera");var referenceSample=Field(1.4,1.4);
referenceEngine.Add(referenceKey,referenceSample,catalogEvidence,FrameStatus.Rejected,referenceTime,"East",8);
referenceEngine.Add(referenceKey,referenceSample,catalogEvidence,FrameStatus.Accepted,referenceTime,"East",8);
referenceEngine.Add(referenceKey,referenceSample,catalogEvidence,FrameStatus.Accepted,referenceTime.AddMinutes(2),"East",8);
var shifted=catalogEvidence with {Catalog=starCatalog.Select(s=>s with {X=s.X+7.3,Y=s.Y-4.2,Flux=s.Flux*.72}).ToArray()};
var comparison=referenceEngine.Compare(referenceKey,referenceSample,shifted,referenceTime.AddMinutes(4),"East");
Check(comparison.PhotometryAvailable && comparison.ReferenceFrames==2 && Math.Abs(comparison.RelativeFlux-.72)<.001,"photometry matches shifted stars against prior accepted frames only");
Check(!referenceEngine.Compare(referenceKey,referenceSample,shifted,referenceTime.AddMinutes(1),"East").PhotometryAvailable,"future reference frames are never used");
Check(!referenceEngine.Compare(referenceKey,referenceSample,shifted,referenceTime.AddMinutes(4),"West").PhotometryAvailable,"meridian sides do not share stellar references");
var heldReference=referenceEngine.Compare(referenceKey,referenceSample,shifted,referenceTime.AddHours(3),"East");
Check(heldReference.PhotometryAvailable && heldReference.RelativeFlux<.75 && heldReference.ReferenceAgeMinutes>=180,"long cloud intervals retain matched clean signal references");
Check(!referenceEngine.Compare(referenceKey,referenceSample,shifted,referenceTime.AddHours(7),"East").PhotometryAvailable,"references beyond six hours are unavailable");
var noShape=referenceEngine.Compare(referenceKey,referenceSample,shifted with {Available=false},referenceTime.AddMinutes(5),"East");
Check(!noShape.Available && noShape.PhotometryAvailable,"reliable photometry survives unavailable shape classification");
Check(Assess(noShape with {RelativeFlux=.5,RelativeFluxUpperBound=.51},1).RejectReasons.Contains("STELLAR_FLUX_LOSS"),"signal loss can reject without claiming verified shapes");
Check(!Assess(matched with {ReferenceAgeMinutes=181},1,stars:500).StarCountFalsePositive,"old references cannot authorize a count-flag rescue");
referenceEngine.Clear();Check(!referenceEngine.Compare(referenceKey,referenceSample,shifted,referenceTime.AddMinutes(4),"East").PhotometryAvailable,"session reset clears stellar references");

var trackingBaseline=new BaselineEngine();var trackingReferences=new StellarReferenceEngine();
FrameQualityResult Track(ImageEvidence evidence,int stars,double bg,int minute) {
    var time=referenceTime.AddMinutes(minute);
    evidence=trackingReferences.Compare(referenceKey,referenceSample,evidence,time,"East");
    var frame=new QualityEngine().Evaluate(new FrameQualityInput {ExposureSeconds=120,StarCount=stars,BackgroundMedian=bg,ImageEvidence=evidence,
        Baseline=trackingBaseline.GetSnapshot(referenceKey,4,time),Guide=new GuideExposureMetrics{HasData=true,Samples=50,RmsArcsec=.5,MaxExcursionArcsec=1}},settings);
    if(ExposureAssessment.CanTrainBaseline(frame)) {trackingBaseline.AddAccepted(referenceKey,stars,bg,8,time);trackingReferences.Add(referenceKey,referenceSample,evidence,frame.Status,time,"East",8);}
    return frame;
}
for(int i=0;i<5;i++)Track(catalogEvidence,1000,1000,i*2);
FrameQualityResult fading=null;
for(int i=1;i<=18;i++) {
    double flux=1-i*.025;
    fading=Track(catalogEvidence with {Catalog=starCatalog.Select(s=>s with {Flux=s.Flux*flux,Peak=s.Peak*flux}).ToArray()},(int)(1000*flux),1000+i*6,10+i*3);
}
Check(fading.IsUsable && fading.ImageEvidence.RelativeFlux>.9 && fading.ImageEvidence.SessionRelativeFlux<.6,"gradual fading follows the recent trend and stays usable above the session floor");
Check(Track(catalogEvidence,1000,1000,70).Status==FrameStatus.Accepted,"clear conditions recover without a sticky cloud rejection");
// Smooth altitude changes must not become clouds; a step away from that trajectory must.
ImageEvidence Signal(double flux) => catalogEvidence with {Catalog=starCatalog.Select(s=>s with {Flux=s.Flux*flux,Peak=s.Peak*flux}).ToArray()};
trackingBaseline.Clear();trackingReferences.Clear();
for(int i=0;i<24;i++) {
    double flux=Math.Exp(-.035*i);
    var gradual=Track(Signal(flux),(int)(1000*flux),1000*Math.Exp(.025*i),i*10);
    Check(gradual.Status!=FrameStatus.Rejected,$"gradual signal/count fall and sky rise remain kept at exposure {i}");
}
var abrupt=Track(Signal(Math.Exp(-.035*24)*.7),300,1700,240);
Check(abrupt.RejectReasons.Contains("SKY_SIGNAL_LOSS") || abrupt.RejectReasons.Contains("STELLAR_FLUX_LOSS"),"abrupt loss after a long gradual trend is rejected");
var held=Track(Signal(Math.Exp(-.035*25)*.7),290,1680,250);
Check(held.Status==FrameStatus.Rejected,"a second cloudy exposure cannot reset the rejected reference");
Check(Track(Signal(Math.Exp(-.035*26)),410,1900,260).IsUsable,"return to the gradual signal trajectory recovers");
Check(Assess(matched with {RelativeFlux=1,SessionFluxUpperBound=.39},1).RejectReasons.Contains("LOW_SESSION_SIGNAL"),"gradual fading still has a configurable minimum usable signal");
Check(!Assess(matched with {RelativeFlux=1,SessionFluxUpperBound=.41},1).RejectReasons.Contains("LOW_SESSION_SIGNAL"),"session floor respects measurement allowance");
Check(!Assess(matched with {RelativeFlux=1,SessionFluxUpperBound=double.NaN},1).RejectReasons.Contains("LOW_SESSION_SIGNAL"),"missing session reference cannot manufacture a low-signal verdict");
trackingBaseline.Clear();trackingReferences.Clear();
foreach(int minute in new[]{0,9,21,30,43,51,63,74,86,95,108,121}) {
    double flux=Math.Exp(.003*minute);
    Check(Track(Signal(flux),(int)(700*flux),1300*Math.Exp(-.003*minute),minute).Status!=FrameStatus.Rejected,"gradual increase with irregular cadence and darkening sky stays kept");
}
Check(Assess(matched with {RelativeFlux=.74},1,stars:730,background:850).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"sudden stellar/count loss rejects even when the sky gets darker");
trackingBaseline.Clear();trackingReferences.Clear();
for(int i=0;i<8;i++)Track(Signal(1),1000,1000,i*2);
for(int i=1;i<=5;i++) {
    double flux=Math.Pow(.92,i);
    var ramp=Track(Signal(flux),(int)(1000*flux),1000+i*15,14+i*2);
    if(i>=3)Check(ramp.Status==FrameStatus.Rejected,"rapid multi-frame attenuation cannot become a gradual reference");
}
Check(Track(Signal(.68),680,1080,30).Status==FrameStatus.Rejected,"a low cloud plateau remains excluded after the initial drop");
Check(Track(Signal(1),1000,1000,32).IsUsable,"clear return ends the cloud interval");
settings.MaxCloudSignalLossPercent=30;
Check(!Assess(matched with {RelativeFlux=.76},1,stars:750,background:1080).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"combined signal loss follows the configured threshold");
settings.MaxCloudSignalLossPercent=20;
Check(Assess(matched with {RelativeFlux=.76},1,stars:750,background:1080).ProbableCause=="SUDDEN STELLAR SIGNAL LOSS","signal rejection is displayed as signal loss, not a generic review");

FrameQualityResult TrendFrame(double flux, double r2) => new QualityEngine().Evaluate(new FrameQualityInput {
    ExposureSeconds=600,StarCount=730,BackgroundMedian=1005,ImageEvidence=matched with {RelativeFlux=flux},
    Baseline=new BaselineSnapshot {StarsReady=true,BackgroundReady=true,StarMedian=1000,BackgroundMedian=1000,
        BackgroundTrendUsable=true,BackgroundTrendR2=r2,BackgroundTrendExpectedNext=970,BackgroundTrendPercentPerFrame=-.5},
    Guide=new GuideExposureMetrics {HasData=true,Samples=100,RmsArcsec=.5,MaxExcursionArcsec=1}
},settings);
Check(TrendFrame(.74,.95).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"signal and count loss can corroborate a rise above a reliable darkening-sky trend");
Check(TrendFrame(.74,.8).RejectReasons.Contains("SKY_SIGNAL_LOSS"),"abrupt signal and count loss does not require a background trend");
Check(TrendFrame(1,.95).Status!=FrameStatus.Rejected,"background trend alone never rejects stable stellar signal");
var uncertainSignal=Assess(matched with {RelativeFlux=.79,RelativeFluxUpperBound=.82},1,stars:750,background:1080);
Check(uncertainSignal.Status==FrameStatus.Warning && !ExposureAssessment.CanTrainBaseline(uncertainSignal),"uncertain threshold crossing is reviewed and cannot train the reference");
Check(Assess(matched with {RelativeFlux=.60,RelativeFluxUpperBound=.63},1).RejectReasons.Contains("STELLAR_FLUX_LOSS"),"clear signal loss remains rejected after measurement allowance");
await SessionStorageCheck.Run(Check);

var wideClean=Field(1.4,1.4) with {ArcsecPerSample=1,OuterFields=Enumerable.Range(0,4).Select(_=>Field(1.4,1.4)).ToArray()};
var cleanProof=ExtendedStarAnalyzer.Verify(wideClean,ImageEvidenceAnalyzer.Analyze(wideClean),40);
Check(cleanProof.ExtendedAvailable && cleanProof.GuideSearchCovered && !cleanProof.RemotePeakConfirmed,"clean extended fields do not manufacture distant stellar images");
var remotePixels=(float[])wideClean.Pixels.Clone();
for(int y=45;y<480;y+=48)for(int x=45;x<480;x+=48)
 for(int dy=-5;dy<=5;dy++)for(int dx=-5;dx<=5;dx++) {
   int yy=y+15+dy,xx=x-20+dx;if(xx>=0&&xx<512&&yy>=0&&yy<512)remotePixels[yy*512+xx]+=(float)(120*Math.Exp(-.5*(dx*dx+dy*dy)/(1.4*1.4)));
 }
var remoteSample=wideClean with {Pixels=remotePixels};var remoteCore=ImageEvidenceAnalyzer.Analyze(remoteSample);
var remoteProof=ExtendedStarAnalyzer.Verify(remoteSample,remoteCore,40);
Check(!remoteCore.Compromised && remoteProof.RemotePeakConfirmed,"repeated faint distant image is detected outside the original core patch");
Check(remoteProof.ExtendedPreview?.IsFrozen==true,"extended visual evidence is safe across WPF dispatchers");
Check(Assess(remoteProof,40,2).Status==FrameStatus.Rejected,"confirmed distant image prevents guide recovery");
Check(captured.OuterFields.Length==4 && captured.OuterFields.All(f=>f.Pixels.All(v=>v==1234)),"owned outer samples preserve Bayer-cell alignment");
Console.WriteLine($"Total owned pixel copies: {(captured.Pixels.Length+captured.OuterFields.Sum(f=>f.Pixels.Length))*4} bytes");

if (args.Length>0) {
    var replay = new List<FrameQualityResult>();
    string outDir = args.Length>1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"replay");
    Directory.CreateDirectory(outDir);
    using var json=JsonDocument.Parse(File.ReadAllText(args[0]));
    foreach(var row in json.RootElement.EnumerateArray()) {
        var bytes=File.ReadAllBytes(row.GetProperty("sample").GetString());var values=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
        var sw=Stopwatch.StartNew(); var shape=ImageEvidenceAnalyzer.Analyze(new ImageSample(values,1024,1024));
        double Value(string name,double fallback=double.NaN) => row.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number?p.GetDouble():fallback;
        var result=new QualityEngine().Evaluate(new FrameQualityInput {
            ExposureSeconds=120, FrameIndex=(int)Value("frame",row.GetProperty("id").GetInt32()), OriginalPath=row.TryGetProperty("filename",out var filename)?filename.GetString():row.GetProperty("id").GetInt32()+".fits",
            StarCount=(int)Value("stars",1000),BackgroundMedian=Value("background",1000),ImageEvidence=shape,
            Target="Cocoon Nebula",Filter="QUAD", Camera="ZWO ASI2600MC Pro", Gain=100, BinX=1,BinY=1,
            Baseline=new BaselineSnapshot {StarMedian=Value("starBaseline"),BackgroundMedian=Value("backgroundBaseline"),StarsReady=double.IsFinite(Value("starBaseline")),BackgroundReady=double.IsFinite(Value("backgroundBaseline")),StarSamples=8,BackgroundSamples=8},
            Guide=new GuideExposureMetrics {HasData=true,Samples=54,RmsArcsec=Value("rms"),MaxExcursionArcsec=Value("peak"),MaxSustainedExcursionSeconds=Value("sustained")}
        },settings);
        replay.Add(result);
        var id=row.GetProperty("id").GetInt32();
        File.WriteAllBytes(Path.Combine(outDir,$"{id}-stars.png"),Convert.FromBase64String(shape.PreviewPngBase64));
        Check(new[]{561,562,563,564,565,567,695}.Contains(id) ? result.Status==FrameStatus.Rejected : result.GuideFalsePositive && result.Status!=FrameStatus.Rejected, $"real sample {id}: expected excluded/retained group");
        Console.WriteLine(JsonSerializer.Serialize(new { id=row.GetProperty("id").GetInt32(),shape.Stars,shape.AxisRatio,shape.TailStrength,shape.Compromised,result.StatusText,result.OverallQuality,result.DecisionSummary,milliseconds=sw.ElapsedMilliseconds }));
    }
    var options=new JsonSerializerOptions {WriteIndented=true}; options.Converters.Add(new FiniteDoubleJsonConverter());
    File.WriteAllText(Path.Combine(outDir,"replay.json"),JsonSerializer.Serialize(replay,options));
    await HtmlReportWriter.WriteAsync(Path.Combine(outDir,"report.html"),replay,new List<SessionEvent>(),DateTime.UtcNow);
}
Console.WriteLine($"1.4 CHECKS: {checks} passed");
