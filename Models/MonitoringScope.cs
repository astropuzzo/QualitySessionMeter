namespace NINA.Plugin.QualitySessionMeter.Models;

public enum MonitoringScope {
    ControlledBlocksOnly = 0,
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

    public string SourceText => Kind switch {
        FrameSourceKind.QsmControlledBlock => "QSM CONTROLLED BLOCK",
        FrameSourceKind.AdvancedSequencer => "ADVANCED SEQUENCER",
        FrameSourceKind.ManualOrExternalLight => "MANUAL / EXTERNAL LIGHT",
        _ => "UNKNOWN"
    };
}
