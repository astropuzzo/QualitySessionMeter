using NINA.Plugin.QualitySessionMeter.Models;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Pure V3 source-scoping policy. ImageType filtering happens before this policy; this class decides
/// whether a LIGHT is eligible once its sequencer provenance/control-arm state is known.
/// </summary>
public static class FrameSourcePolicy {
    public static FrameSourceInfo Resolve(MonitoringScope scope, bool controlled, string sequenceTitle) {
        bool hasSequenceMetadata = !string.IsNullOrWhiteSpace(sequenceTitle);
        bool sequencerKnown = controlled || hasSequenceMetadata;

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

        bool fileActionEligible = monitoringEligible &&
            kind is FrameSourceKind.QsmControlledBlock or FrameSourceKind.AdvancedSequencer;

        return new FrameSourceInfo {
            Kind = kind,
            SequenceTitle = sequenceTitle ?? "",
            QsmControlled = controlled,
            MonitoringEligible = monitoringEligible,
            FileActionEligible = fileActionEligible
        };
    }
}
