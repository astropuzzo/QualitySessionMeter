using NINA.Plugin.QualitySessionMeter.Models;

namespace NINA.Plugin.QualitySessionMeter.Sequencer;

/// <summary>
/// Pure deterministic accounting used by the Advanced Sequencer condition and by CI regression.
/// It deliberately knows nothing about N.I.N.A. execution or filesystem actions.
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

    public static Snapshot Account(Snapshot current, FrameQualityResult result, bool countWarningsAsValid) {
        if (result == null || result.FrameIndex <= current.LastFrameIndex) return current;

        int valid = current.Valid;
        int captured = current.Captured + 1;
        int rejected = current.Rejected;
        int warning = current.Warning;
        int learning = current.Learning;
        int error = current.Error;

        switch (result.Status) {
            case FrameStatus.Accepted:
                valid++;
                break;
            case FrameStatus.Warning:
                warning++;
                if (countWarningsAsValid) valid++;
                break;
            case FrameStatus.Rejected:
                rejected++;
                break;
            case FrameStatus.Learning:
                learning++;
                break;
            case FrameStatus.Error:
                error++;
                break;
        }

        return new Snapshot(valid, captured, rejected, warning, learning, error, result.FrameIndex);
    }
}
