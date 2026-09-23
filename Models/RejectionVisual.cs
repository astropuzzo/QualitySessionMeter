using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Models;

/// <summary>
/// Shared presentation vocabulary for rejected-frame markers in the live and HTML timelines.
/// Deliberately uses short text codes instead of emoji so rendering is deterministic across
/// Windows/N.I.N.A. font stacks. This is presentation-only and never affects rejection decisions.
/// </summary>
public static class RejectionVisual {
    public const string GuideIcon = "G";
    public const string ShapeIcon = "S";
    public const string TransparencyIcon = "T";
    public const string BackgroundIcon = "B";
    public const string UnknownIcon = "?";
    public const string ErrorIcon = "!";
    public const string Legend = "G guiding · S star shape · T transparency · B sky background · ! not assessed";

    public static string GetIcons(FrameQualityResult frame) {
        if (frame == null) return string.Empty;
        if (frame.Status == FrameStatus.Error) return ErrorIcon;
        var codes = string.Concat(QualityVocabulary.Channels(frame.RejectReasons)
            .Where(c => c != QualityChannel.Data)
            .Select(QualityVocabulary.ChannelCode));
        return codes.Length == 0 && frame.Status == FrameStatus.Rejected ? UnknownIcon : codes;
    }

    public static string GetTooltip(FrameQualityResult frame) {
        if (frame == null) return string.Empty;
        var reasons = frame.ReasonText == "—" ? string.Empty : $" · {frame.ReasonText}";
        return $"Frame #{frame.FrameIndex} · {frame.StatusLabel}{reasons}";
    }

    public static string ExpandCodes(string codes) {
        if (string.IsNullOrWhiteSpace(codes)) return "No failed channel";
        var labels = new List<string>();
        foreach (char code in codes) {
            string label = code switch {
                'G' => "G = guiding",
                'S' => "S = star shape",
                'T' => "T = transparency (stars / stellar signal)",
                'B' => "B = sky background",
                '!' => "! = not assessed",
                '?' => "? = unmapped rule",
                _ => null
            };
            if (label != null && !labels.Contains(label)) labels.Add(label);
        }
        return labels.Count == 0 ? "No failed channel" : string.Join("; ", labels);
    }
}
