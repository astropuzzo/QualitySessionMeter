namespace NINA.Plugin.QualitySessionMeter.Models;

public enum AdaptiveThresholdMode {
    Off = 0,
    SuggestOnly = 1,
    Automatic = 2
}

public sealed class CalibrationSuggestion {
    public bool Available { get; init; }
    public string Context { get; init; } = "";
    public int SampleCount { get; init; }
    public double SuggestedMaxGuideRms { get; init; } = double.NaN;
    public double SuggestedExcursionThreshold { get; init; } = double.NaN;
    public double SuggestedHardExcursionThreshold { get; init; } = double.NaN;
    public double SuggestedMaxStarLossPercent { get; init; } = double.NaN;
    public double SuggestedBackgroundIncreasePercent { get; init; } = double.NaN;
    public double SuggestedBackgroundDecreasePercent { get; init; } = double.NaN;
    public string Reason { get; init; } = "";
}

public sealed class PredictiveAssessment {
    public bool Warning { get; init; }
    public double Confidence { get; init; }
    public string Message { get; init; } = "";
    public string Channel { get; init; } = "";
    public double EstimatedFramesToThreshold { get; init; } = double.NaN;
}

public sealed class EnvironmentalSnapshot {
    public bool Available { get; init; }
    public double CloudCover { get; init; } = double.NaN;
    public double Humidity { get; init; } = double.NaN;
    public double WindSpeed { get; init; } = double.NaN;
    public double WindGust { get; init; } = double.NaN;
    public double SkyQuality { get; init; } = double.NaN;
    public double Temperature { get; init; } = double.NaN;
    public double DewPoint { get; init; } = double.NaN;
    public string CorrelationHint { get; init; } = "";
}
