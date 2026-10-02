using NINA.Core.Utility;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Validates startup references and reconciles every dependent channel and verdict.</summary>
public sealed class ReferenceReview {
    private const int RetainedInputs = 2000;
    private sealed record SavedFrame(BaselineKey Key, FrameQualityInput Input);
    private sealed record Reviewed(FrameQualityResult Previous, FrameQualityResult Result, FrameQualityInput Input);
    private readonly BaselineEngine baseline;
    private readonly QualityEngine engine;
    private readonly StellarAnalysisPipeline stellar;
    private readonly Dictionary<int, SavedFrame> inputs = new();
    private readonly Dictionary<BaselineKey, List<int>> provisional = new();
    private readonly Dictionary<BaselineKey, List<int>> betterRuns = new();
    private readonly Dictionary<BaselineKey, ReferenceValidation.Reference> anchors = new();
    private readonly Dictionary<BaselineKey, DateTime> lastRecorded = new();

    public ReferenceReview(BaselineEngine baseline, QualityEngine engine, StellarAnalysisPipeline pipeline = null) {
        this.baseline = baseline;
        this.engine = engine;
        stellar = pipeline;
    }

    public void Record(BaselineKey key, FrameQualityInput input, FrameQualityResult result, bool couldTrain) {
        if (input.TimestampUtc != DateTime.MinValue) {
            if (lastRecorded.TryGetValue(key, out var previousTime) && input.TimestampUtc - previousTime > TimeSpan.FromHours(6)) {
                provisional.Remove(key); betterRuns.Remove(key); anchors.Remove(key);
                foreach (int index in inputs.Where(f => f.Value.Key.Equals(key)).Select(f => f.Key).ToArray()) inputs.Remove(index);
            }
            lastRecorded[key] = input.TimestampUtc;
        }
        // Eligibility is recomputed after revision; the old caller flag cannot authorize new training.
        inputs[result.FrameIndex] = new SavedFrame(key, input);
        if (inputs.Count > RetainedInputs) {
            int oldest = inputs.Keys.Min();
            inputs.Remove(oldest);
            foreach (var list in provisional.Values) list.Remove(oldest);
            foreach (var list in betterRuns.Values) list.Remove(oldest);
        }
        if (result.Status == FrameStatus.Learning) List(provisional, key).Add(result.FrameIndex);
    }

    public void Clear() { inputs.Clear(); provisional.Clear(); betterRuns.Clear(); anchors.Clear(); lastRecorded.Clear(); }
    public int ProvisionalCount(BaselineKey key) => provisional.TryGetValue(key, out var ids) ? ids.Count : 0;

    public List<FrameReclassification> Review(BaselineKey key, FrameQualityResult latest, IReadOnlyList<FrameQualityResult> session, QualitySettings settings) {
        var changes = new Dictionary<int, FrameReclassification>();
        if (ProvisionalCount(key) > 0) {
            var snapshot = baseline.GetSnapshot(key, settings.MinimumLearningFrames);
            bool mature = (!settings.EnableStarCount || snapshot.StarsReady) && (!settings.EnableBackground || snapshot.BackgroundReady);
            if (mature) ValidateProvisional(key, session, settings, changes);
        }
        var effective = session.Select(r => changes.TryGetValue(r.FrameIndex, out var change) ? change.Result : r).ToArray();
        var effectiveLatest = changes.TryGetValue(latest.FrameIndex, out var revision) ? revision.Result : latest;
        if (effectiveLatest.Status != FrameStatus.Error && effectiveLatest.Status != FrameStatus.Learning)
            ReviewBetterConditions(key, effectiveLatest, effective, settings, changes);
        return changes.Values.OrderBy(c => c.Result.FrameIndex).ToList();
    }

