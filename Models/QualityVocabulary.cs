using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Models;

/// <summary>Measurement channel a rule belongs to. Timeline markers and session events use it.</summary>
public enum QualityChannel {
    Guiding,
    StarShape,
    Transparency,
    SkyBackground,
    Data
}

/// <summary>
/// The single source of user-facing names for rule codes, channels and frame states. Rule codes
/// (GUIDE_RMS, STAR_COUNT_DROP, …) stay stable in CSV/JSON exports; people see these labels.
/// Diagnoses name the measured channel, never an unmeasured physical cause such as wind or cloud.
/// </summary>
public static class QualityVocabulary {
    public static string ReasonLabel(string code) => code switch {
        "GUIDE_RMS" => "Guide RMS above limit",
        "SUSTAINED_GUIDE_EXCURSION" => "Sustained guide excursion",
        "HARD_GUIDE_EXCURSION" => "Guide excursion peak",
        "STAR_SHAPE_CONFIRMED" => "Distorted star shapes",
        "STAR_COUNT_DROP" => "Fewer stars than reference",
        "STELLAR_FLUX_LOSS" => "Stellar signal loss",
        "SKY_SIGNAL_LOSS" => "Signal and star loss",
        "LOW_SESSION_SIGNAL" => "Signal below session minimum",
        "BACKGROUND_HIGH" => "Brighter sky background",
        "BACKGROUND_LOW" => "Darker sky background",
        "TRANSPARENCY_CHANGE" => "Sudden transparency change",
        "BORDERLINE_STAR_SHAPE" => "Borderline star shapes",
        "STELLAR_CHECK_UNAVAILABLE" => "Star analysis unavailable",
        "SIGNAL_REFERENCE_UNAVAILABLE" => "No signal reference yet",
        "GUIDE_DATA_UNAVAILABLE" => "No guiding data",
        "STAR_COUNT_UNAVAILABLE" => "No star count",
        "BACKGROUND_UNAVAILABLE" => "No background measurement",
        _ => code ?? ""
    };

    public static QualityChannel? ChannelOf(string code) => code switch {
        "GUIDE_RMS" or "SUSTAINED_GUIDE_EXCURSION" or "HARD_GUIDE_EXCURSION" => QualityChannel.Guiding,
        "STAR_SHAPE_CONFIRMED" or "BORDERLINE_STAR_SHAPE" => QualityChannel.StarShape,
        "STAR_COUNT_DROP" or "STELLAR_FLUX_LOSS" or "SKY_SIGNAL_LOSS" or "LOW_SESSION_SIGNAL" or "TRANSPARENCY_CHANGE" => QualityChannel.Transparency,
        "BACKGROUND_HIGH" or "BACKGROUND_LOW" => QualityChannel.SkyBackground,
        "GUIDE_DATA_UNAVAILABLE" or "STAR_COUNT_UNAVAILABLE" or "BACKGROUND_UNAVAILABLE" => QualityChannel.Data,
        _ => null
    };

    public static string ChannelLabel(QualityChannel channel) => channel switch {
        QualityChannel.Guiding => "Guiding",
        QualityChannel.StarShape => "Star shape",
        QualityChannel.Transparency => "Transparency",
        QualityChannel.SkyBackground => "Sky background",
        _ => "Analysis data"
    };

    /// <summary>One-letter timeline marker per channel. Letters avoid font-dependent symbols.</summary>
    public static string ChannelCode(QualityChannel channel) => channel switch {
        QualityChannel.Guiding => "G",
        QualityChannel.StarShape => "S",
        QualityChannel.Transparency => "T",
        QualityChannel.SkyBackground => "B",
        _ => "!"
    };

    public static IReadOnlyList<QualityChannel> Channels(IEnumerable<string> codes) =>
        (codes ?? Array.Empty<string>()).Select(ChannelOf).Where(c => c.HasValue).Select(c => c.Value).Distinct().OrderBy(c => c).ToArray();

    public static string Labels(IEnumerable<string> codes) =>
        string.Join(", ", (codes ?? Array.Empty<string>()).Distinct().Select(ReasonLabel));

    /// <summary>Labels for use inside a sentence: first letter lowered unless it starts an acronym (RMS).</summary>
    public static string LabelsInSentence(IEnumerable<string> codes) =>
        string.Join(", ", (codes ?? Array.Empty<string>()).Distinct().Select(c => InSentence(ReasonLabel(c))));

    public static string InSentence(string label) =>
        string.IsNullOrEmpty(label) || (label.Length > 1 && char.IsUpper(label[1])) ? label ?? "" : char.ToLowerInvariant(label[0]) + label[1..];

    /// <summary>The failed channels in plain words, e.g. "Guiding + Transparency". Empty when nothing failed.</summary>
    public static string Diagnosis(FrameQualityResult frame) {
        if (frame == null) return "";
        if (frame.Status == FrameStatus.Error) return "Analysis data unavailable";
        var codes = frame.Status == FrameStatus.Rejected ? frame.RejectReasons : frame.ReviewReasons;
        var channels = Channels(codes).Where(c => c != QualityChannel.Data).ToArray();
        if (channels.Length == 0) return "";
        if (channels.Length == 1 && channels[0] == QualityChannel.SkyBackground) {
            return codes.Contains("BACKGROUND_LOW") && !codes.Contains("BACKGROUND_HIGH") ? "Darker sky background" : "Brighter sky background";
        }
        return string.Join(" + ", channels.Select(ChannelLabel));
    }

    public static string StatusLabel(FrameStatus status, bool monitorOnly) => status switch {
        FrameStatus.Accepted => "ACCEPTED",
        FrameStatus.Warning => "ACCEPTED (REVIEW)",
        FrameStatus.Learning => "PROVISIONAL",
        FrameStatus.Rejected when monitorOnly => "REJECTED (MONITOR ONLY)",
        FrameStatus.Rejected => "REJECTED",
        _ => "NOT ASSESSED"
    };
}
