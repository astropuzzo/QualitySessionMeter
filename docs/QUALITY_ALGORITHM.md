# QualitySessionMeter — V1 Quality Algorithm

This document is the normative description of the V1 frame-quality algorithm.

## 1. Inputs

Each LIGHT frame is evaluated from two families of measurements.

### Guide-domain measurements

- exposure-specific guiding RMS;
- maximum guide excursion;
- longest sustained interval above the excursion threshold.

### Image-domain measurements

- detected star count;
- image median/background.

HFR, FWHM and eccentricity are intentionally excluded from the V1 decision engine.

## 2. Exposure-scoped guiding

Guide samples are timestamped continuously.

For a frame with exposure interval:

```text
[t_start, t_end]
```

only guide samples whose timestamps fall in that interval are used.

For each sample, total guide error is treated as a two-axis magnitude in arcseconds:

```text
error_total = sqrt(error_RA^2 + error_DEC^2)
```

The exposure RMS is calculated from the total-error sample series:

```text
RMS = sqrt(mean(error_total^2))
```

The exact source RA/DEC values are converted to arcseconds using the guider pixel scale before scoring.

## 3. Guiding hard rules

### 3.1 Exposure RMS

When enabled:

```text
if ExposureRMS > MaxGuideRMS
    fail GUIDE_RMS
```

Default:

```text
MaxGuideRMS = 1.50 arcsec
```

### 3.2 Sustained excursion

A threshold crossing is not enough by itself. The algorithm measures how long guide error stays above the configured threshold.

When enabled:

```text
if longest continuous time(error_total > ExcursionThreshold)
   >= ExcursionMinimumDuration
    fail SUSTAINED_GUIDE_EXCURSION
```

Defaults:

```text
ExcursionThreshold       = 2.00 arcsec
ExcursionMinimumDuration = 2.0 seconds
```

This is designed to catch a wind event or meaningful temporary disturbance without rejecting a frame because of one isolated noisy guider sample.

### 3.3 Hard excursion

When enabled:

```text
if max(error_total) >= HardExcursionThreshold
    fail HARD_GUIDE_EXCURSION
```

Default:

```text
HardExcursionThreshold = 5.00 arcsec
```

This represents an event severe enough that duration is no longer required to consider the frame suspect.

## 4. Adaptive image baseline

The plugin does not use the first frame as a permanent reference.

For each acquisition context, two rolling buffers are maintained:

```text
star counts
background medians
```

Default rolling window:

```text
N = 8
```

Baseline values are the medians of those buffers.

### Minimum learning population

Default:

```text
MinimumLearningFrames = 4
```

Before the required clean reference population exists, the frame state is `LEARNING` unless an independent guide-domain hard rule already proves the frame bad.

### Baseline eligibility

Only trustworthy frames may update the baseline.

Eligible:

```text
clean LEARNING frames
ACCEPTED frames
```

Not eligible:

```text
WARNING
REJECTED
ERROR
```

This is one of the most important algorithmic invariants.

## 5. Acquisition context isolation

Image statistics are compared only with a compatible context.

V1 context includes relevant acquisition parameters such as:

```text
target
filter
exposure time
gain
binning
camera
```

The purpose is not taxonomy; it is preventing invalid comparisons.

Example:

```text
300 s OIII != 60 s Luminance
```

They require different baselines.

## 6. Star-count deviation

Given:

```text
S = current detected star count
B_s = star-count baseline
```

relative deviation is:

```text
star_delta_pct = 100 * (S - B_s) / B_s
```

The rejection rule is interested primarily in negative deviation.

When enabled:

```text
if star_delta_pct <= -MaxStarLossPercent
    fail STAR_COUNT_DROP
```

Default:

```text
MaxStarLossPercent = 35%
```

Example:

```text
baseline = 1816 stars
current  = 1110 stars

delta = -38.9%
=> hard fail with a 35% threshold
```

## 7. Background deviation

Given:

```text
M = current image median/background
B_m = background baseline
```

relative deviation is:

```text
background_delta_pct = 100 * (M - B_m) / B_m
```

Both directions are meaningful.

When enabled:

```text
if background_delta_pct >= MaxBackgroundIncreasePercent
    fail BACKGROUND_HIGH

if background_delta_pct <= -MaxBackgroundDecreasePercent
    fail BACKGROUND_LOW
```

Defaults:

```text
MaxBackgroundIncreasePercent = 30%
MaxBackgroundDecreasePercent = 30%
```

## 8. Hard reject decision

