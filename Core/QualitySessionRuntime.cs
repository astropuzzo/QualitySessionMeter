using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
#if QSM_DEVELOPMENT
using NINA.Plugin.QualitySessionMeter.Synthetic;
#endif
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class QualitySessionRuntime : IDisposable {
    private readonly IProfileService profileService;
    private readonly IImageSaveMediator imageSaveMediator;
    private readonly ISequenceMediator sequenceMediator;
    private readonly QualitySettings settings;
    private readonly BaselineEngine baseline = new();
    private readonly GuideCollector guideCollector;
    private readonly QualityEngine qualityEngine = new();
    private readonly RejectedFileService rejectedFileService = new();
    private readonly CalibrationSuggestionEngine calibrationEngine = new();
    private readonly PredictiveDegradationEngine predictiveEngine = new();
    private readonly EnvironmentalCorrelationService environmentalService = new();
    private readonly SessionStore sessionStore = new();
    private readonly SemaphoreSlim processingLock = new(1, 1);
    private readonly object controlSync = new();
    private readonly HashSet<string> controlTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> reservedControlTokens = new(StringComparer.Ordinal);
    private readonly object sourceSync = new();
    private readonly List<PendingFrameSource> pendingSources = new();
    private readonly HashSet<string> appliedCalibrationContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ignoredCalibrationContexts = new(StringComparer.OrdinalIgnoreCase);
    private IWeatherDataMediator weatherDataMediator;
    private int frameIndex;
    private bool disposed;
    private CalibrationSuggestion currentCalibrationSuggestion = new();

#if QSM_DEVELOPMENT
    private CancellationTokenSource syntheticCts;
    private BaselineEngine syntheticBaseline;
    private SessionStore syntheticSessionStore;
    private QualitySettings syntheticSettings;
    private int syntheticProcessed;
    private int syntheticTotal;
    private int syntheticFailures;
    private string syntheticStatus = "OFF";
    private string syntheticLastScenario = "";
    private string syntheticFailureSummary = "";
    private string syntheticSessionName = "";
#endif

    public event EventHandler<FrameQualityResult> FrameProcessed;
#if QSM_DEVELOPMENT
    public event EventHandler SyntheticStateChanged;
#endif
    public event EventHandler CalibrationSuggestionChanged;

    public QualitySettings Settings => settings;
    public SessionStore Store => sessionStore;
    public SessionStore ObservableStore {
        get {
#if QSM_DEVELOPMENT
            if (IsSyntheticMode && syntheticSessionStore != null) return syntheticSessionStore;
#endif
            return sessionStore;
        }
    }
    public CalibrationSuggestion CurrentCalibrationSuggestion => currentCalibrationSuggestion;
#if QSM_DEVELOPMENT
    public bool IsSyntheticMode { get; private set; }
    public bool IsSyntheticRunning { get; private set; }
    public int SyntheticProcessed => syntheticProcessed;
    public int SyntheticTotal => syntheticTotal;
    public int SyntheticFailures => syntheticFailures;
    public string SyntheticStatus => syntheticStatus;
    public string SyntheticLastScenario => syntheticLastScenario;
    public string SyntheticFailureSummary => syntheticFailureSummary;
    public string SyntheticSessionName => syntheticSessionName;
    public string ActiveSessionFolder => IsSyntheticMode
        ? syntheticSessionStore?.SessionFolder ?? ""
        : sessionStore.SessionFolder;
#else
    public bool IsSyntheticMode => false;
    public bool IsSyntheticRunning => false;
    public string ActiveSessionFolder => sessionStore.SessionFolder;
#endif
    public bool IsSequencerControlArmed { get { lock (controlSync) return controlTokens.Count > 0; } }

    public QualitySessionRuntime(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        ISequenceMediator sequenceMediator,
        QualitySettings settings) {

        this.profileService = profileService;
        this.imageSaveMediator = imageSaveMediator;
        this.sequenceMediator = sequenceMediator;
        this.settings = settings;
        guideCollector = new GuideCollector(guiderMediator);

        imageSaveMediator.BeforeFinalizeImageSaved += BeforeFinalizeImageSaved;
        imageSaveMediator.ImageSaved += ImageSaved;
    }

    public void AttachWeatherMediator(IWeatherDataMediator mediator) => weatherDataMediator = mediator;

    public string ArmSequencerControl() {
        if (!settings.Enabled || IsSyntheticMode) return "";
        var token = Guid.NewGuid().ToString("N");
        lock (controlSync) controlTokens.Add(token);
        Logger.Info($"QualitySessionMeter V3: armed controlled sequencer block {token[..8]}.");
        return token;
    }

    public void DisarmSequencerControl(string token) {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (controlSync) {
            controlTokens.Remove(token);
            reservedControlTokens.Remove(token);
        }
    }

    public GuideExposureMetrics GetRecentGuideMetrics(double lookbackSeconds = 10) =>
        guideCollector.GetRecentMetrics(lookbackSeconds, settings.ExcursionThreshold);

    public bool HasPersistentControlledDegradation() {
        int required = settings.SmartPauseRejectStreak;
        var recent = sessionStore.Results.Where(x => x.QsmControlled).TakeLast(required).ToArray();
        return recent.Length == required && recent.All(x => x.Status is FrameStatus.Rejected or FrameStatus.Error);
    }

    public void ApplyCurrentCalibrationSuggestion() {
        var suggestion = currentCalibrationSuggestion;
        if (suggestion?.Available != true) return;
        ApplySuggestion(suggestion);
        appliedCalibrationContexts.Add(suggestion.Context ?? "");
        Logger.Info($"QualitySessionMeter V3 calibration applied for {suggestion.Context}: RMS {settings.MaxGuideRms:0.00}, excursion {settings.ExcursionThreshold:0.00}, hard {settings.HardExcursionThreshold:0.00}, star loss {settings.MaxStarLossPercent:0.0}%, bg +{settings.MaxBackgroundIncreasePercent:0.0}/-{settings.MaxBackgroundDecreasePercent:0.0}%.");
        CalibrationSuggestionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void IgnoreCurrentCalibrationSuggestion() {
        if (!string.IsNullOrWhiteSpace(currentCalibrationSuggestion?.Context)) {
            ignoredCalibrationContexts.Add(currentCalibrationSuggestion.Context);
        }
        currentCalibrationSuggestion = new CalibrationSuggestion();
        CalibrationSuggestionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySuggestion(CalibrationSuggestion s) {
        if (Finite(s.SuggestedMaxGuideRms)) settings.MaxGuideRms = Math.Min(s.SuggestedMaxGuideRms, settings.AutoSafetyMaxGuideRms);
        if (Finite(s.SuggestedExcursionThreshold)) settings.ExcursionThreshold = Math.Min(s.SuggestedExcursionThreshold, settings.AutoSafetyMaxExcursion);
        if (Finite(s.SuggestedHardExcursionThreshold)) settings.HardExcursionThreshold = Math.Min(s.SuggestedHardExcursionThreshold, settings.AutoSafetyMaxHardExcursion);
        if (Finite(s.SuggestedMaxStarLossPercent)) settings.MaxStarLossPercent = Math.Min(s.SuggestedMaxStarLossPercent, settings.AutoSafetyMaxStarLossPercent);
        if (Finite(s.SuggestedBackgroundIncreasePercent)) settings.MaxBackgroundIncreasePercent = Math.Min(s.SuggestedBackgroundIncreasePercent, settings.AutoSafetyMaxBackgroundPercent);
        if (Finite(s.SuggestedBackgroundDecreasePercent)) settings.MaxBackgroundDecreasePercent = Math.Min(s.SuggestedBackgroundDecreasePercent, settings.AutoSafetyMaxBackgroundPercent);
    }

    private bool TryReserveControlToken(out string token) {
        lock (controlSync) {
            token = controlTokens.FirstOrDefault(x => !reservedControlTokens.Contains(x)) ?? "";
            if (string.IsNullOrWhiteSpace(token)) return false;
            reservedControlTokens.Add(token);
            return true;
        }
    }

    private bool HasAdvancedSequenceEvidence() {
        try {
            if (!sequenceMediator.IsAdvancedSequenceRunning()) return false;
            var running = sequenceMediator.GetAdvancedSequencerCurrentRunningItems();
            return running != null && running.Count > 0;
        } catch {
            return false;
        }
    }

    private FrameSourceInfo CaptureSource(string sequenceTitle) {
        bool controlled = TryReserveControlToken(out var token);
        var resolved = FrameSourcePolicy.Resolve(
            settings.MonitoringScope,
            controlled,
            sequenceTitle,
            HasAdvancedSequenceEvidence());
        return new FrameSourceInfo {
            Kind = resolved.Kind,
            SequenceTitle = resolved.SequenceTitle,
            QsmControlled = resolved.QsmControlled,
            MonitoringEligible = resolved.MonitoringEligible,
            FileActionEligible = resolved.FileActionEligible,
            ControlToken = token,
            ProvenanceFrozen = true
        };
    }

    private void FreezeSource(int imageId, int exposureNumber, DateTime exposureStart, FrameSourceInfo source) {
        lock (sourceSync) {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            pendingSources.RemoveAll(x => x.CapturedUtc < cutoff);
            pendingSources.Add(new PendingFrameSource(
                imageId,
                exposureNumber,
                NormalizeUtc(exposureStart),
                source,
                DateTime.UtcNow));
        }
    }

    private bool TryTakeFrozenSource(int imageId, int exposureNumber, DateTime exposureStart, out FrameSourceInfo source) {
        source = null;
        var normalized = NormalizeUtc(exposureStart);
        lock (sourceSync) {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            pendingSources.RemoveAll(x => x.CapturedUtc < cutoff);
            int index = pendingSources.FindIndex(x => x.Matches(imageId, exposureNumber, normalized));
            if (index < 0) return false;
            source = pendingSources[index].Source;
            pendingSources.RemoveAt(index);
            return true;
        }
    }

    private async Task BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
        if (IsSyntheticMode) return;
        if (!settings.Enabled || e?.Image?.RawImageData?.MetaData?.Image == null) return;
        if (e.Image.RawImageData.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;

        var meta = e.Image.RawImageData.MetaData;
        var source = CaptureSource(meta.Sequence?.Title ?? "");
        FreezeSource(meta.Image.Id, meta.Image.ExposureNumber, meta.Image.ExposureStart, source);
        if (!source.MonitoringEligible) return;

        try {
            var analysis = e.Image.RawImageData.StarDetectionAnalysis;
            if (analysis == null || analysis.DetectedStars <= 0) {
                var render = e.Image.RawImageData.RenderImage();
                render = await render.Stretch(
                    profileService.ActiveProfile.ImageSettings.AutoStretchFactor,
                    profileService.ActiveProfile.ImageSettings.BlackClipping,
                    profileService.ActiveProfile.ImageSettings.UnlinkedStretch);
                _ = await render.DetectStars(
                    false,
                    profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    default,
                    default);
            }
        } catch (Exception ex) {
            Logger.Warning($"QualitySessionMeter star detection fallback failed: {ex.Message}");
        }
    }

    private void ImageSaved(object sender, ImageSavedEventArgs e) {
        if (IsSyntheticMode) return;
        if (!settings.Enabled || e?.MetaData?.Image == null) return;
        if (e.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;

        if (!TryTakeFrozenSource(
                e.MetaData.Image.Id,
                e.MetaData.Image.ExposureNumber,
                e.MetaData.Image.ExposureStart,
                out var source)) {
            Logger.Warning("QualitySessionMeter ignored a LIGHT because its acquisition provenance was not frozen before finalization.");
            return;
        }
        if (!source.MonitoringEligible) {
            Logger.Debug($"QualitySessionMeter ignored LIGHT from {source.SourceText}; scope={settings.MonitoringScope}.");
            return;
        }
        _ = ProcessImageAsync(e, source);
    }

    private async Task ProcessImageAsync(ImageSavedEventArgs e, FrameSourceInfo source) {
        await processingLock.WaitAsync();

        if (IsSyntheticMode || !settings.Enabled) {
            processingLock.Release();
            return;
        }

        int assignedFrameIndex = Interlocked.Increment(ref frameIndex);
        DateTime frameTimestampUtc = DateTime.UtcNow;

        try {
            var meta = e.MetaData;
            var start = NormalizeUtc(meta.Image.ExposureStart);
            if (start != DateTime.MinValue) frameTimestampUtc = start;

            var guide = guideCollector.GetExposureMetrics(start, e.Duration, settings.ExcursionThreshold);

            var key = new BaselineKey(
                meta.Target?.Name,
                e.Filter ?? meta.FilterWheel?.Filter,
                e.Duration,
                meta.Camera?.Gain ?? -1,
                meta.Camera?.BinX ?? 1,
                meta.Camera?.BinY ?? 1,
                meta.Camera?.Name);

            var snapshot = baseline.GetSnapshot(key, settings.MinimumLearningFrames);
            var input = new FrameQualityInput {
                FrameIndex = assignedFrameIndex,
                TimestampUtc = frameTimestampUtc,
                OriginalPath = e.PathToImage?.IsFile == true ? e.PathToImage.LocalPath : e.PathToImage?.ToString(),
                Target = meta.Target?.Name ?? "",
                Filter = e.Filter ?? meta.FilterWheel?.Filter ?? "",
                ExposureSeconds = e.Duration,
                Gain = meta.Camera?.Gain ?? -1,
                BinX = meta.Camera?.BinX ?? 1,
                BinY = meta.Camera?.BinY ?? 1,
                Camera = meta.Camera?.Name ?? "",
                StarCount = e.StarDetectionAnalysis?.DetectedStars ?? -1,
                BackgroundMedian = e.Statistics?.Median ?? double.NaN,
                Baseline = snapshot,
                Guide = guide
            };

            var result = qualityEngine.Evaluate(input, settings);
            result.SourceKind = source.Kind;
            result.SequenceTitle = source.SequenceTitle;
            result.QsmControlled = source.QsmControlled;
            result.FileActionEligible = source.FileActionEligible;
            result.ProvenanceFrozen = source.ProvenanceFrozen;
            result.QsmControlToken = source.ControlToken;

            var prediction = predictiveEngine.Evaluate(sessionStore.Results, result, settings);
            result.PredictiveWarning = prediction.Warning;
            result.PredictiveConfidence = prediction.Confidence;
            result.PredictiveChannel = prediction.Channel;
            result.PredictiveMessage = prediction.Message;
            result.PredictiveFramesToThreshold = prediction.EstimatedFramesToThreshold;

            var environment = environmentalService.Capture(weatherDataMediator, result, settings);
            result.EnvironmentAvailable = environment.Available;
            result.CloudCover = environment.CloudCover;
            result.Humidity = environment.Humidity;
            result.WindSpeed = environment.WindSpeed;
            result.WindGust = environment.WindGust;
            result.SkyQuality = environment.SkyQuality;
            result.AmbientTemperature = environment.Temperature;
            result.DewPoint = environment.DewPoint;
            result.EnvironmentalHint = environment.CorrelationHint;

            if (result.Status is FrameStatus.Learning or FrameStatus.Accepted) {
                baseline.AddAccepted(key, input.StarCount, input.BackgroundMedian, settings.BaselineWindow);
            }

            if (result.Status == FrameStatus.Rejected && !settings.MonitorOnly) {
                try {
                    result.FinalPath = await rejectedFileService.ApplyAsync(result.OriginalPath, settings, result.FileActionEligible);
                } catch (Exception ex) {
                    result.ErrorMessage = $"Rejected-file action failed: {ex.Message}";
                    Logger.Warning($"QualitySessionMeter rejected-file action failed: {ex.Message}");
                }
            }

            await sessionStore.AppendAsync(result);
            UpdateCalibrationSuggestion();
            FrameProcessed?.Invoke(this, result);
        } catch (Exception ex) {
            Logger.Error(ex);
            var result = new FrameQualityResult {
                FrameIndex = assignedFrameIndex,
                TimestampUtc = frameTimestampUtc,
                OriginalPath = e.PathToImage?.IsFile == true ? e.PathToImage.LocalPath : e.PathToImage?.ToString(),
                FinalPath = e.PathToImage?.IsFile == true ? e.PathToImage.LocalPath : e.PathToImage?.ToString(),
                SourceKind = source.Kind,
                SequenceTitle = source.SequenceTitle,
                QsmControlled = source.QsmControlled,
                FileActionEligible = source.FileActionEligible,
                ProvenanceFrozen = source.ProvenanceFrozen,
                QsmControlToken = source.ControlToken,
                Status = FrameStatus.Error,
                OverallQuality = 0,
                ErrorMessage = ex.Message,
                ProbableCause = "ANALYSIS ERROR",
                MonitorOnly = settings.MonitorOnly
            };
            try { await sessionStore.AppendAsync(result); } catch { }
            FrameProcessed?.Invoke(this, result);
        } finally {
            processingLock.Release();
        }
    }

    private void UpdateCalibrationSuggestion() {
        if (settings.AdaptiveThresholdMode == AdaptiveThresholdMode.Off) {
            if (currentCalibrationSuggestion.Available) {
                currentCalibrationSuggestion = new CalibrationSuggestion();
                CalibrationSuggestionChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        var suggestion = calibrationEngine.Evaluate(sessionStore.Results, settings);
        if (suggestion.Available && ignoredCalibrationContexts.Contains(suggestion.Context)) suggestion = new CalibrationSuggestion();
        currentCalibrationSuggestion = suggestion;
        CalibrationSuggestionChanged?.Invoke(this, EventArgs.Empty);

        if (settings.AdaptiveThresholdMode == AdaptiveThresholdMode.Automatic &&
            suggestion.Available &&
            !appliedCalibrationContexts.Contains(suggestion.Context)) {
            ApplySuggestion(suggestion);
            appliedCalibrationContexts.Add(suggestion.Context);
            Logger.Info($"QualitySessionMeter V3 automatically applied bounded calibration for {suggestion.Context}.");
        }
    }

#if QSM_DEVELOPMENT
    public Task RunCanonicalSyntheticSessionAsync(int frameDelayMs = 150) =>
        RunSyntheticSessionAsync(SyntheticSessionGenerator.CanonicalScenarioId, frameDelayMs);

    public async Task RunSyntheticSessionAsync(string scenarioId, int frameDelayMs = 150) {
        if (disposed || IsSyntheticRunning) return;

        var scenario = SyntheticSessionGenerator.GetScenario(scenarioId);

        await processingLock.WaitAsync();
        try {
            syntheticCts?.Cancel();
            syntheticCts?.Dispose();
            syntheticCts = new CancellationTokenSource();
            syntheticBaseline = new BaselineEngine();
            syntheticSettings = SyntheticSessionGenerator.CreateCanonicalSettings();

            var syntheticRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA",
                "QualitySessionMeter",
                "SyntheticSessions",
                Sanitize(scenario.Id));
            syntheticSessionStore = new SessionStore(syntheticRoot);

            syntheticProcessed = 0;
            syntheticFailures = 0;
            syntheticFailureSummary = "";
            syntheticLastScenario = "";
            syntheticSessionName = scenario.DisplayName;
            syntheticStatus = "RUNNING";
            IsSyntheticMode = true;
            IsSyntheticRunning = true;
            syntheticTotal = scenario.Frames.Count;
            RaiseSyntheticStateChanged();
        } finally {
            processingLock.Release();
        }

        var definitions = scenario.Frames;
        var failures = new List<string>();
        var baseTime = new DateTime(2026, 9, 7, 20, 0, 0, DateTimeKind.Utc);
        var token = syntheticCts.Token;

        try {
            for (int i = 0; i < definitions.Count; i++) {
                token.ThrowIfCancellationRequested();
                var definition = definitions[i];
                syntheticLastScenario = definition.Name;

                var start = baseTime.AddMinutes(i * 4);
                var key = new BaselineKey(
                    definition.Target,
                    definition.Filter,
                    definition.ExposureSeconds,
                    100,
                    1,
                    1,
                    "SyntheticCam");

                var snapshot = syntheticBaseline.GetSnapshot(key, syntheticSettings.MinimumLearningFrames);
                var guideSamples = definition.GuideFactory?.Invoke(start, definition.ExposureSeconds)
                    ?? Array.Empty<GuideSample>();
                var guide = GuideMetricsCalculator.Calculate(guideSamples, start, definition.ExposureSeconds, syntheticSettings.ExcursionThreshold);

                var input = new FrameQualityInput {
                    FrameIndex = i + 1,
                    TimestampUtc = start,
                    OriginalPath = $"SYNTHETIC://{scenario.Id}/frame_{i + 1:000}_{Sanitize(definition.Name)}.fits",
                    Target = definition.Target,
                    Filter = definition.Filter,
                    ExposureSeconds = definition.ExposureSeconds,
                    Gain = 100,
                    BinX = 1,
                    BinY = 1,
                    Camera = "SyntheticCam",
                    StarCount = definition.StarCount,
                    BackgroundMedian = definition.BackgroundMedian,
                    Baseline = snapshot,
                    Guide = guide
                };

                var result = qualityEngine.Evaluate(input, syntheticSettings);
                result.MonitorOnly = true;

                if (result.Status is FrameStatus.Learning or FrameStatus.Accepted) {
                    syntheticBaseline.AddAccepted(key, input.StarCount, input.BackgroundMedian, syntheticSettings.BaselineWindow);
                }

                string mismatch = ValidateSyntheticResult(definition, result);
                if (!string.IsNullOrWhiteSpace(mismatch)) {
                    failures.Add($"Frame {i + 1} — {definition.Name}: {mismatch}");
                    syntheticFailures++;
                }

                await syntheticSessionStore.AppendAsync(result);
                syntheticProcessed++;
                FrameProcessed?.Invoke(this, result);
                RaiseSyntheticStateChanged();

                if (frameDelayMs > 0) await Task.Delay(Math.Clamp(frameDelayMs, 0, 5000), token);
            }

            syntheticFailureSummary = failures.Count == 0
                ? $"All {scenario.DisplayName} frames matched the expected result."
                : string.Join(Environment.NewLine, failures.Take(8)) + (failures.Count > 8 ? Environment.NewLine + "…" : "");
            syntheticStatus = failures.Count == 0
                ? $"PASS {syntheticProcessed}/{syntheticTotal}"
                : $"FAIL {syntheticFailures} of {syntheticTotal}";

            await WriteSyntheticVerdictAsync(scenario, failures);
        } catch (OperationCanceledException) {
            syntheticStatus = $"STOPPED {syntheticProcessed}/{syntheticTotal}";
            syntheticFailureSummary = "Synthetic session stopped by user.";
        } catch (Exception ex) {
            syntheticStatus = "LAB ERROR";
            syntheticFailureSummary = ex.Message;
            Logger.Error(ex);
        } finally {
            IsSyntheticRunning = false;
            RaiseSyntheticStateChanged();
        }
    }

    public void StopSyntheticSession() => syntheticCts?.Cancel();

    public void ExitSyntheticMode() {
        syntheticCts?.Cancel();
        IsSyntheticRunning = false;
        IsSyntheticMode = false;
        syntheticStatus = "OFF";
        syntheticLastScenario = "";
        syntheticFailureSummary = "";
        syntheticSessionName = "";
        RaiseSyntheticStateChanged();
    }

    private async Task WriteSyntheticVerdictAsync(SyntheticSessionDefinition scenario, IReadOnlyList<string> failures) {
        if (syntheticSessionStore == null || string.IsNullOrWhiteSpace(syntheticSessionStore.SessionFolder)) return;

        var sb = new StringBuilder();
        sb.AppendLine("# QualitySessionMeter Synthetic Lab verdict");
        sb.AppendLine();
        sb.AppendLine($"Scenario: {scenario.DisplayName} ({scenario.Id})");
        sb.AppendLine(scenario.Description);
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "VERDICT: PASS" : "VERDICT: FAIL");
        sb.AppendLine($"Frames: {syntheticProcessed}/{syntheticTotal}");
        sb.AppendLine($"Mismatches: {failures.Count}");
        sb.AppendLine($"Expected profile: {scenario.ExpectedSummary}");
        sb.AppendLine();
        sb.AppendLine("This session was generated entirely inside QualitySessionMeter. No camera or real image file was used.");
        if (failures.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Failures:");
            foreach (var failure in failures) sb.AppendLine("- " + failure);
        }

        await File.WriteAllTextAsync(Path.Combine(syntheticSessionStore.SessionFolder, "SYNTHETIC_VERDICT.md"), sb.ToString());
    }

    private static string ValidateSyntheticResult(SyntheticFrameDefinition expected, FrameQualityResult actual) {
        var problems = new List<string>();
        if (actual.Status != expected.ExpectedStatus) problems.Add($"expected {expected.ExpectedStatus}, got {actual.Status}");

        var expectedReasons = expected.ExpectedReasons ?? Array.Empty<string>();
        foreach (var reason in expectedReasons) {
            if (!actual.RejectReasons.Contains(reason, StringComparer.Ordinal)) problems.Add($"missing reason {reason}");
        }

        if (expected.ExpectedStatus == FrameStatus.Rejected) {
            var extra = actual.RejectReasons.Where(x => !expectedReasons.Contains(x, StringComparer.Ordinal)).ToArray();
            if (extra.Length > 0) problems.Add("unexpected reason(s): " + string.Join(", ", extra));
        }

        if (!string.IsNullOrWhiteSpace(expected.ExpectedErrorContains) &&
            (actual.ErrorMessage?.Contains(expected.ExpectedErrorContains, StringComparison.Ordinal) != true)) {
            problems.Add($"expected error containing {expected.ExpectedErrorContains}, got '{actual.ErrorMessage}'");
        }

        if (expected.ExpectedStarBaseline.HasValue &&
            (double.IsNaN(actual.StarBaseline) || Math.Abs(actual.StarBaseline - expected.ExpectedStarBaseline.Value) > 0.001)) {
            problems.Add($"star baseline expected {expected.ExpectedStarBaseline.Value:0.###}, got {actual.StarBaseline:0.###}");
        }

        if (expected.ExpectedBackgroundBaseline.HasValue &&
            (double.IsNaN(actual.BackgroundBaseline) || Math.Abs(actual.BackgroundBaseline - expected.ExpectedBackgroundBaseline.Value) > 0.001)) {
            problems.Add($"background baseline expected {expected.ExpectedBackgroundBaseline.Value:0.###}, got {actual.BackgroundBaseline:0.###}");
        }

        return string.Join("; ", problems);
    }

    private static string Sanitize(string value) {
        if (string.IsNullOrWhiteSpace(value)) return "scenario";
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }

    private void RaiseSyntheticStateChanged() => SyntheticStateChanged?.Invoke(this, EventArgs.Empty);
#endif

    public void ResetSession() {
        processingLock.Wait();
        try {
            baseline.Clear();
            sessionStore.Reset();
            Interlocked.Exchange(ref frameIndex, 0);
            appliedCalibrationContexts.Clear();
            ignoredCalibrationContexts.Clear();
            currentCalibrationSuggestion = new CalibrationSuggestion();
            lock (sourceSync) pendingSources.Clear();
            lock (controlSync) {
                controlTokens.Clear();
                reservedControlTokens.Clear();
            }
        } finally {
            processingLock.Release();
        }
        CalibrationSuggestionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static DateTime NormalizeUtc(DateTime value) {
        if (value == DateTime.MinValue) return value;
        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

    private sealed record PendingFrameSource(
        int ImageId,
        int ExposureNumber,
        DateTime ExposureStartUtc,
        FrameSourceInfo Source,
        DateTime CapturedUtc) {

        public bool Matches(int imageId, int exposureNumber, DateTime exposureStartUtc) {
            if (ImageId >= 0 && imageId >= 0) return ImageId == imageId;
            if (ExposureNumber >= 0 && exposureNumber >= 0 && ExposureNumber != exposureNumber) return false;
            if (ExposureStartUtc == DateTime.MinValue || exposureStartUtc == DateTime.MinValue) return ExposureNumber == exposureNumber;
            return Math.Abs((ExposureStartUtc - exposureStartUtc).TotalSeconds) <= 1.0;
        }
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
#if QSM_DEVELOPMENT
        syntheticCts?.Cancel();
        syntheticCts?.Dispose();
#endif
        imageSaveMediator.BeforeFinalizeImageSaved -= BeforeFinalizeImageSaved;
        imageSaveMediator.ImageSaved -= ImageSaved;
        guideCollector.Dispose();
        lock (controlSync) {
            controlTokens.Clear();
            reservedControlTokens.Clear();
        }
        lock (sourceSync) pendingSources.Clear();
        processingLock.Dispose();
    }
}

public static class QualitySessionRuntimeRegistry {
    private static readonly object Sync = new();
    private static QualitySessionRuntime instance;

    public static QualitySessionRuntime GetOrCreate(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        ISequenceMediator sequenceMediator,
        QualitySettings settings) {
        lock (Sync) {
            instance ??= new QualitySessionRuntime(profileService, imageSaveMediator, guiderMediator, sequenceMediator, settings);
            return instance;
        }
    }

    public static QualitySessionRuntime Current { get { lock (Sync) return instance; } }

    public static void DisposeCurrent() {
        lock (Sync) {
            instance?.Dispose();
            instance = null;
        }
    }
}
