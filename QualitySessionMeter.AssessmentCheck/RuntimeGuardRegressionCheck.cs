using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;
using System.IO;

internal static class RuntimeGuardRegressionCheck {
    public static void Run(Action<bool, string> check) {
        CheckMissingGuideStartup(check);
        CheckAutomaticFileActions(check);
    }

    private static void CheckMissingGuideStartup(Action<bool, string> check) {
        var settings = new QualitySettings(new InMemoryPluginOptionsAccessor()) { ImageEvidenceEnabled = true, MonitorOnly = true };
        var key = new BaselineKey("No-guide startup", "L", 180, 100, 1, 1, "Camera", 50, 0, 512, 512, 1, "East");
        var baseline = new BaselineEngine();
        var engine = new QualityEngine();
        var stellar = new StellarAnalysisPipeline();
        var review = new ReferenceReview(baseline, engine, stellar);
        var results = new List<FrameQualityResult>();
        var start = new DateTime(2026, 10, 2, 20, 0, 0, DateTimeKind.Utc);
        bool initialLearning = true;
        var sample = new ImageSample(new float[512 * 512], 512, 512) { SourceWidth = 512, SourceHeight = 512 };
        for (int i = 0; i < 8; i++) {
            var time = start.AddMinutes(i * 3);
            var evidence = stellar.CompareReference(key, RoundEvidence(), time, "East");
            var input = new FrameQualityInput {
                FrameIndex = i + 1, TimestampUtc = time, StarCount = 1000, BackgroundMedian = 1000, ExposureSeconds = 180,
                Baseline = baseline.GetSnapshot(key, 4, time), Guide = new GuideExposureMetrics { HasData = false }, ImageEvidence = evidence
            };
            var result = engine.Evaluate(input, settings);
            if (i < 4) initialLearning &= result.Status == FrameStatus.Learning;
            baseline.AddMeasurements(key, 1000, 1000, 8, time, ExposureAssessment.CanTrainStarCount(result), ExposureAssessment.CanTrainBackground(result));
            if (ExposureAssessment.CanTrainPhotometry(result)) stellar.AddReference(key, sample, evidence, FrameStatus.Accepted, time, "East", 8);
            review.Record(key, input, result, ExposureAssessment.CanTrainBaseline(result));
            results.Add(result);
            foreach (var change in review.Review(key, result, results, settings))
                results[results.FindIndex(f => f.FrameIndex == change.Result.FrameIndex)] = change.Result;
        }
        check(initialLearning, "missing guide data preserves provisional startup when image and sky measurements exist");
        check(results.All(r => r.IsUsable && r.Status == FrameStatus.Warning && r.ReviewReasons.Contains("GUIDE_DATA_UNAVAILABLE")),
            "no-guide startup validates its references and retains all eight frames with an explicit warning");
        var snapshot = baseline.GetSnapshot(key, 4);
        check(snapshot.StarsReady && snapshot.BackgroundReady && snapshot.StarSamples == 8 && snapshot.BackgroundSamples == 8,
            "missing guiding does not stop independent healthy count and sky reference training");
        check(results[^1].ImageEvidence.PhotometryAvailable && results[^1].ImageEvidence.ReferenceFrames >= 6,
            "healthy warning frames keep training the established photometric reference");

        settings.ImageEvidenceEnabled = false;
        var partial = engine.Evaluate(new FrameQualityInput { StarCount = 1000, BackgroundMedian = 1000, Baseline = new BaselineSnapshot() }, settings);
        check(partial.Status == FrameStatus.Learning, "measured count and sky alone are enough to preserve learning without guide data");
        var unavailable = engine.Evaluate(new FrameQualityInput { StarCount = -1, BackgroundMedian = double.NaN, Baseline = new BaselineSnapshot() }, settings);
        check(unavailable.Status == FrameStatus.Error && unavailable.ConfidenceScore == 0,
            "a frame with no valid active measurement remains unassessed");
    }

    private static ImageEvidence RoundEvidence(double flux = 1) => new() {
        Available = true, Stars = 40, AxisRatio = 1.1, ElongatedFraction = 0, TailStrength = 0, DoublePeakStrength = 0, FwhmPixels = 3,
        SampleWidth = 512, SampleHeight = 512, SourceWidth = 512, SourceHeight = 512, PixelsPerSample = 1, PierSide = "East",
        Catalog = Enumerable.Range(0, 40).Select(i => new StarObservation(55 + i % 8 * 48, 55 + i / 8 * 48, 10000 * flux, 10000 * flux, 3, 10)).ToArray()
    };

