using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Synthetic;

public static class SyntheticEventOracle {
    public static IReadOnlyList<string> ValidateWholeNight(
        SyntheticSessionDefinition scenario,
        IReadOnlyList<FrameQualityResult> frames,
        IReadOnlyList<SessionEvent> events) {

        var failures = new List<string>();
        var abnormal = frames
            .Where(x => x.Status is FrameStatus.Warning or FrameStatus.Rejected or FrameStatus.Error)
            .Select(x => x.FrameIndex)
            .OrderBy(x => x)
            .ToArray();
        var grouped = events.SelectMany(x => x.FrameIndices).OrderBy(x => x).ToArray();

        if (!abnormal.SequenceEqual(grouped)) {
            failures.Add($"{scenario.DisplayName}: grouped event frame set does not exactly match abnormal frame set; abnormal=[{string.Join(",", abnormal)}], grouped=[{string.Join(",", grouped)}]");
        }

        if (abnormal.Length == 0 && events.Count != 0) {
            failures.Add($"{scenario.DisplayName}: excellent/clean session should have zero events, got {events.Count}");
        }

        if (abnormal.Length > 0 && events.Count == 0) {
            failures.Add($"{scenario.DisplayName}: abnormal session produced no events");
        }

        for (int i = 0; i < events.Count; i++) {
            var e = events[i];
            if (e.EventIndex != i + 1) failures.Add($"{scenario.DisplayName}: event indices are not contiguous at position {i}");
            if (e.AffectedFrames != e.FrameIndices.Count) failures.Add($"{scenario.DisplayName}/{e.Id}: AffectedFrames mismatch");
            if (e.AffectedFrames != e.RejectedFrames + e.WarningFrames + e.ErrorFrames) failures.Add($"{scenario.DisplayName}/{e.Id}: status counters do not add up");
            if (e.FrameIndices.Count == 0) failures.Add($"{scenario.DisplayName}/{e.Id}: empty event");
            if (e.FirstFrameIndex != e.FrameIndices.Min() || e.LastFrameIndex != e.FrameIndices.Max()) failures.Add($"{scenario.DisplayName}/{e.Id}: frame bounds mismatch");
            if (e.EndUtc < e.StartUtc) failures.Add($"{scenario.DisplayName}/{e.Id}: negative event duration");
            if (double.IsNaN(e.MeanConfidence) || e.MeanConfidence < 0 || e.MeanConfidence > 100) failures.Add($"{scenario.DisplayName}/{e.Id}: invalid mean confidence");
            if (double.IsNaN(e.PeakConfidence) || e.PeakConfidence < 0 || e.PeakConfidence > 100) failures.Add($"{scenario.DisplayName}/{e.Id}: invalid peak confidence");
        }

        return failures;
    }

    public static IReadOnlyList<string> RunDeterministicGroupingCases() {
        var failures = new List<string>();
        var t0 = new DateTime(2026, 9, 7, 20, 0, 0, DateTimeKind.Utc);

        // Case 1: contiguous cloud rejects + one healthy bridge + same-family reject => one event.
        var engine = new EventGroupingEngine();
        engine.Add(Frame(1, t0, FrameStatus.Rejected, 90, "STAR_COUNT_DROP"));
        engine.Add(Frame(2, t0.AddMinutes(4), FrameStatus.Rejected, 92, "BACKGROUND_HIGH"));
        engine.Add(Frame(3, t0.AddMinutes(8), FrameStatus.Accepted, 90));
        engine.Add(Frame(4, t0.AddMinutes(12), FrameStatus.Rejected, 91, "STAR_COUNT_DROP"));
        var events = engine.Events;
        if (events.Count != 1 || events[0].AffectedFrames != 3) {
            failures.Add("Event oracle case 1: one healthy bridge should keep one image-condition event with 3 affected frames");
        }

        // Case 2: two healthy frames close the event; a later anomaly starts a new one.
        engine.Add(Frame(5, t0.AddMinutes(16), FrameStatus.Accepted, 90));
        engine.Add(Frame(6, t0.AddMinutes(20), FrameStatus.Accepted, 90));
        engine.Add(Frame(7, t0.AddMinutes(24), FrameStatus.Rejected, 95, "STAR_COUNT_DROP"));
        events = engine.Events;
        if (events.Count != 2 || events[1].FirstFrameIndex != 7) {
            failures.Add("Event oracle case 2: two healthy frames must close the previous event");
        }

        // Case 3: contiguous guide + image anomalies are one mixed-condition episode.
        var mixed = new EventGroupingEngine();
        mixed.Add(Frame(1, t0, FrameStatus.Rejected, 94, "GUIDE_RMS"));
        mixed.Add(Frame(2, t0.AddMinutes(4), FrameStatus.Rejected, 96, "STAR_COUNT_DROP"));
        var mixedEvents = mixed.Events;
        if (mixedEvents.Count != 1 || mixedEvents[0].Type != SessionEventType.MixedConditions) {
            failures.Add("Event oracle case 3: contiguous guide + image anomalies must promote the episode to MixedConditions");
        }

        // Case 4: analysis errors have their own diagnostic family.
        var errors = new EventGroupingEngine();
        errors.Add(new FrameQualityResult {
            FrameIndex = 1,
            TimestampUtc = t0,
            Status = FrameStatus.Error,
            ConfidenceScore = 0,
            ProbableCause = "ANALYSIS DATA UNAVAILABLE"
        });
        var errorEvents = errors.Events;
        if (errorEvents.Count != 1 || errorEvents[0].Type != SessionEventType.AnalysisDataLoss) {
            failures.Add("Event oracle case 4: ERROR frame must create AnalysisDataLoss event");
        }

        return failures;
    }

    private static FrameQualityResult Frame(
        int index,
        DateTime timestamp,
        FrameStatus status,
        double confidence,
        params string[] reasons) {

        var result = new FrameQualityResult {
            FrameIndex = index,
            TimestampUtc = timestamp,
            Status = status,
            ConfidenceScore = confidence,
            ProbableCause = reasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal))
                ? "WIND / GUIDING DISTURBANCE"
                : reasons.Length > 0 ? "CLOUD / TRANSPARENCY LOSS" : "NORMAL"
        };
        result.RejectReasons.AddRange(reasons);
        return result;
    }
}