Hard rejection is Boolean and independent of the Quality Meter.

The rule is:

```text
REJECTED if ANY enabled hard rule fails
```

There is no voting system and no requirement for two simultaneous failures.

Examples:

```text
Guide good + background good + stars -45%
=> REJECTED
```

```text
Stars good + background good + sustained guide excursion
=> REJECTED
```

## 9. Quality sub-scores

V1 produces up to four 0–100 sub-scores:

```text
Guiding Quality
Guiding Stability
Transparency Quality
Background Quality
```

The score functions are continuous representations of distance from normal/threshold conditions. Hard thresholds remain authoritative for reject decisions.

The intended qualitative behaviour is:

- 100 = essentially normal/excellent;
- score decreases as the metric approaches its configured failure boundary;
- score falls aggressively once behaviour becomes clearly abnormal;
- missing/unavailable metrics must not be silently treated as perfect data.

The exact V1 mapping implemented in `QualityEngine.cs` is the executable source for numeric interpolation details; this document defines the invariants rather than freezing every tuning coefficient forever.

## 10. Overall Quality Score

A plain arithmetic mean can hide a catastrophic single metric.

Therefore V1 combines the worst available metric with the average of available metrics:

```text
Overall = WorstMetricWeight * WorstMetric
        + (1 - WorstMetricWeight) * AverageMetric
```

Default:

```text
WorstMetricWeight = 0.70
```

Example:

```text
Guiding      = 100
Stability    = 98
Transparency = 20
Background   = 94

average = 78
worst   = 20

overall = 0.70 * 20 + 0.30 * 78
        = 37.4
```

The displayed score is therefore approximately:

```text
37 / 100
```

instead of the misleading plain mean of 78.

## 11. Quality labels

```text
90–100  EXCELLENT
80–89   GOOD
65–79   FAIR
50–64   POOR
0–49    BAD
```

Labels are descriptive, not hard-reject rules.

## 12. Status assignment

V1 states are assigned conceptually as follows:

```text
if analysis cannot be completed safely
    ERROR
else if any hard rule fails
    REJECTED
else if baseline is not yet sufficiently learned
    LEARNING
else if OverallQuality < 65
    WARNING
else
    ACCEPTED
```

The implementation may order some internal checks differently where required for fail-safe handling, but these semantics must remain true.

## 13. Monitor Only mode

Monitor Only changes the action, not the analysis.

A frame that would fail a hard rule is still recorded with the same metrics/reasons, but the UI presents the non-destructive result and no rename/move occurs.

Conceptually:

```text
classification = REJECTED
operational presentation = WOULD REJECT
file action = none
```

## 14. Probable cause classification

Cause classification is diagnostic and secondary to hard-rule results.

Typical mappings:

### Wind / guiding disturbance

```text
guide excursion abnormal
stars near baseline
background near baseline
```

### Cloud / transparency loss

```text
stars strongly reduced
guide normal
background may be near normal or changed
```

### Cloud / bright sky event

```text
stars reduced
background strongly increased
guide normal
```

The classifier must never suppress a hard reason or turn an advisory cause into a stronger statement than the underlying metrics support.

## 15. Session metrics

### Acceptance rate

```text
AcceptanceRate = AcceptedOrUsableCount / ClassifiedCapturedCount * 100
```

The UI keeps rejected fraction separate from accepted-frame quality.

### Session quality

Session Quality is based primarily on accepted/usable frame Quality Scores.

Rejected frames are not averaged into this number because that would conflate two different questions:

1. How good is the usable data?
2. How much acquisition time was rejected?

These are shown separately as:

```text
Accepted Session Quality
Acceptance Rate
```

## 16. V2 algorithmic direction

V2 may improve interpretation with:

- confidence scores;
- event grouping across consecutive frames;
- guide time-series pattern classification;
- slow-trend vs sudden-anomaly separation;
- smarter baseline inertia;
- frame ranking.

V2 should not reintroduce HFR/FWHM/eccentricity as mandatory hard-rejection criteria.

## 17. V3 algorithmic direction

V3 adds sequence control without changing the meaning of V1 classification.

For a requested target of `N` valid frames:

```text
CapturedCount  = every physical exposure
AcceptedCount  = only frames classified valid/accepted
RejectedCount  = rejected frames

Progress = AcceptedCount / RequestedValidFrames
```

Example:

```text
physically captured = 203
rejected            = 8
accepted             = 195
requested            = 300

sequence progress = 195 / 300
```

A rejected frame never consumes one unit of valid-frame progress.
