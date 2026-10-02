using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Bounded local sky estimates. Adjacent-pixel differences separate noise from a sky gradient.</summary>
internal sealed class LocalPixelBackground {
    private const int Grid = 8;
    private readonly double[] levels = new double[Grid * Grid];
    private readonly double[] noises = new double[Grid * Grid];
    private readonly int width, height;
    public double Median => MedianOf(levels);
    public double Noise => MedianOf(noises);

    public LocalPixelBackground(float[] pixels, int width, int height) {
        this.width = width; this.height = height;
        for (int gy = 0; gy < Grid; gy++) for (int gx = 0; gx < Grid; gx++) {
            var values = new List<double>(256); var differences = new List<double>(256);
            int left = gx * width / Grid, right = (gx + 1) * width / Grid;
            int top = gy * height / Grid, bottom = (gy + 1) * height / Grid;
            int strideX = Math.Max(1, (right - left) / 16), strideY = Math.Max(1, (bottom - top) / 16);
            for (int y = top; y < bottom - 1; y += strideY) for (int x = left; x < right - 1; x += strideX) {
                double a = pixels[y * width + x]; values.Add(a);
                differences.Add(Math.Abs(a - pixels[y * width + x + 1]));
                differences.Add(Math.Abs(a - pixels[(y + 1) * width + x]));
            }
            int index = gy * Grid + gx;
            noises[index] = Math.Max(.5, 1.04836 * MedianOf(differences));
            double median = MedianOf(values);
            var clipped = values.Where(v => Math.Abs(v - median) <= 4 * Math.Max(noises[index], 1)).ToArray();
            levels[index] = clipped.Length >= values.Count / 3 ? MedianOf(clipped) : median;
        }
    }

    public double LevelAt(double x, double y) => Interpolate(levels, x, y);
    public double NoiseAt(double x, double y) => Interpolate(noises, x, y);
    private double Interpolate(double[] values, double x, double y) {
        double fx = Math.Clamp(x * Grid / width - .5, 0, Grid - 1), fy = Math.Clamp(y * Grid / height - .5, 0, Grid - 1);
        int ix = Math.Min(Grid - 2, (int)fx), iy = Math.Min(Grid - 2, (int)fy); fx -= ix; fy -= iy;
        return values[iy * Grid + ix] * (1 - fx) * (1 - fy) + values[iy * Grid + ix + 1] * fx * (1 - fy)
            + values[(iy + 1) * Grid + ix] * (1 - fx) * fy + values[(iy + 1) * Grid + ix + 1] * fx * fy;
    }

    public static StarObservation Measure(ImageSample sample, double x, double y, double fwhm, double noise, bool forced) {
        // A wider common aperture avoids interpreting ordinary PSF broadening as extinction.
        const int radius = 12, inner = 18, outer = 22;
        if (x < outer + 1 || y < outer + 1 || x >= sample.Width - outer - 2 || y >= sample.Height - outer - 2) return null;
        var annulus = new List<double>();
        for (int dy = -outer; dy <= outer; dy += 2) for (int dx = -outer; dx <= outer; dx += 2) {
            int r2 = dx * dx + dy * dy;
            if (r2 >= inner * inner && r2 <= outer * outer) annulus.Add(Read(sample, x + dx, y + dy));
        }
        double sky = MedianOf(annulus), sigma = Math.Max(.5, noise);
        var clipped = annulus.Where(v => Math.Abs(v - sky) <= 4 * sigma).ToArray();
        if (clipped.Length >= 20) sky = MedianOf(clipped);
        double flux = 0, peak = 0; int area = 0; bool saturated = false;
        for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++) {
            if (dx * dx + dy * dy > radius * radius) continue;
            double value = Read(sample, x + dx, y + dy); flux += value - sky; area++;
            if (dx * dx + dy * dy <= 4) peak = Math.Max(peak, value - sky);
            saturated |= value >= 58000;
        }
        // This is a conservative measurement allowance in raw ADU, not a calibrated probability.
        double error = Math.Sqrt(sigma * sigma * (area + (double)area * area / Math.Max(20, clipped.Length)) + Math.Max(0, flux));
        return new StarObservation(x, y, flux, peak, fwhm, error, forced, saturated);
    }

    private static double Read(ImageSample sample, double x, double y) {
        int ix = (int)x, iy = (int)y; double fx = x - ix, fy = y - iy; var a = sample.Pixels; int w = sample.Width;
        return a[iy * w + ix] * (1 - fx) * (1 - fy) + a[iy * w + ix + 1] * fx * (1 - fy)
            + a[(iy + 1) * w + ix] * (1 - fx) * fy + a[(iy + 1) * w + ix + 1] * fx * fy;
    }
    internal static double MedianOf(IEnumerable<double> source) {
        var values = source.Where(double.IsFinite).Order().ToArray();
        return values.Length == 0 ? double.NaN : (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
    }
}
