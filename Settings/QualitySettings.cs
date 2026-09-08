using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace NINA.Plugin.QualitySessionMeter.Settings;

public sealed class QualitySettings : INotifyPropertyChanged {
    private readonly IPluginOptionsAccessor accessor;

    public QualitySettings(IPluginOptionsAccessor accessor) { this.accessor = accessor; }

    public bool Enabled {
        get => accessor.GetValueBoolean(nameof(Enabled), false);
        set { accessor.SetValueBoolean(nameof(Enabled), value); Raise(); }
    }

    public MonitoringScope MonitoringScope {
        get => (MonitoringScope)Clamp(accessor.GetValueInt32(nameof(MonitoringScope), (int)NINA.Plugin.QualitySessionMeter.Models.MonitoringScope.AdvancedSequencerLights), 0, 2);
        set { accessor.SetValueInt32(nameof(MonitoringScope), Clamp((int)value, 0, 2)); Raise(); Raise(nameof(MonitoringScopeIndex)); }
    }
    public int MonitoringScopeIndex { get => (int)MonitoringScope; set => MonitoringScope = (MonitoringScope)Clamp(value, 0, 2); }

    public bool MonitorOnly {
        get => accessor.GetValueBoolean(nameof(MonitorOnly), true);
        set { accessor.SetValueBoolean(nameof(MonitorOnly), value); Raise(); }
    }

    public AdaptiveThresholdMode AdaptiveThresholdMode {
        get => (AdaptiveThresholdMode)Clamp(accessor.GetValueInt32(nameof(AdaptiveThresholdMode), (int)NINA.Plugin.QualitySessionMeter.Models.AdaptiveThresholdMode.SuggestOnly), 0, 2);
        set { accessor.SetValueInt32(nameof(AdaptiveThresholdMode), Clamp((int)value, 0, 2)); Raise(); Raise(nameof(AdaptiveThresholdModeIndex)); }
    }
    public int AdaptiveThresholdModeIndex { get => (int)AdaptiveThresholdMode; set => AdaptiveThresholdMode = (AdaptiveThresholdMode)Clamp(value, 0, 2); }

    public int CalibrationWindow {
        get => Clamp(accessor.GetValueInt32(nameof(CalibrationWindow), 12), 8, 50);
        set { accessor.SetValueInt32(nameof(CalibrationWindow), Clamp(value, 8, 50)); Raise(); }
    }

    public double AutoSafetyMaxGuideRms {
        get => Clamp(accessor.GetValueDouble(nameof(AutoSafetyMaxGuideRms), 2.50), 0.5, 10);
        set { accessor.SetValueDouble(nameof(AutoSafetyMaxGuideRms), Clamp(value, 0.5, 10)); Raise(); }
    }
    public double AutoSafetyMaxExcursion {
        get => Clamp(accessor.GetValueDouble(nameof(AutoSafetyMaxExcursion), 4.0), 1, 20);
        set { accessor.SetValueDouble(nameof(AutoSafetyMaxExcursion), Clamp(value, 1, 20)); Raise(); }
    }
    public double AutoSafetyMaxHardExcursion {
        get => Clamp(accessor.GetValueDouble(nameof(AutoSafetyMaxHardExcursion), 8.0), AutoSafetyMaxExcursion, 30);
        set { accessor.SetValueDouble(nameof(AutoSafetyMaxHardExcursion), Clamp(value, AutoSafetyMaxExcursion, 30)); Raise(); }
    }
    public double AutoSafetyMaxStarLossPercent {
        get => Clamp(accessor.GetValueDouble(nameof(AutoSafetyMaxStarLossPercent), 45.0), 10, 80);
        set { accessor.SetValueDouble(nameof(AutoSafetyMaxStarLossPercent), Clamp(value, 10, 80)); Raise(); }
    }
    public double AutoSafetyMaxBackgroundPercent {
        get => Clamp(accessor.GetValueDouble(nameof(AutoSafetyMaxBackgroundPercent), 45.0), 10, 100);
        set { accessor.SetValueDouble(nameof(AutoSafetyMaxBackgroundPercent), Clamp(value, 10, 100)); Raise(); }
    }

    public bool PredictiveWarningsEnabled {
        get => accessor.GetValueBoolean(nameof(PredictiveWarningsEnabled), true);
        set { accessor.SetValueBoolean(nameof(PredictiveWarningsEnabled), value); Raise(); }
    }

    public bool EnvironmentalCorrelationEnabled {
        get => accessor.GetValueBoolean(nameof(EnvironmentalCorrelationEnabled), true);
        set { accessor.SetValueBoolean(nameof(EnvironmentalCorrelationEnabled), value); Raise(); }
    }

