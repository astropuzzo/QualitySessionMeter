# QualitySessionMeter Roadmap

QualitySessionMeter is designed in three product stages. **V1** is the real-time quality monitor, **V2** adds temporal/session intelligence, and **V3** closes the loop with the N.I.N.A. Advanced Sequencer.

This file is also the **handoff/source-of-truth document** for future development. It must be updated with every material implementation change so another AI/developer can resume without reconstructing chat history.

---

# CURRENT HANDOFF STATE — 2026-09-07

## Runtime target

Current field-test target:

```text
N.I.N.A. 3.3 NIGHTLY #057
Application: 3.3.0.1057
NINA.Plugin: 3.3.0.1057-nightly
.NET: 10
Windows: x64
```

The old .NET 8 / NINA.Plugin 3.2 field build must not be used for the current nightly.

## V1 status

**V1 product functionality is implemented.**

The user successfully loaded build `0.1.0.1` in N.I.N.A. 3.3 NIGHTLY #057 after fixing a WPF ResourceDictionary double-load problem.

The original in-N.I.N.A. canonical Synthetic Lab run was executed locally by the user and returned:

```text
PASS 29/29
```

This validated the V1 decision engine locally inside the real N.I.N.A. host, without camera hardware or real image files.

## Work currently in progress

Branch:

```text
synthetic-session-harness
```

Pull request:

```text
PR #1 — V1 validation harness: multi-scenario QSM Synthetic Lab
```

The Synthetic Lab is being expanded from one canonical 29-frame regression run into multiple deterministic **whole-night profiles**:

- Canonical regression;
- Excellent night;
- Average night;
- Poor night;
- Severe / disaster night;
- Deterioration + recovery.

The CI checker is being expanded to execute **all profiles**, not only the canonical regression scenario.

### Merge gate for PR #1

Do not merge until all of the following are true:

- plugin compiles against NINA.Plugin `3.3.0.1057-nightly` / .NET 10;
- every synthetic whole-night profile passes its frame-level oracle;
- CSV/JSON/SVG persistence tests pass;
- isolated `BAD_` and `Rejected` file-action tests pass;
- the in-N.I.N.A. Synthetic Lab remains isolated from live ImageSaved events and real files.

After this gate passes, merge PR #1 into `main` and begin V2 on a dedicated branch, preferably:

```text
v2-smart-quality-analysis
```

## NEXT PRODUCT STEP

**V2 — Smart Quality Analysis.**

Do **not** pull the Advanced Sequencer / Valid Frame Target work forward. Accepted-only sequencer progress remains a V3 feature exactly as defined below.

---

# Synthetic Lab policy

The Synthetic Lab is a **development / pre-release verification harness**, not part of the final product identity.

## During development

It should remain available in development/field-test builds because it allows deterministic testing inside N.I.N.A. without risking a real imaging night.

It should grow together with the plugin: new V2/V3 logic should gain synthetic scenarios/oracles before field testing whenever practical.

## Final V3 public release

The final public V3 package **must not expose or ship the Synthetic Lab as a normal plugin panel/feature**. It must not look like a testing plugin to end users.

The source/test harness may remain in the repository or behind a development-only build flag, but the public release artifact must exclude the Synthetic Lab dockable and development-only test UI.

CI/headless synthetic regression testing should remain even after the public Synthetic Lab UI is removed from release packaging.

---

# V1 — Real-Time Frame Quality Monitor

**Status: implemented; build validated; canonical in-host Synthetic Lab validated 29/29; multi-profile regression expansion in progress.**

V1 answers:

> Is this exposure good, how good is it, and why?

## Core capabilities

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
- Missing required analysis data becomes `ERROR / UNASSESSED`, never a false ACCEPTED frame.

## Important implementation decisions already fixed

- Rejected frames never update the adaptive baseline.
- Learning/accepted frames may build/update the V1 baseline; warnings and rejected frames do not.
- A single guide spike does not count as a sustained excursion by itself.
- Sustained excursion duration is based on consecutive confirmed guide samples over threshold.
- Guiding samples are associated with the actual exposure interval.
- Synthetic mode uses an isolated baseline/session store and disables real-file actions.

## Explicit non-goals

HFR, FWHM and eccentricity are **not** used as rejection criteria.

---

# V2 — Smart Quality Analysis

**Status: NEXT PRODUCT VERSION — start only after PR #1 regression gate is green and merged.**

V2 answers:

> What is happening across the night, and what pattern caused the degradation?

The goal is not to add arbitrary image metrics. It is to make better use of the temporal data V1 already collects.

## 1. Confidence score

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

Confidence must reflect evidence quality/baseline maturity rather than being a decorative number.

## 2. Event grouping

Related abnormal frames are grouped into session events.

Example:

```text
22:51–23:05
Cloud / transparency event
3 rejected frames
Confidence 97%
```

A five-frame cloud passage should not appear as five unrelated failures.

