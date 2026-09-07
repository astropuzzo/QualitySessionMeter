using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class BaselineEngine {
    private sealed class Bucket {
        public readonly Queue<double> Stars = new();
        public readonly Queue<double> Background = new();
    }

    private readonly object sync = new();
    private readonly Dictionary<BaselineKey, Bucket> buckets = new();

    public BaselineSnapshot GetSnapshot(BaselineKey key, int minimumLearningFrames) {
        lock (sync) {
            if (!buckets.TryGetValue(key, out var bucket)) {
                return new BaselineSnapshot();
            }

            var starTrend = FitSlowTrend(bucket.Stars);
            var backgroundTrend = FitSlowTrend(bucket.Background);

            return new BaselineSnapshot {
                StarSamples = bucket.Stars.Count,
                BackgroundSamples = bucket.Background.Count,
                StarsReady = bucket.Stars.Count >= minimumLearningFrames,
                BackgroundReady = bucket.Background.Count >= minimumLearningFrames,
                StarMedian = Median(bucket.Stars),
                BackgroundMedian = Median(bucket.Background),
                StarTrendUsable = starTrend.Usable,
                BackgroundTrendUsable = backgroundTrend.Usable,
                StarTrendExpectedNext = starTrend.ExpectedNext,
                BackgroundTrendExpectedNext = backgroundTrend.ExpectedNext,
                StarTrendPercentPerFrame = starTrend.PercentPerFrame,
                BackgroundTrendPercentPerFrame = backgroundTrend.PercentPerFrame,
                StarTrendR2 = starTrend.R2,
                BackgroundTrendR2 = backgroundTrend.R2
            };
        }
    }

    public void AddAccepted(BaselineKey key, int starCount, double backgroundMedian, int window) {
        lock (sync) {
            if (!buckets.TryGetValue(key, out var bucket)) {
                bucket = new Bucket();
                buckets[key] = bucket;
            }

            if (starCount > 0) {
                bucket.Stars.Enqueue(starCount);
                Trim(bucket.Stars, window);
            }

            if (!double.IsNaN(backgroundMedian) && !double.IsInfinity(backgroundMedian) && backgroundMedian > 0) {
                bucket.Background.Enqueue(backgroundMedian);
                Trim(bucket.Background, window);
            }
        }
    }

    public void Clear() {
        lock (sync) buckets.Clear();
    }

    private static void Trim(Queue<double> queue, int window) {
        while (queue.Count > window) queue.Dequeue();
    }

    private static double Median(IEnumerable<double> values) {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return double.NaN;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static TrendFit FitSlowTrend(IEnumerable<double> source) {
        var y = source.ToArray();
        if (y.Length < 5 || y.Any(v => v <= 0 || double.IsNaN(v) || double.IsInfinity(v))) {
            return new TrendFit();
        }

        double meanX = (y.Length - 1) / 2.0;
        double meanY = y.Average();
        double sxx = 0;
        double sxy = 0;
        for (int i = 0; i < y.Length; i++) {
            double dx = i - meanX;
            sxx += dx * dx;
            sxy += dx * (y[i] - meanY);
        }
        if (sxx <= 0 || meanY <= 0) return new TrendFit();

        double slope = sxy / sxx;
        double intercept = meanY - slope * meanX;
        double predicted = intercept + slope * y.Length;
        double ssTot = y.Sum(v => (v - meanY) * (v - meanY));
        double ssRes = 0;
        for (int i = 0; i < y.Length; i++) {
            double residual = y[i] - (intercept + slope * i);
            ssRes += residual * residual;
        }
        double r2 = ssTot <= 1e-12 ? 0 : Math.Clamp(1.0 - ssRes / ssTot, 0, 1);
        double percentPerFrame = slope / meanY * 100.0;
        double totalPercent = slope * Math.Max(1, y.Length - 1) / meanY * 100.0;

        // Only a statistically coherent, genuinely slow trend is considered usable. Larger changes
        // remain visible but are not normalized as "expected" because they may represent real sky
        // degradation. Rejected/warning frames never reach this queue in the first place.
        bool usable = predicted > 0 &&
                      r2 >= 0.75 &&
                      Math.Abs(totalPercent) >= 2.0 &&
                      Math.Abs(totalPercent) <= 20.0;

        return new TrendFit {
            Usable = usable,
            ExpectedNext = predicted,
            PercentPerFrame = percentPerFrame,
            R2 = r2
        };
    }

    private sealed class TrendFit {
        public bool Usable { get; init; }
        public double ExpectedNext { get; init; } = double.NaN;
        public double PercentPerFrame { get; init; } = double.NaN;
        public double R2 { get; init; } = double.NaN;
    }
}
