# QualitySessionMeter — Project Specification

**Status:** V1 implemented and compiling; V2/V3 planned.  
**Target application:** N.I.N.A.  
**Target runtime:** .NET 8 / Windows  
**Working/public name:** QualitySessionMeter (`QSM`)

This document is the functional source of truth for the project.

## 1. Product vision

Long astrophotography sequences routinely contain individual exposures degraded by transient conditions: wind gusts, temporary guiding excursions, clouds, haze/fog, transparency loss, or sudden sky-background changes.

QualitySessionMeter adds a real-time frame-quality layer to N.I.N.A. so each acquired LIGHT exposure is immediately evaluated, scored, logged, and—when configured—marked or moved if it fails.

The key product principle is:

> A completed camera exposure is not automatically a valid astrophotography exposure.

The plugin must never delete rejected frames as part of normal operation.

## 2. Core product rules

### 2.1 Preserve user data

Supported rejected-file actions:

```text
Monitor Only
Keep in place
Prefix BAD_
Move to Rejected subfolder
```

Deletion is not a normal feature.

### 2.2 Any hard failure may reject

```text
ANY enabled hard rule fails -> REJECTED
```

A second failing metric is not required.

### 2.3 Quality Score is separate from reject logic

Every assessable frame receives a 0–100 Quality Score.

Hard thresholds determine rejection. The score describes overall frame quality.

### 2.4 Robust adaptive references

Do not use the first frame as a permanent baseline.

Use a rolling median of prior trustworthy frames, isolated by acquisition context.

Rejected/warning/error frames must not contaminate the baseline.

### 2.5 Deliberate exclusion of HFR/FWHM/eccentricity

HFR, FWHM and eccentricity are not core rejection metrics because real wind-damaged point–streak–point star profiles can remain deceptively similar under those summaries.

V1 focuses on guiding time-series behaviour, star count, and background.

---

# 3. V1 — Real-Time Frame Quality Monitor

V1 is a complete usable plugin, not a placeholder release.

## 3.1 Quality Meter

Required from the first release:

```text
0–100 score per frame
quality label
sub-scores
session quality
acceptance rate
persistent quality timeline
```

Sub-scores:

- Guiding Quality;
- Guiding Stability;
- Transparency Quality;
- Background Quality.

Overall score uses a worst-metric-dominant formula instead of a plain average.

Default:

```text
Overall = 0.70 * WorstMetric + 0.30 * AverageMetric
```

Labels:

```text
90–100 EXCELLENT
80–89  GOOD
65–79  FAIR
50–64  POOR
0–49   BAD
```

## 3.2 Guiding criteria

### Exposure RMS

Default hard threshold:

```text
1.50 arcsec
```

### Sustained excursion

Default:

```text
error > 2.00 arcsec for at least 2.0 seconds
```

This detects meaningful temporary movement while avoiding rejection from one isolated guide sample.

### Hard excursion

Default:

```text
5.00 arcsec
```

A sufficiently large excursion may reject even when brief.

## 3.3 Image criteria

### Star-count loss

Compare detected stars against an adaptive rolling baseline.

Default:

```text
maximum loss = 35%
```

### Background/median deviation

Compare image median/background against adaptive baseline.

Defaults:

```text
maximum increase = +30%
maximum decrease = -30%
```

Both directions matter because different cloud/fog/sky-light conditions can raise or lower the measured background.

## 3.4 Baseline engine

Defaults:

```text
rolling window = 8 trustworthy frames
minimum learning population = 4
statistic = median
```

Separate baseline contexts include at least:

- target;
- filter;
- exposure time;
- gain;
- binning;
- camera.

Eligible to update baseline:

```text
clean LEARNING
ACCEPTED
```

Not eligible:

```text
WARNING
REJECTED
ERROR
```

## 3.5 V1 frame states

```text
LEARNING
ACCEPTED
WARNING
REJECTED
ERROR
```

`WARNING` means no hard rule failed, but aggregate quality is low enough to deserve attention.

## 3.6 Probable cause

