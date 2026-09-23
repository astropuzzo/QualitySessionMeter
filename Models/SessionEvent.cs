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
    Unknown,
    StarShape
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
    // Enum names are kept for CSV compatibility; the text names the measured channel only.
    public string TypeText => Type switch {
        SessionEventType.CloudTransparency => "Transparency loss",
        SessionEventType.BrightCloudBackground => "Transparency loss, brighter sky",
        SessionEventType.BackgroundHaze => "Sky background change",
        SessionEventType.GuidingDisturbance => "Guiding",
        SessionEventType.StarShape => "Star shape",
        SessionEventType.MixedConditions => "Multiple channels",
        SessionEventType.AnalysisDataLoss => "Analysis data unavailable",
        _ => "Unclassified"
    };
    public string ConfidenceText => MeanConfidence.ToString("0", CultureInfo.InvariantCulture);
    public string SeverityText => ErrorFrames > 0 || RejectedFrames >= 3
        ? "HIGH"
        : RejectedFrames > 0 ? "MEDIUM" : "LOW";
}
