# QualitySessionMeter Roadmap

QualitySessionMeter is developed in three product stages: **V1** real-time quality monitoring, **V2** temporal/session intelligence, and **V3** closed-loop quality-controlled acquisition through the N.I.N.A. Advanced Sequencer.

This file is the **operational handoff/source of truth**. Update it in the same development cycle as every material code change so another AI/developer can resume from the repository without reconstructing chat history.

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

## Repository state

```text
main
  V1 validated baseline
  merge commit: cc16deffe88181bd92a067f2e0366831f02d9959

active branch
  v2-smart-quality-analysis

active PR
  #2 — V2 — Smart Quality Analysis

field-test version being prepared
  0.2.0.0
```

Do not merge PR #2 until the user has loaded the V2 field-test package in N.I.N.A. and re-run the Synthetic Lab successfully.

---

# V1 — CLOSED DEVELOPMENT GATE

V1 has been merged to `main`.

User-local host validation:

```text
Build: 0.1.0.1
Host: N.I.N.A. 3.3 NIGHTLY #057
Plugin load: PASS
In-N.I.N.A. canonical Synthetic Lab: PASS 29/29
```

Regression baseline inherited by V2:

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

The same regression gate verifies session persistence and isolated rejected-file actions using temporary fake files.

---

# V2 — SMART QUALITY ANALYSIS

**Status: IMPLEMENTED ON `v2-smart-quality-analysis`, awaiting final CI on build 0.2.0.0 and then user-local N.I.N.A. host validation.**

V2 answers:

> What is happening across the night, how certain are we, and what temporal pattern caused the degradation?

V2 deliberately layers temporal/session intelligence on top of V1. It does **not** change the V1 product rule that hard rejection thresholds are authoritative and independent from the Quality Score.

## V2.1 — Evidence-based Confidence Score — IMPLEMENTED

Per-frame Confidence is separate from Quality and ACCEPT/REJECT.

```text
Quality:    43 / 100
Confidence: 99%
```

Evidence components persisted for auditability:

- required-data completeness;
- adaptive-baseline maturity;
- separation from configured decision thresholds;
- agreement between independent abnormal channels;
- diagnostic reason text.

Important rules:

- missing required data => confidence 0 / unassessed;
- LEARNING confidence is capped while the adaptive reference is immature;
- near-threshold warnings remain moderate rather than pretending to be certain;
- strong multi-channel failures can reach very high confidence;
- Confidence never overrides a hard rule.

Synthetic confidence oracles are part of the CI gate.

## V2.2 — Event Grouping — IMPLEMENTED

Temporally related abnormal frames are grouped into session events.

Example:

```text
22:51–23:05
CLOUD / TRANSPARENCY EVENT
3 affected frames
Confidence 97%
```

Implemented behavior includes:

- warning/rejection/error event starts;
- short healthy-gap bridging where appropriate;
- closure after stable recovery;
- event severity;
- mean/peak confidence;
- primary cause;
- promotion to mixed-conditions events where the abnormal channels change during one continuous event.

Persistence:

- `events.csv`;
- events embedded in `session.json`;
- events shown in the V2 dashboard/report.

Dedicated grouping oracles run in CI.

## V2.3 — Guide Pattern Analysis — IMPLEMENTED

Guide time-series diagnostics now distinguish:

- `STABLE`;
- `ISOLATED SPIKE`;
- `SUSTAINED`;
- `OSCILLATION`;
- `DRIFT`;
- `WIND-LIKE`;
- `IRREGULAR`;
- unavailable data.

Diagnostics include pattern confidence, drift rate, oscillation range, sign-change count, burstiness and explanation text.

These labels are **diagnostic evidence only**. They do not create a new rejection rule and are not guaranteed physical-cause identifications.

Dedicated synthetic guide-pattern factories/oracles run in CI.

## V2.4 — Slow Trend Analysis — IMPLEMENTED CONSERVATIVELY

QSM fits slow star/background behavior only from the clean reference history for the same acquisition context.

It can classify:

- `STABLE`;
- `GRADUAL`;
- `ABRUPT`;
- unavailable.

The trend layer records expected value, trend rate, fit quality and residual from the expected slow trend.

Critical safety rule:

> Hard V1 star/background rejection continues to use the robust rolling reference. Trend analysis does not normalize away a slowly worsening cloud event.

Rejected frames never enter the trend/reference history.

Coverage includes the whole-night `Deterioration + recovery` profile plus a dedicated deterministic benign/abrupt trend oracle. A seventh selectable UI profile is not required for the current V2 field-test because the Synthetic Lab already exposes distinct Excellent / Average / Poor / Disaster / Deterioration profiles; the dedicated oracle gives tighter algorithmic coverage of benign slow drift.

## V2.5 — Multichannel Timeline — IMPLEMENTED

The N.I.N.A. dashboard timeline now surfaces session context across:

- Overall Quality;
- Confidence;
- Guide RMS;
- star-count deviation;
- background deviation;
- frame state markers.

The HTML report includes the same multichannel inspection concept for post-session review.

