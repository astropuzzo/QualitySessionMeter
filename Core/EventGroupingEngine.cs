using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// V2 temporal event grouper. It consumes completed per-frame results and never modifies their
/// Quality, Confidence or ACCEPT/REJECT state. One healthy frame may bridge two anomalies of the
/// same family; two consecutive non-abnormal frames close the active event.
/// </summary>
public sealed class EventGroupingEngine {
    private readonly object sync = new();
    private readonly List<MutableEvent> events = new();
    private MutableEvent active;
    private int healthyGap;

    public IReadOnlyList<SessionEvent> Events {
        get {
            lock (sync) return events.Select(x => x.Snapshot(x == active)).ToArray();
        }
    }

    public void Reset() {
        lock (sync) {
            events.Clear();
            active = null;
            healthyGap = 0;
        }
    }

    public void Add(FrameQualityResult frame) {
        if (frame == null) return;

        lock (sync) {
            if (!IsAbnormal(frame)) {
                if (active != null) {
                    healthyGap++;
                    if (healthyGap >= 2) CloseActive();
                }
                return;
            }

            var type = Classify(frame);

            if (active == null) {
                Start(frame, type);
                return;
            }

            var gapMinutes = Math.Max(0, (frame.TimestampUtc - active.EndUtc).TotalMinutes);
            bool withinTemporalWindow = gapMinutes <= 12.0;
            bool sameFamily = AreCompatible(active.Type, type);

            if (withinTemporalWindow && healthyGap == 0) {
                // Contiguous abnormal frames are one episode. If evidence changes family during the
                // episode, preserve that information by promoting the event to MixedConditions.
                if (!sameFamily) active.Type = SessionEventType.MixedConditions;
                active.Add(frame);
                healthyGap = 0;
                return;
            }

            if (withinTemporalWindow && healthyGap == 1 && sameFamily) {
                // One apparently healthy exposure inside the same anomaly family is treated as a
                // brief recovery, not as a hard event boundary.
                active.Add(frame);
                healthyGap = 0;
                return;
            }

            CloseActive();
            Start(frame, type);
        }
    }

    private void Start(FrameQualityResult frame, SessionEventType type) {
        active = new MutableEvent(events.Count + 1, type, frame);
        events.Add(active);
        healthyGap = 0;
    }

    private void CloseActive() {
        active = null;
        healthyGap = 0;
    }

    private static bool IsAbnormal(FrameQualityResult frame) =>
        frame.Status is FrameStatus.Warning or FrameStatus.Rejected or FrameStatus.Error;

    public static SessionEventType Classify(FrameQualityResult frame) {
        if (frame.Status == FrameStatus.Error) return SessionEventType.AnalysisDataLoss;

        bool guide = frame.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool stars = frame.RejectReasons.Contains("STAR_COUNT_DROP", StringComparer.Ordinal);
        bool bright = frame.RejectReasons.Contains("BACKGROUND_HIGH", StringComparer.Ordinal);
        bool dark = frame.RejectReasons.Contains("BACKGROUND_LOW", StringComparer.Ordinal);

        // WARNING frames have no hard reject reason. Reuse the V1 cause/metric shape to assign the
        // most likely diagnostic family without pretending this is a physical-cause certainty.
        if (!guide && !stars && !bright && !dark && frame.Status == FrameStatus.Warning) {
            if (frame.GuidingQuality.HasValue && frame.GuidingQuality.Value < 65) guide = true;
            if (frame.StabilityQuality.HasValue && frame.StabilityQuality.Value < 65) guide = true;
            if (frame.TransparencyQuality.HasValue && frame.TransparencyQuality.Value < 65) stars = true;
            if (frame.BackgroundQuality.HasValue && frame.BackgroundQuality.Value < 65) {
                if (!double.IsNaN(frame.BackgroundDeviationPercent) && frame.BackgroundDeviationPercent >= 0) bright = true;
                else dark = true;
            }
        }

        if (guide && (stars || bright || dark)) return SessionEventType.MixedConditions;
        if (stars && bright) return SessionEventType.BrightCloudBackground;
        if (stars) return SessionEventType.CloudTransparency;
        if (bright || dark) return SessionEventType.BackgroundHaze;
        if (guide) return SessionEventType.GuidingDisturbance;
        return SessionEventType.Unknown;
    }

    private static bool AreCompatible(SessionEventType a, SessionEventType b) {
        if (a == b) return true;
        if (a == SessionEventType.MixedConditions || b == SessionEventType.MixedConditions) return true;

        bool imageA = a is SessionEventType.CloudTransparency or SessionEventType.BrightCloudBackground or SessionEventType.BackgroundHaze;
        bool imageB = b is SessionEventType.CloudTransparency or SessionEventType.BrightCloudBackground or SessionEventType.BackgroundHaze;
        return imageA && imageB;
    }

    private sealed class MutableEvent {
        private readonly List<int> frameIndices = new();
        private readonly List<double> confidences = new();
        private readonly List<string> causes = new();

        public int Index { get; }
        public SessionEventType Type { get; set; }
        public DateTime StartUtc { get; private set; }
        public DateTime EndUtc { get; private set; }
        public int FirstFrameIndex { get; private set; }
        public int LastFrameIndex { get; private set; }
        public int Rejected { get; private set; }
        public int Warning { get; private set; }
        public int Error { get; private set; }

        public MutableEvent(int index, SessionEventType type, FrameQualityResult first) {
            Index = index;
            Type = type;
            StartUtc = first.TimestampUtc;
            EndUtc = first.TimestampUtc;
            FirstFrameIndex = first.FrameIndex;
            Add(first);
        }

        public void Add(FrameQualityResult frame) {
            EndUtc = frame.TimestampUtc;
            LastFrameIndex = frame.FrameIndex;
            if (frameIndices.Count == 0) FirstFrameIndex = frame.FrameIndex;
            frameIndices.Add(frame.FrameIndex);
            if (!double.IsNaN(frame.ConfidenceScore) && !double.IsInfinity(frame.ConfidenceScore)) confidences.Add(frame.ConfidenceScore);
            if (!string.IsNullOrWhiteSpace(frame.ProbableCause)) causes.Add(frame.ProbableCause);

            switch (frame.Status) {
                case FrameStatus.Rejected: Rejected++; break;
                case FrameStatus.Warning: Warning++; break;
                case FrameStatus.Error: Error++; break;
            }
        }

        public SessionEvent Snapshot(bool isOpen) {
            double mean = confidences.Count == 0 ? 0 : confidences.Average();
            double peak = confidences.Count == 0 ? 0 : confidences.Max();
            string primaryCause = causes
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key)
                .FirstOrDefault() ?? Type.ToString();

            return new SessionEvent {
                EventIndex = Index,
                Type = Type,
                StartUtc = StartUtc,
                EndUtc = EndUtc,
                FirstFrameIndex = FirstFrameIndex,
                LastFrameIndex = LastFrameIndex,
                FrameIndices = frameIndices.ToArray(),
                AffectedFrames = frameIndices.Count,
                RejectedFrames = Rejected,
                WarningFrames = Warning,
                ErrorFrames = Error,
                MeanConfidence = mean,
                PeakConfidence = peak,
                PrimaryCause = primaryCause,
                IsOpen = isOpen
            };
        }
    }
}
