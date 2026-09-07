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

            return new BaselineSnapshot {
                StarSamples = bucket.Stars.Count,
                BackgroundSamples = bucket.Background.Count,
                StarsReady = bucket.Stars.Count >= minimumLearningFrames,
                BackgroundReady = bucket.Background.Count >= minimumLearningFrames,
                StarMedian = Median(bucket.Stars),
                BackgroundMedian = Median(bucket.Background)
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
}