## V2.6 — Frame Ranking — IMPLEMENTED

Accepted frames are ranked by Quality, using Confidence as a secondary diagnostic tie-breaker.

Dashboard/report surfaces:

- best accepted frames;
- worst accepted frames.

Ranking never replaces hard ACCEPT/REJECT logic.

## V2.7 — Advanced Reporting — IMPLEMENTED

Session output now includes:

- `frames.csv`;
- `events.csv`;
- `session.json`;
- `quality.svg`;
- self-contained interactive `report.html`.

The HTML report includes:

- session summary;
- Quality and Confidence;
- multichannel timeline;
- grouped events;
- rejection/warning cause distribution;
- best/worst accepted frames;
- frame history with diagnostic Guide Pattern and Trend fields;
- no CDN/external runtime dependency.

## V2 dashboard — IMPLEMENTED

The N.I.N.A. dockable was expanded into a scrollable session dashboard to prevent the new V2 data from becoming tiny/unreadable.

Current surfaces include:

- Quality;
- Confidence;
- current status/reason/probable cause;
- Guide RMS / maximum excursion;
- stars/background;
- Guide Pattern;
- Trend;
- session counters;
- multichannel timeline;
- frame history;
- session events;
- accepted-frame ranking;
- report/session-folder controls.

Presentation fix for the field-test candidate:

- `LEARNING` is displayed as `Quality — / LEARNING`, not misleadingly as `100 / EXCELLENT`;
- analysis `ERROR` is displayed as `Quality — / UNASSESSED`;
- the internal numerical value remains available for regression/persistence where required.

---

# V2 VALIDATION GATE

The latest pre-version-bump V2 CI run passed on Windows / .NET 10 / NINA.Plugin 3.3.0.1057-nightly.

Verified in that run:

```text
Plugin build: PASS
Synthetic checker build: PASS
6 whole-night profiles: PASS
108 whole-night frames: PASS
Confidence oracles: PASS
Event grouping oracles: PASS
Guide-pattern oracles: PASS
Trend oracles: PASS
frames.csv persistence: PASS
events.csv persistence: PASS
session.json persistence: PASS
quality.svg generation: PASS
report.html generation: PASS
isolated BAD_ file action: PASS
isolated Rejected-folder action: PASS
```

Known non-blocking build warning from the N.I.N.A. nightly dependency graph:

```text
NU1603: NINA.Image 3.3.0.1057-nightly requests NINA.Accord.Imaging >= 3.5.3-alpha;
NuGet resolves NINA.Accord.Imaging 3.5.3.
```

There were also nullable-annotation warnings in `HtmlReportWriter` because this project intentionally has nullable annotations disabled. They are compiler warnings only, not runtime failures; they can be cleaned separately and are not considered a host-test blocker.

### Exact next step

1. Let CI compile the final **0.2.0.0** candidate after the LEARNING/UNASSESSED presentation fix and artifact rename.
2. Require full synthetic regression green again.
3. Download the clean two-file field-test artifact.
4. Ask the user to replace the existing DLL/PDB in the same N.I.N.A. plugin folder.
5. User tests build 0.2.0.0 locally in N.I.N.A. 3.3 NIGHTLY #057:
   - plugin load;
   - selectable Synthetic Lab profiles;
   - at minimum Canonical, Average, Severe/Disaster and Deterioration + recovery;
   - new Confidence / Guide Pattern / Trend / Event / Ranking UI;
   - verify LEARNING shows `Quality —`;
   - open generated `report.html` and inspect usability.
6. If host validation passes, update this roadmap with the results and merge PR #2 to `main`.
7. Only then start V3.

---

# SYNTHETIC LAB POLICY

The Synthetic Lab is a **development / pre-release verification harness**, not a production product feature.

## During V1/V2/V3 development

Keep it available in development/field-test builds because it allows deterministic testing inside N.I.N.A. without risking a real imaging night.

Selectable whole-night profiles currently exposed inside N.I.N.A.:

- Canonical regression — all core rule/edge cases;
- Excellent night — stable high-quality sequence;
- Average night — normal variation, warnings and occasional rejects;
- Poor night — frequent wind/cloud/background failures;
- Severe / disaster night — repeated major failures;
- Deterioration + recovery — progressive degradation, collapse and recovery.

The Synthetic Lab uses the same production Quality/Baseline/Guide/session paths wherever practical, while keeping camera access and real rejected-file actions isolated.

## Final public V3 release

The final public V3 artifact **must not expose or ship the QSM Synthetic Lab as a normal dockable/plugin feature**. The public plugin must look like the production quality-control product, not a testing utility.

Allowed final arrangement:

- Synthetic Lab source remains behind a development-only build flag or development package;
- headless synthetic regression remains permanently in CI;
- production V3 package contains only production QSM features.

---

# V1 — REAL-TIME FRAME QUALITY MONITOR

**Status: IMPLEMENTED + MERGED + HOST-VALIDATED CANONICAL TEST.**

V1 answers:

> Is this exposure good, how good is it, and why?

Core capabilities:

- Quality Meter 0–100;
- exposure Guide RMS;
- sustained guide excursions;
- hard guide excursions;
- relative star-count loss;
- relative background increase/decrease;
- robust context-separated rolling baselines;
- `ANY enabled hard rule fails -> REJECTED`;
- basic probable-cause classification;
- live Quality timeline/history;
- Session Quality / Acceptance Rate;
- Monitor Only;
- safe keep / `BAD_` / `Rejected` folder actions;
- no automatic deletion;
- CSV/JSON/SVG persistence;
- unavailable required analysis data => `ERROR / UNASSESSED`, never false ACCEPTED.

Fixed rules:

- Rejected frames never update the adaptive baseline;
- Warning frames do not update the V1 baseline;
- Learning and Accepted frames may build/update the baseline;
- a single guide spike does not count as sustained duration;
- guide metrics use samples inside the exposure interval;
- Quality Score and hard decision remain separate;
- Synthetic mode uses isolated state and disables real file actions.

Explicitly excluded from rejection logic:

- HFR;
- FWHM;
- eccentricity.

---

# V3 — QUALITY-CONTROLLED ACQUISITION

**Status: PLANNED FINAL MAJOR PRODUCT STAGE AFTER V2 HOST VALIDATION. Do not implement early unless the user explicitly changes this roadmap.**

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until that valid-frame target is complete?

## Valid Frame Target — central behavior

This is **not** a separate “re-acquire rejected frames later” pass. Sequence progress itself counts only accepted frames.

```text
Requested valid frames: 300
Physically captured:     203
Rejected:                  8
Sequence progress:      195 / 300
```

A rejected exposure never increments valid progress.

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

```text
( ) Captured exposures
(*) Accepted exposures
```

## Multiple filters

```text
L   206 / 300 valid   (217 captured, 11 rejected)
R   100 / 100 valid   (108 captured,  8 rejected) COMPLETE
G    70 / 100 valid   ( 72 captured,  2 rejected)
```

## Auto-calibration

Estimate stable-session variation and propose thresholds for star-count loss, background deviation, Guide RMS and guide excursion.

User actions:

```text
Apply / Modify / Ignore
```

## Adaptive threshold modes

```text
OFF
Suggest Only
Automatic
```

Preferred default: **Suggest Only**. Automatic changes require explicit opt-in.

## Smart pause / resume

A single bad frame does not stop acquisition. Persistent deterioration may pause the sequence. Recovery must be confirmed by multiple healthy checks before resume.

## Environmental correlation

Optionally correlate Quality with wind, humidity, cloud sensor, SQM, temperature and dew point where N.I.N.A. exposes suitable data. Sensors remain optional.

## Predictive degradation

Use worsening Quality/Confidence/trend behavior to warn about likely upcoming degradation; never pre-reject an exposure that has not been acquired.

## Final-release Synthetic Lab requirement

Before public V3 release:

- exclude Synthetic Lab dockable/test UI from the production package;
- retain automated synthetic regression in CI;
- keep development-only harness source only if useful;
- verify that the public artifact presents production QualitySessionMeter functionality only.

---

# VERSION MATRIX

| Feature | V1 | V2 | V3 |
|---|:---:|:---:|:---:|
| Quality Meter 0–100 | ✅ | ✅ | ✅ |
| Guide RMS | ✅ | ✅ | ✅ |
| Sustained/hard excursion | ✅ | ✅ | ✅ |
| Star/background adaptive baseline | ✅ | ✅ | ✅ |
| Hard reject decision | ✅ | ✅ | ✅ |
| Persistent session log | ✅ | ✅ | ✅ |
| Basic probable cause | ✅ | ✅ | ✅ |
| Confidence score |  | ✅ | ✅ |
| Event grouping |  | ✅ | ✅ |
| Guide-pattern analysis |  | ✅ | ✅ |
| Slow-trend diagnostics |  | ✅ | ✅ |
| Multichannel timeline |  | ✅ | ✅ |
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

# ARCHITECTURAL CONSTRAINTS

1. Never refactor the Quality Engine in a way that prevents V3 accepted-frame counting.
2. Keep Quality/decision state independent from filesystem actions.
3. Rejected-file handling must never become the source of truth for ACCEPT/REJECT.
4. V2 temporal intelligence layers on raw per-frame evidence; it must not erase or hide it.
5. V3 sequencer control consumes the same ACCEPTED/REJECTED state already recorded by the engine.
6. Development Synthetic Lab code must remain separable from production release packaging.
7. Do not claim a feature validated unless the relevant CI/synthetic/host test actually passed.
8. HFR/FWHM/eccentricity remain excluded from core reject logic unless the user explicitly changes that decision.

---

# DOCUMENTATION / HANDOFF DISCIPLINE

For every material change:

1. update this roadmap/current-state section in the same development cycle;
2. record completed behavior and discovered bugs/constraints;
3. state the exact next implementation step;
4. keep branch/PR/runtime compatibility current;
5. record validation evidence before marking work complete.

A future AI should read this file first, then `README.md`, `CHANGELOG.md`, and the active branch/PR status.
