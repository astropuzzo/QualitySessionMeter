using NINA.Plugin.QualitySessionMeter.Models;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Pure V3 source-scoping policy. ImageType filtering happens before this policy; this class decides
/// whether a LIGHT is eligible once its sequencer provenance/control-arm state is known.
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
            MonitoringScope.ControlledBlocksOnly => controlled,
            MonitoringScope.AdvancedSequencerLights => sequencerKnown,
            MonitoringScope.AllLights => true,
            _ => false
        };

        // Monitoring may use live Advanced Sequencer evidence, but filesystem mutation is stricter:
        // require an explicit QSM arm or sequence metadata that survives asynchronous saving.
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
