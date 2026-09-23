using NINA.Plugin.QualitySessionMeter.Models;

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
