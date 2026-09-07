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

## Repository state

```text
main
  V1 + V2 validated baseline
  V2 merge commit: 9d8185c5080bac5cd856f5c130d09cdae8b70705

active branch
  v3-quality-controlled-acquisition

active PR
  #3 — V3 — Quality-Controlled Acquisition
  draft

V2 final field-test line
  0.2.0.1
  CI #82: PASS
  whole-night regression: 6 profiles / 108 frames / 0 failures

current V3 status
  IN PROGRESS
  initial QSM Valid Frame Target condition compiles (CI #87 PASS)
```

The user explicitly approved moving to V3 on 2026-09-07 and wants a **field-test candidate usable tonight**. Do not reopen V2 design work unless a regression is found.

## Critical V3 safety requirement discovered before field test: acquisition source scoping

The V1/V2 runtime currently filters on `ImageType == LIGHT`, and `Enabled` defaults to true while `MonitorOnly` defaults to true. That is safe from file mutation by default, but it is not sufficient for final V3 because a generic/manual exposure explicitly tagged as `LIGHT` could still be analyzed when QSM is enabled.

N.I.N.A. ordinary Imaging snapshots and plate-solving exposures are normally tagged `SNAPSHOT`, so they are already excluded by the LIGHT filter. However, V3 must make provenance explicit and auditable rather than relying on this alone.

Before any real-sky V3 field test implement all of the following:

1. **Global activation control** remains explicit: QSM can be OFF or ON; OFF means no frame analysis and V3 sequence control refuses to operate.
2. Add a **frame-source scope** with safe defaults:
   - `Advanced Sequencer LIGHTs` — default monitoring scope;
   - `QSM controlled blocks only` — strict V3 control scope;
   - `All LIGHTs` — advanced opt-in compatibility mode only.
3. Determine source using stable evidence from N.I.N.A.:
   - `ImageType == LIGHT` is mandatory;
   - `ISequenceMediator.IsAdvancedSequenceRunning()` / current running items where applicable;
   - N.I.N.A. sequence metadata (`MetaData.Sequence.Title`) written by `TakeExposure`;
   - explicit runtime **QSM controlled-block arm token** set by `QSM Valid Frame Target` while its parent block is executing.
4. The V3 condition must arm the runtime before the controlled LIGHT is captured and keep that arm active until the matching `FrameProcessed` result is consumed.
5. **File rename/move actions must never apply to snapshots, plate-solve frames, or unrelated manual/test exposures.** For the first production-safe V3 implementation, filesystem mutation is permitted only for an explicitly scoped eligible LIGHT; the safest/default control path is a QSM-armed Advanced Sequencer block.
6. Record provenance in each frame result/log so a user can audit why QSM touched a file:
   - source kind;
   - sequence title when available;
   - QSM-controlled yes/no;
   - original path;
   - final path after optional file action.
7. Source determination must be captured before/as the image is finalized, not inferred later only from whether a sequence happens to still be running, because N.I.N.A. image saving is asynchronous.
8. The first real-sky V3 test remains `Monitor Only`; rename/move is tested only after acquisition-source scoping is verified locally.

### File-action lifecycle fact already verified

In current N.I.N.A., `ImageSaved` is raised after `SaveToDisk(...)` has successfully returned the actual saved path. Therefore QSM receives the concrete path of the file that was just written. `RejectedFileService` operates on that exact path with collision-safe `File.Move`. This makes rename/move technically viable, but provenance scoping above is mandatory before enabling it on sky.

## Immediate V3 field-test priority

