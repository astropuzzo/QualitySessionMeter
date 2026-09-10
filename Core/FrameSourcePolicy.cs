using NINA.Plugin.QualitySessionMeter.Models;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Source-scoping policy for saved LIGHT frames. ImageType filtering happens before this policy;
/// this class decides whether a LIGHT belongs to the Advanced Sequencer or to the wider all-LIGHTs scope.
/// Internal QSM provenance is retained only for sequence-helper correlation and file-action safety.
/// </summary>
public static class FrameSourcePolicy {
    public static FrameSourceInfo Resolve(MonitoringScope scope, bool controlled, string sequenceTitle, bool advancedSequenceEvidence = false) {
        bool hasSequenceMetadata = !string.IsNullOrWhiteSpace(sequenceTitle);
        bool sequencerKnown = controlled || hasSequenceMetadata || advancedSequenceEvidence;

        var kind = controlled
            ? FrameSourceKind.QsmControlledBlock
            : sequencerKnown
                ? FrameSourceKind.AdvancedSequencer
                : FrameSourceKind.ManualOrExternalLight;

        bool monitoringEligible = scope switch {
            MonitoringScope.AdvancedSequencerLights => sequencerKnown,
            MonitoringScope.AllLights => true,
            _ => false
        };

        // Monitoring may use live Advanced Sequencer evidence, but filesystem mutation is stricter:
        // require internal sequence correlation or sequence metadata that survives asynchronous saving.
        bool fileActionEligible = monitoringEligible && (controlled || hasSequenceMetadata);

        return new FrameSourceInfo {
            Kind = kind,
            SequenceTitle = sequenceTitle ?? "",
            QsmControlled = controlled,
            MonitoringEligible = monitoringEligible,
            FileActionEligible = fileActionEligible
        };
    }
}
