using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Synthetic;
using System.Text.Json;

var failures = new List<string>();
var outputRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), "QualitySessionMeter-SyntheticCheck", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(outputRoot);

var scenarios = SyntheticSessionGenerator.GetScenarios();
var baseTime = new DateTime(2026, 9, 7, 20, 0, 0, DateTimeKind.Utc);
int totalFrames = 0;

Console.WriteLine($"QualitySessionMeter synthetic check: {scenarios.Count} whole-night profiles");
Console.WriteLine($"Output: {outputRoot}");
Console.WriteLine();

foreach (var scenario in scenarios) {
    var settings = SyntheticSessionGenerator.CreateCanonicalSettings();
    var baseline = new BaselineEngine();
    var engine = new QualityEngine();
    var store = new SessionStore(Path.Combine(outputRoot, "sessions", scenario.Id));
    int failuresBefore = failures.Count;

    Console.WriteLine($"=== {scenario.DisplayName} ({scenario.Id}) ===");
    Console.WriteLine(scenario.Description);
    Console.WriteLine(scenario.ExpectedSummary);

    for (int i = 0; i < scenario.Frames.Count; i++) {
        var d = scenario.Frames[i];
        var start = baseTime.AddDays(Array.IndexOf(scenarios.ToArray(), scenario)).AddMinutes(i * 4);
        var key = new BaselineKey(d.Target, d.Filter, d.ExposureSeconds, 100, 1, 1, "SyntheticCam");
        var snapshot = baseline.GetSnapshot(key, settings.MinimumLearningFrames);
        var guideSamples = d.GuideFactory?.Invoke(start, d.ExposureSeconds) ?? Array.Empty<GuideSample>();
        var guide = GuideMetricsCalculator.Calculate(guideSamples, start, d.ExposureSeconds, settings.ExcursionThreshold);

        var input = new FrameQualityInput {
            FrameIndex = i + 1,
            TimestampUtc = start,
            OriginalPath = $"SYNTHETIC://{scenario.Id}/frame_{i + 1:000}.fits",
            Target = d.Target,
            Filter = d.Filter,
            ExposureSeconds = d.ExposureSeconds,
            Gain = 100,
            BinX = 1,
            BinY = 1,
            Camera = "SyntheticCam",
            StarCount = d.StarCount,
            BackgroundMedian = d.BackgroundMedian,
            Baseline = snapshot,
            Guide = guide
        };

        var result = engine.Evaluate(input, settings);
        result.MonitorOnly = true;

        if (result.Status is FrameStatus.Learning or FrameStatus.Accepted) {
            baseline.AddAccepted(key, input.StarCount, input.BackgroundMedian, settings.BaselineWindow);
        }

        Validate(scenario, d, result, failures);
        await store.AppendAsync(result);
        totalFrames++;

        Console.WriteLine(
            $"{i + 1,2}. {d.Name,-42} expected={d.ExpectedStatus,-8} actual={result.Status,-8} " +
            $"Q={result.OverallQuality,5:0.0} C={result.ConfidenceScore,5:0.0} pattern={result.GuidePattern,-18} " +
            $"trend={result.TrendText,-7} reasons={result.ReasonText}");
    }

    failures.AddRange(SyntheticEventOracle.ValidateWholeNight(scenario, store.Results, store.Events));
    ValidateArtifacts(scenario, store, scenario.Frames.Count, failures);
    int scenarioFailures = failures.Count - failuresBefore;
    Console.WriteLine($"Events grouped: {store.Events.Count}");
    Console.WriteLine(scenarioFailures == 0
        ? $"PROFILE VERDICT: PASS {scenario.Frames.Count}/{scenario.Frames.Count}"
        : $"PROFILE VERDICT: FAIL ({scenarioFailures} mismatch/error records)");
    Console.WriteLine();
}

failures.AddRange(SyntheticEventOracle.RunDeterministicGroupingCases());
failures.AddRange(SyntheticGuidePatternOracle.Run());
failures.AddRange(SyntheticTrendOracle.Run());
await ValidateFileActions(outputRoot, failures);

var verdict = failures.Count == 0 ? "PASS" : "FAIL";
Console.WriteLine($"GLOBAL VERDICT: {verdict}");
Console.WriteLine($"Profiles: {scenarios.Count}; Frames exercised: {totalFrames}; Failures: {failures.Count}");