1. Finish acquisition-source scoping / explicit activation safety above.
2. Complete Advanced Sequencer **QSM Valid Frame Target**.
3. Never modify/decrement N.I.N.A.'s built-in `Take Exposure` counter.
4. The sequencer loop must wait for the QSM result belonging to the just-finished eligible LIGHT before deciding whether the target progressed.
5. `REJECTED` and `ERROR` never advance valid progress.
6. `ACCEPTED` advances valid progress.
7. `WARNING` counts as valid by default because it is not rejected; expose a setting so this can be disabled.
8. `LEARNING` does **not** count toward the valid target in the first V3 implementation because relative image-quality evidence is not mature yet.
9. A missing QSM result must fail safe: stop/finish the quality-controlled loop rather than capture indefinitely.
10. Add deterministic V3 regression proving examples such as `13 captured / 3 rejected = 10 / 10 valid` plus source-scope rejection tests.
11. Produce two packaging modes:
    - development/field-test package may retain Synthetic Lab;
    - production package must exclude the Synthetic Lab dockable.

---

# V1 — CLOSED

Validated and merged. Core rules remain authoritative in V2/V3:

- Quality Meter 0–100;
- Guide RMS;
- sustained guide excursion;
- hard guide excursion;
- relative star-count loss;
- relative background change;
- context-separated rolling baselines;
- `ANY enabled hard rule fails -> REJECTED`;
- rejected/warning frames never contaminate the adaptive baseline;
- a single guide spike is not a sustained excursion;
- missing required analysis data => `ERROR / UNASSESSED`, never false ACCEPTED;
- no automatic deletion;
- Quality Score and hard decision remain separate concepts.

Deliberately excluded from core reject logic unless the user explicitly changes this decision:

- HFR;
- FWHM;
- eccentricity.

---

# V2 — CLOSED / MERGED

V2 answers:

> What is happening across the night, how certain are we, and what temporal pattern caused the degradation?

Implemented and merged features:

- evidence-based Confidence Score independent from Quality and hard decision;
- Session Event Grouping with severity/cause/confidence;
- guide-pattern analysis: `STABLE`, `ISOLATED SPIKE`, `SUSTAINED`, `OSCILLATION`, `DRIFT`, `WIND-LIKE`, `IRREGULAR`;
- conservative slow-trend diagnostics: `STABLE`, `GRADUAL`, `ABRUPT`;
- multichannel timeline;
- Best/Worst accepted frame ranking;
- `frames.csv`, `events.csv`, `session.json`, `quality.svg`, interactive self-contained `report.html`;
- rejected-frame timeline markers and compact cause icons:
  - `💨` guide/wind;
  - `☁` stars/cloud/transparency;
  - `🌫` background/haze;
  - combinations for multi-channel failures;
  - `⚠` analysis error;
- WPF and HTML share the same rejection-visual mapping.

Final recorded V2 regression:

```text
Candidate: 0.2.0.1
CI run: #82 / 34151670360
Conclusion: SUCCESS

Canonical regression       PASS 29/29
Excellent night            PASS 17/17
Average night              PASS 15/15
Poor night                 PASS 17/17
Severe / disaster night    PASS 16/16
Deterioration + recovery   PASS 14/14

GLOBAL VERDICT: PASS
Profiles: 6
Whole-night frames: 108
Failures: 0
```

Known non-blocking dependency warning:

- N.I.N.A. nightly requests `NINA.Accord.Imaging >= 3.5.3-alpha`; NuGet resolves `3.5.3` (`NU1603`).

---

# SYNTHETIC LAB POLICY

Synthetic Lab is a **development / pre-release verification harness**, not a production feature.

Current selectable development profiles:

- Canonical regression;
- Excellent night;
- Average night;
- Poor night;
- Severe / disaster night;
- Deterioration + recovery.

The Lab uses isolated baseline/session state, ignores live image-save processing while active and never applies real rejected-file actions.

## Final public V3 release

The final production V3 artifact **must not expose or ship `QSM Synthetic Lab` as a normal production dockable**.

Required arrangement:

- Synthetic Lab source may remain behind a development build flag;
- headless synthetic regression remains permanent CI coverage;
- production package contains production QSM functionality only.

---

