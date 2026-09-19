using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Diagnostics;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>One decision path shared by the live save callback and chronological field replays.</summary>
public sealed class StellarAnalysisPipeline {
    private readonly StellarReferenceEngine references = new();

    public ImageEvidence Analyze(BaselineKey context,ImageSample sample,GuideExposureMetrics guide,BaselineSnapshot baseline,int starCount,DateTime time,string side,QualitySettings settings) {
        if(!settings.ImageEvidenceEnabled)return new ImageEvidence {Detail="Stellar analysis disabled."};
        var clock=Stopwatch.StartNew();
        var evidence=ImageEvidenceAnalyzer.Analyze(sample,settings.GetStarShapeLimits());
        bool countDrop=settings.EnableStarCount && baseline?.StarsReady==true && starCount>=0
            && starCount<baseline.StarMedian*(1-settings.MaxStarLossPercent/100);
        if(ExposureAssessment.IsGuideRejectCandidate(guide,settings)||evidence.Compromised||countDrop)
            evidence=ExtendedStarAnalyzer.Verify(sample,evidence,guide?.HasData==true?guide.MaxExcursionArcsec:double.NaN);
        if(settings.VerifyStarCountWithFlux || settings.RejectSignalDegradation)evidence=references.Compare(context,sample,evidence,time,side);
        return evidence with {ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds};
    }
    public void AddReference(BaselineKey context,ImageSample sample,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window)
        =>references.Add(context,sample,evidence,status,time,side,window);
    public void Clear()=>references.Clear();
}
