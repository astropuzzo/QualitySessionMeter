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
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(IDockableVM))]
public sealed class QualitySessionMeterDockable : DockableVM, IDisposable {
    private readonly QualitySessionRuntime runtime;
    private bool lastSyntheticMode;

    public override bool IsTool { get; } = true;

    public ObservableCollection<FrameQualityResult> Frames { get; } = new();
    public ObservableCollection<SessionEvent> Events { get; } = new();
    public ObservableCollection<FrameQualityResult> BestAccepted { get; } = new();
    public ObservableCollection<FrameQualityResult> WorstAccepted { get; } = new();
    public QualitySettings Settings => runtime.Settings;

    private FrameQualityResult currentFrame;
    public FrameQualityResult CurrentFrame {
        get => currentFrame;
        private set { currentFrame = value; RaisePropertyChanged(); RaiseAllSummary(); }
    }

    private int syntheticDelayMs = 150;
    public int SyntheticDelayMs {
        get => syntheticDelayMs;
        set { syntheticDelayMs = Math.Clamp(value, 0, 5000); RaisePropertyChanged(); }
    }

    public int Captured => Frames.Count;
    public int Accepted => Frames.Count(x => x.Status == FrameStatus.Accepted);
    public int Warning => Frames.Count(x => x.Status == FrameStatus.Warning);
    public int Rejected => Frames.Count(x => x.Status == FrameStatus.Rejected);
    public int Learning => Frames.Count(x => x.Status == FrameStatus.Learning);
    public int Errors => Frames.Count(x => x.Status == FrameStatus.Error);
    public int Usable => Accepted + Warning;
    public int EventCount => Events.Count;
    public double AcceptanceRate => Usable + Rejected == 0 ? 0 : Usable * 100.0 / (Usable + Rejected);
    public double SessionQuality => Frames.Where(x => x.IsUsable).Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
    public double SessionConfidence => Frames.Where(x => x.IsUsable && !double.IsNaN(x.ConfidenceScore) && !double.IsInfinity(x.ConfidenceScore)).Select(x => x.ConfidenceScore).DefaultIfEmpty(0).Average();
    public string SessionFolder => runtime.ActiveSessionFolder;
    public bool IsSyntheticMode => runtime.IsSyntheticMode;
    public bool IsSyntheticRunning => runtime.IsSyntheticRunning;
    public bool IsLiveMode => !runtime.IsSyntheticMode;
    public string SyntheticStatus => runtime.SyntheticStatus;
    public string SyntheticProgress => runtime.SyntheticTotal <= 0 ? "0 / 0" : $"{runtime.SyntheticProcessed} / {runtime.SyntheticTotal}";
    public string SyntheticLastScenario => runtime.SyntheticLastScenario;
    public string SyntheticFailureSummary => runtime.SyntheticFailureSummary;
    public string ModeText => runtime.IsSyntheticMode
        ? "SYNTHETIC LAB — NO CAMERA / NO REAL FILES"
        : !Settings.Enabled
            ? "QSM OFF — NO ANALYSIS / NO FILE ACTIONS"
            : Settings.MonitorOnly
                ? $"MONITOR ONLY · {Settings.MonitoringScope}"
                : $"ACTIVE REJECT HANDLING · {Settings.MonitoringScope}";

    public ICommand ResetSessionCommand { get; }
    public ICommand OpenSessionFolderCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand RunSyntheticSessionCommand { get; }
    public ICommand StopSyntheticSessionCommand { get; }
    public ICommand ReturnToLiveCommand { get; }

    [ImportingConstructor]
    public QualitySessionMeterDockable(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        ISequenceMediator sequenceMediator) : base(profileService) {

        Title = PluginConstants.DisplayName;
        ImageGeometry = PluginIcon.CreateMeterGeometry();

        var accessor = new PluginOptionsAccessor(profileService, PluginConstants.Identifier);
        var settings = new QualitySettings(accessor);
        runtime = QualitySessionRuntimeRegistry.GetOrCreate(profileService, imageSaveMediator, guiderMediator, sequenceMediator, settings);
        lastSyntheticMode = runtime.IsSyntheticMode;

        ReloadLiveFrames();

        runtime.FrameProcessed += RuntimeFrameProcessed;
        runtime.SyntheticStateChanged += RuntimeSyntheticStateChanged;
        Settings.PropertyChanged += SettingsChanged;

        ResetSessionCommand = new RelayCommand(ResetSession);
        OpenSessionFolderCommand = new RelayCommand(OpenSessionFolder);
        OpenReportCommand = new RelayCommand(OpenReport);
        RunSyntheticSessionCommand = new AsyncRelayCommand(RunSyntheticSessionAsync);
        StopSyntheticSessionCommand = new RelayCommand(runtime.StopSyntheticSession);
        ReturnToLiveCommand = new RelayCommand(ReturnToLive);
    }

