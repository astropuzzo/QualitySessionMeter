using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Profile.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NINA.Plugin.QualitySessionMeter.Settings;

public sealed class QualitySettings : INotifyPropertyChanged {
    private readonly IPluginOptionsAccessor accessor;

    public QualitySettings(IPluginOptionsAccessor accessor) {
        this.accessor = accessor;
    }

    public bool Enabled {
        get => accessor.GetValueBoolean(nameof(Enabled), true);
        set { accessor.SetValueBoolean(nameof(Enabled), value); Raise(); }
    }

    public bool MonitorOnly {
        get => accessor.GetValueBoolean(nameof(MonitorOnly), true);
        set { accessor.SetValueBoolean(nameof(MonitorOnly), value); Raise(); }
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
        set { accessor.SetValueDouble(nameof(ExcursionThreshold), Clamp(value, 0.05, 50)); Raise(); }
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
        set { accessor.SetValueDouble(nameof(HardExcursionThreshold), Clamp(value, 0.1, 100)); Raise(); }
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
        set { accessor.SetValueInt32(nameof(BaselineWindow), Clamp(value, 4, 50)); Raise(); }
    }

    public int MinimumLearningFrames {
        get => Clamp(accessor.GetValueInt32(nameof(MinimumLearningFrames), 4), 2, 20);
        set { accessor.SetValueInt32(nameof(MinimumLearningFrames), Clamp(value, 2, 20)); Raise(); }
    }

    public double WorstMetricWeight {
        get => Clamp(accessor.GetValueDouble(nameof(WorstMetricWeight), 0.70), 0.50, 0.95);
        set { accessor.SetValueDouble(nameof(WorstMetricWeight), Clamp(value, 0.50, 0.95)); Raise(); }
    }

    public RejectedFileAction RejectedFileAction {
        get => (RejectedFileAction)Clamp(accessor.GetValueInt32(nameof(RejectedFileAction), 0), 0, 2);
        set { accessor.SetValueInt32(nameof(RejectedFileAction), Clamp((int)value, 0, 2)); Raise(); Raise(nameof(RejectedFileActionIndex)); }
    }

    public int RejectedFileActionIndex {
        get => (int)RejectedFileAction;
        set => RejectedFileAction = (RejectedFileAction)Clamp(value, 0, 2);
    }

    public void NotifyProfileChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public event PropertyChangedEventHandler PropertyChanged;
    private void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
}
