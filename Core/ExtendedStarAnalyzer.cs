using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Checks other field regions and repeated distant lobes in owned linear pixels.</summary>
public static class ExtendedStarAnalyzer {
    public static ImageEvidence Verify(ImageSample sample, ImageEvidence core, double guidePeak) {
        if (!core.Available || sample == null) return core;
        var clock = Stopwatch.StartNew();
        ImageEvidence Incomplete(string reason) => core with {
            ExtendedAttempted = true, Detail = core.Detail + " Extended check unavailable: " + reason,
            ElapsedMilliseconds = core.ElapsedMilliseconds + clock.Elapsed.TotalMilliseconds
        };
        double scale = sample.ArcsecPerSample;
        bool hasScale = double.IsFinite(scale) && scale > 0;
        int radius = hasScale && double.IsFinite(guidePeak)
            ? (int)Math.Clamp(Math.Ceiling(guidePeak / scale) + 8, 32, 64) : 48;
        int side = radius*2+1, w=sample.Width, h=sample.Height;
        var patches = new List<float[]>();
        foreach (var star in core.Catalog.OrderByDescending(s=>s.Peak)) {
            if (Math.Min(Math.Min(star.X,star.Y),Math.Min(w-1-star.X,h-1-star.Y)) <= radius+2 || star.Peak < 1500) continue;
            var patch = new float[side*side]; var border = new List<double>();
            for (int y=-radius;y<=radius;y++) for (int x=-radius;x<=radius;x++) {
                double v=Read(sample.Pixels,w,star.X+x,star.Y+y);
                patch[(y+radius)*side+x+radius]=(float)v;
                if (x*x+y*y >= (radius-4)*(radius-4) && x*x+y*y <= radius*radius) border.Add(v);
            }
            double bg=Median(border);
            for(int i=0;i<patch.Length;i++) patch[i]=(float)((patch[i]-bg)/star.Peak);
            patches.Add(patch);
            if(patches.Count >= Math.Clamp(core.Limits.TargetStars,20,100)) break;
        }
        if(patches.Count<20) return Incomplete("fewer than 20 bright isolated stars.");
        var stack=new float[side*side];var buffer=new float[patches.Count];
        for(int i=0;i<stack.Length;i++) {
            for(int j=0;j<patches.Count;j++)buffer[j]=patches[j][i];
            Array.Sort(buffer);stack[i]=(buffer[(buffer.Length-1)/2]+buffer[buffer.Length/2])/2;
            if(i%1024==0 && clock.ElapsedMilliseconds>1500)return Incomplete("time limit.");
        }
        var outerNoise=new List<double>(); double remote=0;int peakX=0,peakY=0;
        for(int y=-radius+2;y<=radius-2;y++)for(int x=-radius+2;x<=radius-2;x++) {
            int r2=x*x+y*y;if(r2<11*11 || r2>(radius-3)*(radius-3))continue;
            double value=Mean3(stack,side,x+radius,y+radius)-Mean3(stack,side,-x+radius,-y+radius);
            if(r2>(radius-10)*(radius-10))outerNoise.Add(Math.Abs(value));
            if(value>remote){remote=value;peakX=x;peakY=y;}
        }
        double noise=1.4826*Median(outerNoise), threshold=Math.Max(core.Limits.MaxRemotePeakPercent/100,noise*6);
        double support=remote>=threshold ? patches.Count(p=>Mean3(p,side,peakX+radius,peakY+radius)-Mean3(p,side,-peakX+radius,-peakY+radius)>threshold/2)/(double)patches.Count : 0;
        int verified=1,compromised=core.Compromised?1:0;double worst=core.Eccentricity;
        foreach(var region in sample.OuterFields.Take(4)) {
            var evidence=ImageEvidenceAnalyzer.Analyze(region,core.Limits with {TargetStars=30});
            if(evidence.Available) {verified++;worst=Math.Max(worst,evidence.Eccentricity);if(evidence.Compromised)compromised++;}
            if(clock.ElapsedMilliseconds>1500)return Incomplete("time limit.");
        }
        var png=Preview(stack,side);
        if(clock.ElapsedMilliseconds>1500)return Incomplete("time limit.");
        bool covered=hasScale && double.IsFinite(guidePeak) && guidePeak>=0 && guidePeak < (radius-4)*scale;
        var result=core with {
            ExtendedAttempted=true,ExtendedAvailable=true,GuideSearchCovered=covered,
            SearchRadiusArcsec=hasScale?(radius-4)*scale:double.NaN,
            RemotePeakStrength=remote,RemotePeakSupport=support,RemotePeakDistancePixels=Math.Sqrt(peakX*peakX+peakY*peakY)*sample.PixelsPerSample,
            VerifiedRegions=verified,CompromisedRegions=compromised,WorstRegionEccentricity=worst,ExtendedPreviewPngBase64=png,
            ElapsedMilliseconds=core.ElapsedMilliseconds+clock.Elapsed.TotalMilliseconds
        };
        return result with {Detail=core.Detail+$" Extended field: {verified}/5 regions; distant lobe {remote*100:0.00}% / {core.Limits.MaxRemotePeakPercent:0.00}%; support {support:P0}."};
    }

    private static double Read(float[] a,int w,double x,double y) {
        int ix=(int)x,iy=(int)y;double fx=x-ix,fy=y-iy;
        return (a[iy*w+ix]*(1-fx)+a[iy*w+ix+1]*fx)*(1-fy)+(a[(iy+1)*w+ix]*(1-fx)+a[(iy+1)*w+ix+1]*fx)*fy;
    }
    private static double Mean3(float[] a,int w,int x,int y) {
        double sum=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)sum+=a[(y+dy)*w+x+dx];return sum/9;
    }
    private static double Median(IEnumerable<double> values) {
        var a=values.Order().ToArray();return a.Length==0?0:(a[(a.Length-1)/2]+a[a.Length/2])/2;
    }
    private static string Preview(float[] values,int side) {
        var pixels=values.Select(v=>(byte)(255*Math.Asinh(Math.Clamp(v,0,1)/.001)/Math.Asinh(1000))).ToArray();
        var bitmap=BitmapSource.Create(side,side,96,96,PixelFormats.Gray8,null,pixels,side);bitmap.Freeze();
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();encoder.Save(stream);return Convert.ToBase64String(stream.ToArray());
    }
}
