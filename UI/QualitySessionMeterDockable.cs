using CommunityToolkit.Mvvm.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using PrepareImageParameters = NINA.Core.Utility.PrepareImageParameters;
using NINA.Image.Interfaces;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(IDockableVM))]
public sealed class QualitySessionMeterDockable : DockableVM, IDisposable {
    private readonly QualitySessionRuntime runtime;
    private readonly IImagingMediator imagingMediator;
    private readonly IImageDataFactory imageDataFactory;
#if QSM_DEVELOPMENT
    private bool lastSyntheticMode;
#endif

    public override bool IsTool { get; } = true;

    public ObservableCollection<FrameQualityResult> Frames { get; } = new();
    public ObservableCollection<SessionEvent> Events { get; } = new();
    public ObservableCollection<FrameQualityResult> BestAccepted { get; } = new();
    public ObservableCollection<FrameQualityResult> WorstAccepted { get; } = new();
    public ObservableCollection<FrameQualityResult> RejectedFrames { get; } = new();
    public QualitySettings Settings => runtime.Settings;

    public CalibrationSuggestion Calibration => runtime.CurrentCalibrationSuggestion;
    public bool CalibrationAvailable => Calibration?.Available == true;
    public Visibility CalibrationSuggestionVisibility => CalibrationAvailable ? Visibility.Visible : Visibility.Collapsed;
    public string CalibrationStatus => CalibrationAvailable
        ? $"READY · {Calibration.SampleCount} stable accepted frames · {Calibration.Context}"
        : Calibration?.Reason ?? "No calibration suggestion is currently available.";
    public string CalibrationValues => CalibrationAvailable
        ? $"RMS ≤ {Calibration.SuggestedMaxGuideRms:0.00}\" · excursion {Calibration.SuggestedExcursionThreshold:0.00}\" · hard {Calibration.SuggestedHardExcursionThreshold:0.00}\" · stars -{Calibration.SuggestedMaxStarLossPercent:0.0}% · background +{Calibration.SuggestedBackgroundIncreasePercent:0.0}/-{Calibration.SuggestedBackgroundDecreasePercent:0.0}%"
        : "";

    private FrameQualityResult selectedRejectedFrame;
    public FrameQualityResult SelectedRejectedFrame {
        get => selectedRejectedFrame;
        set {
            if (ReferenceEquals(selectedRejectedFrame, value)) return;
            selectedRejectedFrame = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanReviewSelectedRejected));
            RaisePropertyChanged(nameof(CanRestoreSelectedRejected));
            if (value != null) _ = OpenFrameInImageTabAsync(value);
        }
    }

    private string reviewMessage = "Select a rejected frame to review it in N.I.N.A.'s Image view.";
    public string ReviewMessage {
        get => reviewMessage;
        private set { reviewMessage = value ?? ""; RaisePropertyChanged(); }
    }

    public bool CanReviewSelectedRejected => SelectedRejectedFrame != null && !SelectedRejectedFrame.IsSyntheticFile;
    public bool CanRestoreSelectedRejected => CanReviewSelectedRejected && SelectedRejectedFrame.IsBadFileApplied;

    private FrameQualityResult currentFrame;
    public FrameQualityResult CurrentFrame {
        get => currentFrame;
        private set { currentFrame = value; RaisePropertyChanged(); RaiseAllSummary(); }
    }

#if QSM_DEVELOPMENT
    private int syntheticDelayMs = 150;
    public int SyntheticDelayMs {
        get => syntheticDelayMs;
        set { syntheticDelayMs = Math.Clamp(value, 0, 5000); RaisePropertyChanged(); }
    }

#endif
    public System.Collections.Generic.IEnumerable<FrameQualityResult> SecondPassFrames => Frames.Where(x => x.ImageEvidence.Attempted).Reverse().Take(50);
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
#if QSM_DEVELOPMENT
    public bool IsSyntheticMode => runtime.IsSyntheticMode;
    public bool IsSyntheticRunning => runtime.IsSyntheticRunning;
    public bool IsLiveMode => !runtime.IsSyntheticMode;
    public string SyntheticStatus => runtime.SyntheticStatus;
    public string SyntheticProgress => runtime.SyntheticTotal <= 0 ? "0 / 0" : $"{runtime.SyntheticProcessed} / {runtime.SyntheticTotal}";
    public string SyntheticLastScenario => runtime.SyntheticLastScenario;
    public string SyntheticFailureSummary => runtime.SyntheticFailureSummary;
