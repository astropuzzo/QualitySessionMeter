using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugin.QualitySessionMeter.Models;

public sealed class FrameQualityResult {
    public List<GuideSample> GuideEvidence { get; set; } = new();
    public string AssessmentVersion { get; set; } = "1.3";
    public ImageEvidence ImageEvidence { get; set; } = new();
    public string DecisionSummary { get; set; } = "";
    public string ThresholdsUsed { get; set; } = "";
    public List<string> ReviewReasons { get; set; } = new();
    public bool GuideFalsePositive { get; set; }
    public string SecondPassText => GuideFalsePositive ? "GUIDE FALSE POSITIVE" : ImageEvidence.Available ? ImageEvidence.Summary : "NOT VERIFIED";
    public string FrameIdentity => $"Frame #{FrameIndex} · {FileName}";
    public int FrameIndex { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string OriginalPath { get; set; }
    public string FinalPath { get; set; }
    public string Target { get; set; }
    public string Filter { get; set; }
    public double ExposureSeconds { get; set; }
    public int Gain { get; set; }
    public int BinX { get; set; }
    public int BinY { get; set; }
    public string Camera { get; set; }

    public FrameSourceKind SourceKind { get; set; } = FrameSourceKind.Unknown;
    public string SequenceTitle { get; set; } = "";
    public bool QsmControlled { get; set; }
    public bool FileActionEligible { get; set; }
    public bool ProvenanceFrozen { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string QsmControlToken { get; set; } = "";

    public int StarCount { get; set; } = -1;
    public double StarBaseline { get; set; } = double.NaN;
    public double StarDeviationPercent { get; set; } = double.NaN;

    public double BackgroundMedian { get; set; } = double.NaN;
    public double BackgroundBaseline { get; set; } = double.NaN;
    public double BackgroundDeviationPercent { get; set; } = double.NaN;

    public bool StarTrendUsable { get; set; }
    public double StarTrendExpected { get; set; } = double.NaN;
    public double StarTrendPercentPerFrame { get; set; } = double.NaN;
    public double StarTrendR2 { get; set; } = double.NaN;
    public double StarTrendResidualPercent { get; set; } = double.NaN;
    public TrendInterpretationKind StarTrendKind { get; set; } = TrendInterpretationKind.Unavailable;

    public bool BackgroundTrendUsable { get; set; }
    public double BackgroundTrendExpected { get; set; } = double.NaN;
    public double BackgroundTrendPercentPerFrame { get; set; } = double.NaN;
    public double BackgroundTrendR2 { get; set; } = double.NaN;
    public double BackgroundTrendResidualPercent { get; set; } = double.NaN;
    public TrendInterpretationKind BackgroundTrendKind { get; set; } = TrendInterpretationKind.Unavailable;

    public int GuideSamples { get; set; }
    public double GuideRmsArcsec { get; set; } = double.NaN;
    public double MaxGuideExcursionArcsec { get; set; } = double.NaN;
    public double SustainedGuideExcursionSeconds { get; set; } = double.NaN;

    public GuidePatternKind GuidePattern { get; set; } = GuidePatternKind.Unavailable;
    public double GuidePatternConfidence { get; set; } = double.NaN;
    public double GuideDriftArcsecPerMinute { get; set; } = double.NaN;
    public double GuideOscillationRangeArcsec { get; set; } = double.NaN;
    public int GuidePatternSignChanges { get; set; }
    public double GuidePatternBurstiness { get; set; } = double.NaN;
    public string GuidePatternDetail { get; set; } = "";

    public double? GuidingQuality { get; set; }
    public double? StabilityQuality { get; set; }
    public double? TransparencyQuality { get; set; }
    public double? BackgroundQuality { get; set; }
    public double OverallQuality { get; set; }

    public double ConfidenceScore { get; set; } = double.NaN;
    public double ConfidenceDataCompleteness { get; set; } = double.NaN;
    public double ConfidenceBaselineMaturity { get; set; } = double.NaN;
    public double ConfidenceThresholdSeparation { get; set; } = double.NaN;
    public double ConfidenceAgreement { get; set; } = double.NaN;
    public string ConfidenceReason { get; set; } = "";

    // V3 predictive diagnostics. These never create a hard rejection by themselves.
    public bool PredictiveWarning { get; set; }
    public double PredictiveConfidence { get; set; } = double.NaN;
    public string PredictiveChannel { get; set; } = "";
    public string PredictiveMessage { get; set; } = "";
    public double PredictiveFramesToThreshold { get; set; } = double.NaN;

    // V3 optional weather/environmental correlation. Diagnostic only unless a future explicit control rule says otherwise.
    public bool EnvironmentAvailable { get; set; }
    public double CloudCover { get; set; } = double.NaN;
    public double Humidity { get; set; } = double.NaN;
    public double WindSpeed { get; set; } = double.NaN;
    public double WindGust { get; set; } = double.NaN;
    public double SkyQuality { get; set; } = double.NaN;
    public double AmbientTemperature { get; set; } = double.NaN;
    public double DewPoint { get; set; } = double.NaN;
    public string EnvironmentalHint { get; set; } = "";

    public FrameStatus Status { get; set; }
    public List<string> RejectReasons { get; set; } = new();
    public string ProbableCause { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public bool MonitorOnly { get; set; }

    public bool IsUsable => Status is FrameStatus.Accepted or FrameStatus.Warning;

    public string QualityLabel {
        get {
            if (AssessmentVersion == "1.4" && Status == FrameStatus.Rejected) return ImageEvidence.Compromised ? "STAR DAMAGE" : "LIMIT EXCEEDED";
            if (AssessmentVersion == "1.4" && Status == FrameStatus.Warning) return "KEPT FOR REVIEW";
            if (Status == FrameStatus.Learning) return "LEARNING";
            if (Status == FrameStatus.Error) return "UNASSESSED";
            return OverallQuality switch {
                >= 90 => "EXCELLENT",
                >= 80 => "GOOD",
                >= 65 => "FAIR",
                >= 50 => "POOR",
                _ => "BAD"
            };
        }
    }

    public string ConfidenceLabel => double.IsNaN(ConfidenceScore) ? "N/A" : ConfidenceScore switch {
        >= 95 => "VERY HIGH",
        >= 80 => "HIGH",
        >= 65 => "MODERATE",
        >= 45 => "LOW",
        _ => "VERY LOW"
    };

    public string StatusText => Status switch {
        FrameStatus.Learning => "LEARNING",
        FrameStatus.Accepted => "ACCEPTED",
        FrameStatus.Warning => "WARNING",
        FrameStatus.Rejected when MonitorOnly => "WOULD REJECT",
        FrameStatus.Rejected => "REJECTED",
        _ => "ERROR"
    };

    public string SourceText => SourceKind switch {
        FrameSourceKind.QsmControlledBlock => "QSM CONTROLLED BLOCK",
        FrameSourceKind.AdvancedSequencer => "ADVANCED SEQUENCER",
        FrameSourceKind.ManualOrExternalLight => "MANUAL / EXTERNAL LIGHT",
        _ => "UNKNOWN"
    };

    public string PredictionText => PredictiveWarning
        ? $"{PredictiveChannel}: {PredictiveMessage} ({PredictiveConfidence:0}% conf.)"
        : "—";

    public string ReasonText {
        get {
            var parts = new List<string>();
            if (RejectReasons.Count > 0) parts.Add(string.Join(", ", RejectReasons));
            if (!string.IsNullOrWhiteSpace(ErrorMessage)) parts.Add(ErrorMessage);
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    public string FileName => string.IsNullOrWhiteSpace(FinalPath ?? OriginalPath)
        ? "(unknown)"
        : System.IO.Path.GetFileName(FinalPath ?? OriginalPath);

    public string OriginalFileName => string.IsNullOrWhiteSpace(OriginalPath)
        ? "(unknown)"
        : System.IO.Path.GetFileName(OriginalPath);

    public string FinalFileName => string.IsNullOrWhiteSpace(FinalPath)
        ? OriginalFileName
        : System.IO.Path.GetFileName(FinalPath);

    public bool IsSyntheticFile => (OriginalPath ?? "").StartsWith("SYNTHETIC://", StringComparison.OrdinalIgnoreCase);

    public bool IsBadFileApplied {
        get {
            if (Status != FrameStatus.Rejected || IsSyntheticFile) return false;
            var path = FinalPath ?? OriginalPath ?? "";
            var file = System.IO.Path.GetFileName(path);
            var directory = System.IO.Path.GetDirectoryName(path) ?? "";
            return file.StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(System.IO.Path.GetFileName(directory), "Rejected", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(OriginalPath) && !string.Equals(path, OriginalPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string FileDispositionText {
        get {
            if (IsSyntheticFile) return "SYNTHETIC";
            if (Status != FrameStatus.Rejected) return "NORMAL";
            var path = FinalPath ?? OriginalPath ?? "";
            if (System.IO.Path.GetFileName(path).StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) return "BAD_ PREFIX";
            if (string.Equals(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? ""), "Rejected", StringComparison.OrdinalIgnoreCase)) return "REJECTED FOLDER";
            return IsBadFileApplied ? "FILE MOVED" : "KEPT";
        }
    }

    public string QualityText => Status is FrameStatus.Learning or FrameStatus.Error
        ? "—"
        : OverallQuality.ToString("0", CultureInfo.InvariantCulture);

    public string ConfidenceText => double.IsNaN(ConfidenceScore)
        ? "N/A"
        : ConfidenceScore.ToString("0", CultureInfo.InvariantCulture) + (AssessmentVersion == "1.4" ? " / 100" : "%");

    public string GuideRmsText => FormatArcsec(GuideRmsArcsec);
    public string ExcursionText => double.IsNaN(MaxGuideExcursionArcsec)
        ? "max excursion N/A"
        : "max excursion " + FormatArcsec(MaxGuideExcursionArcsec);
    public string GuidePatternText => GuidePattern switch {
        GuidePatternKind.Unavailable => "N/A",
        GuidePatternKind.Stable => "STABLE",
        GuidePatternKind.IsolatedSpike => "ISOLATED SPIKE",
        GuidePatternKind.SustainedExcursion => "SUSTAINED",
        GuidePatternKind.Oscillation => "OSCILLATION",
        GuidePatternKind.Drift => "DRIFT",
        GuidePatternKind.WindLike => "WIND-LIKE",
        _ => "IRREGULAR"
    };
    public string GuidePatternConfidenceText => double.IsNaN(GuidePatternConfidence)
        ? "confidence N/A"
        : "confidence " + GuidePatternConfidence.ToString("0", CultureInfo.InvariantCulture) + "%";
    public string TrendText {
        get {
            if (StarTrendKind == TrendInterpretationKind.AbruptAnomaly || BackgroundTrendKind == TrendInterpretationKind.AbruptAnomaly) return "ABRUPT";
            if (StarTrendKind == TrendInterpretationKind.GradualChange || BackgroundTrendKind == TrendInterpretationKind.GradualChange) return "GRADUAL";
            if (StarTrendKind == TrendInterpretationKind.Stable || BackgroundTrendKind == TrendInterpretationKind.Stable) return "STABLE";
            return "N/A";
        }
    }
    public string StarsText => StarCount >= 0 ? StarCount.ToString(CultureInfo.InvariantCulture) : "N/A";
    public string StarDeltaText => FormatPercent(StarDeviationPercent);
    public string StarTrendResidualText => double.IsNaN(StarTrendResidualPercent)
        ? "trend residual N/A"
        : "trend residual " + FormatPercent(StarTrendResidualPercent);
    public string BackgroundText => double.IsNaN(BackgroundMedian) ? "N/A" : BackgroundMedian.ToString("0.##", CultureInfo.InvariantCulture);
    public string BackgroundDeltaText => FormatPercent(BackgroundDeviationPercent);
    public string BackgroundTrendResidualText => double.IsNaN(BackgroundTrendResidualPercent)
        ? "trend residual N/A"
        : "trend residual " + FormatPercent(BackgroundTrendResidualPercent);

    private static string FormatArcsec(double value) => double.IsNaN(value) ? "N/A" : value.ToString("0.00", CultureInfo.InvariantCulture) + "\"";
    private static string FormatPercent(double value) => double.IsNaN(value) ? "N/A" : value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
}
