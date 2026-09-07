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

/// <summary>
/// Repeats the parent Advanced Sequencer container until the requested number of QSM-valid LIGHT
/// frames has been reached. QSM's FrameQualityResult is the only source of truth; filesystem actions
/// and N.I.N.A.'s built-in Take Exposure iteration counter are deliberately not modified.
///
/// Field-test contract: the parent container should produce exactly one LIGHT per iteration.
/// The condition waits at end-of-block for QSM to finish classifying that LIGHT before deciding
/// whether another iteration is required.
/// </summary>
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

    // Runtime-only watermark. Captured at the start of each parent block iteration.
    private int blockStartFrameIndex;
    private bool blockResultConsumed;

    [ImportingConstructor]
    public QsmValidFrameTargetCondition() {
    }

    [JsonProperty]
    public int TargetValidFrames {
        get => targetValidFrames;
        set {
            targetValidFrames = Math.Max(1, value);
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ProgressText));
        }
    }

    [JsonProperty]
    public bool CountWarningsAsValid {
        get => countWarningsAsValid;
        set {
            countWarningsAsValid = value;
            RaisePropertyChanged();
        }
    }

    [JsonProperty]
    public int ClassificationTimeoutSeconds {
        get => classificationTimeoutSeconds;
        set {
            classificationTimeoutSeconds = Math.Clamp(value, 5, 120);
            RaisePropertyChanged();
        }
    }

    [JsonProperty]
    public int ValidFrames {
        get => validFrames;
        private set {
            validFrames = Math.Max(0, value);
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ProgressText));
            RaisePropertyChanged(nameof(IsComplete));
        }
    }

    [JsonProperty]
    public int CapturedFrames {
        get => capturedFrames;
        private set { capturedFrames = Math.Max(0, value); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int RejectedFrames {
        get => rejectedFrames;
        private set { rejectedFrames = Math.Max(0, value); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int WarningFrames {
        get => warningFrames;
        private set { warningFrames = Math.Max(0, value); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int LearningFrames {
        get => learningFrames;
        private set { learningFrames = Math.Max(0, value); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int ErrorFrames {
        get => errorFrames;
        private set { errorFrames = Math.Max(0, value); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public int LastAccountedFrameIndex {
        get => lastAccountedFrameIndex;
        private set { lastAccountedFrameIndex = Math.Max(0, value); RaisePropertyChanged(); }
    }

    [JsonProperty]
    public string LastStatus {
        get => lastStatus;
        private set { lastStatus = value ?? ""; RaisePropertyChanged(); }
    }

    [JsonProperty]
    public string LastCause {
        get => lastCause;
        private set { lastCause = value ?? ""; RaisePropertyChanged(); }
    }

    [JsonProperty]
    public bool RuntimeFault {
        get => runtimeFault;
        private set { runtimeFault = value; RaisePropertyChanged(); }
    }

    [JsonProperty]
    public string RuntimeFaultText {
        get => runtimeFaultText;
        private set { runtimeFaultText = value ?? ""; RaisePropertyChanged(); }
    }

    public string ProgressText => $"{ValidFrames} / {TargetValidFrames} valid";
    public string BreakdownText => $"{CapturedFrames} captured · {RejectedFrames} rejected · {WarningFrames} warning · {LearningFrames} learning · {ErrorFrames} error";
    public bool IsComplete => ValidFrames >= TargetValidFrames;
    public IList<string> Issues { get; private set; } = new List<string>();

    public override void SequenceBlockStarted() {
        base.SequenceBlockStarted();
        var runtime = QualitySessionRuntimeRegistry.Current;
        blockStartFrameIndex = runtime?.Store?.Results?.LastOrDefault()?.FrameIndex ?? LastAccountedFrameIndex;
        blockResultConsumed = false;
    }

    public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
        if (RuntimeFault) return false;
        if (IsComplete) return false;

        // SequentialStrategy calls conditions before/between child items too. Only account the QSM
        // result after the final item of one parent-container iteration has completed.
        if (previousItem != null && nextItem == null && !blockResultConsumed) {
            blockResultConsumed = true;
            var result = WaitForNextLiveResult(blockStartFrameIndex);
            if (result == null) {
                RuntimeFault = true;
                RuntimeFaultText = $"No QSM classification arrived within {ClassificationTimeoutSeconds}s. Quality-controlled loop stopped fail-safe.";
                LastStatus = "QSM TIMEOUT";
                LastCause = RuntimeFaultText;
                Logger.Error($"QualitySessionMeter V3: {RuntimeFaultText}");
                return false;
            }

            Account(result);
        }

        return !RuntimeFault && !IsComplete;
    }

    private FrameQualityResult WaitForNextLiveResult(int afterFrameIndex) {
        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null || runtime.IsSyntheticMode) {
            RuntimeFault = true;
            RuntimeFaultText = runtime == null
                ? "QualitySessionMeter runtime is unavailable."
                : "Synthetic Lab is active; V3 sequence control is disabled.";
            return null;
        }

        var existing = runtime.Store.Results
            .Where(r => r.FrameIndex > Math.Max(afterFrameIndex, LastAccountedFrameIndex))
            .OrderBy(r => r.FrameIndex)
            .FirstOrDefault();
        if (existing != null) return existing;

        var tcs = new TaskCompletionSource<FrameQualityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<FrameQualityResult> handler = null;
        handler = (_, result) => {
            if (result == null || runtime.IsSyntheticMode) return;
            if (result.FrameIndex <= Math.Max(afterFrameIndex, LastAccountedFrameIndex)) return;
            tcs.TrySetResult(result);
        };

        runtime.FrameProcessed += handler;
        try {
            // Re-check after subscribing to close the race between the first store check and event hook.
            existing = runtime.Store.Results
                .Where(r => r.FrameIndex > Math.Max(afterFrameIndex, LastAccountedFrameIndex))
                .OrderBy(r => r.FrameIndex)
                .FirstOrDefault();
            if (existing != null) return existing;

            try {
                return tcs.Task
                    .WaitAsync(TimeSpan.FromSeconds(ClassificationTimeoutSeconds))
                    .GetAwaiter()
                    .GetResult();
            } catch (TimeoutException) {
                return null;
            }
        } finally {
            runtime.FrameProcessed -= handler;
        }
    }

    private void Account(FrameQualityResult result) {
        if (result == null || result.FrameIndex <= LastAccountedFrameIndex) return;

        LastAccountedFrameIndex = result.FrameIndex;
        CapturedFrames++;
        LastStatus = result.StatusText;
        LastCause = result.ProbableCause ?? "";

        switch (result.Status) {
            case FrameStatus.Accepted:
                ValidFrames++;
                break;
            case FrameStatus.Warning:
                WarningFrames++;
                if (CountWarningsAsValid) ValidFrames++;
                break;
            case FrameStatus.Rejected:
                RejectedFrames++;
                break;
            case FrameStatus.Learning:
                LearningFrames++;
                break;
            case FrameStatus.Error:
                ErrorFrames++;
                break;
        }

        RaisePropertyChanged(nameof(BreakdownText));
        Logger.Info($"QualitySessionMeter V3 valid-frame progress: {ProgressText}; {BreakdownText}; last={LastStatus} {LastCause}");
    }

    public override void ResetProgress() {
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
        if (QualitySessionRuntimeRegistry.Current == null) issues.Add("QualitySessionMeter runtime is not initialized.");
        Issues = issues;
        RaisePropertyChanged(nameof(Issues));
        return issues.Count == 0;
    }

    public override object Clone() {
        var clone = new QsmValidFrameTargetCondition {
            Icon = Icon,
            Name = Name,
            Category = Category,
            Description = Description,
            TargetValidFrames = TargetValidFrames,
            CountWarningsAsValid = CountWarningsAsValid,
            ClassificationTimeoutSeconds = ClassificationTimeoutSeconds
        };
        return clone;
    }

    public override string ToString() =>
        $"QSM Valid Frame Target: {ValidFrames}/{TargetValidFrames} valid, {CapturedFrames} captured, {RejectedFrames} rejected";
}
