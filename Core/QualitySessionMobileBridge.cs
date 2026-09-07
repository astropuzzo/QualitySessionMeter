using NINA.Plugin.Interfaces;
using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Read-only, assembly-decoupled integration contract for companion plugins such as Touch 'n' Stars.
/// Communication stays inside N.I.N.A. through IMessageBroker; QSM does not open another network port.
/// A companion/server plugin may expose the returned plain Dictionary/List payload through its own API.
/// </summary>
public sealed class QualitySessionMobileBridge : ISubscriber, IDisposable {
    public const int ContractVersion = 1;
    public const string RequestSnapshotTopic = "QualitySessionMeter.ApiV1.RequestSnapshot";
    public const string SnapshotTopic = "QualitySessionMeter.ApiV1.Snapshot";

    private const int DefaultRecentFrames = 160;
    private readonly IMessageBroker broker;
    private bool disposed;

    public QualitySessionMobileBridge(IMessageBroker broker) {
        this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
        broker.Subscribe(RequestSnapshotTopic, this);
    }

    public async Task OnMessageReceived(IMessage message) {
        if (disposed || message == null || !string.Equals(message.Topic, RequestSnapshotTopic, StringComparison.Ordinal)) return;

        // Keep V1 read-only and deterministic. A future write/control contract must be explicitly versioned
        // and safety-reviewed instead of piggy-backing commands onto this snapshot request.
        var snapshot = BuildSnapshot();
        var response = new QsmBrokerMessage(
            SnapshotTopic,
            snapshot,
            message.MessageId,
            new Dictionary<string, object> {
                ["contract"] = "quality-session-meter/v1",
                ["readOnly"] = true
            });

        await broker.Publish(response);
    }

    public static IDictionary<string, object> BuildSnapshot() {
        var runtime = QualitySessionRuntimeRegistry.Current;
        if (runtime == null) {
            return new Dictionary<string, object> {
                ["contractVersion"] = ContractVersion,
                ["available"] = false,
                ["readOnly"] = true,
                ["message"] = "QualitySessionMeter runtime is not initialized yet.",
                ["frames"] = new List<object>()
            };
        }

        var settings = runtime.Settings;
        var frames = runtime.Store?.Results?.TakeLast(DefaultRecentFrames).ToArray() ?? Array.Empty<FrameQualityResult>();
        var current = frames.LastOrDefault();

        int accepted = frames.Count(x => x.Status == FrameStatus.Accepted);
        int warnings = frames.Count(x => x.Status == FrameStatus.Warning);
        int rejected = frames.Count(x => x.Status == FrameStatus.Rejected);
        int learning = frames.Count(x => x.Status == FrameStatus.Learning);
        int errors = frames.Count(x => x.Status == FrameStatus.Error);
        int usable = accepted + warnings;
        double acceptanceRate = usable + rejected == 0 ? 0 : usable * 100.0 / (usable + rejected);
        double sessionQuality = frames.Where(x => x.IsUsable).Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
        double sessionConfidence = frames.Where(x => x.IsUsable && Finite(x.ConfidenceScore))
            .Select(x => x.ConfidenceScore).DefaultIfEmpty(0).Average();

        var result = new Dictionary<string, object> {
            ["contractVersion"] = ContractVersion,
            ["available"] = true,
            ["readOnly"] = true,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["mode"] = new Dictionary<string, object> {
                ["enabled"] = settings.Enabled,
                ["monitorOnly"] = settings.MonitorOnly,
                ["monitoringScope"] = settings.MonitoringScope.ToString(),
                ["syntheticMode"] = runtime.IsSyntheticMode,
                ["syntheticRunning"] = runtime.IsSyntheticRunning
            },
            ["summary"] = new Dictionary<string, object> {
                ["captured"] = frames.Length,
                ["accepted"] = accepted,
                ["usable"] = usable,
                ["warning"] = warnings,
                ["rejected"] = rejected,
                ["learning"] = learning,
                ["errors"] = errors,
                ["acceptanceRate"] = acceptanceRate,
                ["sessionQuality"] = sessionQuality,
                ["sessionConfidence"] = sessionConfidence
            },
            ["settings"] = new Dictionary<string, object> {
                ["minimumLearningFrames"] = settings.MinimumLearningFrames,
                ["baselineWindow"] = settings.BaselineWindow,
                ["enableGuideRms"] = settings.EnableGuideRms,
                ["maxGuideRms"] = settings.MaxGuideRms,
                ["enableSustainedExcursion"] = settings.EnableSustainedExcursion,
                ["excursionThreshold"] = settings.ExcursionThreshold,
                ["excursionMinimumDuration"] = settings.ExcursionMinimumDuration,
                ["enableHardExcursion"] = settings.EnableHardExcursion,
                ["hardExcursionThreshold"] = settings.HardExcursionThreshold,
                ["enableStarCount"] = settings.EnableStarCount,
                ["maxStarLossPercent"] = settings.MaxStarLossPercent,
                ["enableBackground"] = settings.EnableBackground,
                ["maxBackgroundIncreasePercent"] = settings.MaxBackgroundIncreasePercent,
                ["maxBackgroundDecreasePercent"] = settings.MaxBackgroundDecreasePercent,
                ["predictiveWarningsEnabled"] = settings.PredictiveWarningsEnabled,
                ["environmentalCorrelationEnabled"] = settings.EnvironmentalCorrelationEnabled,
                ["smartRecoveryEnabled"] = settings.SmartPauseEnabled
            },
            ["series"] = new Dictionary<string, object> {
                ["quality"] = Series("Quality", "#8AB4F8", "score", "0–100; higher is better"),
                ["confidence"] = Series("Confidence", "#C58AF9", "%", "0–100; higher means a more reliable assessment"),
                ["guideRms"] = Series("Guide RMS", "#81C995", "arcsec", "absolute RMS; lower is better"),
                ["starDelta"] = Series("Stars Δ", "#FDD663", "% vs rolling baseline", "negative means fewer stars than the rolling clean-frame reference"),
                ["backgroundDelta"] = Series("Background Δ", "#F28B82", "% vs rolling baseline", "positive = brighter than baseline; negative = darker")
            },
            ["currentFrame"] = current == null ? null : MobileFrame(current),
            ["frames"] = frames.Select(x => (object)MobileFrame(x)).ToList()
        };

        return result;
    }

