# QualitySessionMeter Roadmap

QualitySessionMeter is developed in three product stages: **V1** real-time quality monitoring, **V2** temporal/session intelligence, and **V3** closed-loop quality-controlled acquisition through the N.I.N.A. Advanced Sequencer.

This file is also the **operational handoff/source of truth**. Update it in the same development cycle as every material code change so another AI/developer can resume from the repository without reconstructing chat history.

---

# CURRENT HANDOFF STATE — 2026-09-07

## Runtime target

```text
N.I.N.A. 3.3 NIGHTLY #057
Application: 3.3.0.1057
NINA.Plugin: 3.3.0.1057-nightly
.NET: 10
Windows: x64
```

Do not use the obsolete .NET 8 / NINA.Plugin 3.2 field-test build with this N.I.N.A. nightly.

## V1 validation status — GATE GREEN

**V1 product functionality is implemented and the development regression gate is green.**

Host validation performed by the user:

```text
Build: 0.1.0.1
Host: N.I.N.A. 3.3 NIGHTLY #057
Plugin load: PASS
In-N.I.N.A. canonical Synthetic Lab: PASS 29/29
```

The earlier WPF startup failure (`Cannot re-initialize ResourceDictionary instance`) was fixed by removing duplicate manual loading of exported ResourceDictionaries from the dockable constructors.

The Synthetic Lab was then expanded into six deterministic whole-night profiles. Latest CI result:

```text
Canonical regression       PASS 29/29
Excellent night            PASS 17/17
Average night              PASS 15/15
Poor night                 PASS 17/17
Severe / disaster night    PASS 16/16
Deterioration + recovery   PASS 14/14

GLOBAL VERDICT: PASS
Profiles: 6
Frames exercised: 108
Failures: 0
```

The same CI run also passed:

- plugin build against `NINA.Plugin 3.3.0.1057-nightly` / .NET 10;
- CSV persistence;
- JSON persistence;
- SVG persistence;
- isolated `BAD_` prefix file-action test;
- isolated move-to-`Rejected` file-action test;
- field-test artifact packaging.

A synthetic regression previously exposed a real V1 persistence bug (`NaN` values breaking `session.json` during LEARNING); unavailable numeric metrics are now serialized as JSON `null`.

## Current branch / PR

```text
Branch: synthetic-session-harness
PR: #1 — V1 validation harness: multi-scenario QSM Synthetic Lab
Status: validation gate GREEN; ready to merge to main
```

### Exact next repository steps

1. Merge PR #1 into `main`.
2. Verify the post-merge `main` build is green.
3. Create branch `v2-smart-quality-analysis` from updated `main`.
4. Change this handoff section to show V2 as IN PROGRESS.
5. Begin V2 with the **confidence-score foundation**, including persistence/UI and deterministic synthetic oracles.

## Product sequencing constraint

Do **not** pull the Advanced Sequencer / Valid Frame Target work forward. Accepted-only sequencer progress remains a **V3** feature exactly as specified below.

---

# Synthetic Lab policy

The Synthetic Lab is a **development / pre-release verification harness**, not a production product feature.

## During V1/V2/V3 development

Keep it available in development/field-test builds because it allows deterministic testing inside N.I.N.A. without risking a real imaging night. New algorithms should receive synthetic scenarios/oracles before field testing whenever practical.

Current whole-night profiles:

- Canonical regression — all V1 rules/edge cases;
- Excellent night — stable high-quality session;
- Average night — normal variation, warnings, occasional rejects;
- Poor night — frequent wind/cloud/background failures;
- Severe / disaster night — repeated major failures;
- Deterioration + recovery — gradual decline, collapse and recovery; intended as a foundation for V2 temporal analysis.

## Final public V3 release

The final public V3 artifact **must not expose or ship the QSM Synthetic Lab as a normal dockable/plugin feature**. The public plugin must look like the production quality-control product, not a test harness.

The source harness may remain in the repository or behind a development-only build flag. **Headless synthetic regression in CI must remain** even when the test UI is excluded from final release packaging.

---

# V1 — Real-Time Frame Quality Monitor

