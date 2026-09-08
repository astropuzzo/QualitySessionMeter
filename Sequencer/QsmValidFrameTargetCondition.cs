using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Sequencer;

[ExportMetadata("Name", "QSM Valid Frame Target")]
[ExportMetadata("Description", "Repeat this container until QualitySessionMeter has classified the requested number of valid LIGHT frames.")]
[ExportMetadata("Icon", "QSM_ValidTargetSVG")]
[ExportMetadata("Category", "QualitySessionMeter")]
[Export(typeof(ISequenceCondition))]
[JsonObject(MemberSerialization.OptIn)]
public sealed class QsmValidFrameTargetCondition : SequenceCondition, IValidatable {
    private const int DefaultClassificationTimeoutSeconds = 20;

    private int targetValidFrames = 10;
    private bool countWarningsAsValid = true;
    private int classificationTimeoutSeconds = DefaultClassificationTimeoutSeconds;
    private int validFrames;
    private int capturedFrames;
    private int rejectedFrames;
    private int warningFrames;
    private int learningFrames;
    private int errorFrames;
    private int lastAccountedFrameIndex;
    private string lastStatus = "WAITING";
    private string lastCause = "";
    private bool runtimeFault;
    private string runtimeFaultText = "";
    private int blockStartFrameIndex;
    private bool blockResultConsumed;
    private string controlToken = "";

    [ImportingConstructor]
    public QsmValidFrameTargetCondition() {
    }

