using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class BaselineEngine {
    private sealed class Bucket {
        public readonly Queue<double> Stars = new();
        public readonly Queue<double> Background = new();
        public readonly Queue<DateTime> StarTimes = new();
        public readonly Queue<DateTime> BackgroundTimes = new();
    }

    private readonly object sync = new();
    private readonly Dictionary<BaselineKey, Bucket> buckets = new();

    public BaselineSnapshot GetSnapshot(BaselineKey key, int minimumLearningFrames, DateTime? time = null) {
        lock (sync) {
            if (!buckets.TryGetValue(key, out var bucket)) {
                return new BaselineSnapshot();
            }

            var starTrend = FitSlowTrend(bucket.Stars, bucket.StarTimes, time);
            var backgroundTrend = FitSlowTrend(bucket.Background, bucket.BackgroundTimes, time);

            return new BaselineSnapshot {
                StarSamples = bucket.Stars.Count,
                BackgroundSamples = bucket.Background.Count,
                StarsReady = bucket.Stars.Count >= minimumLearningFrames,
                BackgroundReady = bucket.Background.Count >= minimumLearningFrames,
                StarMedian = Median(time.HasValue ? bucket.Stars.TakeLast(3) : bucket.Stars),
                BackgroundMedian = Median(time.HasValue ? bucket.Background.TakeLast(3) : bucket.Background),
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

    public void AddAccepted(BaselineKey key, int starCount, double backgroundMedian, int window, DateTime? time = null) {
        lock (sync) {
            if (!buckets.TryGetValue(key, out var bucket)) {
                bucket = new Bucket();
                buckets[key] = bucket;
            }

            if (starCount > 0) {
                bucket.Stars.Enqueue(starCount);
                Trim(bucket.Stars, window);
                bucket.StarTimes.Enqueue(time ?? DateTime.MinValue);
                while(bucket.StarTimes.Count > window)bucket.StarTimes.Dequeue();
            }

            if (!double.IsNaN(backgroundMedian) && !double.IsInfinity(backgroundMedian) && backgroundMedian > 0) {
                bucket.Background.Enqueue(backgroundMedian);
                Trim(bucket.Background, window);
                bucket.BackgroundTimes.Enqueue(time ?? DateTime.MinValue);
                while(bucket.BackgroundTimes.Count > window)bucket.BackgroundTimes.Dequeue();
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

    private static TrendFit FitSlowTrend(IEnumerable<double> source, IEnumerable<DateTime> times, DateTime? time) {
        var y = source.ToArray();
        if (y.Length < 5 || y.Any(v => v <= 0 || double.IsNaN(v) || double.IsInfinity(v))) {
            return new TrendFit();
        }

        var stamps = times.ToArray();
        bool timed = time.HasValue && stamps.Length == y.Length && stamps.All(t=>t!=DateTime.MinValue && t<=time.Value);
        var x = timed ? stamps.Select(t=>(t-stamps[0]).TotalMinutes).ToArray() : Enumerable.Range(0,y.Length).Select(i=>(double)i).ToArray();
        double cadence = timed ? Median(x.Zip(x.Skip(1),(a,b)=>b-a).Where(v=>v>0)) : 1;
        if(!double.IsFinite(cadence) || cadence<=0)return new TrendFit();
        double next = timed ? Math.Min((time.Value-stamps[0]).TotalMinutes,x[^1]+cadence*2) : y.Length;
        double meanX = x.Average();
        double meanY = y.Average();
        double sxx = 0;
        double sxy = 0;
        for (int i = 0; i < y.Length; i++) {
            double dx = x[i] - meanX;
            sxx += dx * dx;
            sxy += dx * (y[i] - meanY);
        }
        if (sxx <= 0 || meanY <= 0) return new TrendFit();

        double slope = sxy / sxx;
        double intercept = meanY - slope * meanX;
        double predicted = intercept + slope * next;
        double ssTot = y.Sum(v => (v - meanY) * (v - meanY));
        double ssRes = 0;
        for (int i = 0; i < y.Length; i++) {
            double residual = y[i] - (intercept + slope * x[i]);
            ssRes += residual * residual;
        }
        double r2 = ssTot <= 1e-12 ? 0 : Math.Clamp(1.0 - ssRes / ssTot, 0, 1);
        double percentPerFrame = slope * cadence / meanY * 100.0;
        double totalPercent = slope * (x[^1]-x[0]) / meanY * 100.0;

        // Timestamped assessment follows coherent recent changes; extrapolation is bounded to
        // two normal exposure intervals so a rejected run cannot erase an abrupt signal deficit.
        bool usable = predicted > 0 && r2 >= (timed ? .85 : .75) && Math.Abs(totalPercent) >= 2
            && (timed ? Math.Abs(percentPerFrame) <= 10 : Math.Abs(totalPercent) <= 20);

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