Likely event classes include:

- cloud / transparency;
- bright-cloud / background;
- wind / guiding disturbance;
- mixed conditions;
- analysis/data-loss event.

## 3. Guide-pattern analysis

Use the complete exposure guide time series to distinguish patterns such as:

- isolated spike;
- sustained excursion;
- oscillation;
- drift;
- wind-like disturbance;
- possible cable/mechanical event.

This must add diagnostic information beyond a single RMS value.

## 4. Slow-trend compensation

V1 uses a rolling median. V2 explicitly distinguishes gradual change from abrupt anomaly.

Slow trend example:

```text
1900 -> 1870 -> 1830 -> 1800 -> 1760 -> 1720 stars
```

Sudden anomaly example:

```text
1900 -> 1870 -> 1840 -> 1810 -> 1040 stars
```

The first may be a normal altitude/sky trend; the second is likely a cloud/transparency anomaly.

The new `Deterioration + recovery` Synthetic Lab profile is intentionally retained as a regression foundation for this work.

## 5. Multichannel session timeline

Combined inspection of:

- Overall Quality;
- confidence;
- star count / relative transparency;
- background;
- guide RMS;
- guide excursions/patterns;
- detected session events.

The UI should allow the user to understand *when* and *why* the session quality changed.

## 6. Frame ranking

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

Ranking does not replace hard ACCEPT/REJECT rules.

## 7. Advanced reporting

Planned export:

- CSV;
- JSON;
- interactive HTML report;
- PNG/SVG plots;
- rejection-reason distribution;
- filter breakdown;
- grouped session events;
- best/worst accepted frames;
- confidence / temporal diagnostics.

## V2 development rule

Every new V2 algorithm should, where practical, gain a deterministic synthetic case before it is considered ready for field testing.

---

# V3 — Quality-Controlled Acquisition

**Status: planned final major product stage after V2. Do not implement early unless the roadmap is explicitly changed by the user.**

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until that valid-frame target is complete?

## Valid Frame Target — central V3 behaviour

This is deliberately **not** implemented as a separate “re-acquire rejected frames later” step.

The Advanced Sequencer workflow must count only frames accepted by the Quality Engine.

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

This replaces the earlier idea of a separate “re-acquire rejected frames” pass.

## Count modes

Potential sequencer option:

```text
Sequence Count Mode

( ) Captured exposures
(*) Accepted exposures
```

Traditional N.I.N.A. behaviour remains available while QualitySessionMeter can provide a quality-controlled mode.

## Multiple filters

Each filter block independently tracks its valid target.

Example:

```text
L   206 / 300 valid   (217 captured, 11 rejected)
R   100 / 100 valid   (108 captured,  8 rejected) COMPLETE
G    70 / 100 valid   ( 72 captured,  2 rejected)
```

## Auto-calibration

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

## Adaptive threshold modes

Planned:

```text
OFF
Suggest Only
Automatic
```

Preferred default is **Suggest Only**. Automatic threshold changes must be explicitly enabled by the user.

## Smart pause / resume

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

## Environmental correlation

Where N.I.N.A. exposes suitable data, optionally correlate Quality with:

- wind;
- humidity;
- cloud sensor;
- SQM;
- temperature;
- dew point.

Environmental sensors remain optional.

## Predictive degradation

Use worsening quality trends to warn that conditions are deteriorating before several long exposures are wasted.

Prediction informs acquisition control; it does not pre-reject an exposure that has not yet been acquired.

## Final-release Synthetic Lab requirement

Before publishing the final V3 product artifact:

- remove/exclude the Synthetic Lab dockable from public packaging;
- keep automated synthetic regression in CI;
- retain development-only synthetic capabilities in source only if useful;
- verify the release package presents QualitySessionMeter purely as the production quality-control plugin.

---

# Version matrix

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

Synthetic Lab is intentionally **not** listed as a production feature because it is a development/test harness and must be absent from the final public V3 package.

---

# Architectural constraints

1. Never refactor the Quality Engine in a way that makes V3 accepted-frame counting impossible.
2. Keep quality decision/state independent from filesystem actions.
3. Rejected-file handling must never be the source of truth for ACCEPT/REJECT state.
4. V2 temporal intelligence must layer on top of raw per-frame evidence rather than replace it.
5. V3 sequencer control must consume the same ACCEPTED/REJECTED decisions already recorded by the engine.
6. Development Synthetic Lab code must remain separable from final release packaging.

---

# Documentation / handoff discipline

For every material code change:

1. update this roadmap/current-state section in the same development cycle;
2. record completed behavior and any discovered bug/constraint;
3. state the exact next implementation step;
4. keep branch/PR/runtime compatibility information current;
5. do not claim a feature validated unless its relevant CI/synthetic/host test actually passed.

A future AI should start by reading this file, then `README.md`, `CHANGELOG.md`, and the active PR/branch status.