    public bool SmartPauseEnabled {
        get => accessor.GetValueBoolean(nameof(SmartPauseEnabled), false);
        set { accessor.SetValueBoolean(nameof(SmartPauseEnabled), value); Raise(); }
    }
    public int SmartPauseRejectStreak {
        get => Clamp(accessor.GetValueInt32(nameof(SmartPauseRejectStreak), 3), 2, 10);
        set { accessor.SetValueInt32(nameof(SmartPauseRejectStreak), Clamp(value, 2, 10)); Raise(); }
    }
    public int SmartPauseSeconds {
        get => Clamp(accessor.GetValueInt32(nameof(SmartPauseSeconds), 120), 10, 1800);
        set { accessor.SetValueInt32(nameof(SmartPauseSeconds), Clamp(value, 10, 1800)); Raise(); }
    }
    public int SmartResumeHealthyGuideChecks {
        get => Clamp(accessor.GetValueInt32(nameof(SmartResumeHealthyGuideChecks), 3), 1, 10);
        set { accessor.SetValueInt32(nameof(SmartResumeHealthyGuideChecks), Clamp(value, 1, 10)); Raise(); }
    }

    public bool EnableGuideRms {
        get => accessor.GetValueBoolean(nameof(EnableGuideRms), true);
        set { accessor.SetValueBoolean(nameof(EnableGuideRms), value); Raise(); }
    }
    public double MaxGuideRms {
        get => accessor.GetValueDouble(nameof(MaxGuideRms), 1.50);
        set { accessor.SetValueDouble(nameof(MaxGuideRms), Clamp(value, 0.05, 20)); Raise(); }
    }

    public bool EnableSustainedExcursion {
        get => accessor.GetValueBoolean(nameof(EnableSustainedExcursion), true);
        set { accessor.SetValueBoolean(nameof(EnableSustainedExcursion), value); Raise(); }
    }
    public double ExcursionThreshold {
        get => accessor.GetValueDouble(nameof(ExcursionThreshold), 2.00);
        set {
            var clamped = Clamp(value, 0.05, 50);
            accessor.SetValueDouble(nameof(ExcursionThreshold), clamped);
            if (HardExcursionThreshold < clamped) {
                accessor.SetValueDouble(nameof(HardExcursionThreshold), clamped);
                Raise(nameof(HardExcursionThreshold));
            }
            Raise();
        }
    }
    public double ExcursionMinimumDuration {
        get => accessor.GetValueDouble(nameof(ExcursionMinimumDuration), 2.0);
        set { accessor.SetValueDouble(nameof(ExcursionMinimumDuration), Clamp(value, 0.1, 60)); Raise(); }
    }

    public bool EnableHardExcursion {
        get => accessor.GetValueBoolean(nameof(EnableHardExcursion), true);
        set { accessor.SetValueBoolean(nameof(EnableHardExcursion), value); Raise(); }
    }
    public double HardExcursionThreshold {
        get => accessor.GetValueDouble(nameof(HardExcursionThreshold), 5.0);
        set { accessor.SetValueDouble(nameof(HardExcursionThreshold), Clamp(value, ExcursionThreshold, 100)); Raise(); }
    }

    public bool EnableStarCount {
        get => accessor.GetValueBoolean(nameof(EnableStarCount), true);
        set { accessor.SetValueBoolean(nameof(EnableStarCount), value); Raise(); }
    }
    public double MaxStarLossPercent {
        get => accessor.GetValueDouble(nameof(MaxStarLossPercent), 35.0);
        set { accessor.SetValueDouble(nameof(MaxStarLossPercent), Clamp(value, 1, 95)); Raise(); }
    }

    public bool EnableBackground {
        get => accessor.GetValueBoolean(nameof(EnableBackground), true);
        set { accessor.SetValueBoolean(nameof(EnableBackground), value); Raise(); }
    }
    public double MaxBackgroundIncreasePercent {
        get => accessor.GetValueDouble(nameof(MaxBackgroundIncreasePercent), 30.0);
        set { accessor.SetValueDouble(nameof(MaxBackgroundIncreasePercent), Clamp(value, 1, 500)); Raise(); }
    }
    public double MaxBackgroundDecreasePercent {
        get => accessor.GetValueDouble(nameof(MaxBackgroundDecreasePercent), 30.0);
        set { accessor.SetValueDouble(nameof(MaxBackgroundDecreasePercent), Clamp(value, 1, 95)); Raise(); }
    }

