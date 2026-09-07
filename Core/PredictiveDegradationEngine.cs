using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class PredictiveDegradationEngine {
    public PredictiveAssessment Evaluate(IReadOnlyList<FrameQualityResult> history, FrameQualityResult current, QualitySettings settings) {
        if (!settings.PredictiveWarningsEnabled || current == null || history == null) return new PredictiveAssessment();
        if (current.Status is FrameStatus.Error or FrameStatus.Learning) return new PredictiveAssessment();

        var same = history
            .Where(x => SameContext(x, current))
            .Where(x => x.Status is not FrameStatus.Error and not FrameStatus.Learning)
            .TakeLast(6)
            .Concat(new[] { current })
            .TakeLast(6)
            .ToArray();

        if (same.Length < 4) return new PredictiveAssessment();

        var candidates = new List<PredictiveAssessment>();

        var star = same.Where(x => IsFinite(x.StarDeviationPercent))
            .Select(x => Math.Max(0, -x.StarDeviationPercent)).ToArray();
        AddThresholdCandidate(candidates, "STARS", star, settings.MaxStarLossPercent,
            "Star count is trending toward the configured transparency-loss reject threshold.");

        var bgHigh = same.Where(x => IsFinite(x.BackgroundDeviationPercent))
            .Select(x => Math.Max(0, x.BackgroundDeviationPercent)).ToArray();
        AddThresholdCandidate(candidates, "BACKGROUND HIGH", bgHigh, settings.MaxBackgroundIncreasePercent,
            "Background is rising toward the configured high-background reject threshold.");

        var bgLow = same.Where(x => IsFinite(x.BackgroundDeviationPercent))
            .Select(x => Math.Max(0, -x.BackgroundDeviationPercent)).ToArray();
        AddThresholdCandidate(candidates, "BACKGROUND LOW", bgLow, settings.MaxBackgroundDecreasePercent,
            "Background is falling toward the configured low-background reject threshold.");

        var q = same.Select(x => x.OverallQuality).Where(IsFinite).ToArray();
        if (q.Length >= 4) {
            var slope = Slope(q);
            if (slope <= -5 && q[^1] > 50) {
                double frames = (50 - q[^1]) / slope;
                if (frames >= 0 && frames <= 3.5) {
                    candidates.Add(new PredictiveAssessment {
                        Warning = true,
                        Channel = "QUALITY",
                        Confidence = Math.Clamp(65 + Math.Abs(slope) * 4, 65, 95),
                        EstimatedFramesToThreshold = frames,
                        Message = $"Overall Quality is degrading rapidly; poor-quality territory is projected in about {Math.Max(1, Math.Ceiling(frames)):0} frame(s)."
                    });
                }
            }
        }

        return candidates.OrderByDescending(x => x.Confidence).FirstOrDefault() ?? new PredictiveAssessment();
    }

    private static void AddThresholdCandidate(List<PredictiveAssessment> target, string channel, double[] values, double threshold, string message) {
        if (values.Length < 4 || threshold <= 0) return;
        double slope = Slope(values);
        double current = values[^1];
        if (slope <= Math.Max(0.75, threshold * 0.025) || current >= threshold) return;
        double frames = (threshold - current) / slope;
        if (frames < 0 || frames > 3.5) return;

        double confidence = Math.Clamp(60 + (slope / threshold) * 300 + (1.0 - frames / 4.0) * 20, 60, 96);
        target.Add(new PredictiveAssessment {
            Warning = true,
            Channel = channel,
            Confidence = confidence,
            EstimatedFramesToThreshold = frames,
            Message = message + $" Estimated crossing: ~{Math.Max(1, Math.Ceiling(frames)):0} frame(s)."
        });
    }

    private static double Slope(double[] y) {
        if (y.Length < 2) return 0;
        double meanX = (y.Length - 1) / 2.0;
        double meanY = y.Average();
        double num = 0, den = 0;
        for (int i = 0; i < y.Length; i++) {
            double dx = i - meanX;
            num += dx * (y[i] - meanY);
            den += dx * dx;
        }
        return den <= 0 ? 0 : num / den;
    }

    private static bool SameContext(FrameQualityResult a, FrameQualityResult b) =>
        string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Filter, b.Filter, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(a.ExposureSeconds - b.ExposureSeconds) < 0.001 &&
        a.Gain == b.Gain && a.BinX == b.BinX && a.BinY == b.BinY &&
        string.Equals(a.Camera, b.Camera, StringComparison.OrdinalIgnoreCase);

    private static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
}
