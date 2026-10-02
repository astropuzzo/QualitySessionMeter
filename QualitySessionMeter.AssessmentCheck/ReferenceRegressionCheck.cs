using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Plugin.QualitySessionMeter.Testing;

/// <summary>Reference regressions run independently of filesystem fallback and UI checks.</summary>
internal static class ReferenceRegressionCheck {
    private sealed class Session {
        private readonly BaselineKey key = new("Reference test", "L", 180, 100, 1, 1, "Camera");
        private readonly DateTime start = new(2026, 10, 2, 20, 0, 0, DateTimeKind.Utc);
        private readonly QualityEngine engine = new();
        private readonly BaselineEngine baseline = new();
        private readonly StellarAnalysisPipeline stellar;
        private readonly ReferenceReview review;
        private readonly QualitySettings settings;
        private TimeSpan timeShift;
        public List<FrameQualityResult> Results { get; } = new();
        public Session(bool image = false, bool catalog = false) {
            settings = new(new InMemoryPluginOptionsAccessor()) {
                Enabled = true, MonitorOnly = true, ImageEvidenceEnabled = image, MinimumLearningFrames = 4, BaselineWindow = 8,
                MaxStarLossPercent = 35, MaxBackgroundIncreasePercent = 30, MaxBackgroundDecreasePercent = 30
            };
            stellar = catalog ? new StellarAnalysisPipeline() : null;
            review = new(baseline, engine, stellar);
        }
        public BaselineSnapshot Snapshot => baseline.GetSnapshot(key, 4);
        public void Advance(TimeSpan elapsed) => timeShift += elapsed;
        public void Add(int stars, double background, double flux = 1, double guideRms = .6, double axisRatio = 1.1, bool failedComparison = false) {
            var time = start.AddMinutes(Results.Count * 3) + timeShift;
            var evidence = settings.ImageEvidenceEnabled ? Round(flux) : new ImageEvidence();
            if (settings.ImageEvidenceEnabled) evidence = evidence with {AxisRatio = axisRatio};
            if (failedComparison) evidence = evidence with {PhotometryState=PhotometryState.InsufficientMatches,ReferenceFrames=0,MatchedStars=0,RelativeFlux=double.NaN};
            if (stellar != null) evidence = stellar.CompareReference(key, evidence, time, "East");
            var input = new FrameQualityInput {
                FrameIndex = Results.Count + 1, TimestampUtc = time, ExposureSeconds = 180,
                StarCount = stars, BackgroundMedian = background, Baseline = baseline.GetSnapshot(key, 4, time), ImageEvidence = evidence,
                Guide = new GuideExposureMetrics { HasData = true, Samples = 90, RmsArcsec = guideRms, MaxExcursionArcsec = 1, MaxSustainedExcursionSeconds = 0 }
            };
            var result = engine.Evaluate(input, settings);
            baseline.AddMeasurements(key, stars, background, 8, time,
                ExposureAssessment.CanTrainStarCount(result), ExposureAssessment.CanTrainBackground(result));
            if (stellar != null && ExposureAssessment.CanTrainPhotometry(result))
                stellar.AddEvidenceReference(key, evidence, FrameStatus.Accepted, time, "East", 8);
            review.Record(key, input, result, ExposureAssessment.CanTrainBaseline(result));
            Results.Add(result);
            foreach (var change in review.Review(key, result, Results, settings))
                Results[Results.FindIndex(f => f.FrameIndex == change.Result.FrameIndex)] = change.Result;
        }
        private static ImageEvidence Round(double flux) => new() {
            Available = true, Stars = 40, AxisRatio = 1.1, ElongatedFraction = 0, TailStrength = 0, DoublePeakStrength = 0, FwhmPixels = 3,
            RelativeFlux = flux, RelativeFluxUpperBound = flux + .005, ReferenceFrames = 2, MatchedStars = 40,
            PhotometryState = PhotometryState.Reliable, PhotometryCoverageReliable = true, PhotometryRegionsExpected = 1, PhotometryRegionsMeasured = 1,
            SampleWidth = 512, SampleHeight = 512, SourceWidth = 512, SourceHeight = 512, PixelsPerSample = 1, PierSide = "East",
            Catalog = Enumerable.Range(0, 40).Select(i => new StarObservation(55 + i % 8 * 48, 55 + i / 8 * 48, 10000 * flux, 10000 * flux, 3, 10)).ToArray()
        };
    }