    private async Task RunSyntheticSessionAsync() {
        if (!runtime.IsSyntheticMode) {
            Frames.Clear();
            CurrentFrame = null;
            RefreshDerivedViews();
        }
        await runtime.RunCanonicalSyntheticSessionAsync(SyntheticDelayMs);
        RefreshDerivedViews();
        RaiseAllSummary();
    }

    private void ReturnToLive() => runtime.ExitSyntheticMode();

    private void ReloadLiveFrames() {
        Frames.Clear();
        foreach (var frame in runtime.Store.Results.TakeLast(500)) Frames.Add(frame);
        CurrentFrame = Frames.LastOrDefault();
        RefreshDerivedViews();
        RaiseAllSummary();
    }

    private void RuntimeFrameProcessed(object sender, FrameQualityResult frame) {
        void Apply() {
            Frames.Add(frame);
            while (Frames.Count > 500) Frames.RemoveAt(0);
            CurrentFrame = frame;
            RefreshDerivedViews();
            RaiseAllSummary();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

    private void RuntimeSyntheticStateChanged(object sender, EventArgs e) {
        void Apply() {
            bool nowSynthetic = runtime.IsSyntheticMode;
            if (nowSynthetic && !lastSyntheticMode) {
                Frames.Clear();
                CurrentFrame = null;
                RefreshDerivedViews();
            } else if (!nowSynthetic && lastSyntheticMode) {
                lastSyntheticMode = false;
                ReloadLiveFrames();
                return;
            }

            lastSyntheticMode = nowSynthetic;
            RaisePropertyChanged(nameof(IsSyntheticMode));
            RaisePropertyChanged(nameof(IsSyntheticRunning));
            RaisePropertyChanged(nameof(IsLiveMode));
            RaisePropertyChanged(nameof(SyntheticStatus));
            RaisePropertyChanged(nameof(SyntheticProgress));
            RaisePropertyChanged(nameof(SyntheticLastScenario));
            RaisePropertyChanged(nameof(SyntheticFailureSummary));
            RaisePropertyChanged(nameof(SessionFolder));
            RaisePropertyChanged(nameof(ModeText));
            RaiseAllSummary();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

    private void SettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => RaisePropertyChanged(nameof(ModeText));

    private void RefreshDerivedViews() {
        BestAccepted.Clear();
        foreach (var frame in Frames.Where(x => x.Status == FrameStatus.Accepted)
                     .OrderByDescending(x => x.OverallQuality).ThenByDescending(x => x.ConfidenceScore).Take(5)) BestAccepted.Add(frame);

        WorstAccepted.Clear();
        foreach (var frame in Frames.Where(x => x.Status == FrameStatus.Accepted)
                     .OrderBy(x => x.OverallQuality).ThenBy(x => x.ConfidenceScore).Take(5)) WorstAccepted.Add(frame);

        var grouper = new EventGroupingEngine();
        foreach (var frame in Frames) grouper.Add(frame);
        Events.Clear();
        foreach (var e in grouper.Events.TakeLast(12)) Events.Add(e);

        RaisePropertyChanged(nameof(EventCount));
        RaisePropertyChanged(nameof(SessionConfidence));
    }

    private void ResetSession() {
        if (runtime.IsSyntheticMode) return;
        runtime.ResetSession();
        Frames.Clear();
        CurrentFrame = null;
        RefreshDerivedViews();
        RaiseAllSummary();
    }

    private void OpenSessionFolder() {
        var path = runtime.ActiveSessionFolder;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void OpenReport() {
        var folder = runtime.ActiveSessionFolder;
        if (string.IsNullOrWhiteSpace(folder)) return;
        var path = Path.Combine(folder, "report.html");
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void RaiseAllSummary() {
        RaisePropertyChanged(nameof(Captured));
        RaisePropertyChanged(nameof(Accepted));
        RaisePropertyChanged(nameof(Warning));
        RaisePropertyChanged(nameof(Rejected));
        RaisePropertyChanged(nameof(Learning));
        RaisePropertyChanged(nameof(Errors));
        RaisePropertyChanged(nameof(Usable));
        RaisePropertyChanged(nameof(EventCount));
        RaisePropertyChanged(nameof(AcceptanceRate));
        RaisePropertyChanged(nameof(SessionQuality));
        RaisePropertyChanged(nameof(SessionConfidence));
        RaisePropertyChanged(nameof(SessionFolder));
        RaisePropertyChanged(nameof(ModeText));
        RaisePropertyChanged(nameof(IsSyntheticMode));
        RaisePropertyChanged(nameof(IsSyntheticRunning));
        RaisePropertyChanged(nameof(IsLiveMode));
        RaisePropertyChanged(nameof(SyntheticStatus));
        RaisePropertyChanged(nameof(SyntheticProgress));
        RaisePropertyChanged(nameof(SyntheticLastScenario));
        RaisePropertyChanged(nameof(SyntheticFailureSummary));
    }

    public void Dispose() {
        runtime.FrameProcessed -= RuntimeFrameProcessed;
        runtime.SyntheticStateChanged -= RuntimeSyntheticStateChanged;
        Settings.PropertyChanged -= SettingsChanged;
    }
}
