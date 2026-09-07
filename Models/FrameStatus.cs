namespace NINA.Plugin.QualitySessionMeter.Models;

public enum FrameStatus {
    Learning = 0,
    Accepted = 1,
    Warning = 2,
    Rejected = 3,
    Error = 4
}

public enum RejectedFileAction {
    KeepInPlace = 0,
    PrefixBad = 1,
    MoveToRejectedFolder = 2
}
