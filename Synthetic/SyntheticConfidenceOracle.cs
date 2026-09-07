using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.Synthetic;

/// <summary>
/// Deterministic V2 confidence expectations layered on top of the existing whole-night synthetic
/// profiles. The oracle intentionally uses broad semantic bands rather than exact floating-point
/// values so implementation improvements can evolve without weakening the behavioural contract.
/// </summary>
public static class SyntheticConfidenceOracle {
    public static string Validate(SyntheticFrameDefinition expected, FrameQualityResult actual) {
        if (actual == null) return "confidence oracle received null result";

        var problems = new List<string>();

        ValidateFiniteRange(actual.ConfidenceScore, "ConfidenceScore", problems);
        ValidateFiniteRange(actual.ConfidenceDataCompleteness, "ConfidenceDataCompleteness", problems);
        ValidateFiniteRange(actual.ConfidenceBaselineMaturity, "ConfidenceBaselineMaturity", problems);
        ValidateFiniteRange(actual.ConfidenceThresholdSeparation, "ConfidenceThresholdSeparation", problems);
        ValidateFiniteRange(actual.ConfidenceAgreement, "ConfidenceAgreement", problems);

        if (string.IsNullOrWhiteSpace(actual.ConfidenceReason)) {
            problems.Add("ConfidenceReason must not be empty");
        }

        switch (actual.Status) {
            case FrameStatus.Error:
                ExpectRange(actual.ConfidenceScore, 0, 0.01, "ERROR confidence", problems);
                ExpectRange(actual.ConfidenceDataCompleteness, 0, 0.01, "ERROR data completeness", problems);
                break;

            case FrameStatus.Learning:
                ExpectRange(actual.ConfidenceScore, 0, 65, "LEARNING confidence", problems);
                break;

            case FrameStatus.Warning:
                ExpectRange(actual.ConfidenceScore, 35, 72.01, "WARNING confidence", problems);
                break;

            case FrameStatus.Accepted:
                ExpectRange(actual.ConfidenceScore, 55, 100, "ACCEPTED confidence", problems);
                break;

            case FrameStatus.Rejected:
                int channels = CountIndependentAbnormalChannels(actual);
                if (channels >= 2) {
                    ExpectRange(actual.ConfidenceScore, 82, 100, "multi-channel REJECT confidence", problems);
                } else {
                    ExpectRange(actual.ConfidenceScore, 65, 100, "single-channel REJECT confidence", problems);
                }
                break;
        }

        // Named anchor cases guarantee that the score means what the V2 roadmap says it means.
        if (expected.Name.Contains("excellent stable", StringComparison.OrdinalIgnoreCase)) {
            ExpectRange(actual.ConfidenceScore, 80, 100, "excellent-night confidence", problems);
        }

        if (expected.Name.Contains("near background limit warning", StringComparison.OrdinalIgnoreCase) ||
            expected.Name.Contains("warning before collapse", StringComparison.OrdinalIgnoreCase) ||
            expected.Name.Contains("near-limit warning", StringComparison.OrdinalIgnoreCase)) {
            ExpectRange(actual.ConfidenceScore, 35, 72.01, "near-boundary confidence", problems);
        }

        if (expected.Name.Contains("cloud plus bright", StringComparison.OrdinalIgnoreCase) ||
            expected.Name.Contains("mixed cloud and guiding", StringComparison.OrdinalIgnoreCase) ||
            expected.Name.Contains("cloud wall", StringComparison.OrdinalIgnoreCase)) {
            ExpectRange(actual.ConfidenceScore, 85, 100, "strong corroborated anomaly confidence", problems);
        }

        if (expected.Name.Contains("missing ", StringComparison.OrdinalIgnoreCase)) {
            ExpectRange(actual.ConfidenceScore, 0, 0.01, "missing-data confidence", problems);
        }

        return string.Join("; ", problems);
    }

    private static int CountIndependentAbnormalChannels(FrameQualityResult result) {
        bool guide = result.RejectReasons.Any(x => x.Contains("GUIDE", StringComparison.Ordinal));
        bool stars = result.RejectReasons.Contains("STAR_COUNT_DROP", StringComparer.Ordinal);
        bool background = result.RejectReasons.Contains("BACKGROUND_HIGH", StringComparer.Ordinal) ||
                          result.RejectReasons.Contains("BACKGROUND_LOW", StringComparer.Ordinal);
        return (guide ? 1 : 0) + (stars ? 1 : 0) + (background ? 1 : 0);
    }

    private static void ValidateFiniteRange(double value, string name, List<string> problems) {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100) {
            problems.Add($"{name} must be finite and inside 0..100, got {value}");
        }
    }

    private static void ExpectRange(double value, double minInclusive, double maxInclusive, string label, List<string> problems) {
        if (double.IsNaN(value) || value < minInclusive || value > maxInclusive) {
            problems.Add($"{label} expected {minInclusive:0.#}..{maxInclusive:0.#}, got {value:0.##}");
        }
    }
}
