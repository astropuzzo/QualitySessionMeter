using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Sequencer;

/// <summary>
/// Pure deterministic accounting used by the Advanced Sequencer condition and by CI regression.
/// It deliberately knows nothing about N.I.N.A. execution or filesystem actions.
/// ACCEPTED and WARNING (accepted with reservation) are valid. LEARNING is provisional and becomes
/// valid or rejected only when its reference is validated; REJECTED and ERROR never count.
/// </summary>
public static class ValidFrameProgressTracker {
    public readonly record struct Snapshot(
        int Valid,
        int Captured,
        int Rejected,
        int Warning,
        int Learning,
        int Error,
        int LastFrameIndex);

    public static Snapshot Account(Snapshot current, FrameQualityResult result) {
        if (result == null || result.FrameIndex <= current.LastFrameIndex) return current;
        var next = Apply(current, result.Status, +1);
        return next with { Captured = current.Captured + 1, LastFrameIndex = result.FrameIndex };
    }

    /// <summary>Moves an already counted frame from its previous verdict to its revised verdict.</summary>
    public static Snapshot Reclassify(Snapshot current, FrameStatus previous, FrameStatus revised) =>
        previous == revised ? current : Apply(Apply(current, previous, -1), revised, +1);

    public static bool IsValid(FrameStatus status) => status is FrameStatus.Accepted or FrameStatus.Warning;

    /// <summary>A counted verdict. The timestamp tells a frame apart from one reusing its index in a new session.</summary>
    public readonly record struct Counted(FrameStatus Status, DateTime TimestampUtc);

    /// <summary>
    /// Moves every counted frame whose stored verdict changed, whatever its earlier status.
    /// A reference rebuilt on better conditions can revise accepted frames, not only provisional ones.
    /// </summary>
    public static Snapshot Reconcile(Snapshot current, IDictionary<int, Counted> counted, IEnumerable<FrameQualityResult> results) {
        if (counted == null || results == null) return current;
        foreach (var result in results) {
            if (result == null || !counted.TryGetValue(result.FrameIndex, out var entry)) continue;
            if (entry.TimestampUtc != result.TimestampUtc || entry.Status == result.Status) continue;
            current = Reclassify(current, entry.Status, result.Status);
            counted[result.FrameIndex] = entry with { Status = result.Status };
        }
        return current;
    }

    /// <summary>
    /// Frame indices restart with each session. A saved floor above the newest stored index belongs
    /// to an earlier session and would hide every new frame.
    /// </summary>
    public static int SessionFloor(int lastAccountedFrameIndex, int latestStoredFrameIndex) =>
        Math.Max(0, Math.Min(lastAccountedFrameIndex, latestStoredFrameIndex));

    private static Snapshot Apply(Snapshot s, FrameStatus status, int delta) {
        if (IsValid(status)) s = s with { Valid = s.Valid + delta };
        return status switch {
            FrameStatus.Warning => s with { Warning = s.Warning + delta },
            FrameStatus.Rejected => s with { Rejected = s.Rejected + delta },
            FrameStatus.Learning => s with { Learning = s.Learning + delta },
            FrameStatus.Error => s with { Error = s.Error + delta },
            _ => s
        };
    }
}
