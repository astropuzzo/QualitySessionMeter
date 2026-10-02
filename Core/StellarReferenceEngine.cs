using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Matched and forced aperture measurements against past clean references, by region.</summary>
public sealed class StellarReferenceEngine {
    private sealed record Reference(DateTime Time, DateTime ShapeTime, bool ShapeEligible, StellarRegionEvidence[] Regions);
    private sealed record Measurement(int Count, int Expected, int Missing, double Ratio, double Margin, double Lower, double Affected, bool Spatial,
        StarObservation[] Catalog);
    private readonly Dictionary<(BaselineKey,string),Queue<Reference>> references = new();
    private readonly Dictionary<(BaselineKey,string),List<Reference>> anchors = new();
    private static string Geometry(ImageEvidence e,string side) => $"{e.SourceWidth}x{e.SourceHeight}/{e.SampleWidth}x{e.SampleHeight}/{e.PixelsPerSample}/{side}";

    public ImageEvidence Compare(BaselineKey context,ImageSample sample,ImageEvidence current,DateTime time,string side) {
        if(sample==null)return CompareReference(context,current,time,side);
        current=current with {SampleWidth=sample.Width,SampleHeight=sample.Height,PixelsPerSample=sample.PixelsPerSample,
            SourceWidth=sample.SourceWidth,SourceHeight=sample.SourceHeight,PierSide=side,ShapeTimestampUtc=time};
        return CompareCore(context,current,time,side,sample);
    }

    public ImageEvidence CompareReference(BaselineKey context,ImageEvidence current,DateTime time,string side)
        =>CompareCore(context,current,time,side,null);

    public ImageEvidence CompareShapes(BaselineKey context,ImageEvidence current,DateTime time,string side) {
        var key=(context,Geometry(current,side));
        var eligible=references.TryGetValue(key,out var queue)
            ?queue.Where(r=>r.Time<time && time-r.Time<=TimeSpan.FromHours(6)).TakeLast(12).ToArray():Array.Empty<Reference>();
        return ApplyShapeReferences(current,eligible,time);
    }