**Status: IMPLEMENTED + HOST-VALIDATED CANONICAL TEST + 108-FRAME CI REGRESSION GREEN.**

V1 answers:

> Is this exposure good, how good is it, and why?

## Core capabilities

- Quality Meter 0–100.
- Exposure Guide RMS.
- Sustained guide excursions.
- Hard guide excursions.
- Relative star-count loss.
- Relative background increase/decrease.
- Robust adaptive rolling medians.
- Context-separated baselines by target/filter/exposure/gain/binning/camera.
- `ANY enabled hard rule fails -> REJECTED`.
- Basic probable-cause classification.
- Live Quality timeline and frame history.
- Session Quality and Acceptance Rate.
- Monitor Only mode.
- Safe rejected-file keep / `BAD_` prefix / move to `Rejected` handling.
- No automatic deletion.
- Persistent CSV, JSON and SVG output.
- Missing required analysis data => `ERROR / UNASSESSED`, never false ACCEPTED.

## Fixed implementation rules

- Rejected frames never update the adaptive baseline.
- Warning frames do not update the V1 baseline.
- Learning and Accepted frames can build/update the baseline.
- A single guide spike does not count as a sustained excursion.
- Sustained duration requires consecutive confirmed above-threshold guide samples.
- Guide metrics are calculated only from samples inside the exposure interval.
- Quality Score and hard ACCEPT/REJECT decision remain separate concepts.
- Synthetic mode has an isolated baseline/session store and disables real-file actions.

## Explicitly excluded from rejection logic

- HFR
- FWHM
- eccentricity

These remain excluded because real wind-damaged point–streak–point frames can retain deceptively similar values.

---

# V2 — Smart Quality Analysis

**Status: NEXT PRODUCT VERSION. Start after PR #1 is merged and the V2 branch is created.**

V2 answers:

> What is happening across the night, how certain are we, and what temporal pattern caused the degradation?

V2 must use the V1 evidence more intelligently rather than add arbitrary image metrics.

## 1. Confidence score — FIRST V2 MILESTONE

Add per-frame confidence separate from Quality.

Examples:

```text
Quality: 43/100
Confidence: 99%
Reason: severe guiding disturbance
```

```text
Quality: 61/100
Confidence: 54%
Reason: possible transparency loss; baseline still weak
```

Confidence must be evidence-driven, not decorative. Initial model should consider at least:

- baseline maturity relative to configured learning/window size;
- required data availability;
- distance/margin from relevant hard thresholds;
- number of independent abnormal channels that agree;
- strength of guiding evidence for guide-driven classifications.

Required implementation surface:

- `FrameQualityResult` fields for confidence and explanation/evidence;
- CSV/JSON persistence;
- dashboard/history display;
- deterministic Synthetic Lab / headless CI cases for strong high-confidence evidence and weaker/near-threshold evidence.

## 2. Event grouping

Group temporally related abnormal frames into session events instead of treating every bad exposure as unrelated.

Example:

```text
22:51–23:05
Cloud / transparency event
3 affected frames
Confidence 97%
```

Likely event classes:

- cloud / transparency;
- bright-cloud / background;
- wind / guiding disturbance;
- mixed conditions;
- analysis/data-loss event.

Events need clear start/end rules and must close after a configurable/defined healthy gap.

## 3. Guide-pattern analysis

Use the complete exposure guide time series to distinguish:

- isolated spike;
- sustained excursion;
- oscillation;
- drift;
- wind-like disturbance;
- possible cable/mechanical event.

Pattern labels are diagnostic evidence, not guaranteed physical-cause claims. Add deterministic synthetic sample factories for oscillation/drift/wind-like patterns.

## 4. Slow-trend compensation

Distinguish gradual change from abrupt anomalies.

Benign/gradual example:

```text
1900 -> 1870 -> 1830 -> 1800 -> 1760 -> 1720 stars
```

Abrupt anomaly:

```text
1900 -> 1870 -> 1840 -> 1810 -> 1040 stars
```