    private void ValidateProvisional(BaselineKey key, IReadOnlyList<FrameQualityResult> session, QualitySettings settings, Dictionary<int, FrameReclassification> changes) {
        var ids = List(provisional, key);
        var frames = Frames(session, ids);
        if (frames.Count == 0) { ids.Clear(); return; }
        var candidates = Candidates(frames);
        var selectedIds = ReferenceValidation.SelectReferenceCandidates(candidates, settings).Select(c => c.FrameIndex).ToHashSet();
        var reference = ReferenceValidation.Build(candidates, settings);
        if (reference.Samples < Math.Min(2, settings.MinimumLearningFrames)) return;

        DateTime epoch = frames.Max(f => f.TimestampUtc).AddTicks(1);
        // Recompare against a different population only when validation excluded a separated level.
        // Smooth startup trajectories keep their original chronological photometry.
        var comparison = selectedIds.Count < frames.Count ? frames.Where(f => selectedIds.Contains(f.FrameIndex)).ToArray() : null;
        var reviewed = frames.Select(f => Reevaluate(key, f, reference, epoch, settings, comparison)).ToArray();
        bool mature = (!settings.EnableStarCount || reviewed.Count(f => ExposureAssessment.CanTrainStarCount(f.Result)) >= settings.MinimumLearningFrames)
            && (!settings.EnableBackground || reviewed.Count(f => ExposureAssessment.CanTrainBackground(f.Result)) >= settings.MinimumLearningFrames);
        if (mature) {
            ids.Clear();
            // Anchor only an observed, newly eligible population; never a cloudy/clear midpoint.
            var vetted = reviewed.Where(f => ExposureAssessment.CanTrainStarCount(f.Result) && ExposureAssessment.CanTrainBackground(f.Result)).ToArray();
            anchors[key] = ReferenceValidation.Build(vetted.Select(f => Candidate(f.Input)).ToArray(), settings);
        }
        foreach (var frame in reviewed) {
            if (!mature && frame.Result.Status != FrameStatus.Rejected) {
                frame.Result.Status = FrameStatus.Learning;
                frame.Result.DecisionSummary = "Provisional: waiting for enough consistent reference frames.";
            }
            if (frame.Result.Status == FrameStatus.Rejected) ids.Remove(frame.Result.FrameIndex);
            AddChange(changes, frame.Previous, frame.Result);
        }
        Rebuild(key, reviewed, session, settings);
        Logger.Info($"QualitySessionMeter reviewed {reviewed.Length} provisional frames; reference validated={mature}.");
    }

    private void ReviewBetterConditions(BaselineKey key, FrameQualityResult latest, IReadOnlyList<FrameQualityResult> session, QualitySettings settings, Dictionary<int, FrameReclassification> changes) {
        if (!anchors.TryGetValue(key, out var anchor)) return;
        var run = List(betterRuns, key);
        if (!ReferenceValidation.ShowsBetterConditions(latest, anchor, settings)) { run.Clear(); return; }
        if (run.Count == 0) {
            var recent = inputs.Where(x => x.Value.Key.Equals(key) && x.Key <= latest.FrameIndex)
                .OrderBy(x => x.Key).TakeLast(6).Select(x => Candidate(x.Value.Input)).ToArray();
            if (!ReferenceValidation.IsUnexpectedImprovement(latest) || ReferenceValidation.FollowsGradualContinuation(recent)) return;
        }
        if (!run.Contains(latest.FrameIndex)) run.Add(latest.FrameIndex);
        if (run.Count < settings.MinimumLearningFrames) return;
        var runFrames = Frames(session, run);
        if (runFrames.Count < settings.MinimumLearningFrames) return;
        var reference = ReferenceValidation.Build(Candidates(runFrames), settings);
        if (reference.Samples < settings.MinimumLearningFrames) return;
        int firstRun = runFrames.Min(f => f.FrameIndex);
        var earlier = Frames(session, inputs.Where(x => x.Value.Key.Equals(key) && x.Key < firstRun).Select(x => x.Key));
        int protectedThrough = TrustedGradualEnd(earlier, settings);
        DateTime epoch = runFrames.Max(f => f.TimestampUtc).AddTicks(1);
        var revisedRun = runFrames.Select(f => Reevaluate(key, f, reference, epoch, settings, runFrames)).ToArray();
        if ((settings.EnableStarCount && revisedRun.Count(f => ExposureAssessment.CanTrainStarCount(f.Result)) < settings.MinimumLearningFrames)
            || (settings.EnableBackground && revisedRun.Count(f => ExposureAssessment.CanTrainBackground(f.Result)) < settings.MinimumLearningFrames)) {
            // Evaluation is tentative: no baseline or recorded evidence changes until it qualifies.
            run.Clear();
            return;
        }
        // A later clearing does not measure how an earlier healthy trajectory looked at its own time.
        // Only the following unsettled population belongs to the replacement reference epoch.
        var affected = earlier.Where(f => f.FrameIndex > protectedThrough).ToArray();
        var reviewed = affected.Select(f => Reevaluate(key, f, reference, epoch, settings, runFrames)).Concat(revisedRun).ToArray();
        foreach (var frame in reviewed) AddChange(changes, frame.Previous, frame.Result);
        run.Clear();
        anchors[key] = reference;
        Rebuild(key, reviewed, session, settings, protectedThrough);
        Logger.Info($"QualitySessionMeter rebuilt a corroborated reference and reviewed {affected.Length} earlier frames.");
    }