    private ImageEvidence CompareCore(BaselineKey context,ImageEvidence current,DateTime time,string side,ImageSample sample) {
        var clock=Stopwatch.StartNew();
        var regions=GetRegions(current);int expectedRegions=regions.Length;
        var key=(context,Geometry(current,side));
        bool hasQueue=references.TryGetValue(key,out var queue);
        var eligible=hasQueue?queue.Where(r=>r.Time<=time && time-r.Time<=TimeSpan.FromHours(6)).TakeLast(12).ToArray():Array.Empty<Reference>();
        anchors.TryGetValue(key,out var initial);
        var compared=new List<StellarRegionEvidence>();
        foreach(var region in regions) {
            var catalog=region.Catalog;var matches=new List<Measurement>();var attempts=new List<Measurement>();var ages=new List<double>();
            var raw=region.RegionId==0?sample:sample?.OuterFields.ElementAtOrDefault(region.RegionId-1);
            foreach(var reference in eligible) {
                if(clock.ElapsedMilliseconds>1200)break;
                var previous=reference.Regions.FirstOrDefault(r=>r.RegionId==region.RegionId && r.Width==region.Width && r.Height==region.Height);
                if(previous==null)continue;
                var match=Measure(catalog,previous.Catalog,raw,region.Width,region.Height,region.Noise,region.FwhmPixels);
                attempts.Add(match);
                catalog=match.Catalog;
                if(match.Count<20 || !double.IsFinite(match.Ratio))continue;
                matches.Add(match);ages.Add((time-reference.Time).TotalMinutes);
            }
            if(matches.Count<2) {
                compared.Add(region with {Catalog=catalog,PhotometryState=eligible.Length>=2?PhotometryState.InsufficientMatches:PhotometryState.ReferenceLearning,
                    RelativeFlux=double.NaN,RelativeFluxUpperBound=double.NaN,SessionRelativeFlux=double.NaN,SessionFluxUpperBound=double.NaN,
                    MatchedStars=attempts.Count>0?attempts.Max(m=>m.Count):0,ReferenceFrames=matches.Count,
                    ExpectedReferenceStars=attempts.Count>0?attempts.Max(m=>m.Expected):0,MissingReferenceStars=attempts.Count>0?attempts.Max(m=>m.Missing):0,
                    ReferenceAgeMinutes=double.NaN,LowerQuartileRelativeFlux=double.NaN,AffectedStarFraction=double.NaN,
                    SignalTrendUsed=false,SignalTrendPercentPerHour=double.NaN});
                continue;
            }
            var ratios=matches.Select(m=>m.Ratio).ToArray();var margins=matches.Select(m=>m.Margin).ToArray();
            var forecast=Forecast(ratios,margins,ages.ToArray(),eligible.Select(r=>r.Time).ToArray());
            double sessionFlux=double.NaN,sessionUpper=double.NaN;
            if(initial!=null) {
                var initialMatches=new List<Measurement>();
                foreach(var reference in initial.Where(r=>r.Time<=time)) {
                    if(clock.ElapsedMilliseconds>1200)break;
                    var previous=reference.Regions.FirstOrDefault(r=>r.RegionId==region.RegionId);
                    if(previous==null)continue;
                    var match=Measure(catalog,previous.Catalog,raw,region.Width,region.Height,region.Noise,region.FwhmPixels);
                    catalog=match.Catalog;
                    if(match.Count>=20 && double.IsFinite(match.Ratio))initialMatches.Add(match);
                }
                if(initialMatches.Count>=2) {
                    sessionFlux=Median(initialMatches.Select(m=>m.Ratio));
                    sessionUpper=sessionFlux+Math.Max(sessionFlux*.005,Median(initialMatches.Select(m=>m.Margin)));
                }
            }
            double normalization=Median(ratios)>0?forecast.Flux/Median(ratios):1;
            compared.Add(region with {Catalog=catalog,PhotometryState=matches.Any(m=>m.Spatial)?PhotometryState.SpatiallyVariable:PhotometryState.Reliable,
                RelativeFlux=forecast.Flux,RelativeFluxUpperBound=forecast.Flux+forecast.Margin,SessionRelativeFlux=sessionFlux,SessionFluxUpperBound=sessionUpper,
                LowerQuartileRelativeFlux=Median(matches.Select(m=>m.Lower))*normalization,AffectedStarFraction=Median(matches.Select(m=>m.Affected)),
                MatchedStars=matches.Min(m=>m.Count),ExpectedReferenceStars=matches.Max(m=>m.Expected),MissingReferenceStars=matches.Max(m=>m.Missing),
                ReferenceFrames=matches.Count,ReferenceAgeMinutes=ages.Max(),SignalTrendUsed=forecast.Trend,SignalTrendPercentPerHour=forecast.Rate});
        }
        var core=compared.FirstOrDefault(r=>r.RegionId==0);
        var valid=compared.Where(r=>r.PhotometryAvailable).ToArray();
        bool spatial=valid.Any(r=>r.PhotometryState==PhotometryState.SpatiallyVariable)
            || (valid.Length>=2 && valid.Max(r=>r.RelativeFlux)-valid.Min(r=>r.RelativeFlux)>.20*Math.Max(.2,Median(valid.Select(r=>r.RelativeFlux))));
        bool coverage=valid.Length==expectedRegions && expectedRegions>0;
        double fwhmReference=Median(eligible.Select(r=>r.Regions.FirstOrDefault(x=>x.RegionId==0)?.FwhmPixels??double.NaN).Where(v=>v>0));
        int detailStart=current.Detail.IndexOf(" Stellar signal ",StringComparison.Ordinal);
        string shapeDetail=detailStart>=0?current.Detail[..detailStart]:current.Detail;
        string signalDetail=core?.PhotometryAvailable==true
            ?$" Stellar signal {core.RelativeFlux:P0} · {valid.Length}/{expectedRegions} measured regions · {core.MatchedStars} matched stars"
                +(spatial?" · uneven attenuation.":".")
            :$" Stellar signal {(eligible.Length>=2?"comparison unavailable":"reference learning")} · {valid.Length}/{expectedRegions} measured regions.";
        var result=current with {PierSide=side,Regions=compared.ToArray(),Catalog=core?.Catalog??current.Catalog,
            PhotometryState=spatial?PhotometryState.SpatiallyVariable:core?.PhotometryState??PhotometryState.NotMeasured,
            RelativeFlux=core?.RelativeFlux??double.NaN,RelativeFluxUpperBound=core?.RelativeFluxUpperBound??double.NaN,
            SessionRelativeFlux=core?.SessionRelativeFlux??double.NaN,SessionFluxUpperBound=core?.SessionFluxUpperBound??double.NaN,
            MatchedStars=core?.MatchedStars??0,ReferenceFrames=core?.ReferenceFrames??0,ReferenceAgeMinutes=core?.ReferenceAgeMinutes??double.NaN,
            ExpectedReferenceStars=core?.ExpectedReferenceStars??0,MissingReferenceStars=core?.MissingReferenceStars??0,
            AffectedStarFraction=valid.Length>0?valid.Max(r=>r.AffectedStarFraction):double.NaN,
            LowerQuartileRelativeFlux=core?.LowerQuartileRelativeFlux??double.NaN,PhotometryRegionsMeasured=valid.Length,PhotometryRegionsExpected=expectedRegions,
            PhotometryCoverageReliable=coverage,SpatialSignalInconsistent=spatial,
            RegionalSignalMinimum=valid.Length>0?valid.Min(r=>r.RelativeFlux):double.NaN,
            RegionalSignalUpperMinimum=valid.Length>0?valid.Min(r=>r.RelativeFluxUpperBound):double.NaN,
            SignalTrendUsed=core?.SignalTrendUsed??false,SignalTrendPercentPerHour=core?.SignalTrendPercentPerHour??double.NaN,
            FwhmRatio=fwhmReference>0?current.FwhmPixels/fwhmReference:double.NaN,Detail=shapeDetail+signalDetail};
        return ApplyShapeReferences(result,eligible,time);
    }