# V3 — QUALITY-CONTROLLED ACQUISITION

**Status: IN PROGRESS — FINAL PLANNED MAJOR STAGE.**

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until that valid-frame target is complete?

## 1. Valid Frame Target — CENTRAL FEATURE / FIRST IMPLEMENTATION BLOCK

This is not an after-the-fact re-acquisition pass. Sequence progress itself counts valid frames.

```text
Requested valid frames: 300
Physically captured:     203
Rejected:                  8
Valid sequence progress: 195 / 300
```

The intended loop semantics are:

```text
while ValidCount < RequestedValidFrames:
    capture eligible LIGHT
    wait for QSM classification of that exact LIGHT
    CapturedCount++

    ACCEPTED -> ValidCount++
    WARNING  -> ValidCount++ by default (configurable)
    REJECTED -> ValidCount unchanged
    ERROR    -> ValidCount unchanged
    LEARNING -> ValidCount unchanged
```

At completion:

```text
Valid requested: 300
Valid:           300
Rejected:         17
Total captured:  321   # example also includes learning/error if any
```

### Advanced Sequencer integration design

Do **not** decrement or patch N.I.N.A.'s standard `Take Exposure` iteration counter.

A native plugin sequence condition is being implemented as:

```text
QSM Valid Frame Target
```

It is attached to the repeated Advanced Sequencer container. It owns persistent progress:

- target valid frames;
- valid frames;
- captured eligible frames observed while active;
- rejected frames;
- warning frames;
- learning frames;
- error frames;
- last accounted QSM frame index;
- last status/cause;
- timeout/fail-safe state.

The condition synchronises with `QualitySessionRuntime.FrameProcessed` so N.I.N.A. cannot begin the next loop decision before QSM finishes classifying the exposure.

For the first field-test implementation, the parent controlled container should produce exactly one eligible LIGHT per iteration. This avoids ambiguous mapping and will be relaxed only after a multi-exposure correlation design is explicitly tested.

### Multiple filters

Use one QSM valid-target condition per filter/exposure block, e.g.:

```text
L   206 / 300 valid   (217 captured, 11 rejected)
R   100 / 100 valid   (108 captured,  8 rejected) COMPLETE
G    70 / 100 valid   ( 72 captured,  2 rejected)
```

A future helper container may make setup more ergonomic, but the condition is the source of truth so existing Advanced Sequencer routines can be retrofitted without replacing their `Take Exposure` instruction.

## 2. Count modes

Planned field options:

```text
Mode: Valid / accepted-quality frames   [default]
Count WARNING as valid: yes             [default]
Count LEARNING as valid: no             [default]
```

A pure captured-exposure mode may be exposed for comparison/debugging but is not the defining V3 behavior.

## 3. Auto calibration

Planned after the valid-target + source-scope gate is stable:

- derive suggestions from a stable early-session window;
- never silently alter limits in default mode;
- UI actions: `Apply / Modify / Ignore`;
- suggestions must show which evidence produced the proposed threshold.

## 4. Adaptive thresholds

Modes:

```text
OFF
Suggest Only   <- preferred default
Automatic
```

Automatic mode must remain bounded by explicit safety limits and must not normalize persistent bad weather as acceptable.

## 5. Smart pause / resume

Planned behavior:

- do not pause for one isolated bad frame;
- pause only after persistent degradation/event evidence;
- resume only after multiple consecutive healthy checks;
- no endless pause/resume oscillation;
- preserve sequence state and counters.

## 6. Environmental correlation

Optional where N.I.N.A. exposes trustworthy data:

- wind;
- humidity;
- cloud sensor;
- SQM;
- temperature;
- dew point.

Environmental values are diagnostic/correlative unless explicitly enabled for control.

## 7. Predictive degradation

Use V2 trend/event evidence to warn before hard rejection becomes likely. Prediction must be clearly labeled as predictive, not as a measured reject reason.

