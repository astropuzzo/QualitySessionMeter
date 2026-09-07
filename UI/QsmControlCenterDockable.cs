using CommunityToolkit.Mvvm.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(IDockableVM))]
public sealed class QsmControlCenterDockable : DockableVM, IDisposable {
    private readonly QualitySessionRuntime runtime;

    public override bool IsTool { get; } = true;
    public QualitySettings Settings => runtime.Settings;
    public CalibrationSuggestion Calibration => runtime.CurrentCalibrationSuggestion;
    public FrameQualityResult LastFrame => runtime.Store.Results.LastOrDefault();

    public string ActivationText => !Settings.Enabled
        ? "OFF — QSM will not analyze, count, rename or move any frame."
        : Settings.MonitorOnly
            ? $"ON · MONITOR ONLY · {Settings.MonitoringScope}"
            : $"ON · ACTIVE FILE HANDLING · {Settings.MonitoringScope}";

    public string CalibrationStatus => Calibration?.Available == true
        ? $"READY · {Calibration.SampleCount} stable accepted frames · {Calibration.Context}"
        : Calibration?.Reason ?? "Waiting for a stable same-context sample window.";

    public string CalibrationValues => Calibration?.Available == true
        ? $"RMS ≤ {Calibration.SuggestedMaxGuideRms:0.00}\" · excursion {Calibration.SuggestedExcursionThreshold:0.00}\" · hard {Calibration.SuggestedHardExcursionThreshold:0.00}\" · stars -{Calibration.SuggestedMaxStarLossPercent:0.0}% · background +{Calibration.SuggestedBackgroundIncreasePercent:0.0}/-{Calibration.SuggestedBackgroundDecreasePercent:0.0}%"
        : "—";

    public string PredictionText => LastFrame?.PredictionText ?? "—";
    public string EnvironmentText => LastFrame?.EnvironmentAvailable == true
        ? LastFrame.EnvironmentalHint
        : "No connected weather/environment data for the latest assessed frame.";

    public ICommand ApplyCalibrationCommand { get; }
    public ICommand IgnoreCalibrationCommand { get; }

    [ImportingConstructor]
    public QsmControlCenterDockable(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        ISequenceMediator sequenceMediator) : base(profileService) {

        Title = "QSM Control Center";
        ImageGeometry = PluginIcon.CreateMeterGeometry();

        var accessor = new PluginOptionsAccessor(profileService, PluginConstants.Identifier);
        var localSettings = new QualitySettings(accessor);
        runtime = QualitySessionRuntimeRegistry.GetOrCreate(profileService, imageSaveMediator, guiderMediator, sequenceMediator, localSettings);

        ApplyCalibrationCommand = new RelayCommand(runtime.ApplyCurrentCalibrationSuggestion);
        IgnoreCalibrationCommand = new RelayCommand(runtime.IgnoreCurrentCalibrationSuggestion);

        runtime.FrameProcessed += FrameProcessed;
        runtime.CalibrationSuggestionChanged += CalibrationChanged;
        Settings.PropertyChanged += SettingsChanged;
    }

    private void FrameProcessed(object sender, FrameQualityResult e) => DispatchRefresh();
    private void CalibrationChanged(object sender, EventArgs e) => DispatchRefresh();
    private void SettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => DispatchRefresh();

    private void DispatchRefresh() {
        void Refresh() {
            RaisePropertyChanged(nameof(ActivationText));
            RaisePropertyChanged(nameof(Calibration));
            RaisePropertyChanged(nameof(CalibrationStatus));
            RaisePropertyChanged(nameof(CalibrationValues));
            RaisePropertyChanged(nameof(LastFrame));
            RaisePropertyChanged(nameof(PredictionText));
            RaisePropertyChanged(nameof(EnvironmentText));
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Refresh);
        else Refresh();
    }

    public void Dispose() {
        runtime.FrameProcessed -= FrameProcessed;
        runtime.CalibrationSuggestionChanged -= CalibrationChanged;
        Settings.PropertyChanged -= SettingsChanged;
    }
}
