using Newtonsoft.Json;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Sequencer;

/// <summary>
/// Optional V3 recovery gate. Place it after Take Exposure inside a QSM-controlled loop.
/// It never interrupts an active shutter: it can only delay the next iteration after QSM has
/// classified the current frame. Disabled by default in QSM settings.
/// </summary>
[ExportMetadata("Name", "QSM Smart Recovery Gate")]
[ExportMetadata("Description", "After persistent QSM rejects, wait between exposures and require healthy guiding before allowing the next probe frame.")]
[ExportMetadata("Icon", "QSM_RecoverySVG")]
[ExportMetadata("Category", "QualitySessionMeter")]
[Export(typeof(ISequenceItem))]
[JsonObject(MemberSerialization.OptIn)]
public sealed class QsmSmartRecoveryGate : SequenceItem {
    private readonly IGuiderMediator guiderMediator;
    private int blockStartFrameIndex;
    private string lastAction = "IDLE";
    private string lastDetail = "Smart recovery is controlled by QSM settings.";

    [ImportingConstructor]
    public QsmSmartRecoveryGate(IGuiderMediator guiderMediator) {
        this.guiderMediator = guiderMediator;
    }

    private QsmSmartRecoveryGate(QsmSmartRecoveryGate copyMe) : this(copyMe.guiderMediator) {
        CopyMetaData(copyMe);
    }

    public string LastAction {
        get => lastAction;
        private set { lastAction = value ?? ""; RaisePropertyChanged(); }
    }

    public string LastDetail {
        get => lastDetail;
        private set { lastDetail = value ?? ""; RaisePropertyChanged(); }
    }

    public override void SequenceBlockStarted() {
        base.SequenceBlockStarted();
        var runtime = QualitySessionRuntimeRegistry.Current;
        blockStartFrameIndex = runtime?.Store?.Results?.LastOrDefault()?.FrameIndex ?? 0;
        LastAction = "READY";
        LastDetail = runtime?.Settings?.SmartPauseEnabled == true
            ? "Watching current frame for persistent degradation."
            : "Disabled in QSM settings — pass-through.";
    }

    public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null || !runtime.Settings.Enabled || !runtime.Settings.SmartPauseEnabled || runtime.IsSyntheticMode) {
            LastAction = "PASS";
            return;
        }

        var result = await WaitForControlledResultAsync(runtime, blockStartFrameIndex, 30, token);
        if (result == null) {
            LastAction = "NO RESULT";
            LastDetail = "No controlled QSM result arrived; Valid Frame Target remains responsible for fail-safe loop termination.";
            return;
        }

        if (!runtime.HasPersistentControlledDegradation()) {
            LastAction = "PASS";
            LastDetail = $"{result.StatusText}; persistent reject streak not reached.";
            return;
        }

        int cooldown = runtime.Settings.SmartPauseSeconds;
        LastAction = "RECOVERY WAIT";
        LastDetail = $"Persistent degradation detected. Waiting {cooldown}s before another exposure.";
        progress?.Report(new ApplicationStatus { Source = "QualitySessionMeter", Status = LastDetail });
        await Task.Delay(TimeSpan.FromSeconds(cooldown), token);

        if (!(runtime.Settings.EnableGuideRms || runtime.Settings.EnableSustainedExcursion || runtime.Settings.EnableHardExcursion)) {
            LastAction = "PROBE READY";
            LastDetail = "Cooldown complete; guiding gates are disabled, so the next exposure is the recovery probe.";
            progress?.Report(new ApplicationStatus { Source = "QualitySessionMeter", Status = "" });
            return;
        }

        int required = runtime.Settings.SmartResumeHealthyGuideChecks;
        LastAction = "WAITING FOR GUIDE";
        LastDetail = $"Cooldown complete; waiting for {required} consecutive healthy guide samples.";
        progress?.Report(new ApplicationStatus { Source = "QualitySessionMeter", Status = LastDetail });

        bool recovered = await WaitForHealthyGuideAsync(runtime, required, token);
        LastAction = recovered ? "RECOVERED" : "PROBE READY";
        LastDetail = recovered
            ? "Guiding recovered; next exposure may proceed."
            : "No reliable guide recovery signal arrived before the safety timeout; allowing one probe exposure instead of waiting indefinitely.";
        progress?.Report(new ApplicationStatus { Source = "QualitySessionMeter", Status = "" });
    }

    private async Task<bool> WaitForHealthyGuideAsync(QualitySessionRuntime runtime, int required, CancellationToken token) {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int healthy = 0;
        double scale = double.NaN;
        try { scale = guiderMediator.GetInfo()?.PixelScale ?? double.NaN; } catch { }
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) return false;

        double sampleLimit = Math.Min(runtime.Settings.MaxGuideRms, runtime.Settings.ExcursionThreshold * 0.80);
        sampleLimit = Math.Max(0.30, sampleLimit);

        void Handler(object sender, IGuideStep step) {
            if (step == null) return;
            double error = Math.Sqrt(step.RADistanceRaw * step.RADistanceRaw + step.DECDistanceRaw * step.DECDistanceRaw) * scale;
            if (error <= sampleLimit) healthy++; else healthy = 0;
            if (healthy >= required) tcs.TrySetResult(true);
        }

        guiderMediator.GuideEvent += Handler;
        try {
            var maxWait = TimeSpan.FromSeconds(Math.Clamp(runtime.Settings.SmartPauseSeconds, 30, 300));
            try {
                return await tcs.Task.WaitAsync(maxWait, token);
            } catch (TimeoutException) {
                return false;
            }
        } finally {
            guiderMediator.GuideEvent -= Handler;
        }
    }

    private static async Task<FrameQualityResult> WaitForControlledResultAsync(
        QualitySessionRuntime runtime,
        int afterFrameIndex,
        int timeoutSeconds,
        CancellationToken token) {

        var existing = runtime.Store.Results
            .Where(r => r.FrameIndex > afterFrameIndex && r.QsmControlled)
            .OrderBy(r => r.FrameIndex)
            .FirstOrDefault();
        if (existing != null) return existing;

        var tcs = new TaskCompletionSource<FrameQualityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<FrameQualityResult> handler = null;
        handler = (_, result) => {
            if (result?.QsmControlled == true && result.FrameIndex > afterFrameIndex) tcs.TrySetResult(result);
        };
        runtime.FrameProcessed += handler;
        try {
            existing = runtime.Store.Results
                .Where(r => r.FrameIndex > afterFrameIndex && r.QsmControlled)
                .OrderBy(r => r.FrameIndex)
                .FirstOrDefault();
            if (existing != null) return existing;
            try {
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), token);
            } catch (TimeoutException) {
                return null;
            }
        } finally {
            runtime.FrameProcessed -= handler;
        }
    }

    public override object Clone() => new QsmSmartRecoveryGate(this) {
        Icon = Icon,
        Name = Name,
        Category = Category,
        Description = Description
    };

    public override string ToString() => $"QSM Smart Recovery Gate: {LastAction}";
}
