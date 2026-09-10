namespace NINA.Plugin.QualitySessionMeter.Models;

public enum MonitoringScope {
    AdvancedSequencerLights = 1,
    AllLights = 2
}

public enum FrameSourceKind {
    Unknown = 0,
    AdvancedSequencer = 1,
    QsmControlledBlock = 2,
    ManualOrExternalLight = 3
}

public sealed class FrameSourceInfo {
    public FrameSourceKind Kind { get; init; } = FrameSourceKind.Unknown;
    public string SequenceTitle { get; init; } = "";
    public bool QsmControlled { get; init; }
    public bool MonitoringEligible { get; init; }
    public bool FileActionEligible { get; init; }
    public string ControlToken { get; init; } = "";
    public bool ProvenanceFrozen { get; init; }

    public string SourceText => Kind switch {
        FrameSourceKind.QsmControlledBlock => "QSM SEQUENCE FRAME",
        FrameSourceKind.AdvancedSequencer => "ADVANCED SEQUENCER",
        FrameSourceKind.ManualOrExternalLight => "MANUAL / EXTERNAL LIGHT",
        _ => "UNKNOWN"
    };
}