#endif
    public string ModeText {
        get {
#if QSM_DEVELOPMENT
            if (runtime.IsSyntheticMode) return "SYNTHETIC LAB — NO CAMERA / NO REAL FILES";
#endif
            return !Settings.Enabled
                ? "QSM OFF — NO ANALYSIS / NO FILE ACTIONS"
                : Settings.MonitorOnly
                    ? $"MONITOR ONLY · {Settings.MonitoringScope}"
                    : $"ACTIVE REJECT HANDLING · {Settings.MonitoringScope}";
        }
    }

    public ICommand ResetSessionCommand { get; }
    public ICommand OpenSessionFolderCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand OpenSelectedRejectedCommand { get; }
    public ICommand RestoreSelectedRejectedCommand { get; }
    public ICommand ApplyCalibrationCommand { get; }
    public ICommand IgnoreCalibrationCommand { get; }
#if QSM_DEVELOPMENT
    public ICommand RunSyntheticSessionCommand { get; }
    public ICommand StopSyntheticSessionCommand { get; }
    public ICommand ReturnToLiveCommand { get; }
#endif

    [ImportingConstructor]
    public QualitySessionMeterDockable(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        ISequenceMediator sequenceMediator,
        IImagingMediator imagingMediator,
        IImageDataFactory imageDataFactory) : base(profileService) {

        Title = PluginConstants.DisplayName;
        ImageGeometry = PluginIcon.CreateMeterGeometry();
        this.imagingMediator = imagingMediator ?? throw new ArgumentNullException(nameof(imagingMediator));
        this.imageDataFactory = imageDataFactory ?? throw new ArgumentNullException(nameof(imageDataFactory));

        var accessor = new PluginOptionsAccessor(profileService, PluginConstants.Identifier);
        var settings = new QualitySettings(accessor);
        runtime = QualitySessionRuntimeRegistry.GetOrCreate(profileService, imageSaveMediator, guiderMediator, sequenceMediator, settings);
#if QSM_DEVELOPMENT
        lastSyntheticMode = runtime.IsSyntheticMode;
#endif

        ReloadLiveFrames();

        runtime.FrameProcessed += RuntimeFrameProcessed;
        runtime.FrameReviewChanged += RuntimeFrameReviewChanged;
        runtime.CalibrationSuggestionChanged += RuntimeCalibrationSuggestionChanged;
#if QSM_DEVELOPMENT
        runtime.SyntheticStateChanged += RuntimeSyntheticStateChanged;
#endif
        Settings.PropertyChanged += SettingsChanged;

        ResetSessionCommand = new RelayCommand(ResetSession);
        OpenSessionFolderCommand = new RelayCommand(OpenSessionFolder);
        OpenReportCommand = new RelayCommand(OpenReport);
        OpenSelectedRejectedCommand = new AsyncRelayCommand(OpenSelectedRejectedAsync);
        RestoreSelectedRejectedCommand = new AsyncRelayCommand(RestoreSelectedRejectedAsync);
        ApplyCalibrationCommand = new RelayCommand(runtime.ApplyCurrentCalibrationSuggestion);
        IgnoreCalibrationCommand = new RelayCommand(runtime.IgnoreCurrentCalibrationSuggestion);
#if QSM_DEVELOPMENT
        RunSyntheticSessionCommand = new AsyncRelayCommand(RunSyntheticSessionAsync);
        StopSyntheticSessionCommand = new RelayCommand(runtime.StopSyntheticSession);
        ReturnToLiveCommand = new RelayCommand(ReturnToLive);
#endif
    }

#if QSM_DEVELOPMENT
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