    private static ImageEvidence ApplyShapeReferences(ImageEvidence current,Reference[] eligible,DateTime time) {
        DateTime cutoff=current.ShapeTimestampUtc!=default?current.ShapeTimestampUtc:time;
        bool NoTailOrPeak(StellarRegionEvidence r)=>double.IsFinite(r.TailStrength)&&r.TailStrength<current.Limits.MaxTailPercent/100
            &&double.IsFinite(r.DoublePeakStrength)&&r.DoublePeakStrength<current.Limits.MaxDoublePeakPercent/100;
        var regions=GetRegions(current).Select(region=> {
            var previous=eligible.Where(r=>r.ShapeEligible&&r.ShapeTime<cutoff&&cutoff-r.ShapeTime<=TimeSpan.FromHours(6))
                .GroupBy(r=>r.ShapeTime).Select(g=>g.First()).OrderBy(r=>r.ShapeTime)
                .SelectMany(r=>r.Regions.Where(p=>p.RegionId==region.RegionId&&p.Width==region.Width&&p.Height==region.Height
                &&p.ShapeAvailable&&double.IsFinite(p.Eccentricity)&&NoTailOrPeak(p))).TakeLast(3).ToArray();
            double center=Median(previous.Select(r=>r.Eccentricity));
            double scatter=previous.Length>0?Median(previous.Select(r=>Math.Abs(r.Eccentricity-center))):double.NaN;
            double range=previous.Length>0?previous.Max(r=>r.Eccentricity)-previous.Min(r=>r.Eccentricity):double.NaN;
            bool stable=region.RegionId>0&&region.ShapeAvailable&&region.Eccentricity>=current.Limits.MaxEccentricity
                &&NoTailOrPeak(region)&&previous.Length>=3&&scatter<=.02&&range<=.06
                &&region.Eccentricity<=center+.02;
            return region with {ShapeReferenceFrames=previous.Length,ShapeReferenceEccentricity=center,
                ShapeReferenceScatter=scatter,StableOpticalElongation=stable};
        }).ToArray();
        var outside=regions.Where(r=>r.RegionId>0&&r.ShapeAvailable&&r.Eccentricity>=current.Limits.MaxEccentricity).ToArray();
        bool verified=current.HasRescueMargin&&current.ExtendedAvailable&&current.GuideSearchCovered&&current.VerifiedRegions>=4
            &&outside.Length>0&&outside.All(r=>r.StableOpticalElongation);
        int raw=current.RawCompromisedRegions>0?current.RawCompromisedRegions:current.CompromisedRegions;
        int cleared=verified?regions.Count(r=>r.ShapeCompromised&&r.StableOpticalElongation):0;
        int stableRegions=verified?outside.Length:0;
        int priorDetail=current.Detail.IndexOf(" Stable peripheral elongation ",StringComparison.Ordinal);
        string detail=priorDetail>=0?current.Detail[..priorDetail]:current.Detail;
        if(stableRegions>0)detail+=" Stable peripheral elongation "+string.Join("; ",outside.Select(r=>
            $"region {r.RegionId}: eccentricity {r.Eccentricity:0.00}, reference {r.ShapeReferenceEccentricity:0.00} ({r.ShapeReferenceFrames} prior frames)"))+".";
        return current with {Regions=regions,ShapeReferenceVerified=verified,StableOpticalRegions=stableRegions,
            RawCompromisedRegions=raw,CompromisedRegions=Math.Max(0,raw-cleared),Detail=detail};
    }

