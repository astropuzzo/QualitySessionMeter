using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugin.QualitySessionMeter.Models;

public enum SessionEventType {
    CloudTransparency,
    BrightCloudBackground,
    BackgroundHaze,
    GuidingDisturbance,
    MixedConditions,
    AnalysisDataLoss,
    Unknown
}

public sealed class SessionEvent {
    public int EventIndex { get; init; }
    public SessionEventType Type { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public int FirstFrameIndex { get; init; }
    public int LastFrameIndex { get; init; }
    public IReadOnlyList<int> FrameIndices { get; init; } = Array.Empty<int>();
    public int AffectedFrames { get; init; }
    public int RejectedFrames { get; init; }
    public int WarningFrames { get; init; }
    public int ErrorFrames { get; init; }
    public double MeanConfidence { get; init; }
    public double PeakConfidence { get; init; }
    public string PrimaryCause { get; init; } = "";
    public bool IsOpen { get; init; }

    public string Id => $"E{EventIndex:000}";
    public string TimeRangeText => StartUtc == default
        ? "—"
        : $"{StartUtc.ToLocalTime():HH:mm}–{EndUtc.ToLocalTime():HH:mm}";
    public string TypeText => Type switch {
        SessionEventType.CloudTransparency => "CLOUD / TRANSPARENCY",
        SessionEventType.BrightCloudBackground => "BRIGHT CLOUD / BACKGROUND",
        SessionEventType.BackgroundHaze => "BACKGROUND / HAZE",
        SessionEventType.GuidingDisturbance => "GUIDING DISTURBANCE",
        SessionEventType.MixedConditions => "MIXED CONDITIONS",
        SessionEventType.AnalysisDataLoss => "ANALYSIS DATA LOSS",
        _ => "UNKNOWN"
    };
    public string ConfidenceText => MeanConfidence.ToString("0", CultureInfo.InvariantCulture) + "%";
    public string SeverityText => ErrorFrames > 0 || RejectedFrames >= 3
        ? "CRITICAL"
        : RejectedFrames > 0 ? "HIGH" : "MODERATE";
}
