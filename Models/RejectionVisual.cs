using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Models;

/// <summary>
/// Shared visual vocabulary for rejected-frame markers in the live and HTML timelines.
/// This is presentation-only and never affects rejection decisions.
/// </summary>
public static class RejectionVisual {
    public const string GuideIcon = "💨";
    public const string SkyIcon = "☁";
    public const string BackgroundIcon = "🌫";
    public const string UnknownIcon = "❌";
    public const string ErrorIcon = "⚠";

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
        var icons = GetIcons(frame);
        var cause = string.IsNullOrWhiteSpace(frame.ProbableCause) ? "Unknown cause" : frame.ProbableCause;
        var reasons = frame.ReasonText == "—" ? string.Empty : $" · {frame.ReasonText}";
        return $"Frame #{frame.FrameIndex} · {icons} {cause}{reasons}";
    }

    private static bool Contains(string value, string token) =>
        value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void AddDistinct(List<string> icons, string icon) {
        foreach (var existing in icons) {
            if (string.Equals(existing, icon, StringComparison.Ordinal)) return;
        }
        icons.Add(icon);
    }
}