    private static StellarRegionEvidence[] GetRegions(ImageEvidence e) {
        var central=e.Regions.FirstOrDefault(r=>r.RegionId==0)??new StellarRegionEvidence {RegionId=0,Width=e.SampleWidth,Height=e.SampleHeight,
            PixelsPerSample=e.PixelsPerSample,ShapeAvailable=e.Available,ShapeCompromised=e.Compromised,Eccentricity=e.Eccentricity,FwhmPixels=e.FwhmPixels};
        central=central with {Catalog=e.Catalog,FwhmPixels=e.FwhmPixels,TailStrength=e.TailStrength,
            DoublePeakStrength=e.DoublePeakStrength,ElongatedFraction=e.ElongatedFraction};
        return new[]{central}.Concat(e.Regions.Where(r=>r.RegionId!=0)).ToArray();
    }

    public void Add(BaselineKey context,ImageSample sample,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window) {
        if(sample==null)return;
        AddEvidenceReference(context,evidence with {SampleWidth=sample.Width,SampleHeight=sample.Height,PixelsPerSample=sample.PixelsPerSample,
            SourceWidth=sample.SourceWidth,SourceHeight=sample.SourceHeight,PierSide=side},status,time,side,window);
    }

    public void AddEvidenceReference(BaselineKey context,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window) {
        if(!evidence.Available || evidence.Compromised || evidence.Catalog.Length<20 || status is not (FrameStatus.Accepted or FrameStatus.Learning))return;
        var key=(context,Geometry(evidence,side));
        if(!references.TryGetValue(key,out var queue))references[key]=queue=new();
        if(queue.Count>0 && time-queue.Last().Time>TimeSpan.FromHours(6)) {
            queue.Clear(); anchors.Remove(key);
        }
        var reference=new Reference(time,evidence.ShapeTimestampUtc!=default?evidence.ShapeTimestampUtc:time,status==FrameStatus.Accepted,
            GetRegions(evidence).Select(r=>r with {Catalog=r.Catalog.ToArray()}).ToArray());
        queue.Enqueue(reference);
        if(!anchors.TryGetValue(key,out var initial))anchors[key]=initial=new();
        if(initial.Count<3)initial.Add(reference);
        while(queue.Count>Math.Clamp(window,4,50))queue.Dequeue();
    }