    [JsonProperty]
    public int TargetValidFrames {
        get => targetValidFrames;
        set { targetValidFrames = Math.Max(1, value); RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressText)); }
    }

    [JsonProperty]
    public bool CountWarningsAsValid {
        get => countWarningsAsValid;
        set { countWarningsAsValid = value; RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int ClassificationTimeoutSeconds {
        get => classificationTimeoutSeconds;
        set { classificationTimeoutSeconds = Math.Clamp(value, 5, 120); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int ValidFrames { get => validFrames; private set { validFrames = Math.Max(0, value); RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressText)); RaisePropertyChanged(nameof(IsComplete)); } }
    [JsonProperty]
    public int CapturedFrames { get => capturedFrames; private set { capturedFrames = Math.Max(0, value); RaisePropertyChanged(); } }
    [JsonProperty]
    public int RejectedFrames { get => rejectedFrames; private set { rejectedFrames = Math.Max(0, value); RaisePropertyChanged(); } }
    [JsonProperty]
    public int WarningFrames { get => warningFrames; private set { warningFrames = Math.Max(0, value); RaisePropertyChanged(); } }
    [JsonProperty]
    public int LearningFrames { get => learningFrames; private set { learningFrames = Math.Max(0, value); RaisePropertyChanged(); } }
    [JsonProperty]
    public int ErrorFrames { get => errorFrames; private set { errorFrames = Math.Max(0, value); RaisePropertyChanged(); } }
    [JsonProperty]
    public int LastAccountedFrameIndex { get => lastAccountedFrameIndex; private set { lastAccountedFrameIndex = Math.Max(0, value); RaisePropertyChanged(); } }
    [JsonProperty]
    public string LastStatus { get => lastStatus; private set { lastStatus = value ?? ""; RaisePropertyChanged(); } }
    [JsonProperty]
    public string LastCause { get => lastCause; private set { lastCause = value ?? ""; RaisePropertyChanged(); } }
    [JsonProperty]
    public bool RuntimeFault { get => runtimeFault; private set { runtimeFault = value; RaisePropertyChanged(); } }
    [JsonProperty]
    public string RuntimeFaultText { get => runtimeFaultText; private set { runtimeFaultText = value ?? ""; RaisePropertyChanged(); } }

    public string ProgressText => $"{ValidFrames} / {TargetValidFrames} valid";
    public string BreakdownText => $"{CapturedFrames} captured · {RejectedFrames} rejected · {WarningFrames} warning · {LearningFrames} learning · {ErrorFrames} error";
    public bool IsComplete => ValidFrames >= TargetValidFrames;
    public IList<string> Issues { get; private set; } = new List<string>();

    public override void SequenceBlockStarted() {
        base.SequenceBlockStarted();
        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null || !runtime.Settings.Enabled || IsComplete || RuntimeFault) return;

        Disarm();
        controlToken = runtime.ArmSequencerControl();
        blockStartFrameIndex = runtime.Store?.Results?.LastOrDefault()?.FrameIndex ?? LastAccountedFrameIndex;
        blockResultConsumed = false;
    }

    public override void SequenceBlockTeardown() {
        Disarm();
        base.SequenceBlockTeardown();
    }

    public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
        if (RuntimeFault || IsComplete) { Disarm(); return false; }

        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null || !runtime.Settings.Enabled) {
            RuntimeFault = true;
            RuntimeFaultText = runtime == null ? "QualitySessionMeter runtime is unavailable." : "QualitySessionMeter is OFF.";
            LastStatus = "QSM OFF";
            LastCause = RuntimeFaultText;
            Disarm();
            return false;
        }

        if (previousItem != null && nextItem == null && !blockResultConsumed) {
            blockResultConsumed = true;
            var result = WaitForNextLiveResult(blockStartFrameIndex, controlToken);
            if (result == null) {
                RuntimeFault = true;
                RuntimeFaultText = $"No QSM classification arrived within {ClassificationTimeoutSeconds}s. Quality-controlled loop stopped fail-safe.";
                LastStatus = "QSM TIMEOUT";
                LastCause = RuntimeFaultText;
                Logger.Error($"QualitySessionMeter V3: {RuntimeFaultText}");
                Disarm();
                return false;
            }

            Account(result);
            Disarm();
        }

        return !RuntimeFault && !IsComplete;
    }

    private FrameQualityResult WaitForNextLiveResult(int afterFrameIndex, string expectedControlToken) {
        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null || runtime.IsSyntheticMode || !runtime.Settings.Enabled) return null;

        var existing = runtime.Store.Results
            .Where(r => r.FrameIndex > Math.Max(afterFrameIndex, LastAccountedFrameIndex) &&
                        r.QsmControlled && string.Equals(r.QsmControlToken, expectedControlToken, StringComparison.Ordinal))
            .OrderBy(r => r.FrameIndex)
            .FirstOrDefault();
        if (existing != null) return existing;

        var tcs = new TaskCompletionSource<FrameQualityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<FrameQualityResult> handler = null;
        handler = (_, result) => {
            if (result == null || runtime.IsSyntheticMode || !result.QsmControlled) return;
            if (!string.Equals(result.QsmControlToken, expectedControlToken, StringComparison.Ordinal)) return;
            if (result.FrameIndex <= Math.Max(afterFrameIndex, LastAccountedFrameIndex)) return;
            tcs.TrySetResult(result);
        };

        runtime.FrameProcessed += handler;
        try {
            existing = runtime.Store.Results
                .Where(r => r.FrameIndex > Math.Max(afterFrameIndex, LastAccountedFrameIndex) &&
                        r.QsmControlled && string.Equals(r.QsmControlToken, expectedControlToken, StringComparison.Ordinal))
                .OrderBy(r => r.FrameIndex)
                .FirstOrDefault();
            if (existing != null) return existing;

            try {
                return tcs.Task.WaitAsync(TimeSpan.FromSeconds(ClassificationTimeoutSeconds)).GetAwaiter().GetResult();
            } catch (TimeoutException) {
                return null;
            }
        } finally {
            runtime.FrameProcessed -= handler;
        }
    }

    private void Account(FrameQualityResult result) {
        var current = new ValidFrameProgressTracker.Snapshot(
            ValidFrames, CapturedFrames, RejectedFrames, WarningFrames, LearningFrames, ErrorFrames, LastAccountedFrameIndex);
        var next = ValidFrameProgressTracker.Account(current, result, CountWarningsAsValid);
        if (next.LastFrameIndex == current.LastFrameIndex) return;

        ValidFrames = next.Valid;
        CapturedFrames = next.Captured;
        RejectedFrames = next.Rejected;
        WarningFrames = next.Warning;
        LearningFrames = next.Learning;
        ErrorFrames = next.Error;
        LastAccountedFrameIndex = next.LastFrameIndex;
        LastStatus = result.StatusText;
        LastCause = result.ProbableCause ?? "";
        RaisePropertyChanged(nameof(BreakdownText));
        Logger.Info($"QualitySessionMeter V3 valid-frame progress: {ProgressText}; {BreakdownText}; last={LastStatus} {LastCause}");
    }

    private void Disarm() {
        if (string.IsNullOrWhiteSpace(controlToken)) return;
        QualitySessionRuntimeRegistry.Current?.DisarmSequencerControl(controlToken);
        controlToken = "";
    }

    public override void ResetProgress() {
        Disarm();
        ValidFrames = 0;
        CapturedFrames = 0;
        RejectedFrames = 0;
        WarningFrames = 0;
        LearningFrames = 0;
        ErrorFrames = 0;
        LastAccountedFrameIndex = 0;
        LastStatus = "WAITING";
        LastCause = "";
        RuntimeFault = false;
        RuntimeFaultText = "";
        blockStartFrameIndex = 0;
        blockResultConsumed = false;
        Status = SequenceEntityStatus.CREATED;
        RaisePropertyChanged(nameof(BreakdownText));
    }

    public bool Validate() {
        var issues = new List<string>();
        if (TargetValidFrames < 1) issues.Add("QSM Valid Frame Target must be at least 1.");
        if (ClassificationTimeoutSeconds < 5) issues.Add("QSM classification timeout must be at least 5 seconds.");
        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null) issues.Add("QualitySessionMeter runtime is not initialized.");
        else if (!runtime.Settings.Enabled) issues.Add("QualitySessionMeter is OFF. Enable it before running a QSM Valid Frame Target.");
        Issues = issues;
        RaisePropertyChanged(nameof(Issues));
        return issues.Count == 0;
    }

    public override object Clone() => new QsmValidFrameTargetCondition {
        Icon = Icon,
        Name = Name,
        Category = Category,
        Description = Description,
        TargetValidFrames = TargetValidFrames,
        CountWarningsAsValid = CountWarningsAsValid,
        ClassificationTimeoutSeconds = ClassificationTimeoutSeconds
    };

    public override string ToString() => $"QSM Valid Frame Target: {ValidFrames}/{TargetValidFrames} valid, {CapturedFrames} captured, {RejectedFrames} rejected";
}
