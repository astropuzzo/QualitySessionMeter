using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;

/// <summary>
/// Replays star-count/background sessions through the same engine, baseline and reference review the
/// runtime uses, applying retrospective verdicts exactly as the session store does.
/// </summary>
internal static class ReferenceReviewCheck {
    private sealed class Session {
        private static readonly BaselineKey Key = new("M31", "L", 180, 100, 1, 1, "Camera");
        private static readonly DateTime Start = new(2026, 9, 20, 21, 0, 0, DateTimeKind.Utc);
        private readonly BaselineEngine baseline = new();
        private readonly QualityEngine engine = new();
        private readonly ReferenceReview review;
        private readonly QualitySettings settings;
        public readonly List<FrameQualityResult> Results = new();
        public int Reclassified;

        public Session(QualitySettings settings) {
            this.settings = settings;
            review = new ReferenceReview(baseline, engine);
        }

        public void Add(int stars, double background) {
            var time = Start.AddMinutes(3 * Results.Count);
            var input = new FrameQualityInput {
                FrameIndex = Results.Count + 1, TimestampUtc = time, ExposureSeconds = 180, StarCount = stars, BackgroundMedian = background,
                Baseline = baseline.GetSnapshot(Key, settings.MinimumLearningFrames, time), ImageEvidence = new ImageEvidence(),
                Guide = new GuideExposureMetrics { HasData = true, Samples = 90, RmsArcsec = .6, MaxExcursionArcsec = 1, MaxSustainedExcursionSeconds = 0 }
            };
            var result = engine.Evaluate(input, settings);
            bool train = ExposureAssessment.CanTrainBaseline(result);
            if (train) baseline.AddAccepted(Key, stars, background, settings.BaselineWindow, time);
            review.Record(Key, input, result, train);
            Results.Add(result);
            foreach (var change in review.Review(Key, result, Results, settings)) {
                Results[Results.FindIndex(x => x.FrameIndex == change.Result.FrameIndex)] = change.Result;
                Reclassified++;
            }
        }

        public FrameStatus Status(int frame) => Results[frame - 1].Status;
        public bool All(IEnumerable<int> frames, FrameStatus status) => frames.All(f => Status(f) == status);
        public double StarReference => baseline.GetSnapshot(Key, settings.MinimumLearningFrames).StarMedian;
    }

    public static void Run(Action<bool, string> check) {
        QualitySettings Settings() => new(new InMemoryPluginOptionsAccessor()) {
            Enabled = true, MonitorOnly = true, ImageEvidenceEnabled = false, MinimumLearningFrames = 4, BaselineWindow = 8,
            MaxStarLossPercent = 35, MaxBackgroundIncreasePercent = 30, MaxBackgroundDecreasePercent = 30, MaxGuideRms = 1.5
        };

        var clean = new Session(Settings());
        foreach (var stars in new[] { 1000, 980, 1015, 995 }) clean.Add(stars, 1000);
        check(clean.All(new[] { 1, 2, 3, 4 }, FrameStatus.Accepted) && clean.Results.Take(4).All(r => r.ReferenceRevised),
            "provisional frames become ACCEPTED once they agree on a reference");
        clean.Add(1005, 1000);
        check(clean.Status(5) == FrameStatus.Accepted, "frames after reference validation are assessed normally");

        var oneCloud = new Session(Settings());
        oneCloud.Add(1000, 1000); oneCloud.Add(980, 1005); oneCloud.Add(450, 1400); oneCloud.Add(1010, 995);
        check(oneCloud.Status(3) == FrameStatus.Rejected && oneCloud.Results[2].RejectReasons.Contains("STAR_COUNT_DROP"),
            "a cloudy provisional frame is rejected and cannot enter the reference");
        check(oneCloud.All(new[] { 1, 2, 4 }, FrameStatus.Learning), "remaining frames stay provisional until enough of them agree");
        oneCloud.Add(1000, 1000);
        check(oneCloud.All(new[] { 1, 2, 4, 5 }, FrameStatus.Accepted), "the agreeing frames are accepted once the reference is complete");
        check(Math.Abs(oneCloud.StarReference - 1000) < 30, "the reference excludes the rejected cloudy frame");

        var cloudyStart = new Session(Settings());
        for (int i = 0; i < 6; i++) cloudyStart.Add(500 + i * 5, 1300);
        check(cloudyStart.All(Enumerable.Range(1, 6), FrameStatus.Accepted), "a uniformly cloudy start cannot be recognised from itself");
        for (int i = 0; i < 5; i++) cloudyStart.Add(1000 + i * 3, 1000);
        check(cloudyStart.All(Enumerable.Range(1, 6), FrameStatus.Rejected), "a clearer run rebuilds the reference and rejects the cloudy start");
        check(cloudyStart.All(Enumerable.Range(7, 5), FrameStatus.Accepted), "frames in the clearer run are accepted");
        check(cloudyStart.StarReference > 900, "the rebuilt reference follows the clearer conditions");

        var hazyStart = new Session(Settings());
        for (int i = 0; i < 5; i++) hazyStart.Add(520, 1600);
        for (int i = 0; i < 6; i++) hazyStart.Add(1000, 1000);
        check(hazyStart.All(Enumerable.Range(1, 5), FrameStatus.Rejected), "a hazy bright-sky start is rejected once the sky clears");
        check(hazyStart.All(Enumerable.Range(6, 6), FrameStatus.Accepted), "darker clear frames are no longer stuck as BACKGROUND_LOW");

        var lateImprovement = new Session(Settings());
        for (int i = 0; i < 20; i++) lateImprovement.Add(800, 1000);
        for (int i = 0; i < 5; i++) lateImprovement.Add(1300, 1000);
        check(lateImprovement.All(Enumerable.Range(1, 25), FrameStatus.Accepted), "late in a context, earlier verdicts stand after an improvement");
        lateImprovement.Add(800, 1000);
        check(lateImprovement.Status(26) == FrameStatus.Rejected, "a return to the old level is judged against the improved reference");

        var gradualDecline = new Session(Settings());
        for (int i = 0; i < 12; i++) gradualDecline.Add(1000 - i * 15, 1000 + i * 20);
        check(gradualDecline.All(Enumerable.Range(1, 12), FrameStatus.Accepted), "a gradual decline never triggers a retrospective rejection");
    }
}
