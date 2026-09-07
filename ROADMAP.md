# QualitySessionMeter Roadmap

QualitySessionMeter is developed in three product stages: **V1** real-time quality monitoring, **V2** temporal/session intelligence, and **V3** quality-controlled acquisition through the N.I.N.A. Advanced Sequencer.

This file is the operational handoff/source of truth.

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

## Repository / release state

```text
main
  V1 + V2 validated baseline
  V2 merge: 9d8185c5080bac5cd856f5c130d09cdae8b70705

active branch
  v3-quality-controlled-acquisition

active PR
  #3 — V3 — Quality-Controlled Acquisition

V3 version
  1.0.0.0

V3 status
  RELEASE CANDIDATE
  implementation complete
  local production build: PASS / 0 errors
  local field-test build: PASS / 0 errors
  local SyntheticCheck build: PASS / 0 errors
  final Windows GitHub Actions regression/package gate required before merge/release
```

Known non-blocking dependency warning:

- `NINA.Image 3.3.0.1057-nightly` requests `NINA.Accord.Imaging >= 3.5.3-alpha`; NuGet resolves `3.5.3` (`NU1603`).

---

# V1 — CLOSED / MERGED

Authoritative core behavior retained by V2/V3:

- Quality Meter 0–100;
- exposure-specific guide RMS;
- sustained guide excursion;
- hard guide excursion;
- relative star-count loss;
- relative background change;
- context-separated rolling baselines;
- rejected/warning frames never contaminate the baseline;
- missing required analysis => `ERROR / UNASSESSED`;
- no automatic deletion;
- Quality Score remains independent from hard ACCEPT/REJECT.

Deliberately excluded from hard reject logic unless explicitly reconsidered:

- HFR;
- FWHM;
- eccentricity.

---

# V2 — CLOSED / MERGED

Implemented and validated:

- Confidence Score independent from Quality and hard decision;
- Session Event Grouping;
- guide-pattern analysis (`STABLE`, `ISOLATED SPIKE`, `SUSTAINED`, `OSCILLATION`, `DRIFT`, `WIND-LIKE`, `IRREGULAR`);
- conservative slow-trend diagnostics;
- multichannel timeline;
- Best/Worst accepted-frame ranking;
- rejected-frame timeline markers and shared cause visuals;
- `frames.csv`, `events.csv`, `session.json`, `quality.svg`, self-contained `report.html`.

Final V2 regression:

```text
Candidate: 0.2.0.1
CI: PASS
Canonical regression       PASS 29/29
Excellent night            PASS 17/17
Average night              PASS 15/15
Poor night                 PASS 17/17
Severe / disaster night    PASS 16/16
Deterioration + recovery   PASS 14/14
Whole-night frames: 108
Failures: 0
```

---

# V3 — QUALITY-CONTROLLED ACQUISITION

**Status: IMPLEMENTED — FINAL RELEASE GATE.**

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until the valid-frame target is complete?

## 1. Explicit OFF / ON and source scoping

Implemented:

- QSM OFF means no analysis, no valid-frame control and no file action;
- safe monitoring scopes:
  - `QSM controlled blocks only`;
  - `Advanced Sequencer LIGHTs` — recommended/default scope;
  - `All LIGHTs` — opt-in compatibility mode;
- `ImageType == LIGHT` remains mandatory;
- sequence metadata and QSM arm state establish acquisition provenance;
- transient Advanced Sequencer evidence may authorize monitoring but **never by itself authorizes file mutation**;
- manual/external LIGHTs can be monitored in `All LIGHTs` but cannot be renamed/moved;
- snapshots / plate-solving frames remain outside QSM LIGHT processing.

File mutation remains stricter than monitoring. Rejected-file handling never becomes the source of truth for the quality decision.

## 2. QSM Valid Frame Target

Implemented as a native Advanced Sequencer condition.

Do **not** decrement or patch N.I.N.A.'s normal `Take Exposure` counter. QSM owns separate persistent valid progress.

```text
while ValidCount < RequestedValidFrames:
    capture eligible LIGHT
    wait for QSM classification
    CapturedCount++

    ACCEPTED -> ValidCount++
    WARNING  -> ValidCount++ by default (configurable)
    REJECTED -> ValidCount unchanged
    ERROR    -> ValidCount unchanged
    LEARNING -> ValidCount unchanged
```

Example:

```text
Requested valid: 10
Learning:         4
Rejected:         3
Warning:          1
Accepted:         9
Captured:        17
Valid:           10 / 10
```

Safety semantics:

- missing classification times out and stops the controlled loop fail-safe;
- duplicate checks cannot double-count the same `FrameIndex`;
- progress is resettable and persisted by the condition;
- first production design assumes one eligible LIGHT per controlled block iteration.

## 3. Adaptive calibration / thresholds

Implemented modes:

```text
OFF
Suggest Only   <- recommended default
Automatic      <- bounded by explicit safety ceilings
```

Behavior:

- suggestions use stable accepted same-context frames;
- `Suggest Only` never changes thresholds automatically;
- user can Apply or Ignore a suggestion;
- Automatic remains bounded by safety ceilings;
- rejected/bad-weather evidence is not normalized into the baseline.

## 4. Predictive degradation

Implemented as a diagnostic channel separate from hard rejection.

- uses recent V2 trend evidence;
- reports channel, confidence, message and estimated frames-to-threshold where available;
- prediction by itself does not create a reject.

