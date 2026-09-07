using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Models;

/// <summary>
/// Shared presentation vocabulary for rejected-frame markers in the live and HTML timelines.
/// Deliberately uses short text codes instead of emoji so rendering is deterministic across
/// Windows/N.I.N.A. font stacks. This is presentation-only and never affects rejection decisions.
/// </summary>
public static class RejectionVisual {
    public const string GuideIcon = "G";
    public const string SkyIcon = "S";
    public const string BackgroundIcon = "B";
    public const string UnknownIcon = "?";
    public const string ErrorIcon = "!";

    public static string GetIcons(FrameQualityResult frame) {
        if (frame == null) return string.Empty;

        var icons = new List<string>(3);
        foreach (var reason in frame.RejectReasons ?? new List<string>()) {
            switch (reason) {
                case "GUIDE_RMS":
                case "SUSTAINED_GUIDE_EXCURSION":
                case "HARD_GUIDE_EXCURSION":
                    AddDistinct(icons, GuideIcon);
                    break;
                case "STAR_COUNT_DROP":
                    AddDistinct(icons, SkyIcon);
                    break;
                case "BACKGROUND_HIGH":
                case "BACKGROUND_LOW":
                    AddDistinct(icons, BackgroundIcon);
                    break;
            }
        }

        // Fallback for future reason codes while keeping the marker useful.
        if (icons.Count == 0) {
            var cause = frame.ProbableCause ?? string.Empty;
            if (Contains(cause, "WIND") || Contains(cause, "GUIDE")) AddDistinct(icons, GuideIcon);
            if (Contains(cause, "CLOUD") || Contains(cause, "TRANSPARENCY")) AddDistinct(icons, SkyIcon);
            if (Contains(cause, "BACKGROUND") || Contains(cause, "HAZE") || Contains(cause, "BRIGHT SKY")) AddDistinct(icons, BackgroundIcon);
        }

        if (frame.Status == FrameStatus.Error && icons.Count == 0) AddDistinct(icons, ErrorIcon);
        if (frame.Status == FrameStatus.Rejected && icons.Count == 0) AddDistinct(icons, UnknownIcon);

        return string.Concat(icons);
    }

    public static string GetTooltip(FrameQualityResult frame) {
        if (frame == null) return string.Empty;
        var codes = GetIcons(frame);
        var cause = string.IsNullOrWhiteSpace(frame.ProbableCause) ? "Unknown cause" : frame.ProbableCause;
        var reasons = frame.ReasonText == "—" ? string.Empty : $" · {frame.ReasonText}";
        return $"Frame #{frame.FrameIndex} · {ExpandCodes(codes)} · {cause}{reasons}";
    }

    public static string ExpandCodes(string codes) {
        if (string.IsNullOrWhiteSpace(codes)) return "No cause code";
        var labels = new List<string>();
        foreach (char code in codes) {
            switch (code) {
                case 'G': AddDistinct(labels, "G = guiding / tracking"); break;
                case 'S': AddDistinct(labels, "S = stars / transparency / cloud"); break;
                case 'B': AddDistinct(labels, "B = background / haze / sky brightness"); break;
                case '!': AddDistinct(labels, "! = analysis error / unassessed"); break;
                case '?': AddDistinct(labels, "? = unmapped rejection cause"); break;
            }
        }
        return labels.Count == 0 ? "No cause code" : string.Join("; ", labels);
    }

    private static bool Contains(string value, string token) =>
        value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void AddDistinct(List<string> values, string value) {
        foreach (var existing in values) {
            if (string.Equals(existing, value, StringComparison.Ordinal)) return;
        }
        values.Add(value);
    }
}
