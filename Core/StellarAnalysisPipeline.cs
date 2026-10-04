using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>One decision path shared by the live save callback and chronological field replays.</summary>
public sealed class StellarAnalysisPipeline {
    private readonly StellarReferenceEngine references = new();

    public ImageEvidence Analyze(BaselineKey context,ImageSample sample,GuideExposureMetrics guide,BaselineSnapshot baseline,int starCount,DateTime time,string side,QualitySettings settings) {
        if(!settings.ImageEvidenceEnabled)return new ImageEvidence {Detail="Stellar analysis disabled."};
        var clock=Stopwatch.StartNew();
        var evidence=ImageEvidenceAnalyzer.Analyze(sample,settings.GetStarShapeLimits());
        var regions=new List<StellarRegionEvidence>(evidence.Regions);
        if(sample!=null)for(int i=0;i<sample.OuterFields.Length && i<4;i++) {
            var field=sample.OuterFields[i];
            var limits=settings.GetStarShapeLimits() with {TargetStars=60,MinimumStars=20};
            ImageEvidence outer=clock.ElapsedMilliseconds<1400
                ?ImageEvidenceAnalyzer.Analyze(field,limits,Math.Max(20,1400-clock.Elapsed.TotalMilliseconds),false):new ImageEvidence();
            var region=outer.Regions.FirstOrDefault()??new StellarRegionEvidence {Width=field.Width,Height=field.Height,PixelsPerSample=field.PixelsPerSample};
            regions.Add(region with {RegionId=i+1});
        }
        evidence=evidence with {Regions=regions.ToArray(),PierSide=side,ShapeTimestampUtc=time,PhotometryRegionsExpected=sample==null?0:1+sample.OuterFields.Length};
        bool countDrop=settings.EnableStarCount && baseline?.StarsReady==true && starCount>=0
            && starCount<baseline.StarMedian*(1-settings.MaxStarLossPercent/100);
        if(ExposureAssessment.IsGuideRejectCandidate(guide,settings)||evidence.Compromised||countDrop)
            evidence=ExtendedStarAnalyzer.Verify(sample,evidence,guide?.HasData==true?guide.MaxExcursionArcsec:double.NaN);
        if(settings.VerifyStarCountWithFlux || settings.RejectSignalDegradation)evidence=references.Compare(context,sample,evidence,time,side);
        else evidence=references.CompareShapes(context,evidence,time,side);
        return evidence with {ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds};
    }
    public void AddReference(BaselineKey context,ImageSample sample,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window)
        =>references.Add(context,sample,evidence,status,time,side,window);
    public void ResetReferences(BaselineKey context)=>references.ResetReferences(context);
    public ImageEvidence CompareReference(BaselineKey context,ImageEvidence evidence,DateTime time,string side)
        =>references.CompareReference(context,evidence,time,side);
    public void AddEvidenceReference(BaselineKey context,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window)
        =>references.AddEvidenceReference(context,evidence,status,time,side,window);
    public void Clear()=>references.Clear();
}
