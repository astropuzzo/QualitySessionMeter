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
  V1 validated baseline
  merge commit: cc16deffe88181bd92a067f2e0366831f02d9959

active branch
  v2-smart-quality-analysis

active PR
  #2 — V2 — Smart Quality Analysis
  draft / not merged

latest field-test line
  0.2.0.1
  purpose: rejected-frame timeline cause markers after successful 0.2.0.0 host review
```

**Do not merge PR #2 yet.** Build `0.2.0.0` already rendered successfully in the user's real N.I.N.A. host and the V2 report/dashboard were inspected. The remaining V2 host gate is the user-requested timeline enhancement implemented in `0.2.0.1`.

### User-local V2 host review already completed

User supplied screenshots from N.I.N.A. and `report.html` showing that the V2 dashboard/report load and render correctly, including:

- Quality + Confidence;
- Guide Pattern;
- Trend;
- Session Events;
- Best/Worst accepted ranking;
- multichannel timeline;
- frame history;
- generated HTML report.

The report was readable and did not require a general redesign.

### Final V2 UI request before merge

Rejected frames must be immediately visible in both timelines rather than requiring the user to inspect the frame-history table.

Implemented presentation vocabulary:

```text
💨  guide / wind problem
☁  star-count / cloud / transparency problem
🌫  background / haze problem
❌  rejected with an unmapped future reason
⚠  analysis error / unassessed marker
```

Rules:

- every `REJECTED` frame receives a strong vertical red marker/band at its exact timeline x-position;
- a compact cause badge is displayed above the marker;
- multi-channel rejects show multiple icons, e.g. `☁🌫` or `💨☁🌫`;
- marker mapping is presentation-only and never changes Quality, Confidence or hard reject decisions;
- HTML markers include a tooltip containing frame number, probable cause and raw reason text;
- HTML frame-history Cause cells also show the compact icon(s);
- WPF and HTML use the same shared `RejectionVisual` mapping.

Exact next gate: CI on `0.2.0.1`, then a short user-local visual check of rejected-frame markers in N.I.N.A. and the HTML report. If that passes, merge PR #2 and start V3.

---

# V1 — CLOSED DEVELOPMENT GATE

V1 is merged to `main` and already passed local host validation.

```text
Build 0.1.0.1
Plugin load in N.I.N.A. 3.3 NIGHTLY #057: PASS
In-host Canonical Synthetic Lab: PASS 29/29
```

V1 core rules remain unchanged in V2:

- Quality Meter 0–100;
- Guide RMS;
- sustained guide excursion;
- hard guide excursion;
- relative star-count loss;
- relative background change;
- context-separated rolling baselines;
- `ANY enabled hard rule fails -> REJECTED`;
- rejected frames never enter the baseline;
- warning frames do not update the baseline;
- a single guide spike is not a sustained excursion;
- missing required analysis data => `ERROR / UNASSESSED`, never false ACCEPTED;
- no automatic deletion;
- Quality Score and hard decision remain separate concepts.

Explicitly excluded from core reject logic unless the user changes this decision:

- HFR;
- FWHM;
- eccentricity.

---

# V2 — SMART QUALITY ANALYSIS

**Status: IMPLEMENTED + CI VALIDATED + MAIN HOST SURFACES REVIEWED; awaiting final 0.2.0.1 rejection-marker visual check.**

V2 answers:

> What is happening across the night, how certain are we, and what temporal pattern caused the degradation?

V2 layers intelligence on V1 evidence without replacing V1 hard rules.

## 1. Evidence-based Confidence Score — IMPLEMENTED

Per-frame Confidence is independent from Quality and ACCEPT/REJECT.

Evidence components:

- required-data completeness;
- baseline maturity;
- separation from configured thresholds;
- agreement between independent abnormal channels;
- diagnostic explanation text.

Rules:

- missing required data => confidence 0;
- LEARNING confidence is capped;
- near-threshold warnings stay moderate;
- strong multi-channel failures can reach very high confidence;
- confidence never overrides a hard reject rule.

Persisted to CSV/JSON and shown in the dashboard/history/report.

## 2. Event Grouping — IMPLEMENTED

Temporally related abnormal frames are grouped into session events with:

- event type;
- start/end;
- affected-frame count;
- rejected/warning/error counts;
- mean/peak confidence;
- severity;
- primary cause;
- healthy-gap bridging and stable-recovery closure;
- mixed-condition promotion where evidence changes during one continuous event.

Outputs:

- `events.csv`;
- event list inside `session.json`;
- dashboard/report event surfaces.

## 3. Guide Pattern Analysis — IMPLEMENTED

Diagnostic guide-pattern classes:

- `STABLE`;
- `ISOLATED SPIKE`;
- `SUSTAINED`;
- `OSCILLATION`;
- `DRIFT`;
- `WIND-LIKE`;
- `IRREGULAR`;
- unavailable.

Additional diagnostics include pattern confidence, drift rate, oscillation range, sign-change count, burstiness and explanation text.

These labels are diagnostic interpretations only and do not create new reject rules.

## 4. Slow Trend Analysis — IMPLEMENTED CONSERVATIVELY

Star/background behavior can be classified as:

- `STABLE`;
- `GRADUAL`;
- `ABRUPT`;
- unavailable.

The trend layer records expected value, trend rate, fit quality and residual.

Critical safety rule:

> Hard V1 star/background rejection continues to use the robust rolling reference. Trend analysis does not normalize away a slowly worsening cloud event.

Rejected frames never enter trend/reference history.

Coverage includes the selectable `Deterioration + recovery` profile plus dedicated deterministic benign/abrupt trend oracles.

## 5. Multichannel Timeline — IMPLEMENTED + REJECT MARKERS ENHANCED IN 0.2.0.1

Live/session inspection combines:

- Overall Quality;
- Confidence;
- Guide RMS;
- star-count deviation;
- background deviation;
- frame-state markers;
- strong rejected-frame vertical markers;
- compact rejected-frame cause icons using the shared `RejectionVisual` mapping.

The WPF timeline and HTML report share the same reason-to-icon vocabulary so the visual language cannot silently diverge.

## 6. Frame Ranking — IMPLEMENTED

Accepted frames are ranked into Best/Worst views using Quality as the primary ranking value and Confidence as diagnostic/tie-break context.

Ranking never replaces hard ACCEPT/REJECT.

## 7. Advanced Reporting — IMPLEMENTED

Each session can produce:

- `frames.csv`;
- `events.csv`;
- `session.json`;
- `quality.svg`;
- self-contained interactive `report.html`.

The HTML report contains session summary, multichannel timeline, events, cause distribution, ranking and detailed frame history. It has no CDN/runtime web dependency.

From `0.2.0.1`, rejected frames in the HTML timeline receive red bands/lines plus compact cause badges and hover tooltips; the cause icons are also visible in rejected/error frame-history rows.

## V2 N.I.N.A. dashboard — IMPLEMENTED

The dockable is a scrollable dashboard containing:

- Quality;
- Confidence;
- status/reason/probable cause;
- Guide RMS / max excursion;
- stars/background;
- Guide Pattern;
- Trend;
- session counters;
- multichannel timeline;
- frame history;
- grouped session events;
- Best/Worst accepted ranking;
- report/session-folder controls.

Presentation fixes:

- `LEARNING` => `Quality — / LEARNING` rather than `100 / EXCELLENT`;
- `ERROR` => `Quality — / UNASSESSED`;
- the internal numeric score remains available for deterministic regression/persistence;
- `0.2.0.1`: rejected timeline frames are visually explicit and carry mini cause icons.

---

# V2 REGRESSION BASELINE

The last fully recorded V2 candidate before the rejection-marker enhancement passed GitHub Actions on Windows against `.NET 10` and `NINA.Plugin 3.3.0.1057-nightly`.

```text
Candidate: 0.2.0.0
Commit: b709d09bf05739b4c3af61a36364dd5f2af0548f
Workflow run: #73 / 34150474822
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