    public void ResetReferences(BaselineKey context) {
        foreach(var key in references.Keys.Where(k=>k.Item1.Equals(context)).ToArray())references.Remove(key);
        foreach(var key in anchors.Keys.Where(k=>k.Item1.Equals(context)).ToArray())anchors.Remove(key);
    }
    public void Clear(){references.Clear();anchors.Clear();}

    private static (double Flux,double Margin,bool Trend,double Rate) Forecast(double[] ratios,double[] margins,double[] ages,DateTime[] times) {
        double flux=Median(ratios),margin=Median(margins),rate=double.NaN;bool trend=false;
        foreach(int n in new[]{ratios.Length,4,3}.Distinct().Where(n=>n>=3&&n<=ratios.Length)) {
            var x=ages.TakeLast(n).ToArray();var y=ratios.TakeLast(n).Select(v=>Math.Log(Math.Max(1e-8,v))).ToArray();
            double mx=x.Average(),my=y.Average(),xx=x.Sum(v=>(v-mx)*(v-mx));
            double cadence=Median(times.Zip(times.Skip(1),(a,b)=>(b-a).TotalMinutes).Where(v=>v>0));
            if(xx<=1e-6 || !(cadence>0))continue;
            double slope=x.Select((v,i)=>(v-mx)*(y[i]-my)).Sum()/xx,intercept=my-slope*mx;
            double total=y.Sum(v=>(v-my)*(v-my)),residual=x.Select((v,i)=>Math.Pow(y[i]-intercept-slope*v,2)).Sum();
            double r2=total>1e-9?1-residual/total:0,scatter=Math.Sqrt(residual/n);
            if(r2>=.85 && scatter<=.025 && Math.Abs(slope)<=.02 && Math.Abs(slope)*cadence<=.10 && x.Max()-x.Min()>=cadence*1.5) {
                double horizon=Math.Max(cadence*2,5),gap=Math.Max(0,x.Min()-horizon);
                flux=Math.Exp(intercept+slope*gap);
                margin=Math.Max(flux*scatter*2,flux*Median(margins.Zip(ratios,(m,r)=>r>0?m/r:0)));
                rate=(Math.Exp(slope*60)-1)*100;trend=true;break;
            }
        }
        return(flux,Math.Max(Math.Max(flux*.005,.001),margin),trend,rate);
    }

    internal static (int Count,double Ratio,double Margin) Match(StarObservation[] current,StarObservation[] previous) {
        var m=Measure(current,previous,null,0,0,double.NaN,double.NaN);return(m.Count,m.Ratio,m.Margin);
    }