---

# V3 FIELD-TEST GATES

Before asking the user to try V3 on sky:

1. plugin builds against `.NET 10` + `NINA.Plugin 3.3.0.1057-nightly`;
2. all existing 108 V1/V2 synthetic frames still pass unchanged;
3. source-scoping tests prove:
   - `SNAPSHOT` ignored;
   - plate-solve snapshot ignored;
   - manual/test LIGHT outside selected sequence scope ignored by safe default;
   - eligible Advanced Sequencer LIGHT accepted;
   - QSM controlled-block LIGHT marked controlled;
   - file action never runs for ineligible source;
4. add deterministic valid-target tests, including at minimum:
   - all-good run reaches target with captured == valid + learning;
   - `10 valid` with `3 rejected` finishes at `10/10 valid` and `13+ learning captured` as expected;
   - warning counts as valid by default;
   - rejected does not increment valid;
   - error does not increment valid;
   - learning does not increment valid;
   - duplicate `Check()` calls do not double-count one QSM frame;
   - reset clears condition progress;
   - persisted counters survive serialization/clone path where applicable;
   - missing QSM result times out and stops safely rather than looping forever;
5. Advanced Sequencer condition appears and renders correctly in N.I.N.A.;
6. development field-test package can include Synthetic Lab for offline validation;
7. production package build excludes Synthetic Lab UI;
8. roadmap/changelog/version metadata updated before artifact handoff.

For the **first real-sky V3 test**, keep QSM file handling in `Monitor Only` so sequence-count behavior is tested independently from rename/move actions.

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
| Rejected-frame cause markers |  | ✅ | ✅ |
| Frame ranking |  | ✅ | ✅ |
| Interactive HTML report |  | ✅ | ✅ |
| Explicit ON/OFF activation | ✅ | ✅ | 🚧 hardening |
| Acquisition source scoping |  |  | 🚧 |
| Auto calibration |  |  | ⏳ |
| Adaptive threshold suggestions |  |  | ⏳ |
| Environmental correlation |  |  | ⏳ |
| Smart pause/resume |  |  | ⏳ |
| Accepted/valid-only sequence progress |  |  | 🚧 |
| Valid Frame Target |  |  | 🚧 |
| Predictive degradation |  |  | ⏳ |

Synthetic Lab is intentionally omitted from the production feature matrix because it is a development/test harness.

---

# ARCHITECTURAL CONSTRAINTS

1. Never refactor the Quality Engine in a way that breaks V3 valid-frame counting.
2. Keep Quality/decision state independent from filesystem actions.
3. Rejected-file handling must never become the source of truth for ACCEPT/REJECT.
4. V2 temporal intelligence layers on raw per-frame evidence; it must not erase or hide it.
5. V3 sequencer control consumes the same `FrameQualityResult` already recorded by the engine.
6. Development Synthetic Lab code must remain separable from production packaging.
7. Do not claim a feature validated unless the relevant CI/synthetic/host test passed.
8. HFR/FWHM/eccentricity remain excluded from core reject logic unless the user explicitly changes that decision.
9. Rejection marker icons are presentation only; raw reject reason codes remain authoritative.
10. The V3 loop must fail safe if classification is missing or ambiguous; never capture indefinitely because of a plugin synchronization fault.
11. Never infer file eligibility merely from extension/path. Acquisition provenance must be established from N.I.N.A. image type + sequence/runtime evidence.
12. Never rename/move an ineligible or provenance-unknown frame.

---

# DOCUMENTATION / HANDOFF DISCIPLINE

For every material change:

1. update this roadmap/current-state section in the same development cycle;
2. record completed behavior and discovered bugs/constraints;
3. state the exact next implementation step;
4. keep branch/PR/runtime compatibility current;
5. record validation evidence before marking work complete.

A future AI should read this file first, then `README.md`, `CHANGELOG.md`, and the active branch/PR status.