    private Reviewed Reevaluate(BaselineKey key, FrameQualityResult previous, ReferenceValidation.Reference reference, DateTime epoch, QualitySettings settings,
        IReadOnlyList<FrameQualityResult> comparison = null) {
        var input = inputs[previous.FrameIndex].Input;
        var image = input.ImageEvidence ?? new ImageEvidence();
        if (stellar != null && settings.ImageEvidenceEnabled && comparison != null) {
            // Retrospective comparison is a level comparison, not a forecast into the future.
            // Equal reference times disable trend extrapolation. Excluding this frame prevents a
            // self-match from manufacturing good evidence in a sparse new population.
            var verifier = new StellarAnalysisPipeline();
            foreach (var frame in comparison.Where(f => f.FrameIndex != previous.FrameIndex)) {
                if (!ExposureAssessment.CanTrainPhotometry(frame)) continue;
                var evidence = inputs[frame.FrameIndex].Input.ImageEvidence;
                if (evidence == null) continue;
                verifier.AddEvidenceReference(key, evidence, frame.Status==FrameStatus.Learning?FrameStatus.Learning:FrameStatus.Accepted,
                    epoch, evidence.PierSide, settings.BaselineWindow);
            }
            image = verifier.CompareReference(key, image, epoch, image.PierSide);
        }
        var revisedInput = input.WithBaseline(reference.ToSnapshot(input.TimestampUtc), image);
        var next = engine.Evaluate(revisedInput, settings);
        next.SourceKind = previous.SourceKind; next.SequenceTitle = previous.SequenceTitle;
        next.QsmControlled = previous.QsmControlled; next.FileActionEligible = previous.FileActionEligible;
        next.ProvenanceFrozen = previous.ProvenanceFrozen; next.QsmControlToken = previous.QsmControlToken;
        next.MonitorOnly = previous.MonitorOnly;
        next.FinalPath = previous.FinalPath;
        next.PredictiveWarning = previous.PredictiveWarning; next.PredictiveConfidence = previous.PredictiveConfidence;
        next.PredictiveChannel = previous.PredictiveChannel; next.PredictiveMessage = previous.PredictiveMessage;
        next.PredictiveFramesToThreshold = previous.PredictiveFramesToThreshold;
        next.EnvironmentAvailable = previous.EnvironmentAvailable; next.CloudCover = previous.CloudCover;
        next.Humidity = previous.Humidity; next.WindSpeed = previous.WindSpeed; next.WindGust = previous.WindGust;
        next.SkyQuality = previous.SkyQuality; next.AmbientTemperature = previous.AmbientTemperature;
        next.DewPoint = previous.DewPoint; next.EnvironmentalHint = previous.EnvironmentalHint;
        next.ReferenceRevised = true;
        return new Reviewed(previous, next, revisedInput);
    }