    public static void Run(Action<bool, string> check) {
        var warnings = new Session(image: true);
        warnings.Add(1000, 1000); warnings.Add(1000, 1000);
        warnings.Add(700, 1000, .75); warnings.Add(700, 1000, .75);
        check(Math.Abs(warnings.Snapshot.StarMedian - 1000) < 1 && warnings.Snapshot.StarSamples == 2,
            "moderate provisional attenuation cannot mature or lower the clean reference");
        warnings.Add(1000, 1000); warnings.Add(1000, 1000);
        check(warnings.Results.Skip(2).Take(2).All(r => r.IsUsable && r.Status == FrameStatus.Warning && !ExposureAssessment.CanTrainBaseline(r)),
            "moderate startup losses remain usable warnings after clean-reference validation");

        var rise = new Session();
        for (int i = 0; i < 18; i++) rise.Add(1000 + i * 70, 1000);
        check(rise.Results.All(r => r.IsUsable), "coherent rising star count does not retrospectively reject early frames");
        var decline = new Session();
        for (int i = 0; i < 28; i++) decline.Add(1000 - i * 15, 1000 + i * 20);
        check(decline.Results.All(r => r.IsUsable), "coherent descending star count and rising background preserve normal drift");

        var mixed = new Session(image: true, catalog: true);
        for (int i = 0; i < 3; i++) mixed.Add(500, 1300, .5);
        for (int i = 0; i < 9; i++) mixed.Add(1000, 1000);
        check(mixed.Results.Take(3).All(r => r.Status == FrameStatus.Rejected) && mixed.Results.Skip(3).All(r => r.IsUsable),
            "mixed cloudy/clear startup uses an observed level and is corrected when clear evidence persists");

        var interrupted = new Session();
        for (int i = 0; i < 4; i++) interrupted.Add(500, 1600);
        for (int i = 0; i < 3; i++) interrupted.Add(1000, 1000);
        // Recorded rejections caused by a superseded background reference must also be revisited.
        foreach (var frame in interrupted.Results.Skip(4).Take(3)) { frame.Status = FrameStatus.Rejected; frame.RejectReasons.Add("BACKGROUND_LOW"); }
        interrupted.Add(500, 1600);
        for (int i = 0; i < 4; i++) interrupted.Add(1000, 1000);
        check(interrupted.Results.Skip(4).Take(3).All(r => r.IsUsable && r.ReferenceRevised)
            && interrupted.Results.Skip(8).All(r => r.IsUsable), "interrupted clearing restores earlier rejected clear frames as well as the final run");

        var anchors = new Session(image: true, catalog: true);
        for (int i = 0; i < 4; i++) anchors.Add(500, 1300, .5);
        for (int i = 0; i < 8; i++) anchors.Add(1000, 1000);
        for (int i = 1; i <= 25; i++) anchors.Add((int)Math.Round(1000 * Math.Pow(.95, i)), 1000, Math.Pow(.95, i));
        var low = anchors.Results[^1];
        check(anchors.Results.Take(4).All(r => r.Status == FrameStatus.Rejected), "new clear evidence rejects the initially contaminated reference frames");
        check(low.Status == FrameStatus.Rejected && low.RejectReasons.Contains("LOW_SESSION_SIGNAL")
            && low.ImageEvidence.SessionRelativeFlux < .35, "rejected startup catalogs no longer weaken the minimum-session-signal floor");

        var guiding = new Session(image: true, catalog: true);
        for (int i = 0; i < 4; i++) guiding.Add(500, 1300, .5);
        guiding.Add(1000, 1000, 1, 4);
        for (int i = 0; i < 4; i++) guiding.Add(1000, 1000);
        check(guiding.Results[4].Status == FrameStatus.Rejected && guiding.Results[4].RejectReasons.Contains("GUIDE_RMS"),
            "reference correction preserves independent unresolved guiding rejection");

        var naturalPhotometry = new Session(image:true,catalog:true);
        for(int i=0;i<28;i++) naturalPhotometry.Add((int)Math.Round(1000*Math.Exp(-.015*i)),1000*Math.Exp(.018*i),Math.Exp(-.015*i));
        check(naturalPhotometry.Results.All(r=>r.IsUsable), "natural matched-signal startup and continuation stay usable");
        check(naturalPhotometry.Results.Take(2).All(r=>!r.ImageEvidence.PhotometryAvailable), "startup validation never manufactures past photometry from future self-matches");

        var shapeWarningBackground = new Session(image:true);
        for(int i=0;i<4;i++) shapeWarningBackground.Add(1000,1000);
        for(int i=1;i<=20;i++) shapeWarningBackground.Add(1000,1000*Math.Pow(.97,i),1,axisRatio:1.22);
        check(shapeWarningBackground.Results.All(r=>r.IsUsable) && shapeWarningBackground.Snapshot.BackgroundMedian < 650,
            "independent borderline-shape warnings do not freeze a gradual healthy background");

        var moderateStep = new Session(image:true);
        for(int i=0;i<8;i++) moderateStep.Add(1000,1000);
        for(int i=0;i<4;i++) moderateStep.Add(900,1250,.85);
        check(moderateStep.Results.Skip(8).All(r=>r.Status==FrameStatus.Warning && !ExposureAssessment.CanTrainBaseline(r))
            && moderateStep.Snapshot.StarMedian==1000 && moderateStep.Snapshot.BackgroundMedian==1000,
            "correlated moderate 15-percent signal step warns without contaminating references");

        var comparisonFailure = new Session(image:true);
        for(int i=0;i<8;i++) comparisonFailure.Add(1000,1000);
        comparisonFailure.Add(1000,1000,failedComparison:true);
        check(comparisonFailure.Results[^1].Status==FrameStatus.Warning && comparisonFailure.Results[^1].ReviewReasons.Contains("SIGNAL_COMPARISON_FAILED")
            && !ExposureAssessment.CanTrainPhotometry(comparisonFailure.Results[^1]) && !ExposureAssessment.CanTrainBaseline(comparisonFailure.Results[^1]),
            "failed mature photometry keeps the frame for review and cannot train any linked reference");

        var independentBackground = new Session();
        for (int i = 0; i < 4; i++) independentBackground.Add(1000, 1000, guideRms: 4);
        for (int i = 0; i < 4; i++) independentBackground.Add(1000, 1000);
        check(independentBackground.Results.Take(4).All(r => r.Status == FrameStatus.Rejected)
            && independentBackground.Snapshot.StarSamples == 4 && independentBackground.Snapshot.BackgroundSamples == 8,
            "provisional validation retains valid background from independent guiding rejections");

        var longHaze = new Session(image:true,catalog:true);
        for(int i=0;i<20;i++) longHaze.Add(500,1300,.5);
        for(int i=0;i<5;i++) longHaze.Add(1000,1000);
        check(longHaze.Results.Take(20).All(r=>r.Status==FrameStatus.Rejected) && longHaze.Results.Skip(20).All(r=>r.IsUsable)
            && Math.Abs(longHaze.Results[^1].ImageEvidence.SessionRelativeFlux-1)<.02,
            "an extended contaminated startup still repairs the session photometric anchors");

        var lateClearing = new Session(image: true, catalog: true);
        for (int i = 0; i < 18; i++) lateClearing.Add(1000 + i * 70, 2000 - i * 30, 1 + i * .07);
        for (int i = 0; i < 6; i++) lateClearing.Add(600, 2200, .5);
        for (int i = 0; i < 5; i++) lateClearing.Add(4000, 1000, 4);
        check(lateClearing.Results.Take(18).All(r => r.IsUsable),
            "late clearing cannot reclassify an earlier healthy gradual rise against a future absolute level");

        var expiredReference = new Session(image: true, catalog: true);
        for (int i = 0; i < 8; i++) expiredReference.Add(1000, 1000);
        expiredReference.Advance(TimeSpan.FromHours(7));
        for (int i = 0; i < 4; i++) expiredReference.Add(900, 1050, .8);
        check(expiredReference.Results.Skip(8).All(r => r.IsUsable) && expiredReference.Snapshot.StarSamples == 4
            && expiredReference.Snapshot.BackgroundSamples == 4 && expiredReference.Results[^1].ImageEvidence.PhotometryAvailable,
            "an expired context bootstraps fresh references despite an ordinary five-percent sky change");

        var incompleteExpired = new Session(image: true, catalog: true);
        incompleteExpired.Add(500, 1000, .5); incompleteExpired.Add(500, 1000, .5);
        incompleteExpired.Advance(TimeSpan.FromHours(7));
        for (int i = 0; i < 4; i++) incompleteExpired.Add(1000, 1050);
        check(incompleteExpired.Results.Take(2).All(r => r.Status == FrameStatus.Learning && !r.ReferenceRevised)
            && incompleteExpired.Results.Skip(2).All(r => r.IsUsable) && incompleteExpired.Snapshot.StarSamples == 4,
            "an interrupted provisional startup cannot be mixed with a new reference epoch after expiry");
    }
}
