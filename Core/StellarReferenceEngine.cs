using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Past clean frames only. Star identity is matched before comparing aperture flux.</summary>
public sealed class StellarReferenceEngine {
    private sealed record Reference(DateTime Time,StarObservation[] Stars,double Fwhm);
    private readonly Dictionary<(BaselineKey,string),Queue<Reference>> references=new();
    private readonly Dictionary<(BaselineKey,string),List<Reference>> anchors=new();
    private static string Geometry(ImageSample sample,string side) => $"{sample.Width}x{sample.Height}/{sample.PixelsPerSample}/{side}";

    public ImageEvidence Compare(BaselineKey context, ImageSample sample, ImageEvidence current, DateTime time, string side) {
        if(sample==null || current.Catalog.Length<20 || !references.TryGetValue((context,Geometry(sample,side)),out var queue))return current;
        var ratios=new List<double>();var widths=new List<double>();var counts=new List<int>();var ages=new List<double>();var margins=new List<double>();
        foreach(var reference in queue) {
            // Long cloud intervals must not turn into a fresh "all clear" when the last clean frames age.
            // Older matched references can detect loss, but cannot authorize a count-flag rescue.
            if(time < reference.Time || time-reference.Time > TimeSpan.FromHours(6))continue;
            var match=Match(current.Catalog,reference.Stars);
            if(match.Count<20 || !double.IsFinite(match.Ratio))continue;
            ratios.Add(match.Ratio);margins.Add(match.Margin);counts.Add(match.Count);widths.Add(reference.Fwhm);ages.Add((time-reference.Time).TotalMinutes);
        }
        if(ratios.Count<2)return current;
        // Ratios against older clean frames encode the recent trajectory. Forecast its continuation,
        // instead of treating normal altitude/extinction changes as a deficit against an old median.
        double flux=Median(ratios), margin=Median(margins), rate=double.NaN;bool trendUsed=false;
        foreach(int fitCount in new[]{ratios.Count,4,3}.Distinct().Where(n=>n>=3 && n<=ratios.Count)) {
            var x=ages.TakeLast(fitCount).ToArray();var y=ratios.TakeLast(fitCount).Select(v=>Math.Log(v)).ToArray();
            double mx=x.Average(),my=y.Average(),xx=x.Sum(v=>(v-mx)*(v-mx));
            var gaps=queue.Zip(queue.Skip(1),(a,b)=>(b.Time-a.Time).TotalMinutes).Where(v=>v>0).ToArray();
            double cadence=Median(gaps);
            if(xx>1e-6 && cadence>0) {
                double slope=x.Select((v,i)=>(v-mx)*(y[i]-my)).Sum()/xx,intercept=my-slope*mx;
                double total=y.Sum(v=>(v-my)*(v-my)),residual=x.Select((v,i)=>Math.Pow(y[i]-intercept-slope*v,2)).Sum();
                double r2=total>1e-9?1-residual/total:0,scatter=Math.Sqrt(residual/y.Length);
                if(r2>=.85 && scatter<=.025 && Math.Abs(slope)<=.02 && Math.Abs(slope)*cadence<=.10 && x.Max()-x.Min()>=cadence*1.5) {
                    // Do not extrapolate indefinitely through a run of rejected frames.
                    double horizon=Math.Max(cadence*2,5),gap=Math.Max(0,x.Min()-horizon);
                    flux=Math.Exp(intercept+slope*gap);
                    margin=Math.Max(flux*scatter*2,flux*Median(margins.Zip(ratios,(m,r)=>m/r)));
                    rate=(Math.Exp(slope*60)-1)*100;trendUsed=true;
                    break;
                }
            }
        }
        margin=Math.Max(flux*.005,margin);
        double sessionFlux=double.NaN,sessionUpper=double.NaN;
        if(anchors.TryGetValue((context,Geometry(sample,side)),out var initial)) {
            var initialMatches=initial.Where(r=>r.Time<=time).Select(r=>Match(current.Catalog,r.Stars))
                .Where(m=>m.Count>=20 && double.IsFinite(m.Ratio)).ToArray();
            if(initialMatches.Length>=2) {
                sessionFlux=Median(initialMatches.Select(m=>m.Ratio));
                sessionUpper=sessionFlux+Math.Max(sessionFlux*.005,Median(initialMatches.Select(m=>m.Margin)));
            }
        }
        double fwhm=Median(widths);
        return current with {RelativeFlux=flux,RelativeFluxUpperBound=flux+margin,SessionRelativeFlux=sessionFlux,SessionFluxUpperBound=sessionUpper,
            SignalTrendUsed=trendUsed,SignalTrendPercentPerHour=rate,MatchedStars=counts.Min(),ReferenceFrames=ratios.Count,ReferenceAgeMinutes=ages.Max(),
            FwhmRatio=fwhm>0?current.FwhmPixels/fwhm:double.NaN,
            Detail=current.Detail+$" Signal {flux:P0} of recent expected level (+{margin:P1} measurement allowance); {counts.Min()} matched stars. "
                +(trendUsed?$"Gradual trend {rate:+0.0;-0.0;0}%/h. ":"")
                +(double.IsFinite(sessionFlux)?$"Session signal {sessionFlux:P0}. ":"")
                +$"Reference age {ages.Min():0}–{ages.Max():0} min."};
    }

