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
            $"Q={result.OverallQuality,5:0.0} C={result.ConfidenceScore,5:0.0} RMS={Fmt(result.GuideRmsArcsec),6} " +
            $"Peak={Fmt(result.MaxGuideExcursionArcsec),6} Sustain={Fmt(result.SustainedGuideExcursionSeconds),5} reasons={result.ReasonText}");
    }

    ValidateArtifacts(scenario, store, scenario.Frames.Count, failures);
    int scenarioFailures = failures.Count - failuresBefore;
    Console.WriteLine(scenarioFailures == 0
        ? $"PROFILE VERDICT: PASS {scenario.Frames.Count}/{scenario.Frames.Count}"
        : $"PROFILE VERDICT: FAIL ({scenarioFailures} mismatch/error records)");
    Console.WriteLine();
}

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
    var json = Path.Combine(store.SessionFolder, "session.json");
    var svg = Path.Combine(store.SessionFolder, "quality.svg");

    foreach (var file in new[] { csv, json, svg }) {
        if (!File.Exists(file) || new FileInfo(file).Length == 0) {
            failures.Add($"{scenario.DisplayName}: artifact missing or empty: {file}");
        }
    }

    if (File.Exists(csv)) {
        var lines = File.ReadLines(csv).ToArray();
        if (lines.Length != expectedFrames + 1) {
            failures.Add($"{scenario.DisplayName}: frames.csv expected {expectedFrames + 1} lines including header, got {lines.Length}");
        }
        if (lines.Length > 0 && !lines[0].Contains("Confidence", StringComparison.Ordinal)) {
            failures.Add($"{scenario.DisplayName}: frames.csv does not persist V2 confidence fields");
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
            if (frameCount > 0 && !frames[0].TryGetProperty("ConfidenceScore", out _)) {
                failures.Add($"{scenario.DisplayName}: session.json does not persist ConfidenceScore");
            }
        } catch (Exception ex) {
            failures.Add($"{scenario.DisplayName}: session.json parse failed: {ex.Message}");
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

static string Fmt(double value) => double.IsNaN(value) ? "N/A" : value.ToString("0.00");