## 5. Environmental correlation

Implemented as optional diagnostic correlation using connected N.I.N.A. weather data when available:

- cloud cover;
- humidity;
- wind speed / gust;
- SQM;
- temperature;
- dew point.

Environmental correlation is diagnostic only in V3 production.

## 6. Smart Recovery Gate

Implemented as opt-in `QSM Smart Recovery Gate`.

- reacts only **between** exposures;
- never aborts an active shutter;
- only engages after a persistent controlled reject/error streak;
- waits a bounded cooldown;
- optionally requires consecutive healthy guider samples;
- safety timeout allows a probe frame instead of waiting indefinitely.

## 7. Production Control Center

Implemented production dockable exposing:

- global activation;
- monitoring scope;
- Monitor Only / active file handling state;
- adaptive calibration mode and suggestion controls;
- predictive status;
- environment status;
- Smart Recovery settings.

---

# SYNTHETIC LAB / PACKAGING POLICY

Synthetic Lab is a **development / field-test harness**, not a public production feature.

Field-test build:

```text
QsmDevelopmentBuild=true
Synthetic Lab dockable/resources included
6 deterministic whole-night profiles available in-host
```

Production build:

```text
QsmDevelopmentBuild=false
Synthetic Lab dockable/resources excluded
Production QSM functionality only
```

Headless SyntheticCheck remains permanent CI coverage in both release workflows.

CI now produces separate artifacts:

- `QualitySessionMeter-v1.0.0.0-production-nina-3.3-nightly-057`;
- `QualitySessionMeter-v1.0.0.0-field-test-nina-3.3-nightly-057`;
- `QualitySessionMeter-v1.0.0.0-synthetic-report`.

A successful push to `main` creates GitHub release tag `v1.0.0.0` with the production ZIP.

---

# V3 RELEASE GATES

Mandatory before merge/public release:

1. production build against `.NET 10` + `NINA.Plugin 3.3.0.1057-nightly`;
2. field-test build with Synthetic Lab;
3. production source-set gate proves Synthetic Lab dockable/resources are excluded;
4. field-test source-set gate proves Synthetic Lab is included;
5. all V1/V2 synthetic whole-night profiles remain unchanged: 6 profiles / 108 frames;
6. V3 valid-frame oracle passes;
7. V3 source/file-action safety oracle passes;
8. file-action tests prove ineligible sources cannot be mutated;
9. CSV/JSON/SVG/HTML persistence passes;
10. GitHub Actions Windows job is green;
11. PR #3 is merged only after the green gate;
12. release `v1.0.0.0` and production ZIP are verified after the `main` workflow.

For the first real-sky V3 test, use **Monitor Only** first. Enable rename/move only after verifying source/count behavior in the actual N.I.N.A. sequence.

---

# VERSION MATRIX

| Feature | V1 | V2 | V3 |
|---|:---:|:---:|:---:|
| Quality Meter 0–100 | ✅ | ✅ | ✅ |
| Guide RMS / excursions | ✅ | ✅ | ✅ |
| Adaptive star/background baseline | ✅ | ✅ | ✅ |
| Hard reject decision | ✅ | ✅ | ✅ |
| Persistent session log | ✅ | ✅ | ✅ |
| Confidence score |  | ✅ | ✅ |
| Event grouping |  | ✅ | ✅ |
| Guide-pattern analysis |  | ✅ | ✅ |
| Slow-trend diagnostics |  | ✅ | ✅ |
| Multichannel timeline |  | ✅ | ✅ |
| Rejected-frame cause markers |  | ✅ | ✅ |
| Frame ranking |  | ✅ | ✅ |
| Interactive HTML report |  | ✅ | ✅ |
| Explicit ON/OFF activation | ✅ | ✅ | ✅ |
| Acquisition source scoping |  |  | ✅ |
| Auto calibration |  |  | ✅ |
| Adaptive threshold suggestions |  |  | ✅ |
| Environmental correlation |  |  | ✅ |
| Smart pause/resume |  |  | ✅ |
| Accepted/valid-only sequence progress |  |  | ✅ |
| Valid Frame Target |  |  | ✅ |
| Predictive degradation |  |  | ✅ |

Synthetic Lab is intentionally omitted from the production feature matrix.

---

# ARCHITECTURAL CONSTRAINTS

1. Never refactor the Quality Engine in a way that breaks valid-frame counting.
2. Keep Quality/decision state independent from filesystem actions.
3. Rejected-file handling must never become the source of truth for ACCEPT/REJECT.
4. V2 temporal intelligence must not erase raw per-frame evidence.
5. V3 sequencer control consumes the same `FrameQualityResult` recorded by the engine.
6. Synthetic Lab must remain separable from production packaging.
7. Do not claim a validation gate passed until the relevant CI/host test actually passed.
8. HFR/FWHM/eccentricity remain excluded from hard reject logic unless explicitly changed.
9. The V3 loop must fail safe on missing classification; never capture indefinitely because of a synchronization fault.
10. Never infer file eligibility merely from extension/path.
11. Never rename/move an ineligible manual/external frame.

---

# NEXT STEP

The only remaining release action is operational rather than feature development:

```text
Windows CI green
→ mark PR #3 ready
→ merge to main
→ main CI green
→ verify GitHub release v1.0.0.0
→ field-test artifact handed to user for immediate N.I.N.A. test
```
