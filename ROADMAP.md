# QualitySessionMeter Roadmap

QualitySessionMeter is designed in three product stages. V1 is a complete real-time quality monitor; V2 adds temporal intelligence; V3 closes the loop with the N.I.N.A. sequencer.

## V1 — Real-Time Frame Quality Monitor

**Status: implemented / build validated**

V1 answers:

> Is this exposure good, how good is it, and why?

### Core capabilities

- Quality Meter 0–100 from day one.
- Exposure guide RMS.
- Sustained guide excursions.
- Hard guide excursions.
- Relative star-count loss.
- Relative background increase/decrease.
- Robust adaptive rolling baselines.
- Context-separated baselines by target/filter/exposure/gain/binning/camera.
- `ANY enabled hard rule fails -> REJECTED`.
- Basic probable-cause classification.
- Live Quality timeline.
- Session Quality and Acceptance Rate.
- Monitor Only mode.
- Safe rejected-file marking/moving without deletion.
- Persistent CSV, JSON and SVG session output.

### Explicit non-goals

HFR, FWHM and eccentricity are not used as rejection criteria.

---

## V2 — Smart Quality Analysis

V2 answers:

> What is happening across the night, and what pattern caused the degradation?

The goal is not to add arbitrary image metrics. It is to make better use of the temporal data V1 already collects.

### Confidence score

Each result gains a confidence estimate so the UI can distinguish:

```text
Quality: 43/100
Confidence: 99%
Reason: severe guiding disturbance
```

from:

```text
Quality: 61/100
Confidence: 54%
Reason: possible transparency loss; baseline still weak
```

### Event grouping

Related rejected frames are grouped into session events.

Example:

```text
22:51–23:05
Cloud / transparency event
3 rejected frames
Confidence 97%
```

This prevents a five-frame cloud passage from looking like five unrelated failures.

### Guide-pattern analysis

Use the complete guide time series to distinguish patterns such as:

- isolated spike;
- sustained excursion;
- oscillation;
- drift;
- wind-like disturbance;
- possible cable/mechanical event.

The goal is stronger diagnosis than a single RMS number can provide.

### Slow-trend compensation

V1 uses a rolling median. V2 will explicitly distinguish gradual change from abrupt anomaly.

Example of slow trend:

```text
1900 -> 1870 -> 1830 -> 1800 -> 1760 -> 1720 stars
```

versus sudden event:

```text
1900 -> 1870 -> 1840 -> 1810 -> 1040 stars
```

The first may be a normal altitude/sky trend; the second is likely a cloud or transparency anomaly.

### Multichannel session timeline

Combined inspection of:

- overall Quality;
- star count;
- background;
- guide RMS;
- guide excursions;
- detected events.

### Frame ranking

Rank accepted frames by Quality so the user can quickly inspect:

```text
BEST ACCEPTED
00124.fit   98
00118.fit   97
...
```

and:

```text
WORST ACCEPTED
00139.fit   67
00142.fit   69
...
```

### Advanced reporting

Planned export:

- CSV;
- JSON;
- interactive HTML report;
- PNG/SVG plots;
- rejection-reason distribution;
- filter breakdown;
- grouped environmental/session events.

---

## V3 — Quality-Controlled Acquisition

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until that valid-frame target is complete?

### Valid Frame Target — central V3 behaviour

This is deliberately **not** implemented as a separate “re-acquire rejected frames later” step.

The sequence itself must count only frames accepted by the Quality Engine.

Example:

```text
Requested valid frames: 300
Physically captured:     203
Rejected:                  8

Sequence progress:      195 / 300
```

A rejected exposure never increments valid progress.

Conceptually:

```text
while AcceptedCount < RequestedValidFrames:
    capture()
    CapturedCount++

    analyze()

    if ACCEPTED:
        AcceptedCount++
    else:
        RejectedCount++
```

At completion:

```text
Valid requested: 300
Accepted:        300
Rejected:         17
Total captured:  317
```

### Count modes

Potential sequencer option:

```text
Sequence Count Mode

( ) Captured exposures
(*) Accepted exposures
```

Traditional N.I.N.A. behaviour remains available while QualitySessionMeter can provide a quality-controlled mode.

### Multiple filters

Each filter block independently tracks its valid target.

Example:

```text
L   206 / 300 valid   (217 captured, 11 rejected)
R   100 / 100 valid   (108 captured,  8 rejected) COMPLETE
G    70 / 100 valid   ( 72 captured,  2 rejected)
```

### Auto-calibration

From an initial stable sample, estimate normal session variation and propose sensible thresholds for:

- star-count loss;
- background deviation;
- guide RMS;
- guide excursion.

User actions:

```text
Apply
Modify
Ignore
```

### Adaptive threshold modes

Planned:

```text
OFF
Suggest Only
Automatic
```

Preferred default is **Suggest Only**. Automatic threshold changes must be explicitly enabled by the user.

### Smart pause / resume

Persistent degradation can pause acquisition, while a single bad frame does not.

Example:

```text
GOOD
BAD
GOOD
=> continue
```

versus:

```text
BAD
BAD
BAD
=> persistent degradation -> pause
```

Recovery must be verified over multiple healthy checks before resuming.

### Environmental correlation

Where N.I.N.A. exposes suitable data, optionally correlate Quality with:

- wind;
- humidity;
- cloud sensor;
- SQM;
- temperature;
- dew point.

Environmental sensors remain optional.

### Predictive degradation

Use worsening quality trends to warn that conditions are deteriorating before several long exposures are wasted.

Prediction informs acquisition control; it does not pre-reject an exposure that has not yet been acquired.

---

## Version matrix

| Feature | V1 | V2 | V3 |
|---|:---:|:---:|:---:|
| Quality Meter 0–100 | ✅ | ✅ | ✅ |
| Guide RMS | ✅ | ✅ | ✅ |
| Sustained/hard excursion | ✅ | ✅ | ✅ |
| Star/background adaptive baseline | ✅ | ✅ | ✅ |
| Hard auto-reject decision | ✅ | ✅ | ✅ |
| Live timeline | ✅ | ✅ | ✅ |
| Persistent session log | ✅ | ✅ | ✅ |
| Basic probable cause | ✅ | ✅ | ✅ |
| Confidence score |  | ✅ | ✅ |
| Event grouping |  | ✅ | ✅ |
| Guide-pattern analysis |  | ✅ | ✅ |
| Slow-trend compensation |  | ✅ | ✅ |
| Frame ranking |  | ✅ | ✅ |
| Interactive HTML report |  | ✅ | ✅ |
| Auto calibration |  |  | ✅ |
| Adaptive threshold suggestions |  |  | ✅ |
| Environmental correlation |  |  | ✅ |
| Smart pause/resume |  |  | ✅ |
| Accepted-only sequence progress |  |  | ✅ |
| Valid Frame Target |  |  | ✅ |
| Predictive degradation |  |  | ✅ |

## Architectural constraint

V1 code should never be refactored in a way that makes the V3 accepted-frame counting model impossible. The Quality Engine, frame result model and session state are intentionally separated from file handling so the same ACCEPTED/REJECTED decision can later drive sequencer progress without coupling sequence control to filesystem operations.