V1 includes basic cause classification such as:

```text
WIND / GUIDING DISTURBANCE
CLOUD / TRANSPARENCY LOSS
CLOUD / BRIGHT SKY EVENT
BACKGROUND ANOMALY
```

Cause is advisory. Explicit hard reasons remain the authoritative explanation.

## 3.7 V1 user interface

Dockable N.I.N.A. panel must show:

- current Quality Score and label;
- current state;
- rejection reason(s);
- probable cause;
- Guide RMS;
- maximum excursion;
- star count and baseline deviation;
- background median and baseline deviation;
- captured/usable/rejected counters;
- acceptance rate;
- accepted-frame session quality;
- live Quality timeline;
- per-frame history;
- session folder access;
- reset session action.

## 3.8 V1 persistence

Per-session outputs:

```text
frames.csv
session.json
quality.svg
```

Session data must contain enough raw and derived telemetry to support V2 analysis without needing to reconstruct information from screenshots.

## 3.9 V1 Monitor Only

Default/recommended first-night mode.

Analysis remains fully active, but no file is moved or renamed.

This is required for safe threshold calibration.

## 3.10 V1 definition of done

V1 is considered functionally complete when:

- N.I.N.A. loads the plugin;
- LIGHT frames are automatically observed;
- image statistics are collected;
- guider samples are scoped to the exposure interval;
- adaptive baselines work;
- rejected frames cannot contaminate baselines;
- hard rejection works;
- Quality Score works;
- current frame and session data appear in the UI;
- logs/graphs persist;
- Monitor Only works;
- configured file action is safe;
- analysis failures do not endanger acquisition files;
- project compiles in Windows CI against the target N.I.N.A. plugin package.

As of the current V1 implementation, the Windows CI build is successful. Real-night validation remains necessary before treating default thresholds as calibrated values.

---

# 4. V2 — Smart Quality Analysis

V2 makes the existing signal set more intelligent instead of adding unreliable metrics.

## 4.1 Confidence score

Each assessment gains a confidence value.

Examples:

```text
Quality 43 / Confidence 99%
Severe guiding disturbance
```

```text
Quality 61 / Confidence 54%
Possible transparency loss; baseline still weak
```

## 4.2 Event grouping

Consecutive related bad frames should be grouped into temporal events.

Example:

```text
22:51–23:05
Cloud/transparency event
3 rejected frames
```

## 4.3 Guide-pattern analysis

Use the guide time series to distinguish patterns such as:

- isolated spike;
- sustained excursion;
- oscillation;
- drift;
- wind-like disturbance;
- possible mechanical/cable event.

## 4.4 Slow trend vs sudden anomaly

V2 baseline logic should follow slow physically plausible drift while still reacting strongly to abrupt changes.

Example slow trend:

```text
1900 -> 1870 -> 1830 -> 1800 -> 1760
```

Example anomaly:

```text
1900 -> 1870 -> 1840 -> 1810 -> 1040
```

The first may be natural evolution; the second is an event.

## 4.5 Multichannel timeline

Combined visualization of:

- overall quality;
- star count;
- background;
- RMS;
- excursions;
- detected events.

## 4.6 Frame ranking

Rank accepted frames by Quality Score.

Useful views:

```text
best accepted frames
worst accepted frames
```

This helps later PixInsight/Siril selection.

## 4.7 Advanced report

V2 may export an interactive HTML report with:

- session overview;
- quality timeline;
- rejected frames;
- grouped events;
- best/worst exposures;
- rejection-reason distribution;
- guiding timeline;
- filter breakdown.

---

# 5. V3 — Quality-Controlled Acquisition

V3 changes sequence progress from physical exposures to valid exposures.

This is the defining V3 feature.

## 5.1 Valid Frame Target

If the user requests:

```text
300 valid frames
```

and N.I.N.A. has physically captured:

```text
203 frames
```

with:

```text
8 rejected
```

then progress must be:

```text
195 / 300
```

not:

```text
203 / 300
```

A rejected frame does not consume a unit of valid-sequence progress.

