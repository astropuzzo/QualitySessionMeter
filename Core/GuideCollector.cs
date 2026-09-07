using NINA.Core.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class GuideCollector : IDisposable {
    private readonly IGuiderMediator guiderMediator;
    private readonly object sync = new();
    private readonly List<GuideSample> samples = new();
    private bool disposed;

    public GuideCollector(IGuiderMediator guiderMediator) {
        this.guiderMediator = guiderMediator;
        guiderMediator.GuideEvent += OnGuideEvent;
    }

    private void OnGuideEvent(object sender, IGuideStep step) {
        if (step == null || disposed) return;

        double scale = double.NaN;
        try {
            scale = guiderMediator.GetInfo()?.PixelScale ?? double.NaN;
        } catch {
        }

        var sample = new GuideSample {
            TimestampUtc = DateTime.UtcNow,
            RaPixels = step.RADistanceRaw,
            DecPixels = step.DECDistanceRaw,
            PixelScaleArcsecPerPixel = scale
        };

        lock (sync) {
            samples.Add(sample);
            var cutoff = DateTime.UtcNow.AddHours(-2);
            int remove = 0;
            while (remove < samples.Count && samples[remove].TimestampUtc < cutoff) remove++;
            if (remove > 0) samples.RemoveRange(0, remove);
        }
    }

    public GuideExposureMetrics GetExposureMetrics(DateTime exposureStartUtc, double durationSeconds, double excursionThresholdArcsec) {
        if (exposureStartUtc == DateTime.MinValue || durationSeconds <= 0) {
            return new GuideExposureMetrics();
        }

        var end = exposureStartUtc.AddSeconds(durationSeconds);
        GuideSample[] window;
        lock (sync) {
            window = samples
                .Where(x => x.TimestampUtc >= exposureStartUtc && x.TimestampUtc <= end)
                .Where(x => x.HasScale && !double.IsNaN(x.TotalArcsec))
                .ToArray();
        }

        if (window.Length == 0) return new GuideExposureMetrics();

        var errors = window.Select(x => x.TotalArcsec).ToArray();
        double rms = Math.Sqrt(errors.Select(x => x * x).Average());
        double max = errors.Max();

        double typicalCadence = 0;
        if (window.Length > 1) {
            var cadences = new List<double>();
            for (int i = 1; i < window.Length; i++) {
                var dt = (window[i].TimestampUtc - window[i - 1].TimestampUtc).TotalSeconds;
                if (dt > 0 && dt < 30) cadences.Add(dt);
            }
            if (cadences.Count > 0) {
                var ordered = cadences.OrderBy(x => x).ToArray();
                typicalCadence = ordered[ordered.Length / 2];
            }
        }

        double currentRun = 0;
        double longestRun = 0;
        for (int i = 0; i < window.Length; i++) {
            if (errors[i] > excursionThresholdArcsec) {
                double dt;
                if (i + 1 < window.Length) {
                    dt = (window[i + 1].TimestampUtc - window[i].TimestampUtc).TotalSeconds;
                    if (dt < 0 || dt > 30) dt = typicalCadence;
                } else {
                    dt = typicalCadence;
                }
                currentRun += Math.Max(0, dt);
                longestRun = Math.Max(longestRun, currentRun);
            } else {
                currentRun = 0;
            }
        }

        return new GuideExposureMetrics {
            HasData = true,
            Samples = window.Length,
            RmsArcsec = rms,
            MaxExcursionArcsec = max,
            MaxSustainedExcursionSeconds = longestRun
        };
    }

    public void Clear() {
        lock (sync) samples.Clear();
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        guiderMediator.GuideEvent -= OnGuideEvent;
    }
}