Trend modelling must remain context-separated and must not allow rejected frames to normalize bad conditions. Add a benign slow-trend synthetic profile in addition to the existing `Deterioration + recovery` profile.

## 5. Multichannel session timeline

Combined inspection of:

- Overall Quality;
- Confidence;
- star count / transparency residual;
- background;
- Guide RMS;
- guide excursions/patterns;
- detected events.

The UI should make it obvious *when* quality changed and *why*.

## 6. Frame ranking

Rank accepted frames by Quality, with best/worst accepted views. Ranking does not replace hard ACCEPT/REJECT logic.

Example:

```text
BEST ACCEPTED
00124.fit   98
00118.fit   97
```

```text
WORST ACCEPTED
00139.fit   67
00142.fit   69
```

## 7. Advanced reporting

Planned outputs:

- CSV;
- JSON;
- interactive HTML report;
- PNG/SVG plots;
- rejection-reason distribution;
- filter breakdown;
- grouped events;
- best/worst accepted frames;
- confidence and temporal diagnostics.

## V2 development rule

Every new V2 algorithm should gain deterministic synthetic coverage before it is treated as ready for field testing whenever practical.

---

# V3 — Quality-Controlled Acquisition

**Status: PLANNED FINAL MAJOR PRODUCT STAGE AFTER V2. Do not implement early unless the user explicitly changes this roadmap.**

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until that valid-frame target is complete?

## Valid Frame Target — central V3 behavior

This is deliberately **not** a separate “re-acquire rejected frames later” pass. The sequence progress itself must count only accepted frames.

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

## Count modes

Potential Advanced Sequencer option:

```text
( ) Captured exposures
(*) Accepted exposures
```

## Multiple filters

Each filter block independently tracks its valid target:

```text
L   206 / 300 valid   (217 captured, 11 rejected)
R   100 / 100 valid   (108 captured,  8 rejected) COMPLETE
G    70 / 100 valid   ( 72 captured,  2 rejected)
```

## Auto-calibration

Estimate stable-session variation and propose thresholds for:

- star-count loss;
- background deviation;
- Guide RMS;
- guide excursion.

User choices: `Apply / Modify / Ignore`.

## Adaptive threshold modes

```text
OFF
Suggest Only
Automatic
```

Default/preferred mode: **Suggest Only**. Automatic changes require explicit opt-in.

## Smart pause / resume

A single bad frame does not stop acquisition; persistent deterioration can pause it. Recovery must be confirmed by multiple healthy checks before resume.

## Environmental correlation

Where available, optionally correlate Quality with wind, humidity, cloud sensor, SQM, temperature and dew point. Sensors remain optional.

## Predictive degradation

Warn about worsening quality trends and use them as input to acquisition control; never pre-reject an exposure that has not been acquired.

## Final-release Synthetic Lab requirement

Before public V3 release:

- exclude the Synthetic Lab dockable/test UI from production packaging;
- retain automated synthetic regression in CI;
- retain development-only harness code in source only if useful;
- verify the release artifact presents only production QualitySessionMeter functionality.

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

Synthetic Lab is intentionally omitted from the production feature matrix because it is a development/test harness and must be absent from the final public V3 package.

---

# Architectural constraints

1. Never refactor the Quality Engine in a way that prevents V3 accepted-frame counting.
2. Keep quality decision/state independent from filesystem actions.
3. Rejected-file handling must never become the source of truth for ACCEPT/REJECT.
4. V2 temporal intelligence layers on raw per-frame evidence; it must not erase or hide it.
5. V3 sequencer control consumes the same ACCEPTED/REJECTED state already recorded by the engine.
6. Development Synthetic Lab code must remain separable from production release packaging.
7. Do not claim a feature validated unless the relevant CI/synthetic/host test has actually passed.

---

# Documentation / handoff discipline

For every material change:

1. update this roadmap/current-state section in the same development cycle;
2. record completed behavior and discovered bugs/constraints;
3. state the exact next implementation step;
4. keep branch/PR/runtime compatibility current;
5. record validation evidence before marking work complete.

A future AI should read this file first, then `README.md`, `CHANGELOG.md`, and the active branch/PR status.