    public void Add(BaselineKey context,ImageSample sample,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window) {
        if(sample==null || !evidence.Available || evidence.Compromised || evidence.Catalog.Length<20 || status is not (FrameStatus.Accepted or FrameStatus.Learning))return;
        var key=(context,Geometry(sample,side));
        if(!references.TryGetValue(key,out var queue))references[key]=queue=new();
        var reference=new Reference(time,evidence.Catalog.ToArray(),evidence.FwhmPixels);
        queue.Enqueue(reference);
        if(!anchors.TryGetValue(key,out var initial))anchors[key]=initial=new();
        if(initial.Count<3)initial.Add(reference);
        while(queue.Count>Math.Clamp(window,4,50))queue.Dequeue();
    }
    public void Clear(){references.Clear();anchors.Clear();}

    internal static (int Count,double Ratio,double Margin) Match(StarObservation[] current,StarObservation[] previous) {
        var a=current.Where(s=>s.Flux>0&&s.Peak>1000).ToArray();
        var b=previous.Where(s=>s.Flux>0&&s.Peak>3000).ToArray();
        if(a.Length<20||b.Length<20)return(0,double.NaN,double.NaN);
        var votes=new Dictionary<(int,int),int>();
        foreach(var x in a.Take(50))foreach(var y in b.Take(50)) {
            var key=((int)Math.Round((x.X-y.X)/3),(int)Math.Round((x.Y-y.Y)/3));
            votes[key]=votes.GetValueOrDefault(key)+1;
        }
        var best=votes.MaxBy(kv=>kv.Value).Key;double dx=best.Item1*3,dy=best.Item2*3;
        var matches=new List<(StarObservation A,StarObservation B)>();
        foreach(double tolerance in new[]{4.0,2.0,1.5}) {
            matches.Clear();var used=new HashSet<int>();
            foreach(var star in a) {
                int index=-1;double min=tolerance*tolerance;
                for(int i=0;i<b.Length;i++) {
                    double xx=star.X-dx-b[i].X,yy=star.Y-dy-b[i].Y,dist=xx*xx+yy*yy;
                    if(dist<min){min=dist;index=i;}
                }
                if(index>=0&&used.Add(index))matches.Add((star,b[index]));
            }
            if(matches.Count<20)return(matches.Count,double.NaN,double.NaN);
            dx=Median(matches.Select(m=>m.A.X-m.B.X));dy=Median(matches.Select(m=>m.A.Y-m.B.Y));
        }
        var fluxes=matches.Select(m=>m.A.Flux/m.B.Flux).Order().ToArray();double ratio=Median(fluxes);
        double spread=fluxes[fluxes.Length*3/4]-fluxes[fluxes.Length/4];
        // Inconsistent matches/patchy attenuation are not evidence that a count drop is harmless.
        return(matches.Count,spread<=ratio*.35?ratio:double.NaN,1.86*spread/Math.Sqrt(matches.Count));
    }
    private static double Median(IEnumerable<double> source) {
        var a=source.Order().ToArray();return a.Length==0?double.NaN:(a[(a.Length-1)/2]+a[a.Length/2])/2;
    }
}
