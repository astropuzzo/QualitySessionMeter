using NINA.Core.Utility;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Retrospective reference decisions for the star-count/background channels.
///
/// 1. Frames captured while a context is LEARNING are provisional. When enough of them exist, they are
///    re-evaluated against a reference built from the better half of the set. Outliers are rejected;
///    the rest become ACCEPTED or WARNING and only they form the reference.
/// 2. Early in a context, a run of frames that would make the validated reference itself fail the
///    star-count rule proves that reference was formed in worse conditions (cloud, haze, twilight).
///    The reference is rebuilt from that run and the earlier frames are re-checked against it.
///    The rolling baseline already follows gradual change later in the night; without this step a
///    clearing after a hazy start would leave every darker clear frame rejected as BACKGROUND_LOW.
///
/// Not thread-safe: the runtime calls it under its processing lock.
/// </summary>
public sealed class ReferenceReview {
    private const int RetainedInputs = 2000;

    private readonly BaselineEngine baseline;
    private readonly QualityEngine engine;
    private readonly Dictionary<int, (BaselineKey Key, FrameQualityInput Input, bool CouldTrain)> inputs = new();
    private readonly Dictionary<BaselineKey, List<int>> provisional = new();
    private readonly Dictionary<BaselineKey, List<int>> betterRuns = new();
    // The validated early reference per context, kept only while the context is early.
    private readonly Dictionary<BaselineKey, ReferenceValidation.Reference> anchors = new();

    public ReferenceReview(BaselineEngine baseline, QualityEngine engine) {
        this.baseline = baseline;
        this.engine = engine;
    }

    public void Record(BaselineKey key, FrameQualityInput input, FrameQualityResult result, bool couldTrain) {
        inputs[result.FrameIndex] = (key, input, couldTrain);
        if (inputs.Count > RetainedInputs) inputs.Remove(inputs.Keys.Min());
        if (result.Status == FrameStatus.Learning) List(provisional, key).Add(result.FrameIndex);
    }

    public void Clear() {
        inputs.Clear();
        provisional.Clear();
        betterRuns.Clear();
        anchors.Clear();
    }

    public int ProvisionalCount(BaselineKey key) => provisional.TryGetValue(key, out var ids) ? ids.Count : 0;

    /// <summary>Returns the verdicts that change after <paramref name="latest"/> was recorded.</summary>
    public List<FrameReclassification> Review(BaselineKey key, FrameQualityResult latest, IReadOnlyList<FrameQualityResult> session, QualitySettings settings) {
        var changes = new List<FrameReclassification>();
        if (latest.Status == FrameStatus.Learning) {
            var snapshot = baseline.GetSnapshot(key, settings.MinimumLearningFrames);
            bool mature = (!settings.EnableStarCount || snapshot.StarsReady) && (!settings.EnableBackground || snapshot.BackgroundReady);
            if (mature) ValidateProvisional(key, session, settings, changes);
        } else if (latest.Status != FrameStatus.Error) {
            ReviewBetterConditions(key, latest, session, settings, changes);
        }
        return changes;
    }

    private void ValidateProvisional(BaselineKey key, IReadOnlyList<FrameQualityResult> session, QualitySettings settings, List<FrameReclassification> changes) {
        var ids = List(provisional, key);
        var frames = Frames(session, ids);
        if (frames.Count == 0) { ids.Clear(); return; }

        var reference = ReferenceValidation.Build(Candidates(frames), settings);
        var reviewed = frames.Select(f => (f.Result, f.Input, f.CouldTrain, Next: Reevaluate(f.Result, f.Input, reference, settings))).ToArray();
        var training = reviewed.Where(x => x.CouldTrain && x.Next.Status != FrameStatus.Rejected).ToArray();

        if (training.Length >= settings.MinimumLearningFrames) {
            foreach (var x in reviewed) changes.Add(new FrameReclassification(x.Result, x.Next));
            ids.Clear();
            anchors[key] = reference;
            Logger.Info($"QualitySessionMeter validated the reference for {key}: {training.Length} of {reviewed.Length} provisional frames agree.");
        } else {
            // Not enough agreeing frames yet: reject clear outliers now and keep the rest provisional.
            foreach (var x in reviewed.Where(x => x.Next.Status == FrameStatus.Rejected)) {
                changes.Add(new FrameReclassification(x.Result, x.Next));
                ids.Remove(x.Result.FrameIndex);
            }
        }
        baseline.Replace(key, training.Select(x => (x.Input.StarCount, x.Input.BackgroundMedian, x.Input.TimestampUtc)), settings.BaselineWindow);
    }