    public int BaselineWindow {
        get => Clamp(accessor.GetValueInt32(nameof(BaselineWindow), 8), 4, 50);
        set {
            var clamped = Clamp(value, 4, 50);
            accessor.SetValueInt32(nameof(BaselineWindow), clamped);
            var minLearning = accessor.GetValueInt32(nameof(MinimumLearningFrames), 4);
            if (minLearning > clamped) {
                accessor.SetValueInt32(nameof(MinimumLearningFrames), clamped);
                Raise(nameof(MinimumLearningFrames));
            }
            Raise();
        }
    }
    public int MinimumLearningFrames {
        get => Clamp(accessor.GetValueInt32(nameof(MinimumLearningFrames), 4), 2, BaselineWindow);
        set { accessor.SetValueInt32(nameof(MinimumLearningFrames), Clamp(value, 2, BaselineWindow)); Raise(); }
    }

    public double WorstMetricWeight {
        get => Clamp(accessor.GetValueDouble(nameof(WorstMetricWeight), 0.70), 0.50, 0.95);
        set { accessor.SetValueDouble(nameof(WorstMetricWeight), Clamp(value, 0.50, 0.95)); Raise(); }
    }

    public RejectedFileAction RejectedFileAction {
        get => (RejectedFileAction)Clamp(accessor.GetValueInt32(nameof(RejectedFileAction), 0), 0, 2);
        set { accessor.SetValueInt32(nameof(RejectedFileAction), Clamp((int)value, 0, 2)); Raise(); Raise(nameof(RejectedFileActionIndex)); }
    }
    public int RejectedFileActionIndex { get => (int)RejectedFileAction; set => RejectedFileAction = (RejectedFileAction)Clamp(value, 0, 2); }

    // Optional self-contained mobile/web dashboard. This is deliberately separate from
    // the tokenized OpenAstro integration bridge so existing installations remain untouched.
    public bool WebDashboardEnabled {
        get => accessor.GetValueBoolean(nameof(WebDashboardEnabled), false);
        set { accessor.SetValueBoolean(nameof(WebDashboardEnabled), value); Raise(); }
    }

    public int WebDashboardPort {
        get => Clamp(accessor.GetValueInt32(nameof(WebDashboardPort), 18974), 1024, 65535);
        set { accessor.SetValueInt32(nameof(WebDashboardPort), Clamp(value, 1024, 65535)); Raise(); }
    }

    public bool WebDashboardRequirePassword {
        get => accessor.GetValueBoolean(nameof(WebDashboardRequirePassword), false);
        set { accessor.SetValueBoolean(nameof(WebDashboardRequirePassword), value); Raise(); }
    }

    public bool WebDashboardPasswordConfigured =>
        !string.IsNullOrWhiteSpace(accessor.GetValueString("WebDashboardPasswordSalt", string.Empty)) &&
        !string.IsNullOrWhiteSpace(accessor.GetValueString("WebDashboardPasswordHash", string.Empty));

    public string WebDashboardPasswordStatus => WebDashboardPasswordConfigured ? "Password configured" : "No password configured";

    public void SetWebDashboardPassword(string password) {
        if (string.IsNullOrEmpty(password)) return;
        var salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash;
        using (var derive = new Rfc2898DeriveBytes(password, salt, 120000, HashAlgorithmName.SHA256)) {
            hash = derive.GetBytes(32);
        }
        accessor.SetValueString("WebDashboardPasswordSalt", Convert.ToBase64String(salt));
        accessor.SetValueString("WebDashboardPasswordHash", Convert.ToBase64String(hash));
        Raise(nameof(WebDashboardPasswordConfigured));
        Raise(nameof(WebDashboardPasswordStatus));
    }

    public void ClearWebDashboardPassword() {
        accessor.SetValueString("WebDashboardPasswordSalt", string.Empty);
        accessor.SetValueString("WebDashboardPasswordHash", string.Empty);
        Raise(nameof(WebDashboardPasswordConfigured));
        Raise(nameof(WebDashboardPasswordStatus));
    }

    public bool VerifyWebDashboardPassword(string password) {
        if (!WebDashboardPasswordConfigured || password == null) return false;
        try {
            var salt = Convert.FromBase64String(accessor.GetValueString("WebDashboardPasswordSalt", string.Empty));
            var expected = Convert.FromBase64String(accessor.GetValueString("WebDashboardPasswordHash", string.Empty));
            byte[] actual;
            using (var derive = new Rfc2898DeriveBytes(password, salt, 120000, HashAlgorithmName.SHA256)) {
                actual = derive.GetBytes(expected.Length);
            }
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        } catch {
            return false;
        }
    }

    public void NotifyProfileChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public event PropertyChangedEventHandler PropertyChanged;
    private void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
}
