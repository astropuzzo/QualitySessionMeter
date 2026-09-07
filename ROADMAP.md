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
  1.0.0.1 UI hotfix merged
  current main: 933aea229767adba158432250e014675877d31e8

public releases
  v1.0.0.0 — V3 release
  v1.0.0.1 — UI/usability hotfix

active branch
  hotfix-v1.0.0.2-timeline-ux

active PR
  #5 — 1.0.0.2 self-explanatory timeline + mobile integration contract

candidate version
  1.0.0.2

candidate status
  IMPLEMENTED / CI + HOST VISUAL GATE PENDING
```

Known non-blocking dependency warning:

- `NINA.Image 3.3.0.1057-nightly` requests `NINA.Accord.Imaging >= 3.5.3-alpha`; NuGet resolves `3.5.3` (`NU1603`).

---

# V1 — CLOSED / MERGED

Authoritative behavior retained by V2/V3:

- Quality Meter 0–100;
- exposure-specific guide RMS;
- sustained/hard guide excursion;
- relative star-count loss;
- relative background change;
- context-separated rolling baselines;
- rejected/warning frames never contaminate the baseline;
- missing required analysis => `ERROR / UNASSESSED`;
- no automatic deletion;
- Quality Score remains independent from hard ACCEPT/REJECT.

HFR, FWHM and eccentricity remain excluded from hard rejection logic unless explicitly reconsidered.

---

# V2 — CLOSED / MERGED

Implemented and validated:

- Confidence Score independent from Quality and hard decision;
- Session Event Grouping;
- guide-pattern analysis;
- conservative slow-trend diagnostics;
- multichannel timeline;
- Best/Worst accepted-frame ranking;
- rejected-frame cause visualization;
- `frames.csv`, `events.csv`, `session.json`, `quality.svg`, self-contained `report.html`.

Canonical whole-night regression remains 6 profiles / 108 frames / 0 expected failures.

---

# V3 — CLOSED / RELEASED

V3 provides quality-controlled acquisition through the Advanced Sequencer:

- explicit OFF/ON and source scoping;
- safe provenance/file-action gates;
- `QSM Valid Frame Target` with QSM-owned accepted/valid progress;
- `ACCEPTED` advances valid progress;
- `WARNING` advances by default, configurable;
- `REJECTED`, `ERROR`, `LEARNING` do not advance;
- correlated controlled-frame accounting;
- fail-safe classification timeout;
- adaptive threshold suggestion / bounded automatic modes;
- predictive degradation diagnostics;
- optional environment correlation;
- opt-in Smart Recovery Gate operating only between exposures;
- production Control Center;
- field-test vs production packaging separation.

---

# 1.0.0.1 UI / USABILITY HOTFIX — RELEASED

Real-host screenshots exposed UI defects not detectable by headless logic regression.

Completed and released:

- explicit labels beside N.I.N.A. toggle templates that previously showed orphaned ON/OFF switches;
- compact Control Center hierarchy;
- help-rich Plugin Options;
- detailed tooltips for user-editable settings;
- Advanced Sequencer help for Valid Frame Target and Smart Recovery;
- deterministic `G/S/B/!/?` event codes instead of emoji glyphs;
- event badges aligned to exact frame X positions;
- dense badge-overlap suppression while preserving event lines;
- event-line hover with exact frame/cause/reason.

Release `v1.0.0.1` was created after green Windows production/field-test/regression CI.

---

# 1.0.0.2 TIMELINE / MOBILE HOTFIX — IN PROGRESS

Immediate follow-up host testing showed the timeline still required knowledge of QSM internals to understand what the lines represented and what they were relative to.

## Timeline readability — implemented on PR #5

- explicit color-matched legend/dot for every plotted series:
  - blue Quality;
  - purple Confidence;
  - green Guide RMS;
  - yellow Stars Δ;
  - salmon Background Δ;
- units and direction-of-goodness visible in the graph;
- dashed `0% rolling baseline` reference for image deltas;
- live dashed active limits for Guide RMS, star loss and background +/- deviation;
- limit/reference lines bound to current QSM settings so changes during a running sequence update the chart;
- dynamic RMS and image-delta plotting ranges so limits remain visible;
- hover anywhere in the plot selects the nearest frame and shows:
  - Quality/Confidence;
  - Guide RMS + current limit;
  - star value + rolling baseline + delta + threshold;
  - background value + rolling baseline + delta + +/- limits;
  - status/cause/reason;
- explicit `baseline learning/not ready` text while a context has not reached its minimum learning sample count;
- existing event markers remain presentation-only and aligned to the same frame X axis.

## Touch 'n' Stars / mobile integration — QSM side implemented

QSM now exposes a read-only versioned companion contract through N.I.N.A.'s `IMessageBroker`:

```text
request  QualitySessionMeter.ApiV1.RequestSnapshot
response QualitySessionMeter.ApiV1.Snapshot
```

The response is correlated to the request and contains plain serializable dictionaries/lists rather than QSM CLR model instances.

Snapshot coverage:

- QSM mode/status;
- session summary counters;
- live thresholds and baseline configuration;
- current frame;
- latest 160 frames;
- star/background rolling baseline values and deltas;
- prediction/environment hints;
- canonical series colors/units/meaning for matching desktop/mobile visuals.

Safety/architecture:

- contract v1 is read-only;
- QSM opens no new HTTP port;
- no direct QSM DLL reference is required by a companion plugin;
- the intended Touch 'n' Stars adapter uses its existing N.I.N.A. plugin/server and `/api` layer;
- detailed contract: `docs/TOUCH-N-STARS.md`.

### Touch 'n' Stars work still required for phone visibility

The QSM-side bridge alone does not modify the third-party Touch 'n' Stars distribution. TNS must add:

1. a backend adapter such as `GET /api/qsm/snapshot` that bridges its server to the QSM broker request/response topics;
2. a normal responsive TNS plugin under `src/plugins/quality-session-meter/` using its existing Vue/Pinia plugin architecture;
3. touch interaction for nearest-frame inspection instead of mouse-hover-only behavior.

The integration should remain read-only first. Remote threshold/file/control writes require a separate safety-reviewed versioned command contract.

## 1.0.0.2 release gate

```text
Windows production build
→ Windows field-test build
→ packaging separation
→ full V1/V2/V3 synthetic regression unchanged
→ merge PR #5
→ main CI green
→ publish v1.0.0.2
→ N.I.N.A. host screenshot visual verification
```

Do not mark the timeline visually verified until the rebuilt package is opened in the real N.I.N.A. host.

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
| Guide-pattern / trend analysis |  | ✅ | ✅ |
| Multichannel timeline |  | ✅ | ✅ |
| Rejected-frame cause markers |  | ✅ | ✅ |
| Frame ranking / HTML report |  | ✅ | ✅ |
| Explicit ON/OFF activation | ✅ | ✅ | ✅ |
| Acquisition source scoping |  |  | ✅ |
| Adaptive threshold suggestions |  |  | ✅ |
| Environmental correlation |  |  | ✅ |
| Smart Recovery |  |  | ✅ |
| Valid Frame Target |  |  | ✅ |
| Predictive degradation |  |  | ✅ |
| Self-explanatory baseline/limit timeline |  |  | 1.0.0.2 candidate |
| Read-only companion/mobile broker contract |  |  | 1.0.0.2 candidate |

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
9. The V3 loop must fail safe on missing classification.
10. Never infer file eligibility merely from extension/path.
11. Never rename/move an ineligible manual/external frame.
12. User-facing switches must not depend on N.I.N.A. `CheckBox.Content` being visible.
13. Every non-obvious user control must have contextual help.
14. Avoid emoji as precision chart glyphs.
15. Every relative timeline series must visibly identify its reference/baseline and units.
16. Active hard-rejection limits shown on a graph must reflect the live settings used by the engine.
17. Companion/mobile integrations must be versioned and assembly-decoupled; read-only visibility precedes remote control.

---

# NEXT STEP

Complete PR #5 CI, fix any Windows/WPF issues, release **1.0.0.2**, and obtain a fresh host screenshot. In parallel, use `docs/TOUCH-N-STARS.md` as the contract for the Touch 'n' Stars backend/frontend contribution that makes the QSM dashboard visible on phone/tablet.