if (failures.Count > 0) {
    foreach (var failure in failures) Console.WriteLine("FAIL: " + failure);
}

var verdictPath = Path.Combine(outputRoot, "SYNTHETIC_CHECK_VERDICT.txt");
await File.WriteAllLinesAsync(
    verdictPath,
    new[] {
        $"VERDICT: {verdict}",
        $"Profiles: {scenarios.Count}",
        $"Frames exercised: {totalFrames}",
        $"Failures: {failures.Count}"
    }.Concat(scenarios.Select(x => $"- {x.DisplayName}: {x.ExpectedSummary}"))
     .Concat(failures.Select(x => "FAIL: " + x)));

return failures.Count == 0 ? 0 : 1;

static void Validate(
    SyntheticSessionDefinition scenario,
    SyntheticFrameDefinition expected,
    FrameQualityResult actual,
    List<string> failures) {

    var prefix = $"{scenario.DisplayName} / frame {actual.FrameIndex} ({expected.Name})";

    if (actual.Status != expected.ExpectedStatus) {
        failures.Add($"{prefix}: expected status {expected.ExpectedStatus}, got {actual.Status}");
    }

    var expectedReasons = expected.ExpectedReasons ?? Array.Empty<string>();
    foreach (var reason in expectedReasons) {
        if (!actual.RejectReasons.Contains(reason, StringComparer.Ordinal)) {
            failures.Add($"{prefix}: missing reason {reason}; actual=[{string.Join(", ", actual.RejectReasons)}]");
        }
    }

    if (expected.ExpectedStatus == FrameStatus.Rejected) {
        var unexpected = actual.RejectReasons.Where(x => !expectedReasons.Contains(x, StringComparer.Ordinal)).ToArray();
        if (unexpected.Length > 0) {
            failures.Add($"{prefix}: unexpected rejection reason(s): {string.Join(", ", unexpected)}");
        }
    }

    if (!string.IsNullOrWhiteSpace(expected.ExpectedErrorContains) &&
        (actual.ErrorMessage?.Contains(expected.ExpectedErrorContains, StringComparison.Ordinal) != true)) {
        failures.Add($"{prefix}: expected error containing {expected.ExpectedErrorContains}, got '{actual.ErrorMessage}'");
    }

    if (expected.ExpectedStarBaseline.HasValue &&
        (double.IsNaN(actual.StarBaseline) || Math.Abs(actual.StarBaseline - expected.ExpectedStarBaseline.Value) > 0.001)) {
        failures.Add($"{prefix}: star baseline expected {expected.ExpectedStarBaseline.Value:0.###}, got {actual.StarBaseline:0.###}");
    }

    if (expected.ExpectedBackgroundBaseline.HasValue &&
        (double.IsNaN(actual.BackgroundBaseline) || Math.Abs(actual.BackgroundBaseline - expected.ExpectedBackgroundBaseline.Value) > 0.001)) {
        failures.Add($"{prefix}: background baseline expected {expected.ExpectedBackgroundBaseline.Value:0.###}, got {actual.BackgroundBaseline:0.###}");
    }

    if (expected.Name.Contains("single 3 arcsec wind spike", StringComparison.Ordinal) &&
        actual.SustainedGuideExcursionSeconds != 0) {
        failures.Add($"{prefix}: single spike must have sustained duration 0, got {actual.SustainedGuideExcursionSeconds:0.###}");
    }

    var confidenceProblem = SyntheticConfidenceOracle.Validate(expected, actual);
    if (!string.IsNullOrWhiteSpace(confidenceProblem)) {
        failures.Add($"{prefix}: confidence oracle: {confidenceProblem}");
    }
}

