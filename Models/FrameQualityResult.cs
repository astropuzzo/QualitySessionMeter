using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugin.QualitySessionMeter.Models;

public sealed class FrameQualityResult {
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

    public int StarCount { get; set; } = -1;
    public double StarBaseline { get; set; } = double.NaN;
    public double StarDeviationPercent { get; set; } = double.NaN;

    public double BackgroundMedian { get; set; } = double.NaN;
    public double BackgroundBaseline { get; set; } = double.NaN;
    public double BackgroundDeviationPercent { get; set; } = double.NaN;

    public int GuideSamples { get; set; }
    public double GuideRmsArcsec { get; set; } = double.NaN;
    public double MaxGuideExcursionArcsec { get; set; } = double.NaN;
    public double SustainedGuideExcursionSeconds { get; set; } = double.NaN;

    // V2 diagnostic guide-pattern analysis. These labels never change the V1 hard decision.
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

    // V2: confidence is intentionally separate from Quality and ACCEPT/REJECT.
    // The component values make the score auditable in CSV/JSON and later reports.
    public double ConfidenceScore { get; set; } = double.NaN;
    public double ConfidenceDataCompleteness { get; set; } = double.NaN;
    public double ConfidenceBaselineMaturity { get; set; } = double.NaN;
    public double ConfidenceThresholdSeparation { get; set; } = double.NaN;
    public double ConfidenceAgreement { get; set; } = double.NaN;
    public string ConfidenceReason { get; set; } = "";

    public FrameStatus Status { get; set; }
    public List<string> RejectReasons { get; set; } = new();
    public string ProbableCause { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public bool MonitorOnly { get; set; }

    public bool IsUsable => Status is FrameStatus.Accepted or FrameStatus.Warning;
    public string QualityLabel => OverallQuality switch {
        >= 90 => "EXCELLENT",
        >= 80 => "GOOD",
        >= 65 => "FAIR",
        >= 50 => "POOR",
        _ => "BAD"
    };

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

    public string QualityText => OverallQuality.ToString("0", CultureInfo.InvariantCulture);
    public string ConfidenceText => double.IsNaN(ConfidenceScore)
        ? "N/A"
        : ConfidenceScore.ToString("0", CultureInfo.InvariantCulture) + "%";
    public string GuideRmsText => FormatArcsec(GuideRmsArcsec);
    public string ExcursionText => FormatArcsec(MaxGuideExcursionArcsec);
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
        ? "N/A"
        : GuidePatternConfidence.ToString("0", CultureInfo.InvariantCulture) + "%";
    public string StarsText => StarCount >= 0 ? StarCount.ToString(CultureInfo.InvariantCulture) : "N/A";
    public string StarDeltaText => FormatPercent(StarDeviationPercent);
    public string BackgroundText => double.IsNaN(BackgroundMedian) ? "N/A" : BackgroundMedian.ToString("0.##", CultureInfo.InvariantCulture);
    public string BackgroundDeltaText => FormatPercent(BackgroundDeviationPercent);

    private static string FormatArcsec(double value) =>
        double.IsNaN(value) ? "N/A" : value.ToString("0.00", CultureInfo.InvariantCulture) + "\"";

    private static string FormatPercent(double value) =>
        double.IsNaN(value) ? "N/A" : value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
}