    private void SeedPhotometry(BaselineKey key, IEnumerable<FrameQualityResult> frames, QualitySettings settings) {
        if (stellar == null) return;
        stellar.ResetReferences(key);
        foreach (var frame in frames.OrderBy(f => f.TimestampUtc)) {
            if (!ExposureAssessment.CanTrainPhotometry(frame)) continue;
            var input = inputs[frame.FrameIndex].Input;
            stellar.AddEvidenceReference(key, input.ImageEvidence, frame.Status==FrameStatus.Learning?FrameStatus.Learning:FrameStatus.Accepted,
                input.TimestampUtc, input.ImageEvidence?.PierSide ?? "Unknown", settings.BaselineWindow);
        }
    }

    private void RestorePhotometry(BaselineKey key, IEnumerable<FrameQualityResult> frames, QualitySettings settings) => SeedPhotometry(key, frames, settings);

    private void Rebuild(BaselineKey key, IEnumerable<Reviewed> reviewed, IReadOnlyList<FrameQualityResult> session, QualitySettings settings, int trainingAfter = 0) {
        var revisions = reviewed.ToDictionary(f => f.Result.FrameIndex);
        foreach (var frame in revisions.Values) inputs[frame.Result.FrameIndex] = new SavedFrame(key, frame.Input);
        // A guide/shape rejection outside the provisional set can still supply valid background.
        // Rebuild each channel from the complete retained context, with its revised eligibility.
        var frames = session.Where(f => f.FrameIndex > trainingAfter && inputs.TryGetValue(f.FrameIndex, out var saved) && saved.Key.Equals(key))
            .Select(f => revisions.TryGetValue(f.FrameIndex, out var revision) ? revision.Result : f)
            .OrderBy(f => f.TimestampUtc).ThenBy(f => f.FrameIndex).ToArray();
        baseline.ReplaceMeasurements(key, frames.Select(f => (inputs[f.FrameIndex].Input.StarCount, inputs[f.FrameIndex].Input.BackgroundMedian,
            inputs[f.FrameIndex].Input.TimestampUtc, ExposureAssessment.CanTrainStarCount(f), ExposureAssessment.CanTrainBackground(f))), settings.BaselineWindow);
        RestorePhotometry(key, frames, settings);
    }

    private int TrustedGradualEnd(IReadOnlyList<FrameQualityResult> frames, QualitySettings settings) {
        int minimum = Math.Max(8, settings.MinimumLearningFrames);
        var contiguous = new List<ReferenceValidation.Candidate>();
        int end = 0;
        foreach (var frame in frames) {
            if (!frame.IsUsable || !ExposureAssessment.CanTrainBaseline(frame)) { contiguous.Clear(); continue; }
            contiguous.Add(Candidate(inputs[frame.FrameIndex].Input));
            if (contiguous.Count > minimum) contiguous.RemoveAt(0);
            if (contiguous.Count == minimum && ReferenceValidation.HasGradualReferenceTrajectory(contiguous)) end = frame.FrameIndex;
        }
        return end;
    }

    private List<FrameQualityResult> Frames(IReadOnlyList<FrameQualityResult> session, IEnumerable<int> ids) {
        var wanted = ids.ToHashSet();
        return session.Where(r => wanted.Contains(r.FrameIndex) && inputs.ContainsKey(r.FrameIndex)).OrderBy(r => r.TimestampUtc).ThenBy(r => r.FrameIndex).ToList();
    }
    private ReferenceValidation.Candidate[] Candidates(IEnumerable<FrameQualityResult> frames) => frames.Select(f => Candidate(inputs[f.FrameIndex].Input)).ToArray();
    private static ReferenceValidation.Candidate Candidate(FrameQualityInput input) => new(input.FrameIndex, input.StarCount, input.BackgroundMedian, input.TimestampUtc);
    private static void AddChange(Dictionary<int, FrameReclassification> changes, FrameQualityResult previous, FrameQualityResult next) {
        var original = changes.TryGetValue(previous.FrameIndex, out var existing) ? existing.Previous : previous;
        changes[previous.FrameIndex] = new FrameReclassification(original, next);
    }
    private static List<int> List(Dictionary<BaselineKey, List<int>> map, BaselineKey key) {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
        return list;
    }
}