Confidence oracles: PASS
Event grouping oracles: PASS
Guide-pattern oracles: PASS
Trend oracles: PASS
frames.csv: PASS
events.csv: PASS
session.json: PASS
quality.svg: PASS
report.html: PASS
isolated BAD_ action: PASS
isolated Rejected-folder action: PASS
clean plugin package: PASS
```

`0.2.0.1` must re-pass the same complete gate before being handed to the user.

Known non-blocking NuGet warning:

- N.I.N.A. nightly requests `NINA.Accord.Imaging >= 3.5.3-alpha`; NuGet resolves `3.5.3` (`NU1603`).

The previous nullable warnings in `HtmlReportWriter` were also cleaned while implementing the marker update.

---

# EXACT NEXT STEP — FINAL V2 HOST VISUAL GATE

1. Run CI on the exact `0.2.0.1` HEAD.
2. Require the full 108-frame V1+V2 suite and all deterministic oracles to remain green.
3. Produce a clean `0.2.0.1` field-test artifact.
4. User replaces the existing DLL/PDB in N.I.N.A. 3.3 NIGHTLY #057.
5. Run a reject-rich Synthetic Lab profile such as `Poor night` or `Severe / disaster night`.
6. Verify rejected frames have clear red timeline markers and correct mini-icons:
   - guide reject => `💨`;
   - star-count/cloud reject => `☁`;
   - background reject => `🌫`;
   - multi-channel reject => multiple icons.
7. Open generated `report.html` and verify the same visual mapping there.
8. If visually correct, record PASS here, merge PR #2 to `main`, then start V3 from that merge.

Do not ask the user to repeat already-passed general V2 dashboard/report validation unless a new regression is visible.

---

# SYNTHETIC LAB POLICY

Synthetic Lab is a **development / pre-release verification harness**, not a production feature.

Selectable whole-night profiles currently exposed in development/field-test builds:

- Canonical regression;
- Excellent night;
- Average night;
- Poor night;
- Severe / disaster night;
- Deterioration + recovery.

The Lab uses isolated baseline/session state, ignores live image-save processing while active and never applies real rejected-file actions.

## Final public V3 release

The final public V3 artifact **must not expose or ship QSM Synthetic Lab as a normal production dockable**.

Permitted final arrangement:

- development-only Synthetic Lab source/build flag remains in repository if useful;
- headless synthetic regression remains permanent CI coverage;
- production V3 package contains production QSM functionality only.

---

# V3 — QUALITY-CONTROLLED ACQUISITION

**Status: PLANNED FINAL MAJOR STAGE. Do not implement until V2 host validation passes unless the user explicitly changes the roadmap.**

V3 answers:

> How many genuinely valid exposures have I acquired, and can N.I.N.A. keep acquiring until that valid-frame target is complete?

## Valid Frame Target — central behavior

This is not an after-the-fact re-acquisition pass. Sequence progress itself counts accepted frames only.

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

## Additional V3 features

- auto-calibration from stable early-session data with `Apply / Modify / Ignore`;
- adaptive threshold modes `OFF / Suggest Only / Automatic` with **Suggest Only** preferred by default;
- smart pause on persistent deterioration rather than one bad frame;
- smart resume only after multiple healthy checks;
- optional environmental correlation with wind/humidity/cloud/SQM/temperature/dew point where N.I.N.A. exposes suitable data;
- predictive degradation warnings;
- Advanced Sequencer integration consuming the same QSM ACCEPTED/REJECTED state.

Before public V3 release:

- remove/exclude Synthetic Lab UI from the production artifact;
- retain automated synthetic regression in CI;
- verify production package exposes only production QSM features.

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
| Auto calibration |  |  | ✅ |
| Adaptive threshold suggestions |  |  | ✅ |
| Environmental correlation |  |  | ✅ |
| Smart pause/resume |  |  | ✅ |
| Accepted-only sequence progress |  |  | ✅ |
| Valid Frame Target |  |  | ✅ |
| Predictive degradation |  |  | ✅ |

Synthetic Lab is intentionally omitted from the production feature matrix because it is a development/test harness.

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
9. Rejection marker icons are a presentation layer only; raw reject reason codes remain authoritative.

---

# DOCUMENTATION / HANDOFF DISCIPLINE

For every material change:

1. update this roadmap/current-state section in the same development cycle;
2. record completed behavior and discovered bugs/constraints;
3. state the exact next implementation step;
4. keep branch/PR/runtime compatibility current;
5. record validation evidence before marking work complete.

A future AI should read this file first, then `README.md`, `CHANGELOG.md`, and the active branch/PR status.
