using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Past clean frames only. Star identity is matched before comparing aperture flux.</summary>
public sealed class StellarReferenceEngine {
    private sealed record Reference(DateTime Time,StarObservation[] Stars,double Fwhm);
    private readonly Dictionary<(BaselineKey,string),Queue<Reference>> references=new();
    private static string Geometry(ImageSample sample,string side) => $"{sample.Width}x{sample.Height}/{sample.PixelsPerSample}/{side}";

    public ImageEvidence Compare(BaselineKey context, ImageSample sample, ImageEvidence current, DateTime time, string side) {
        if(sample==null || !current.Available || !references.TryGetValue((context,Geometry(sample,side)),out var queue))return current;
        var ratios=new List<double>();var widths=new List<double>();var counts=new List<int>();
        foreach(var reference in queue) {
            if(time < reference.Time || time-reference.Time > TimeSpan.FromMinutes(40))continue;
            var match=Match(current.Catalog,reference.Stars);
            if(match.Count<20 || !double.IsFinite(match.Ratio))continue;
            ratios.Add(match.Ratio);counts.Add(match.Count);widths.Add(reference.Fwhm);
        }
        if(ratios.Count<2)return current;
        double flux=Median(ratios), fwhm=Median(widths);
        return current with {RelativeFlux=flux,MatchedStars=counts.Min(),ReferenceFrames=ratios.Count,
            FwhmRatio=fwhm>0?current.FwhmPixels/fwhm:double.NaN,
            Detail=current.Detail+$" Signal {flux:P0} of {ratios.Count} prior clean frames; {counts.Min()} matched stars."};
    }

    public void Add(BaselineKey context,ImageSample sample,ImageEvidence evidence,FrameStatus status,DateTime time,string side,int window) {
        if(sample==null || !evidence.Available || evidence.Compromised || evidence.Catalog.Length<20 || status is not (FrameStatus.Accepted or FrameStatus.Learning))return;
        var key=(context,Geometry(sample,side));
        if(!references.TryGetValue(key,out var queue))references[key]=queue=new();
        queue.Enqueue(new Reference(time,evidence.Catalog.ToArray(),evidence.FwhmPixels));
        while(queue.Count>Math.Clamp(window,4,50))queue.Dequeue();
    }
    public void Clear()=>references.Clear();

    internal static (int Count,double Ratio) Match(StarObservation[] current,StarObservation[] previous) {
        var a=current.Where(s=>s.Flux>0&&s.Peak>1000).ToArray();
        var b=previous.Where(s=>s.Flux>0&&s.Peak>3000).ToArray();
        if(a.Length<20||b.Length<20)return(0,double.NaN);
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
            if(matches.Count<20)return(matches.Count,double.NaN);
            dx=Median(matches.Select(m=>m.A.X-m.B.X));dy=Median(matches.Select(m=>m.A.Y-m.B.Y));
        }
        var fluxes=matches.Select(m=>m.A.Flux/m.B.Flux).Order().ToArray();double ratio=Median(fluxes);
        double spread=fluxes[fluxes.Length*3/4]-fluxes[fluxes.Length/4];
        // Inconsistent matches/patchy attenuation are not evidence that a count drop is harmless.
        return(matches.Count,spread<=ratio*.35?ratio:double.NaN);
    }
    private static double Median(IEnumerable<double> source) {
        var a=source.Order().ToArray();return a.Length==0?double.NaN:(a[(a.Length-1)/2]+a[a.Length/2])/2;
    }
}