    private static IDictionary<string, object> MobileFrame(FrameQualityResult frame) => new Dictionary<string, object> {
        ["frameIndex"] = frame.FrameIndex,
        ["timestampUtc"] = frame.TimestampUtc == default ? null : frame.TimestampUtc.ToUniversalTime().ToString("O"),
        ["status"] = frame.StatusText,
        ["quality"] = JsonNumber(frame.Status is FrameStatus.Learning or FrameStatus.Error ? double.NaN : frame.OverallQuality),
        ["confidence"] = JsonNumber(frame.ConfidenceScore),
        ["guideRmsArcsec"] = JsonNumber(frame.GuideRmsArcsec),
        ["stars"] = frame.StarCount >= 0 ? (object)frame.StarCount : null,
        ["starBaseline"] = JsonNumber(frame.StarBaseline),
        ["starDeltaPercent"] = JsonNumber(frame.StarDeviationPercent),
        ["background"] = JsonNumber(frame.BackgroundMedian),
        ["backgroundBaseline"] = JsonNumber(frame.BackgroundBaseline),
        ["backgroundDeltaPercent"] = JsonNumber(frame.BackgroundDeviationPercent),
        ["target"] = frame.Target ?? "",
        ["filter"] = frame.Filter ?? "",
        ["probableCause"] = frame.ProbableCause ?? "",
        ["reason"] = frame.ReasonText ?? "",
        ["qsmControlled"] = frame.QsmControlled,
        ["source"] = frame.SourceText,
        ["predictiveWarning"] = frame.PredictiveWarning,
        ["prediction"] = frame.PredictionText ?? "",
        ["environmentAvailable"] = frame.EnvironmentAvailable,
        ["environmentHint"] = frame.EnvironmentalHint ?? ""
    };

    private static IDictionary<string, object> Series(string label, string color, string unit, string meaning) =>
        new Dictionary<string, object> {
            ["label"] = label,
            ["color"] = color,
            ["unit"] = unit,
            ["meaning"] = meaning
        };

    private static object JsonNumber(double value) => Finite(value) ? (object)value : null;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        broker.Unsubscribe(RequestSnapshotTopic, this);
    }

    private sealed class QsmBrokerMessage : IMessage {
        public QsmBrokerMessage(string topic, object content, Guid? correlationId, IDictionary<string, object> customHeaders) {
            Topic = topic;
            Content = content;
            CorrelationId = correlationId;
            CustomHeaders = customHeaders ?? new Dictionary<string, object>();
            SentAt = DateTimeOffset.UtcNow;
            MessageId = Guid.NewGuid();
        }

        public Guid SenderId => PluginConstants.Identifier;
        public string Sender => PluginConstants.Name;
        public DateTimeOffset SentAt { get; }
        public Guid MessageId { get; }
        public DateTimeOffset? Expiration => SentAt.AddSeconds(10);
        public Guid? CorrelationId { get; }
        public int Version => ContractVersion;
        public IDictionary<string, object> CustomHeaders { get; }
        public string Topic { get; }
        public object Content { get; }
    }
}