## 5.2 Counter model

Maintain separate counters:

```text
CapturedCount
AcceptedCount
RejectedCount
```

Conceptual loop:

```text
while AcceptedCount < RequestedValidFrames:
    capture
    CapturedCount++
    analyze

    if accepted:
        AcceptedCount++
    else if rejected:
        RejectedCount++
```

Do not implement this as "capture N and re-acquire rejected later".

Do not mutate/decrement the standard captured counter after the fact.

The acquisition objective is **N valid frames from the beginning**.

## 5.3 Sequence UI

Example:

```text
195 / 300 VALID
203 captured
8 rejected
Quality 91
```

The large progress indicator must represent accepted valid frames.

## 5.4 Multiple filters

Each requested block tracks valid progress independently.

Example:

```text
L: 206 / 300 valid (217 captured, 11 rejected)
R: 100 / 100 valid (108 captured, 8 rejected) COMPLETE
G: 70 / 100 valid (72 captured, 2 rejected)
```

## 5.5 Count mode

Potential user setting:

```text
Captured exposures
Accepted exposures
```

Traditional N.I.N.A. behaviour remains available.

## 5.6 Auto calibration

From a stable initial population, V3 may propose sensible thresholds from observed variation.

Modes should allow:

```text
OFF
Suggest only
Automatic
```

Default direction: `Suggest only`.

## 5.7 Auto pause/resume

Persistent degradation may pause acquisition, but one bad frame should normally not.

Resume must require confirmed recovery, e.g. several consecutive healthy checks.

## 5.8 Environmental correlation

Optional correlation where sensors exist:

- wind;
- humidity;
- cloud sensor;
- SQM;
- temperature;
- dew point.

The plugin must remain fully usable without those sensors.

## 5.9 Predictive degradation

Possible V3 feature: detect worsening trends and warn that rejection is likely within the next exposure(s).

Prediction should inform sequence control, not pre-reject an image that has not been acquired.

---

# 6. Version matrix

| Feature | V1 | V2 | V3 |
|---|:---:|:---:|:---:|
| Quality Meter 0–100 | ✓ | ✓ | ✓ |
| Guide RMS | ✓ | ✓ | ✓ |
| Sustained excursion | ✓ | ✓ | ✓ |
| Hard excursion | ✓ | ✓ | ✓ |
| Adaptive star baseline | ✓ | ✓ | ✓ |
| Adaptive background baseline | ✓ | ✓ | ✓ |
| Any-rule rejection | ✓ | ✓ | ✓ |
| Basic probable cause | ✓ | ✓ | ✓ |
| Live quality graph | ✓ | ✓ | ✓ |
| Persistent logs | ✓ | ✓ | ✓ |
| Monitor Only | ✓ | ✓ | ✓ |
| Safe rejected-file handling | ✓ | ✓ | ✓ |
| Confidence |  | ✓ | ✓ |
| Event grouping |  | ✓ | ✓ |
| Guide-pattern analysis |  | ✓ | ✓ |
| Slow-trend compensation |  | ✓ | ✓ |
| Frame ranking |  | ✓ | ✓ |
| Interactive report |  | ✓ | ✓ |
| Auto calibration |  |  | ✓ |
| Adaptive threshold suggestions |  |  | ✓ |
| Environmental correlation |  |  | ✓ |
| Auto pause/resume |  |  | ✓ |
| Accepted-only sequence progress |  |  | ✓ |
| Valid Frame Target |  |  | ✓ |
| Predictive degradation |  |  | ✓ |

---

# 7. Non-negotiable design decisions

1. Quality Meter exists in V1.
2. HFR/FWHM/eccentricity are not V1 hard-reject metrics.
3. Rejected files are preserved.
4. Any enabled hard failure can reject by itself.
5. Quality Score does not override hard rejection.
6. Rejected/warning/error frames do not update the adaptive baseline.
7. V3 progress counts accepted valid frames, not physical shutter activations.
8. The codebase must remain structured so V2/V3 do not require rewriting the V1 classification core.