static void ValidateArtifacts(
    SyntheticSessionDefinition scenario,
    SessionStore store,
    int expectedFrames,
    List<string> failures) {

    var csv = Path.Combine(store.SessionFolder, "frames.csv");
    var eventsCsv = Path.Combine(store.SessionFolder, "events.csv");
    var json = Path.Combine(store.SessionFolder, "session.json");
    var svg = Path.Combine(store.SessionFolder, "quality.svg");
    var html = Path.Combine(store.SessionFolder, "report.html");

    foreach (var file in new[] { csv, eventsCsv, json, svg, html }) {
        if (!File.Exists(file) || new FileInfo(file).Length == 0) {
            failures.Add($"{scenario.DisplayName}: artifact missing or empty: {file}");
        }
    }

    if (File.Exists(csv)) {
        var lines = File.ReadLines(csv).ToArray();
        if (lines.Length != expectedFrames + 1) {
            failures.Add($"{scenario.DisplayName}: frames.csv expected {expectedFrames + 1} lines including header, got {lines.Length}");
        }
        if (lines.Length > 0) {
            var header = lines[0];
            foreach (var required in new[] { "Confidence", "GuidePattern", "StarTrendKind", "BackgroundTrendKind" }) {
                if (!header.Contains(required, StringComparison.Ordinal)) {
                    failures.Add($"{scenario.DisplayName}: frames.csv missing V2 field {required}");
                }
            }
        }
    }

    if (File.Exists(eventsCsv)) {
        var lines = File.ReadLines(eventsCsv).ToArray();
        if (lines.Length != store.Events.Count + 1) {
            failures.Add($"{scenario.DisplayName}: events.csv expected {store.Events.Count + 1} lines including header, got {lines.Length}");
        }
    }

    if (File.Exists(json)) {
        try {
            using var document = JsonDocument.Parse(File.ReadAllText(json));
            var captured = document.RootElement.GetProperty("captured").GetInt32();
            var frames = document.RootElement.GetProperty("frames");
            var frameCount = frames.GetArrayLength();
            if (captured != expectedFrames || frameCount != expectedFrames) {
                failures.Add($"{scenario.DisplayName}: session.json expected {expectedFrames} frames, got captured={captured}, frames={frameCount}");
            }
            if (frameCount > 0) {
                foreach (var required in new[] { "ConfidenceScore", "GuidePattern", "StarTrendKind", "BackgroundTrendKind" }) {
                    if (!frames[0].TryGetProperty(required, out _)) {
                        failures.Add($"{scenario.DisplayName}: session.json missing frame field {required}");
                    }
                }
            }

            int eventCount = document.RootElement.GetProperty("eventCount").GetInt32();
            int serializedEvents = document.RootElement.GetProperty("events").GetArrayLength();
            if (eventCount != store.Events.Count || serializedEvents != store.Events.Count) {
                failures.Add($"{scenario.DisplayName}: session.json event count mismatch, expected {store.Events.Count}, got eventCount={eventCount}, events={serializedEvents}");
            }
        } catch (Exception ex) {
            failures.Add($"{scenario.DisplayName}: session.json parse failed: {ex.Message}");
        }
    }

    if (File.Exists(html)) {
        var body = File.ReadAllText(html);
        foreach (var marker in new[] { "Session events", "Best accepted frames", "Worst accepted frames", "Confidence", "Guide pattern" }) {
            if (!body.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                failures.Add($"{scenario.DisplayName}: report.html missing section/marker '{marker}'");
            }
        }
    }
}

static async Task ValidateFileActions(string outputRoot, List<string> failures) {
    var root = Path.Combine(outputRoot, "file-actions");
    Directory.CreateDirectory(root);
    var settings = SyntheticSessionGenerator.CreateCanonicalSettings();
    settings.MonitorOnly = false;
    var service = new RejectedFileService();

    var prefixSource = Path.Combine(root, "prefix_test.fits");
    await File.WriteAllTextAsync(prefixSource, "synthetic frame");
    settings.RejectedFileAction = RejectedFileAction.PrefixBad;
    var prefixResult = await service.ApplyAsync(prefixSource, settings);
    if (!File.Exists(prefixResult) || File.Exists(prefixSource) ||
        !Path.GetFileName(prefixResult).StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) {
        failures.Add("PrefixBad file action failed its isolated temp-file test.");
    }

    var moveSource = Path.Combine(root, "move_test.fits");
    await File.WriteAllTextAsync(moveSource, "synthetic frame");
    settings.RejectedFileAction = RejectedFileAction.MoveToRejectedFolder;
    var moveResult = await service.ApplyAsync(moveSource, settings);
    if (!File.Exists(moveResult) || File.Exists(moveSource) ||
        !string.Equals(Path.GetFileName(Path.GetDirectoryName(moveResult)), "Rejected", StringComparison.OrdinalIgnoreCase)) {
        failures.Add("MoveToRejectedFolder file action failed its isolated temp-file test.");
    }
}
