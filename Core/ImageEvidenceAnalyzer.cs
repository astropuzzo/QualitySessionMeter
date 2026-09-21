using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Bounded, linear-pixel shape diagnostics. Never analyze stretched previews or JPEGs.</summary>
public static class ImageEvidenceAnalyzer {
    // Copy a central field, averaging complete Bayer cells. Own the copy before leaving the save callback.
    public static ImageSample Capture(ushort[] pixels, int width, int height, bool bayer, double arcsecPerPixel = double.NaN) {
        if (pixels == null || width < 64 || height < 64 || (long)width * height > pixels.Length) return null;
        int step = bayer ? 2 : 1;
        int w = Math.Min(1024, width / step), h = Math.Min(1024, height / step);
        int x0 = ((width - w * step) / 2) / step * step, y0 = ((height - h * step) / 2) / step * step;
        var copy = new float[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
            int p = (y0 + y * step) * width + x0 + x * step;
            copy[y * w + x] = step == 1 ? pixels[p] : (pixels[p] + (float)pixels[p + 1] + pixels[p + width] + pixels[p + width + 1]) / 4;
        }
        var outer = new List<ImageSample>();
        int side = Math.Min(384, Math.Min(width, height) / step / 4);
        if (side >= 64) foreach (var (cx, cy) in new[] { (.22,.22),(.78,.22),(.22,.78),(.78,.78) }) {
            int ox = Math.Clamp((int)(width * cx - side * step / 2.0) / step * step, 0, width - side * step);
            int oy = Math.Clamp((int)(height * cy - side * step / 2.0) / step * step, 0, height - side * step);
            var field = new float[side * side];
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) {
                int p = (oy + y * step) * width + ox + x * step;
                field[y * side + x] = step == 1 ? pixels[p] : (pixels[p] + (float)pixels[p+1] + pixels[p+width] + pixels[p+width+1]) / 4;
            }
            outer.Add(new ImageSample(field, side, side) { PixelsPerSample = step, ArcsecPerSample = arcsecPerPixel * step });
        }
        return new ImageSample(copy, w, h) { PixelsPerSample = step, ArcsecPerSample = arcsecPerPixel * step, OuterFields = outer.ToArray() };
    }

    public static ImageEvidence Analyze(ImageSample sample, StarShapeLimits limits = null) {
        limits ??= new StarShapeLimits();
        var clock = Stopwatch.StartNew();
        ImageEvidence Unavailable(int count, string why) => new() { Attempted = true, Stars = count, Limits = limits, ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds, Detail = "Stellar check unavailable: " + why };
        if (sample == null) return Unavailable(0, "raw pixels unavailable.");
        var a = sample.Pixels; int w = sample.Width, h = sample.Height;
        if (a == null || w < 40 || h < 40 || w > 1024 || h > 1024 || (long)w * h != a.Length || a.Any(v => !float.IsFinite(v))) return Unavailable(0, "invalid raw field.");
        var sampled = a.Where((_, i) => i % 97 == 0).Select(x => (double)x).ToArray();
        double bg = Median(sampled), noise = 1.4826 * Median(sampled.Select(x => Math.Abs(x - bg)));
        var candidates = new List<(int X, int Y, double Peak)>();
        for (int y = 18; y < h - 18; y++) for (int x = 18; x < w - 18; x++) {
            double p = a[y * w + x];
            if (p < bg + Math.Max(120, noise * 10) || p >= 58000) continue;
            bool max = true;
            for (int dy = -2; dy <= 2 && max; dy++) for (int dx = -2; dx <= 2; dx++)
                if ((dy != 0 || dx != 0) && a[(y + dy) * w + x + dx] >= p) { max = false; break; }
            if (max) candidates.Add((x, y, p));
        }
        var centers = new List<(int X, int Y)>();
        var ratios = new List<double>(); var patches = new List<double[]>(); var cells = new HashSet<int>();
        var fluxes = new List<double>(); var catalog = new List<StarObservation>(); int attempted = 0, examined = 0;
        foreach (var star in candidates.OrderByDescending(x => x.Peak)) {
            if (clock.ElapsedMilliseconds > 1500) return Unavailable(ratios.Count, "analysis time budget exceeded.");
            if (centers.Any(p => Math.Abs(p.X - star.X) < 20 && Math.Abs(p.Y - star.Y) < 20)) continue;
            if (++examined > Math.Clamp(limits.TargetStars, 20, 200) * 4) break;
            int x = star.X, y = star.Y;
            var border = new List<double>();
            for (int t = -12; t <= 12; t++) {
                border.Add(a[(y - 12) * w + x + t]); border.Add(a[(y + 12) * w + x + t]);
                border.Add(a[(y + t) * w + x - 12]); border.Add(a[(y + t) * w + x + 12]);
            }
            double local = Median(border), mass = 0, mx = 0, my = 0;
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) {
                if (dx * dx + dy * dy > 16) continue;
                double v = Math.Max(0, a[(y + dy) * w + x + dx] - local - noise * 2);
                mass += v; mx += dx * v; my += dy * v;
            }
            if (mass <= 0) continue;
            mx /= mass; my /= mass;
            if (Math.Abs(mx) > 1.5 || Math.Abs(my) > 1.5) continue;
            double xx = 0, yy = 0, xy = 0;
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) {
                if (dx * dx + dy * dy > 16) continue;
                double v = Math.Max(0, a[(y + dy) * w + x + dx] - local - noise * 2);
                xx += v * (dx - mx) * (dx - mx); yy += v * (dy - my) * (dy - my); xy += v * (dx - mx) * (dy - my);
            }
            xx /= mass; yy /= mass; xy /= mass;
            double delta = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy);
            double minor = (xx + yy - delta) / 2, major = (xx + yy + delta) / 2;
            double peak = star.Peak - local;
            var fit = FitCore(a,w,x,y,local,peak,noise);
            // Unresolved single-pixel detections are not failed stellar shape measurements.
            // Count only resolved candidates in the reliability fraction, keeping broad/poor fits as failures.
            if (minor < 0.2) continue;
            attempted++;
            if (major > 9 || (minor < .35 && !fit.Valid)) continue;
            // A finite aperture rounds elongated cores. A well-constrained fit can expose this bias;
            // moments remain a conservative fallback when tails violate the Gaussian model.
            double ratioValue = Math.Sqrt(major/minor);
            if(fit.Valid) ratioValue = Math.Max(ratioValue,fit.Ratio);
            var patch = new double[625];
            for (int dy = -12; dy <= 12; dy++) for (int dx = -12; dx <= 12; dx++)
                patch[(dy + 12) * 25 + dx + 12] = (Bilinear(a, w, x + mx + dx, y + my + dy) - local) / peak;
            centers.Add((x, y)); ratios.Add(ratioValue); patches.Add(patch);
            fluxes.Add(mass);
            double apertureFlux = 0;
            for (int dy = -8; dy <= 8; dy++) for (int dx = -8; dx <= 8; dx++)
                if (dx*dx+dy*dy<=64) apertureFlux += a[(y+dy)*w+x+dx]-local;
            catalog.Add(new StarObservation(x+mx,y+my,apertureFlux,peak,(fit.Valid?fit.Fwhm:2.35482*Math.Pow(major*minor,.25))*sample.PixelsPerSample));
            cells.Add((y * 3 / h) * 3 + x * 3 / w);
            if (ratios.Count >= Math.Clamp(limits.TargetStars, 20, 200)) break;
        }
        if (ratios.Count < Math.Clamp(limits.MinimumStars, 20, 200) || cells.Count < 3 || ratios.Count < attempted * .60)
            return Unavailable(ratios.Count, $"{ratios.Count} reliable stars from {attempted} resolved candidates in {cells.Count} field cells.") with {
                Catalog = catalog.ToArray(), FwhmPixels = catalog.Count > 0 ? Median(catalog.Select(s=>s.Fwhm)) : double.NaN
            };
        var stack = Enumerable.Range(0, 625).Select(i => Median(patches.Select(p => p[i]))).ToArray();
        double tail = 0;
        // Opposite-side subtraction suppresses symmetric halos; the median stack suppresses companions/noise.
        for (int y = -10; y <= 10; y++) for (int x = -10; x <= 10; x++) {
            if (x * x + y * y < 25 || x * x + y * y > 100) continue;
            double asymmetry = stack[(y + 12) * 25 + x + 12] - stack[(-y + 12) * 25 - x + 12];
            tail = Math.Max(tail, asymmetry);
        }
        double doublePeak = 0;
        // A repeated detached lobe survives the median stack; isolated astronomical companions usually do not.
        for (int y = -10; y <= 10; y++) for (int x = -10; x <= 10; x++) {
            if (x * x + y * y < 9 || x * x + y * y > 100) continue;
            int index = (y + 12) * 25 + x + 12;
            if (stack[index] > stack[index-1] && stack[index] > stack[index+1]
                && stack[index] > stack[index-25] && stack[index] > stack[index+25]) doublePeak = Math.Max(doublePeak, stack[index]);
        }
        double ratio = Median(ratios), eccentricity = Math.Sqrt(1 - 1 / (ratio * ratio));
        double elongated = ratios.Count(v => Math.Sqrt(1 - 1 / (v * v)) >= limits.MaxEccentricity) / (double)ratios.Count;
        string proof = MakePreview(stack, patches);
        if (clock.ElapsedMilliseconds > 1500) return Unavailable(ratios.Count, "analysis time budget exceeded.");
        return new ImageEvidence {
            Attempted = true, Available = true, Stars = ratios.Count, AxisRatio = ratio, ElongatedFraction = elongated, TailStrength = tail,
            DoublePeakStrength = doublePeak, MedianFlux = Median(fluxes), Limits = limits, PreviewPngBase64 = proof, ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds,
            Catalog = catalog.ToArray(), FwhmPixels = Median(catalog.Select(s=>s.Fwhm)), VerifiedRegions = 1, WorstRegionEccentricity = eccentricity,
            Detail = $"{ratios.Count} central stars · eccentricity {eccentricity:0.00} / limit {limits.MaxEccentricity:0.00} · deformed {elongated:P0} / {limits.DeformedFraction:P0} · tail {tail*100:0.0}% / {limits.MaxTailPercent:0.0}% · secondary peak {doublePeak*100:0.0}% / {limits.MaxDoublePeakPercent:0.0}%."
        };
    }

    private static (bool Valid,double Ratio,double Fwhm) FitCore(float[] a,int w,int x,int y,double bg,double peak,double noise) {
        var matrix=new double[6,7];int points=0;
        var observations=new List<(double[] Basis,double Log,double Weight)>();
        for(int dy=-5;dy<=5;dy++)for(int dx=-5;dx<=5;dx++) {
            double signal=a[(y+dy)*w+x+dx]-bg;
            if(signal<Math.Max(peak*.03,noise*8))continue;
            double[] basis={1,dx,dy,dx*dx,dx*dy,dy*dy};double log=Math.Log(signal),weight=signal/peak;
            observations.Add((basis,log,weight));points++;
            for(int i=0;i<6;i++) { for(int j=0;j<6;j++)matrix[i,j]+=weight*basis[i]*basis[j];matrix[i,6]+=weight*basis[i]*log; }
        }
        if(points<10)return(false,0,0);
        for(int k=0;k<6;k++) {
            int pivot=k;for(int i=k+1;i<6;i++)if(Math.Abs(matrix[i,k])>Math.Abs(matrix[pivot,k]))pivot=i;
            if(Math.Abs(matrix[pivot,k])<1e-9)return(false,0,0);
            for(int j=k;j<=6;j++)(matrix[k,j],matrix[pivot,j])=(matrix[pivot,j],matrix[k,j]);
            double div=matrix[k,k];for(int j=k;j<=6;j++)matrix[k,j]/=div;
            for(int i=0;i<6;i++)if(i!=k) { double m=matrix[i,k];for(int j=k;j<=6;j++)matrix[i,j]-=m*matrix[k,j]; }
        }
        double xx=-2*matrix[3,6],xy=-matrix[4,6],yy=-2*matrix[5,6];
        double delta=Math.Sqrt((xx-yy)*(xx-yy)+4*xy*xy),small=(xx+yy-delta)/2,large=(xx+yy+delta)/2;
        if(small<=0 || large<=0 || 1/large<.2 || 1/small>16)return(false,0,0);
        double error=0,weightSum=0;
        foreach(var o in observations) { double model=0;for(int i=0;i<6;i++)model+=matrix[i,6]*o.Basis[i];error+=o.Weight*(model-o.Log)*(model-o.Log);weightSum+=o.Weight; }
        if(Math.Sqrt(error/weightSum)>.15)return(false,0,0);
        return(true,Math.Sqrt(large/small),2.35482/Math.Pow(small*large,.25));
    }

    private static string MakePreview(double[] stack, List<double[]> patches) {
        const int width = 135, height = 54; var pixels = new byte[width * height];
        void Draw(double[] patch, int left, int top, int scale) {
            for (int y = 0; y < 25; y++) for (int x = 0; x < 25; x++) {
                byte v = (byte)(255 * Math.Pow(Math.Clamp(patch[y * 25 + x], 0, 1), .40));
                for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++) pixels[(top + y * scale + dy) * width + left + x * scale + dx] = v;
            }
        }
        Draw(stack, 1, 2, 2);
        for (int i = 0; i < Math.Min(6, patches.Count); i++) Draw(patches[i * (patches.Count - 1) / 5], 55 + i % 3 * 27, 1 + i / 3 * 27, 1);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return Convert.ToBase64String(stream.ToArray());
    }

    private static double Bilinear(float[] a, int w, double x, double y) {
        int ix = (int)x, iy = (int)y; double fx = x - ix, fy = y - iy;
        return (a[iy * w + ix] * (1 - fx) + a[iy * w + ix + 1] * fx) * (1 - fy)
            + (a[(iy + 1) * w + ix] * (1 - fx) + a[(iy + 1) * w + ix + 1] * fx) * fy;
    }
    private static double Median(IEnumerable<double> values) {
        var a = values.OrderBy(x => x).ToArray(); if (a.Length == 0) return 0;
        return (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2;
    }
}