    private static void CheckAutomaticFileActions(Action<bool, string> check) {
        string parent = Path.GetFullPath(Path.GetTempPath());
        string directory = Path.GetFullPath(Path.Combine(parent, "QSM_RuntimeGuard_" + Guid.NewGuid().ToString("N")));
        if (!directory.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test directory is outside the temporary root.");
        Directory.CreateDirectory(directory);
        try {
            const string payload = "QSM temporary file-action regression data";
            var settings = new QualitySettings(new InMemoryPluginOptionsAccessor()) { MonitorOnly = false, RejectedFileAction = RejectedFileAction.PrefixBad };
            var service = new RejectedFileService();
            FrameQualityResult Frame(string original, string final, FrameStatus status, bool monitor = false, bool eligible = true) => new() {
                OriginalPath = original, FinalPath = final, Status = status, MonitorOnly = monitor, FileActionEligible = eligible, ProvenanceFrozen = true
            };

            string unknown = Path.Combine(directory, "BAD_external.fits");
            File.WriteAllText(unknown, payload);
            var external = Frame(unknown, unknown, FrameStatus.Rejected, eligible: false);
            var externalRecovered = Frame(unknown, unknown, FrameStatus.Warning);
            if (RejectedFileService.MayRestoreAfterReferenceRevision(external, externalRecovered, settings)) service.RestoreAsync(external).GetAwaiter().GetResult();
            check(File.Exists(unknown) && File.ReadAllText(unknown) == payload && !File.Exists(Path.Combine(directory, "external.fits")),
                "an external file already named BAD is never renamed by automatic reference recovery");
            external.FileActionEligible = true;
            check(!RejectedFileService.MayRestoreAfterReferenceRevision(external, externalRecovered, settings),
                "a BAD prefix alone is not evidence that QSM changed the original file");

            string original = Path.Combine(directory, "sequencer.fits");
            File.WriteAllText(original, payload);
            var monitored = Frame(original, original, FrameStatus.Warning, monitor: true);
            var promoted = Frame(original, original, FrameStatus.Rejected);
            if (RejectedFileService.MayApplyAfterReferenceRevision(monitored, promoted, settings))
                promoted.FinalPath = service.ApplyAsync(original, settings, true).GetAwaiter().GetResult();
            check(File.Exists(original) && !File.Exists(Path.Combine(directory, "BAD_sequencer.fits")),
                "a frame acquired in Monitor Only cannot receive a delayed BAD action after settings change");

            var previous = Frame(original, original, FrameStatus.Warning);
            check(RejectedFileService.MayApplyAfterReferenceRevision(previous, promoted, settings),
                "a newly confirmed rejection permits the originally authorized sequencer file action");
            promoted.FinalPath = service.ApplyAsync(original, settings, true).GetAwaiter().GetResult();
            var recovered = Frame(original, promoted.FinalPath, FrameStatus.Warning);
            settings.MonitorOnly = true;
            if (RejectedFileService.MayRestoreAfterReferenceRevision(promoted, recovered, settings)) service.RestoreAsync(promoted).GetAwaiter().GetResult();
            check(!File.Exists(original) && File.Exists(promoted.FinalPath),
                "current Monitor Only blocks automatic restoration of an earlier QSM file action");
            settings.MonitorOnly = false;
            recovered.MonitorOnly = true;
            check(!RejectedFileService.MayRestoreAfterReferenceRevision(promoted, recovered, settings),
                "a revised Monitor Only verdict cannot trigger an automatic file mutation");
            recovered.MonitorOnly = false;
            check(RejectedFileService.MayRestoreAfterReferenceRevision(promoted, recovered, settings),
                "automatic recovery requires a recorded QSM path change and preserved acquisition authorization");
            recovered.FinalPath = service.RestoreAsync(promoted).GetAwaiter().GetResult();
            check(File.Exists(original) && !File.Exists(promoted.FinalPath) && File.ReadAllText(original) == payload,
                "authorized automatic recovery restores the original filename without changing image contents");

            CheckMonitorOnlyAcrossReferenceRevisions(check, directory, service, payload);
        } finally {
            // Only this newly created, validated temporary directory belongs to the test.
            Directory.Delete(directory, true);
        }
    }

    private static void CheckMonitorOnlyAcrossReferenceRevisions(Action<bool, string> check, string directory, RejectedFileService service, string payload) {
        var settings = new QualitySettings(new InMemoryPluginOptionsAccessor()) {
            Enabled = true, MonitorOnly = true, ImageEvidenceEnabled = true, MinimumLearningFrames = 4, BaselineWindow = 8,
            RejectedFileAction = RejectedFileAction.PrefixBad
        };
        var key = new BaselineKey("Monitor Only revisions", "L", 180, 100, 1, 1, "Camera", 50, 0, 512, 512, 1, "East");
        var baseline = new BaselineEngine();
        var engine = new QualityEngine();
        var stellar = new StellarAnalysisPipeline();
        var review = new ReferenceReview(baseline, engine, stellar);
        var results = new List<FrameQualityResult>();
        var start = new DateTime(2026, 10, 2, 21, 0, 0, DateTimeKind.Utc);
        bool retainedRevision = false, retainedMonitorOnly = false, rejectedRevision = false, authorizedMonitoredRename = false;
        int trackedRevisions = 0;

        void Add(int stars, double background, double flux) {
            int index = results.Count + 1;
            var time = start.AddMinutes((index - 1) * 3);
            string path = Path.Combine(directory, $"monitor-revision-{index:D2}.fits");
            File.WriteAllText(path, payload);
            var evidence = stellar.CompareReference(key, RoundEvidence(flux) with { ShapeTimestampUtc = time }, time, "East");
            var input = new FrameQualityInput {
                FrameIndex = index, TimestampUtc = time, OriginalPath = path, ExposureSeconds = 180,
                StarCount = stars, BackgroundMedian = background, Baseline = baseline.GetSnapshot(key, 4, time), ImageEvidence = evidence,
                Guide = new GuideExposureMetrics { HasData = true, Samples = 90, RmsArcsec = .6, MaxExcursionArcsec = 1, MaxSustainedExcursionSeconds = 0 }
            };
            var result = engine.Evaluate(input, settings);
            result.SourceKind = FrameSourceKind.AdvancedSequencer;
            result.FileActionEligible = true;
            result.ProvenanceFrozen = true;
            result.FinalPath = path;
            baseline.AddMeasurements(key, stars, background, 8, time,
                ExposureAssessment.CanTrainStarCount(result), ExposureAssessment.CanTrainBackground(result));
            if (ExposureAssessment.CanTrainPhotometry(result))
                stellar.AddEvidenceReference(key, evidence, result.Status == FrameStatus.Learning ? FrameStatus.Learning : FrameStatus.Accepted, time, "East", 8);
            review.Record(key, input, result, ExposureAssessment.CanTrainBaseline(result));
            results.Add(result);
            foreach (var change in review.Review(key, result, results, settings)) {
                if (change.Result.FrameIndex == 1) {
                    trackedRevisions++;
                    if (!retainedRevision && change.Result.IsUsable) {
                        retainedRevision = change.Previous.Status == FrameStatus.Learning && !settings.MonitorOnly;
                        retainedMonitorOnly = change.Result.MonitorOnly;
                    } else if (retainedRevision && change.Previous.IsUsable && change.Result.Status == FrameStatus.Rejected)
                        rejectedRevision = true;
                }
                if (RejectedFileService.MayApplyAfterReferenceRevision(change.Previous, change.Result, settings)) {
                    if (change.Result.FrameIndex <= 3) authorizedMonitoredRename = true;
                    change.Result.FinalPath = service.ApplyAsync(change.Previous.FinalPath, settings, true).GetAwaiter().GetResult();
                } else if (RejectedFileService.MayRestoreAfterReferenceRevision(change.Previous, change.Result, settings)) {
                    change.Result.FinalPath = service.RestoreAsync(change.Previous).GetAwaiter().GetResult();
                }
                results[results.FindIndex(f => f.FrameIndex == change.Result.FrameIndex)] = change.Result;
            }
        }

        for (int i = 0; i < 3; i++) Add(500, 1300, .5);
        settings.MonitorOnly = false;
        Add(500, 1300, .5);
        for (int i = 0; i < 8; i++) Add(1000, 1000, 1);

        check(retainedRevision && rejectedRevision && trackedRevisions >= 2,
            "Monitor Only regression exercises retained startup validation followed by rejection against a clean reference");
        check(retainedMonitorOnly && results.Take(3).All(r => r.MonitorOnly && r.Status == FrameStatus.Rejected),
            "acquisition Monitor Only survives both retained and rejected reference revisions after settings change");
        check(!authorizedMonitoredRename && results.Take(3).All(r => File.Exists(r.OriginalPath)
            && File.ReadAllText(r.OriginalPath) == payload && !File.Exists(Path.Combine(directory, "BAD_" + Path.GetFileName(r.OriginalPath)))),
            "multiple reference revisions never authorize or rename files acquired in Monitor Only");
    }
}