    private void ReviewBetterConditions(BaselineKey key, FrameQualityResult latest, IReadOnlyList<FrameQualityResult> session, QualitySettings settings, List<FrameReclassification> changes) {
        if (!anchors.TryGetValue(key, out var anchor)) return;
        var run = List(betterRuns, key);
        if (!ReferenceValidation.ShowsBetterConditions(latest, anchor, settings)) { run.Clear(); return; }
        run.Add(latest.FrameIndex);
        if (run.Count < settings.MinimumLearningFrames) return;

        var runFrames = Frames(session, run);
        run.Clear();
        if (runFrames.Count < settings.MinimumLearningFrames) return;

        int firstRunFrame = runFrames.Min(f => f.Result.FrameIndex);
        var earlier = Frames(session, inputs.Where(x => x.Value.Key.Equals(key) && x.Key < firstRunFrame).Select(x => x.Key))
            .Where(f => f.Result.IsUsable).ToList();
        if (earlier.Count > settings.BaselineWindow * 2) {
            // Too late to call the start of the night unrepresentative; earlier verdicts stand.
            anchors.Remove(key);
            return;
        }

        var reference = ReferenceValidation.Build(Candidates(runFrames), settings);
        foreach (var f in runFrames) changes.Add(new FrameReclassification(f.Result, Reevaluate(f.Result, f.Input, reference, settings)));
        foreach (var f in earlier) {
            var next = Reevaluate(f.Result, f.Input, reference, settings);
            if (next.Status == FrameStatus.Rejected) changes.Add(new FrameReclassification(f.Result, next));
        }
        anchors[key] = reference;
        baseline.Replace(key, runFrames.Select(f => (f.Input.StarCount, f.Input.BackgroundMedian, f.Input.TimestampUtc)), settings.BaselineWindow);
        Logger.Info($"QualitySessionMeter rebuilt the reference for {key} from {runFrames.Count} frames in better conditions and re-checked {earlier.Count} earlier frames.");
    }

    private FrameQualityResult Reevaluate(FrameQualityResult previous, FrameQualityInput input, ReferenceValidation.Reference reference, QualitySettings settings) {
        var next = engine.Evaluate(input.WithBaseline(reference.ToSnapshot()), settings);
        // Acquisition provenance, file disposition and diagnostics captured at save time do not change.
        next.SourceKind = previous.SourceKind;
        next.SequenceTitle = previous.SequenceTitle;
        next.QsmControlled = previous.QsmControlled;
        next.FileActionEligible = previous.FileActionEligible;
        next.ProvenanceFrozen = previous.ProvenanceFrozen;
        next.QsmControlToken = previous.QsmControlToken;
        next.FinalPath = previous.FinalPath;
        next.PredictiveWarning = previous.PredictiveWarning;
        next.PredictiveConfidence = previous.PredictiveConfidence;
        next.PredictiveChannel = previous.PredictiveChannel;
        next.PredictiveMessage = previous.PredictiveMessage;
        next.PredictiveFramesToThreshold = previous.PredictiveFramesToThreshold;
        next.EnvironmentAvailable = previous.EnvironmentAvailable;
        next.CloudCover = previous.CloudCover;
        next.Humidity = previous.Humidity;
        next.WindSpeed = previous.WindSpeed;
        next.WindGust = previous.WindGust;
        next.SkyQuality = previous.SkyQuality;
        next.AmbientTemperature = previous.AmbientTemperature;
        next.DewPoint = previous.DewPoint;
        next.EnvironmentalHint = previous.EnvironmentalHint;
        next.ReferenceRevised = true;
        next.DecisionSummary += " Decided after the session reference was validated.";
        return next;
    }

    private List<(FrameQualityResult Result, FrameQualityInput Input, bool CouldTrain)> Frames(IReadOnlyList<FrameQualityResult> session, IEnumerable<int> ids) {
        var wanted = ids.ToHashSet();
        return session
            .Where(r => wanted.Contains(r.FrameIndex) && inputs.ContainsKey(r.FrameIndex))
            .Select(r => (r, inputs[r.FrameIndex].Input, inputs[r.FrameIndex].CouldTrain))
            .ToList();
    }

    private static ReferenceValidation.Candidate[] Candidates(IEnumerable<(FrameQualityResult Result, FrameQualityInput Input, bool CouldTrain)> frames) =>
        frames.Select(f => new ReferenceValidation.Candidate(f.Result.FrameIndex, f.Input.StarCount, f.Input.BackgroundMedian)).ToArray();

    private static List<int> List(Dictionary<BaselineKey, List<int>> map, BaselineKey key) {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
        return list;
    }
}
