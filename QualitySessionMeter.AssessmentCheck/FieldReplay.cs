using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using System.Globalization;
using System.Text.Json;
using System.IO;

internal static class FieldReplay {
    public static async Task Run(string[] args) {
        string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        using var source=JsonDocument.Parse(File.ReadAllText(args[0]));
        var settings=new QualitySettings(new InMemoryPluginOptionsAccessor()) {
            Enabled=true,MonitorOnly=true,MaxGuideRms=1.6,HardExcursionThreshold=3,ExcursionThreshold=2,ExcursionMinimumDuration=5,
            ImageEvidenceEnabled=true,MinimumLearningFrames=4,BaselineWindow=8
        };
        var engine=new QualityEngine();var stellarAnalysis=new StellarAnalysisPipeline();var baseline=new BaselineEngine();
        var results=new List<FrameQualityResult>();var summaries=new List<object>();
        var key=new BaselineKey("Cocoon Nebula","QUAD",120,100,1,1,"ZWO ASI2600MC Pro");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        foreach(var row in source.RootElement.EnumerateArray()) {
            double Value(string name,double fallback=double.NaN)=>row.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number?p.GetDouble():fallback;
            ImageSample Read(string path,int size) {
                var bytes=File.ReadAllBytes(path);var pixels=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,pixels,0,bytes.Length);
                return new ImageSample(pixels,size,size){PixelsPerSample=(int)Value("step",2),ArcsecPerSample=Value("scale")};
            }
            var sample=Read(row.GetProperty("sample").GetString(),1024) with {
                OuterFields=row.GetProperty("outer").EnumerateArray().Select(p=>Read(p.GetString(),384)).ToArray()
            };
            int id=row.GetProperty("id").GetInt32();string side=row.GetProperty("side").GetString();
            var time=DateTime.Parse(row.GetProperty("time").GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal);
            var guide=new GuideExposureMetrics{HasData=true,Samples=54,RmsArcsec=Value("rms"),MaxExcursionArcsec=Value("peak"),MaxSustainedExcursionSeconds=Value("sustained")};
            var snap=args.Contains("--recorded-baselines") ? new BaselineSnapshot {
                StarMedian=Value("starBaseline"),BackgroundMedian=Value("backgroundBaseline"),StarSamples=8,BackgroundSamples=8,
                StarsReady=double.IsFinite(Value("starBaseline"))&&id>=808,BackgroundReady=double.IsFinite(Value("backgroundBaseline"))&&id>=808
            }:baseline.GetSnapshot(key,4);
            var image=stellarAnalysis.Analyze(key,sample,guide,snap,(int)Value("stars"),time,side,settings);
            var input=new FrameQualityInput{FrameIndex=(int)Value("frame"),TimestampUtc=time,OriginalPath=row.GetProperty("filename").GetString(),
                Target="Cocoon Nebula",Filter="QUAD",ExposureSeconds=120,Gain=100,BinX=1,BinY=1,Camera="ZWO ASI2600MC Pro",StarCount=(int)Value("stars"),BackgroundMedian=Value("background"),Baseline=snap,Guide=guide,ImageEvidence=image};
            var result=engine.Evaluate(input,settings);results.Add(result);
            if(result.Status is FrameStatus.Accepted or FrameStatus.Learning)baseline.AddAccepted(key,input.StarCount,input.BackgroundMedian,8);
            stellarAnalysis.AddReference(key,sample,image,result.Status,time,side,8);
            if(image.PreviewPngBase64.Length>0)File.WriteAllBytes(Path.Combine(output,$"{id}-stars.png"),Convert.FromBase64String(image.PreviewPngBase64));
            if(image.ExtendedPreviewPngBase64.Length>0)File.WriteAllBytes(Path.Combine(output,$"{id}-extended.png"),Convert.FromBase64String(image.ExtendedPreviewPngBase64));
            var summary=new {id,original=row.GetProperty("originalStatus").GetString(),status=result.Status.ToString(),result.GuideFalsePositive,result.StarCountFalsePositive,
                image.Eccentricity,image.TailStrength,image.RemotePeakStrength,image.RemotePeakSupport,image.VerifiedRegions,image.WorstRegionEccentricity,image.HasExtendedRescueEvidence,
                image.RelativeFlux,image.MatchedStars,image.ReferenceFrames,image.ElapsedMilliseconds,result.RejectReasons,result.DecisionSummary};
            summaries.Add(summary);
            if(summary.original!=summary.status)Console.WriteLine(JsonSerializer.Serialize(summary,new JsonSerializerOptions{NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
        }
        var options=new JsonSerializerOptions{WriteIndented=true};options.Converters.Add(new FiniteDoubleJsonConverter());
        File.WriteAllText(Path.Combine(output,"replay.json"),JsonSerializer.Serialize(results,options));
        File.WriteAllText(Path.Combine(output,"decisions.json"),JsonSerializer.Serialize(summaries,options));
        var mapFrame=typeof(QualitySessionMobileBridge).GetMethod("MobileFrame",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        File.WriteAllText(Path.Combine(output,"mobile-frames.json"),JsonSerializer.Serialize(results.Select(r=>mapFrame.Invoke(null,new object[]{r})),options));
        await HtmlReportWriter.WriteAsync(Path.Combine(output,"report.html"),results,new List<SessionEvent>(),DateTime.UtcNow);
        if(args.Contains("--assert-cocoon")) {
            var byId=results.ToDictionary(r=>int.Parse(Path.GetFileNameWithoutExtension(r.OriginalPath).Split('_').Last()));
            void Require(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("FIELD PASS "+message);}
            foreach(int id in new[]{859,967,1025,1042})Require(byId[id].IsUsable&&byId[id].GuideFalsePositive,$"{id} recovered with extended evidence");
            foreach(int id in new[]{850,862,863,888,889,890,891,893,898,964,971,989,990,1005,1023,1024,1034})Require(byId[id].Status==FrameStatus.Rejected,$"{id} image defect retained");
            foreach(int id in new[]{820,821,913,1001,1004,1006,1007,1008,1009,1013,1028,1029,1030,1031,1032,1033,1036,1037,1043,1044})Require(byId[id].Status==FrameStatus.Rejected,$"{id} signal loss retained");
            Require(byId[856].RejectReasons.Contains("STAR_SHAPE_CONFIRMED"),"856 elongated stars detected without a guide trigger");
            Require(byId[989].ImageEvidence.RemotePeakConfirmed,"989 distant repeated image measured");
        }
        Console.WriteLine($"Frames {results.Count}; "+string.Join("; ",results.GroupBy(r=>r.Status).Select(g=>$"{g.Key} {g.Count()}"))+$"; replay {clock.Elapsed.TotalSeconds:0.0}s");
    }
}

