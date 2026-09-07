using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class EnvironmentalCorrelationService {
    public EnvironmentalSnapshot Capture(IWeatherDataMediator mediator, FrameQualityResult result, QualitySettings settings) {
        if (!settings.EnvironmentalCorrelationEnabled || mediator == null) return new EnvironmentalSnapshot();
        try {
            var info = mediator.GetInfo();
            if (info == null || !info.Connected) return new EnvironmentalSnapshot();

            bool available = Finite(info.CloudCover) || Finite(info.Humidity) || Finite(info.WindSpeed) ||
                             Finite(info.WindGust) || Finite(info.SkyQuality) || Finite(info.Temperature) || Finite(info.DewPoint);
            if (!available) return new EnvironmentalSnapshot();

            var hints = new List<string>();
            var cause = result?.ProbableCause ?? "";
            if (cause.Contains("WIND", StringComparison.OrdinalIgnoreCase) || cause.Contains("GUID", StringComparison.OrdinalIgnoreCase)) {
                if (Finite(info.WindGust) && info.WindGust >= 4) hints.Add($"wind gust {info.WindGust:0.0} m/s correlates with guiding disturbance");
                else if (Finite(info.WindSpeed) && info.WindSpeed >= 3) hints.Add($"wind {info.WindSpeed:0.0} m/s may correlate with guiding disturbance");
            }
            if (cause.Contains("CLOUD", StringComparison.OrdinalIgnoreCase) && Finite(info.CloudCover) && info.CloudCover >= 30)
                hints.Add($"cloud sensor reports {info.CloudCover:0}% cover");
            if (cause.Contains("HAZE", StringComparison.OrdinalIgnoreCase) && Finite(info.Humidity) && info.Humidity >= 80)
                hints.Add($"humidity {info.Humidity:0}% may correlate with haze/fog");
            if (Finite(info.Temperature) && Finite(info.DewPoint) && info.Temperature - info.DewPoint <= 2)
                hints.Add($"air temperature is only {info.Temperature - info.DewPoint:0.0}°C above dew point");

            return new EnvironmentalSnapshot {
                Available = true,
                CloudCover = info.CloudCover,
                Humidity = info.Humidity,
                WindSpeed = info.WindSpeed,
                WindGust = info.WindGust,
                SkyQuality = info.SkyQuality,
                Temperature = info.Temperature,
                DewPoint = info.DewPoint,
                CorrelationHint = hints.Count == 0 ? "Environmental data recorded; no strong correlation inferred." : string.Join("; ", hints)
            };
        } catch {
            return new EnvironmentalSnapshot();
        }
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
