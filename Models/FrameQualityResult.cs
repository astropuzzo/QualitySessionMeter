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
    public bool StarCountFalsePositive { get; set; }
    public string SecondPassText => GuideFalsePositive || StarCountFalsePositive
        ? Status == FrameStatus.Rejected ? "Flag cleared; another rule failed" : "Flag cleared by star analysis"
        : ImageEvidence.Summary;
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
    // True when this verdict was decided after capture, against a validated or rebuilt reference.
    public bool ReferenceRevised { get; set; }

    public bool IsUsable => Status is FrameStatus.Accepted or FrameStatus.Warning;

    public string QualityLabel {
        get {
            if (Status == FrameStatus.Rejected) return "REJECTED";
            if (Status == FrameStatus.Warning) return "REVIEW";
            if (Status == FrameStatus.Learning) return "PROVISIONAL";
            if (Status == FrameStatus.Error) return "NOT ASSESSED";
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

    // People read StatusLabel. StatusText is the stable status code used by the companion API and CSS.
    public string StatusLabel => QualityVocabulary.StatusLabel(Status, MonitorOnly);

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

    /// <summary>Failed rules (REJECTED) or review findings (ACCEPTED (REVIEW)) in words; "—" when none.</summary>
    public string ReasonText {
        get {
            var parts = new List<string>();
            var codes = Status == FrameStatus.Rejected ? RejectReasons : Status == FrameStatus.Warning ? ReviewReasons : new List<string>();
            if (codes.Count > 0) parts.Add(QualityVocabulary.Labels(codes));
            if (!string.IsNullOrWhiteSpace(ErrorMessage)) parts.Add(ErrorMessage);
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    public string DiagnosisText => string.IsNullOrWhiteSpace(ProbableCause) ? "—" : ProbableCause;

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
            if (IsSyntheticFile) return "Synthetic";
            if (Status != FrameStatus.Rejected) return "—";
            var path = FinalPath ?? OriginalPath ?? "";
            if (System.IO.Path.GetFileName(path).StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) return "Renamed BAD_";
            if (string.Equals(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? ""), "Rejected", StringComparison.OrdinalIgnoreCase)) return "Moved to Rejected";
            return IsBadFileApplied ? "Moved" : "Not moved";
        }
    }

    public string QualityText => Status is FrameStatus.Learning or FrameStatus.Error
        ? "—"
        : OverallQuality.ToString("0", CultureInfo.InvariantCulture);

    public string ConfidenceText => double.IsNaN(ConfidenceScore)
        ? "N/A"
        : ConfidenceScore.ToString("0", CultureInfo.InvariantCulture) + " / 100";

    public string GuideRmsText => FormatArcsec(GuideRmsArcsec);
    public string ExcursionText => double.IsNaN(MaxGuideExcursionArcsec)
        ? ""
        : "peak " + FormatArcsec(MaxGuideExcursionArcsec);
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
        ? ""
        : "pattern match " + GuidePatternConfidence.ToString("0", CultureInfo.InvariantCulture) + "%";
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
        ? ""
        : FormatPercent(StarTrendResidualPercent) + " vs trend";
    public string BackgroundText => double.IsNaN(BackgroundMedian) ? "N/A" : BackgroundMedian.ToString("0", CultureInfo.InvariantCulture) + " ADU";
    public string BackgroundDeltaText => FormatPercent(BackgroundDeviationPercent);
    public string BackgroundTrendResidualText => double.IsNaN(BackgroundTrendResidualPercent)
        ? ""
        : FormatPercent(BackgroundTrendResidualPercent) + " vs trend";

    private static string FormatArcsec(double value) => double.IsNaN(value) ? "N/A" : value.ToString("0.00", CultureInfo.InvariantCulture) + "\"";
    private static string FormatPercent(double value) => double.IsNaN(value) ? "N/A" : value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
}

/// <summary>A verdict replaced after the session reference was validated or rebuilt.</summary>
public sealed record FrameReclassification(FrameQualityResult Previous, FrameQualityResult Result);
