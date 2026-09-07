using NINA.Core.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;

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
        GuideSample[] snapshot;
        lock (sync) snapshot = samples.ToArray();
        return GuideMetricsCalculator.Calculate(snapshot, exposureStartUtc, durationSeconds, excursionThresholdArcsec);
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
