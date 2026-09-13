using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using NINA.Plugin.QualitySessionMeter.Sequencer;
using System.Text.Json;
using System.Diagnostics;
using System.IO;

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
Check(Assess(round,3.03).AssessmentVersion == "1.4", "second-pass switch does not revert to legacy scoring");
settings.ImageEvidenceEnabled = true;
Check(!ExposureAssessment.IsGuideRejectCandidate(new GuideExposureMetrics {HasData=true,RmsArcsec=.8,MaxExcursionArcsec=1},settings), "healthy exposure does not request stellar analysis");
Check(ExposureAssessment.IsGuideRejectCandidate(new GuideExposureMetrics {HasData=true,RmsArcsec=.8,MaxExcursionArcsec=3.1},settings), "guide reject requests second pass");
Check(spike.GuideFalsePositive && spike.ReviewReasons.Contains("HARD_GUIDE_EXCURSION"), "rescue retains original reason for audit");
Check(Assess(round,3.03,stars:500).Status==FrameStatus.Rejected, "stellar rescue cannot clear an independent signal reject");
Check(!ImageEvidenceAnalyzer.Analyze(Field(2.6,1.1),new StarShapeLimits {MaxEccentricity=.90}).Compromised, "eccentricity tolerance changes measured decision");
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
        Check(new[]{561,562,563,564,567,695}.Contains(id) ? result.Status==FrameStatus.Rejected : result.GuideFalsePositive && result.Status!=FrameStatus.Rejected, $"real sample {id}: expected damaged/retained group");
        Console.WriteLine(JsonSerializer.Serialize(new { id=row.GetProperty("id").GetInt32(),shape.Stars,shape.AxisRatio,shape.TailStrength,shape.Compromised,result.StatusText,result.OverallQuality,result.DecisionSummary,milliseconds=sw.ElapsedMilliseconds }));
    }
    var options=new JsonSerializerOptions {WriteIndented=true}; options.Converters.Add(new FiniteDoubleJsonConverter());
    File.WriteAllText(Path.Combine(outDir,"replay.json"),JsonSerializer.Serialize(replay,options));
    await HtmlReportWriter.WriteAsync(Path.Combine(outDir,"report.html"),replay,new List<SessionEvent>(),DateTime.UtcNow);
}
Console.WriteLine($"1.4 CHECKS: {checks} passed");