    private static Measurement Measure(StarObservation[] current,StarObservation[] previous,ImageSample raw,int width,int height,double noise,double fwhm) {
        bool Detected(StarObservation s)=>!s.WasForcedMeasurement&&!s.Saturated&&double.IsFinite(s.X)&&double.IsFinite(s.Y)
            &&double.IsFinite(s.Flux)&&double.IsFinite(s.Peak)&&s.Flux>0&&s.Peak>0
            && (!double.IsFinite(s.FluxUncertainty)||s.Flux>=5*s.FluxUncertainty);
        var a=current.Where(Detected).OrderByDescending(s=>s.Peak).ToArray();
        var b=previous.Where(s=>Detected(s)&&(!double.IsFinite(s.FluxUncertainty)||s.Flux>=10*s.FluxUncertainty)).ToArray();
        Measurement Unavailable()=>new(0,b.Length,0,double.NaN,double.NaN,double.NaN,double.NaN,false,current);
        if(a.Length<6||b.Length<20)return Unavailable();
        var votes=new Dictionary<(int,int),int>();
        foreach(var x in a.Take(50))foreach(var y in b.Take(50)) {
            var bin=((int)Math.Round((x.X-y.X)/3),(int)Math.Round((x.Y-y.Y)/3));votes[bin]=votes.GetValueOrDefault(bin)+1;
        }
        var best=votes.MaxBy(k=>k.Value);if(best.Value<4)return Unavailable();
        double dx=best.Key.Item1*3,dy=best.Key.Item2*3;var registered=new List<(StarObservation A,StarObservation B)>();
        foreach(double tolerance in new[]{4.0,2.0,1.5}) {
            registered.Clear();var used=new HashSet<int>();
            foreach(var star in a) {
                int index=-1;double min=tolerance*tolerance;
                for(int i=0;i<b.Length;i++) {
                    if(used.Contains(i))continue;
                    double xx=star.X-dx-b[i].X,yy=star.Y-dy-b[i].Y,dist=xx*xx+yy*yy;
                    if(dist<min){min=dist;index=i;}
                }
                if(index>=0){used.Add(index);registered.Add((star,b[index]));}
            }
            if(registered.Count<6)return Unavailable();
            dx=Median(registered.Select(m=>m.A.X-m.B.X));dy=Median(registered.Select(m=>m.A.Y-m.B.Y));
        }
        var augmented=current.ToList();var ratios=new List<double>();var upper=new List<double>();var lowerBounds=new List<double>();int expected=0,missing=0;
        foreach(var star in b) {
            double x=star.X+dx,y=star.Y+dy;
            if(width>0 && (x<24||y<24||x>=width-24||y>=height-24))continue;
            expected++;
            var found=augmented.Where(s=>!s.Saturated&&(s.X-x)*(s.X-x)+(s.Y-y)*(s.Y-y)<2.25)
                .OrderBy(s=>(s.X-x)*(s.X-x)+(s.Y-y)*(s.Y-y)).FirstOrDefault();
            if(found==null||found.WasForcedMeasurement)missing++;
            if(found==null && raw!=null) {
                found=LocalPixelBackground.Measure(raw,x,y,fwhm,double.IsFinite(noise)?noise:1,true);
                if(found!=null && augmented.Count<400)augmented.Add(found);
            }
            if(found==null || found.Saturated || !double.IsFinite(found.Flux))continue;
            double ratio=Math.Max(0,found.Flux/star.Flux);
            double ea=double.IsFinite(found.FluxUncertainty)?found.FluxUncertainty:0,eb=double.IsFinite(star.FluxUncertainty)?star.FluxUncertainty:0;
            double error=Math.Sqrt(Math.Pow(ea/star.Flux,2)+Math.Pow(found.Flux*eb/(star.Flux*star.Flux),2));
            ratios.Add(ratio);upper.Add(Math.Max(0,found.Flux/star.Flux+2*error));lowerBounds.Add(Math.Max(0,found.Flux/star.Flux-2*error));
        }
        if(ratios.Count<20)return new(ratios.Count,expected,missing,double.NaN,double.NaN,double.NaN,double.NaN,false,augmented.ToArray());
        var values=ratios.Order().ToArray();double median=Median(values),lower=values[values.Length/4],spread=values[values.Length*3/4]-lower;
        double margin=Math.Max(Median(upper)-median,1.86*spread/Math.Sqrt(values.Length));
        double affected=(upper.Count(v=>v<.75)+Math.Max(0,expected-ratios.Count))/(double)Math.Max(1,expected);
        // Global fading and weak detections alone do not establish patchy attenuation. Require
        // a relative deficit or separated quartiles after their measurement allowances.
        double relativeAffected=upper.Count(v=>v<median*.75)/(double)Math.Max(1,expected);
        var upperValues=upper.Order().ToArray();var lowerValues=lowerBounds.Order().ToArray();
        double confidentSpread=lowerValues[lowerValues.Length*3/4]-upperValues[upperValues.Length/4];
        bool spatial=confidentSpread>Math.Max(.04,median*.20)||(relativeAffected>=.15&&relativeAffected<=.85);
        return new(values.Length,expected,missing,median,Math.Max(.001,margin),lower,affected,spatial,augmented.ToArray());
    }

    private static double Median(IEnumerable<double> source)=>LocalPixelBackground.MedianOf(source);
}