#endif
    private async Task OpenSelectedRejectedAsync() {
        if (SelectedRejectedFrame == null) return;
        await OpenFrameInImageTabAsync(SelectedRejectedFrame);
    }

    private async Task OpenFrameInImageTabAsync(FrameQualityResult frame) {
        if (frame == null) return;
        if (frame.IsSyntheticFile) {
            ReviewMessage = "Synthetic frame: there is no FITS/XISF file to load into the Image view.";
            return;
        }

        var path = ResolveExistingFramePath(frame);
        if (string.IsNullOrWhiteSpace(path)) {
            ReviewMessage = $"File not found for frame #{frame.FrameIndex}: {frame.FileName}";
            return;
        }

        try {
            ReviewMessage = $"Loading {Path.GetFileName(path)} into N.I.N.A. Image…";
            var data = await imageDataFactory.CreateFromFile(path, 16, false, CancellationToken.None);
            var rendered = await imagingMediator.PrepareImage(data, new PrepareImageParameters(true, false), CancellationToken.None);
            if (rendered?.Image != null) imagingMediator.SetImage(rendered.Image);
            ReviewMessage = $"Loaded {Path.GetFileName(path)} · auto verdict {frame.StatusText} · {frame.ProbableCause}";
        } catch (Exception ex) {
            ReviewMessage = $"Could not load {frame.FileName}: {ex.Message}";
        }
    }

    private async Task RestoreSelectedRejectedAsync() {
        var frame = SelectedRejectedFrame;
        if (frame == null) return;
        if (frame.IsSyntheticFile) {
            ReviewMessage = "Synthetic frames do not have a real BAD_ file to restore.";
            return;
        }
        if (!frame.IsBadFileApplied) {
            ReviewMessage = $"{frame.FileName} has no BAD_/Rejected file action to undo.";
            return;
        }

        try {
            var restored = await runtime.RestoreRejectedFileAsync(frame);
            ReviewMessage = $"BAD file action undone: {Path.GetFileName(restored)}. The automatic QSM verdict remains REJECTED for audit/history.";
            RefreshDerivedViews();
            RaisePropertyChanged(nameof(CanRestoreSelectedRejected));
        } catch (Exception ex) {
            ReviewMessage = $"Could not undo BAD for {frame.FileName}: {ex.Message}";
        }
    }

    private static string ResolveExistingFramePath(FrameQualityResult frame) {
        foreach (var candidate in new[] { frame?.FinalPath, frame?.OriginalPath }) {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
        }
        return "";
    }

    private void RuntimeFrameReviewChanged(object sender, FrameQualityResult frame) {
        void Apply() {
            RefreshDerivedViews();
            RaisePropertyChanged(nameof(CanRestoreSelectedRejected));
            RaiseAllSummary();
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

    private void RuntimeCalibrationSuggestionChanged(object sender, EventArgs e) {
        void Apply() => RaiseCalibrationSummary();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

    private void RaiseCalibrationSummary() {
        RaisePropertyChanged(nameof(Calibration));
        RaisePropertyChanged(nameof(CalibrationAvailable));
        RaisePropertyChanged(nameof(CalibrationSuggestionVisibility));
        RaisePropertyChanged(nameof(CalibrationStatus));
        RaisePropertyChanged(nameof(CalibrationValues));
    }

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
            RaiseCalibrationSummary();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

#if QSM_DEVELOPMENT
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
            RaiseCalibrationSummary();
            RaiseAllSummary();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

#endif

    private void SettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
        RaisePropertyChanged(nameof(ModeText));
        RaiseCalibrationSummary();
    }

    private void RefreshDerivedViews() {
        RaisePropertyChanged(nameof(SecondPassFrames));
        BestAccepted.Clear();
        foreach (var frame in Frames.Where(x => x.Status == FrameStatus.Accepted)
                     .OrderByDescending(x => x.OverallQuality).ThenByDescending(x => x.ConfidenceScore).Take(5)) BestAccepted.Add(frame);

        WorstAccepted.Clear();
        foreach (var frame in Frames.Where(x => x.Status == FrameStatus.Accepted)
                     .OrderBy(x => x.OverallQuality).ThenBy(x => x.ConfidenceScore).Take(5)) WorstAccepted.Add(frame);

        var selectedIndex = SelectedRejectedFrame?.FrameIndex;
        RejectedFrames.Clear();
        foreach (var frame in Frames.Where(x => x.Status == FrameStatus.Rejected).OrderByDescending(x => x.FrameIndex).Take(80)) RejectedFrames.Add(frame);
        if (selectedIndex.HasValue) selectedRejectedFrame = RejectedFrames.FirstOrDefault(x => x.FrameIndex == selectedIndex.Value);
        RaisePropertyChanged(nameof(SelectedRejectedFrame));
        RaisePropertyChanged(nameof(CanReviewSelectedRejected));
        RaisePropertyChanged(nameof(CanRestoreSelectedRejected));

        var grouper = new EventGroupingEngine();
        foreach (var frame in Frames) grouper.Add(frame);
        Events.Clear();
        foreach (var e in grouper.Events.TakeLast(12)) Events.Add(e);

        RaisePropertyChanged(nameof(EventCount));
        RaisePropertyChanged(nameof(SessionConfidence));
    }

    private void ResetSession() {
#if QSM_DEVELOPMENT
        if (runtime.IsSyntheticMode) return;
#endif
        runtime.ResetSession();
        Frames.Clear();
        CurrentFrame = null;
        RefreshDerivedViews();
        RaiseAllSummary();
        RaiseCalibrationSummary();
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
#if QSM_DEVELOPMENT
        RaisePropertyChanged(nameof(IsSyntheticMode));
        RaisePropertyChanged(nameof(IsSyntheticRunning));
        RaisePropertyChanged(nameof(IsLiveMode));
        RaisePropertyChanged(nameof(SyntheticStatus));
        RaisePropertyChanged(nameof(SyntheticProgress));
        RaisePropertyChanged(nameof(SyntheticLastScenario));
        RaisePropertyChanged(nameof(SyntheticFailureSummary));
#endif
    }

    public void Dispose() {
        runtime.FrameProcessed -= RuntimeFrameProcessed;
        runtime.FrameReviewChanged -= RuntimeFrameReviewChanged;
        runtime.CalibrationSuggestionChanged -= RuntimeCalibrationSuggestionChanged;
#if QSM_DEVELOPMENT
        runtime.SyntheticStateChanged -= RuntimeSyntheticStateChanged;
#endif
        Settings.PropertyChanged -= SettingsChanged;
    }
}
