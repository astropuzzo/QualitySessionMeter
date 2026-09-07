# QualitySessionMeter Roadmap

QualitySessionMeter is developed in three product stages: **V1** real-time quality monitoring, **V2** temporal/session intelligence, and **V3** quality-controlled acquisition through the N.I.N.A. Advanced Sequencer.

This file is the operational handoff/source of truth. Material code/UI changes must update this state in the same development cycle.

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
  V1 + V2 + V3 merged
  V3 merge commit: 0623f837ac034435bd9c67e0ab4d89e11b9a0018

public release
  v1.0.0.0 — published
  production ZIP published by GitHub Actions
  final main CI: PASS

active hotfix branch
  hotfix-v1.0.0.1-ui-polish

hotfix version
  1.0.0.1

hotfix purpose
  graphical/readability/accessibility fixes discovered during immediate N.I.N.A. host test
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
- rejected-frame timeline markers and cause visualization;
- `frames.csv`, `events.csv`, `session.json`, `quality.svg`, self-contained `report.html`.

Final V2 regression:

```text
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

# V3 — CLOSED / RELEASED

Release: **1.0.0.0**.

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until the valid-frame target is complete?

## Explicit OFF / ON and source scoping

Implemented:

- QSM OFF means no analysis, no valid-frame control and no file action;
- safe monitoring scopes:
  - `QSM controlled blocks only`;
  - `Advanced Sequencer LIGHTs` — recommended/default scope;
  - `All LIGHTs` — opt-in compatibility mode;
- `ImageType == LIGHT` is mandatory;
- sequence metadata and QSM arm state establish acquisition provenance;
- transient sequencer-running evidence may authorize monitoring but never by itself authorizes file mutation;
- manual/external LIGHTs may be monitored in `All LIGHTs` but cannot be renamed/moved without safe provenance;
- snapshots / plate-solving frames remain outside QSM LIGHT processing.

## QSM Valid Frame Target

Implemented as a native Advanced Sequencer condition. QSM never decrements or patches N.I.N.A.'s normal `Take Exposure` counter.

```text
while ValidCount < RequestedValidFrames:
    capture eligible LIGHT
    wait for matching QSM classification
    CapturedCount++

    ACCEPTED -> ValidCount++
    WARNING  -> ValidCount++ by default (configurable)
    REJECTED -> ValidCount unchanged
    ERROR    -> ValidCount unchanged
    LEARNING -> ValidCount unchanged
```

Safety semantics:

- classification is correlated to the controlled acquisition rather than consuming an arbitrary later frame;
- missing classification times out and stops fail-safe;
- duplicate checks cannot double-count the same frame;
- first field design assumes one eligible LIGHT per controlled repeated iteration.

## Adaptive calibration / thresholds

Implemented modes:

```text
OFF
Suggest Only   <- recommended default
Automatic      <- bounded by explicit safety ceilings
```

- stable accepted same-context data drives suggestions;
- Suggest Only never changes thresholds automatically;
- Apply / Ignore actions are explicit;
- Automatic remains bounded by safety ceilings;
- bad/rejected evidence is not normalized into a clean baseline.

## Predictive degradation

Implemented as diagnostic evidence only. Prediction does not create a hard rejection by itself.

## Environmental correlation

Implemented as optional diagnostic correlation using available N.I.N.A. weather/environment data. It remains diagnostic only in V3 production.

## Smart Recovery Gate

Implemented as opt-in Advanced Sequencer behavior:

- acts only between exposures;
- never aborts an active shutter;
- requires persistent controlled reject/error evidence;
- bounded cooldown and optional consecutive healthy guide checks;
- fail-safe behavior prevents indefinite waiting.

## Production Control Center

Implemented for global activation, source scope, Monitor Only, calibration, predictive/environment status and Smart Recovery settings.

---

# 1.0.0.1 UI / USABILITY HOTFIX — IN PROGRESS

Immediate host testing of 1.0.0.0 exposed presentation defects that automated logic tests could not detect. These are treated as real release-quality bugs, not cosmetic preferences.

Observed in N.I.N.A. screenshots:

- N.I.N.A.'s toggle `CheckBox` template rendered `ON/OFF` switches while swallowing the `Content` labels, leaving unexplained switches in both Plugin Options and the QSM Control Center;
- the Control Center stretched controls across a very wide host panel and produced poor visual hierarchy;
- timeline rejection/event badges used emoji glyphs whose Windows font metrics produced inconsistent/misaligned rendering;
- event badges could look detached from their vertical frame marker;
- user-facing controls lacked enough contextual explanation for somebody who did not already know the internal QSM model.

Hotfix implementation completed so far:

- Control Center switches converted to explicit two-column rows: text/help on the left, switch on the right; no dependency on `CheckBox.Content` rendering;
- Control Center width/hierarchy tightened;
- detailed tooltips added for:
  - global QSM enable/disable;
  - Monitor Only;
  - monitoring/source scope and every scope choice;
  - adaptive calibration modes, sample window, proposal status and Apply/Ignore actions;
  - predictive degradation;
  - environmental correlation;
  - Smart Recovery enable, reject streak, cooldown and healthy-resume checks;
- Plugin Options now uses a replacement labeled/help-rich template, again avoiding `CheckBox.Content` for toggle labels;
- Plugin Options tooltips explain every user-editable setting, including what it measures, what a stricter value means, and whether it affects Quality score or hard rejection;
- V3 Advanced Sequencer `QSM Valid Frame Target` now has explicit labels/tooltips for valid target, warning counting, progress semantics and fail-safe behavior;
- V3 `QSM Smart Recovery Gate` now explains its between-exposure behavior and relationship to Control Center settings;
- live timeline emoji badges replaced with deterministic text cause codes:
  - `G` = guiding/tracking;
  - `S` = stars/transparency/cloud;
  - `B` = background/haze/sky brightness;
  - `!` = analysis error;
- event badge is centered on the exact same X coordinate as its vertical frame marker;
- dense timelines suppress overlapping badge text while retaining every event line;
- hovering an event line exposes exact frame/cause/raw reason; hovering the rest of the timeline explains all channels and marker codes.

Remaining 1.0.0.1 gate:

```text
Windows CI compile
→ production + field-test packaging
→ full V1/V2/V3 synthetic regression unchanged
→ merge hotfix
→ main CI
→ publish v1.0.0.1
→ host screenshot verification
```

Do not claim the graphical hotfix verified until the rebuilt package is opened in the real N.I.N.A. host and visually checked.

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

Headless SyntheticCheck remains permanent CI coverage.

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

# ARCHITECTURAL / UX CONSTRAINTS

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
12. User-facing switches must not depend on N.I.N.A. `CheckBox.Content` being visible; label switches explicitly in layout.
13. Every non-obvious user control must have contextual help explaining meaning, effect, safe/default use and whether it influences hard rejection.
14. Avoid emoji as precision chart glyphs; use deterministic vector/text rendering whose alignment is controlled by QSM.

---

# NEXT STEP

Finish the **1.0.0.1 UI/usability hotfix** through Windows CI, merge/release it, then obtain a fresh real-host screenshot before considering the presentation layer closed.
