using CommunityToolkit.Mvvm.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(IDockableVM))]
public sealed class SyntheticLabDockable : DockableVM, IDisposable {
    private readonly QualitySessionRuntime runtime;

    public override bool IsTool { get; } = true;

    private int frameDelayMs = 150;
    public int FrameDelayMs {
        get => frameDelayMs;
        set {
            frameDelayMs = Math.Clamp(value, 0, 5000);
            RaisePropertyChanged();
        }
    }

    public bool IsSyntheticMode => runtime.IsSyntheticMode;
    public bool IsSyntheticRunning => runtime.IsSyntheticRunning;
    public bool CanRun => !runtime.IsSyntheticRunning;
    public bool CanStop => runtime.IsSyntheticRunning;
    public bool CanReturnLive => runtime.IsSyntheticMode && !runtime.IsSyntheticRunning;
    public string Status => runtime.SyntheticStatus;
    public string Progress => runtime.SyntheticTotal <= 0 ? "0 / 0" : $"{runtime.SyntheticProcessed} / {runtime.SyntheticTotal}";
    public string LastScenario => string.IsNullOrWhiteSpace(runtime.SyntheticLastScenario) ? "—" : runtime.SyntheticLastScenario;
    public string FailureSummary => string.IsNullOrWhiteSpace(runtime.SyntheticFailureSummary)
        ? "No verdict yet."
        : runtime.SyntheticFailureSummary;
    public string SessionFolder => runtime.ActiveSessionFolder;
    public string SafetyText => runtime.IsSyntheticMode
        ? "ISOLATED: live ImageSaved events are ignored; no camera or real image file is touched."
        : "Ready. Starting the lab switches QualitySessionMeter into an isolated synthetic session.";

    public ICommand RunCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ReturnLiveCommand { get; }
    public ICommand OpenFolderCommand { get; }

    [ImportingConstructor]
    public SyntheticLabDockable(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator) : base(profileService) {

        Title = "QSM Synthetic Lab";
        ImageGeometry = PluginIcon.CreateMeterGeometry();

        var accessor = new PluginOptionsAccessor(profileService, PluginConstants.Identifier);
        var settings = new QualitySettings(accessor);
        runtime = QualitySessionRuntimeRegistry.GetOrCreate(profileService, imageSaveMediator, guiderMediator, settings);

        runtime.SyntheticStateChanged += RuntimeSyntheticStateChanged;

        RunCommand = new AsyncRelayCommand(RunAsync);
        StopCommand = new RelayCommand(runtime.StopSyntheticSession);
        ReturnLiveCommand = new RelayCommand(runtime.ExitSyntheticMode);
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    private Task RunAsync() => runtime.RunCanonicalSyntheticSessionAsync(FrameDelayMs);

    private void OpenFolder() {
        var path = runtime.ActiveSessionFolder;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void RuntimeSyntheticStateChanged(object sender, EventArgs e) {
        void Apply() {
            RaisePropertyChanged(nameof(IsSyntheticMode));
            RaisePropertyChanged(nameof(IsSyntheticRunning));
            RaisePropertyChanged(nameof(CanRun));
            RaisePropertyChanged(nameof(CanStop));
            RaisePropertyChanged(nameof(CanReturnLive));
            RaisePropertyChanged(nameof(Status));
            RaisePropertyChanged(nameof(Progress));
            RaisePropertyChanged(nameof(LastScenario));
            RaisePropertyChanged(nameof(FailureSummary));
            RaisePropertyChanged(nameof(SessionFolder));
            RaisePropertyChanged(nameof(SafetyText));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

    public void Dispose() {
        runtime.SyntheticStateChanged -= RuntimeSyntheticStateChanged;
    }
}
