# QualitySessionMeter Roadmap

QualitySessionMeter is developed in three core stages: **V1** real-time quality monitoring, **V2** temporal/session intelligence, and **V3** quality-controlled acquisition through the N.I.N.A. Advanced Sequencer. Remote/mobile observability is layered on top of those stable cores and must not weaken their safety semantics.

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
  1.0.0.1 UI/usability hotfix merged
  1.0.0.2 self-explanatory timeline/mobile broker foundation merged
  current released main: 707fec90f381a9f2b64467ca643c406ede81b401

public releases
  v1.0.0.0 — V3
  v1.0.0.1 — UI/usability hotfix
  v1.0.0.2 — timeline readability + in-process mobile contract

active branch
  feature/openastro-mobile-http

active PR
  #6 — OpenAstro mobile bridge — read-only HTTP snapshot

candidate version
  1.1.0.0

candidate status
  FEATURE COMPLETE / FINAL VERSIONED CI + MERGE/RELEASE PENDING
```

Known non-blocking dependency warning:

- `NINA.Image 3.3.0.1057-nightly` requests `NINA.Accord.Imaging >= 3.5.3-alpha`; NuGet resolves `3.5.3` (`NU1603`).

---

# V1 — CLOSED / MERGED

Authoritative behavior retained by every later release:

- Quality Meter 0–100;
- exposure-specific guide RMS;
- sustained/hard guide excursion;
- relative star-count loss;
- relative background change;
- context-separated rolling baselines;
- baseline continuously updates from clean evidence;
- rejected/warning/error frames never contaminate the baseline;
- missing required analysis => `ERROR / UNASSESSED`;
- no automatic deletion;
- Quality Score remains independent from hard ACCEPT/REJECT.

HFR, FWHM and eccentricity remain deliberately excluded from hard rejection logic unless explicitly reconsidered.

---

# V2 — CLOSED / MERGED

Implemented and retained:

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
- `QSM Valid Frame Target` with QSM-owned valid progress;
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

# 1.0.0.1 — RELEASED

Real-host UI fixes:

- explicit labels beside N.I.N.A. toggle templates;
- compact Control Center hierarchy;
- help-rich Plugin Options and detailed tooltips;
- Advanced Sequencer help for Valid Frame Target and Smart Recovery;
- deterministic `G/S/B/!/?` event codes instead of emoji glyphs;
- exact event/frame X alignment and dense-overlap suppression.

---

# 1.0.0.2 — RELEASED

Timeline/mobile foundation:

- color-matched legend for Quality, Confidence, Guide RMS, Stars Δ and Background Δ;
- explicit dashed `0% rolling baseline`;
- live visible rejection limits;
- dynamic chart ranges/units;
- nearest-frame hover showing raw values, baselines, deltas and limits;
- process-local versioned `QualitySessionMeter.ApiV1` `IMessageBroker` snapshot contract;
- full-session summary + latest 160 chart frames;
- plain dictionary/list/scalar payload for assembly-decoupled companions.

---

# 1.1.0.0 OPENASTRO REMOTE MONITOR — RELEASE CANDIDATE

Goal: allow the user's modified ASIAIR/OpenAstro Control panel to become the single remote-facing session monitor while N.I.N.A./QSM remains private on the trusted LAN/Tailscale network.

Architecture:

```text
N.I.N.A. + QSM (Windows)
        |
        | tokenized read-only HTTP on trusted LAN/Tailscale
        v
OpenAstro Control (modified ASIAIR / internal eMMC)
        |
        | existing authenticated remote panel
        v
phone / tablet / remote browser
```

## QSM HTTP bridge — implemented

- new `QualitySessionHttpBridge`;
- bridge is **disabled** unless `QSM_REMOTE_TOKEN` exists;
- optional `QSM_REMOTE_PORT`, default `18973`;
- optional `QSM_REMOTE_BIND`;
- raw TCP HTTP implementation avoids Windows `HttpListener` URL-ACL/admin requirements;
- fixed-time token comparison;
- authenticated routes accept `X-QSM-Token` or Bearer auth;
- V1 remote bridge is strictly GET/read-only;
- no remote threshold changes, file mutations or sequencer commands.

Routes:

```text
GET /healthz
GET /api/v1/snapshot       authenticated
GET /api/v1/preview.jpg    authenticated
```

Security rule: port 18973 must not be exposed directly to the public Internet. The intended client is the trusted OpenAstro node.

## Mobile/session snapshot — implemented

Existing `QualitySessionMeter.ApiV1` snapshot now includes:

- full-session counters;
- latest 160 assessed frames for charting;
- current frame/status/cause;
- target, filter, exposure, gain, binning and camera;
- Quality / Confidence;
- star/background baselines and deltas;
- relevant live QSM settings;
- prediction/environment hints;
- canonical chart colors/units.

## True live guiding — implemented

Remote guide telemetry is **not** reconstructed from the RMS of the previous exposure and does not require a second direct PHD2 connection.

QSM reuses its existing N.I.N.A. `IGuiderMediator.GuideEvent` stream and exposes a rolling 20-second window:

- timestamped RA error in arcsec;
- timestamped DEC error in arcsec;
- latest total error;
- total RMS;
- RA RMS;
- DEC RMS;
- maximum excursion;
- up to 120 recent guide samples.

This remains guider-agnostic at the remote layer because N.I.N.A. supplies the guide events.

## Latest LIGHT preview — implemented

The bridge creates the mobile preview from N.I.N.A.'s processed `ImageSaved` `BitmapSource`:

- LIGHT frames only;
- max width 1280 px;
- JPEG quality 82;
- encoded and retained in RAM only;
- FITS/XISF is never re-read for the preview;
- no preview file is written to disk;
- preview generation failure cannot affect image saving or QSM classification.

This keeps remote traffic light and prevents the monitoring feature from depending on OpenAstro SERVER NVMe or removable Media USB.

## Validation so far

A pre-version-bump Windows run on commit `0390010bd7bcafbb2afecf049bc33298bf95430c` is fully green:

```text
production source-set gate     PASS
production build/package       PASS
field-test build/package       PASS
V1/V2/V3 synthetic regression  PASS
artifact upload                 PASS
```

One host-API compile issue was caught and fixed before merge: N.I.N.A. #057 exposes `Image.Id` as a non-nullable `int`; preview metadata now handles that exact API correctly.

Final 1.1.0.0 gate:

```text
versioned Windows CI green
→ merge PR #6
→ main CI green
→ publish v1.1.0.0 production package
→ install field-test build in real N.I.N.A.
→ configure trusted QSM token/path
→ field-test OpenAstro page with real sequence, guiding and LIGHT preview
```

Do not mark the remote preview/guide presentation as host-verified until a real N.I.N.A. + OpenAstro session has been observed.

---

# SYNTHETIC LAB / PACKAGING POLICY

Synthetic Lab is a development/field-test harness, not a public production feature.

```text
field-test: QsmDevelopmentBuild=true  -> Synthetic Lab included
production: QsmDevelopmentBuild=false -> Synthetic Lab code/UI excluded
```

Headless SyntheticCheck remains permanent CI coverage.

---

# ARCHITECTURAL / UX CONSTRAINTS

1. Never refactor the Quality Engine in a way that breaks valid-frame counting.
2. Quality/decision state remains independent from filesystem actions.
3. Rejected-file handling never becomes the source of truth for ACCEPT/REJECT.
4. V2 temporal intelligence must preserve raw per-frame evidence.
5. V3 sequencer control consumes the same `FrameQualityResult` recorded by the engine.
6. Synthetic Lab remains separable from production packaging.
7. Do not claim a validation gate passed until the relevant CI/host test actually passed.
8. HFR/FWHM/eccentricity remain excluded from hard reject logic unless explicitly changed.
9. The V3 loop fails safe on missing classification.
10. Never infer file eligibility merely from extension/path.
11. Never rename/move an ineligible manual/external frame.
12. Every non-obvious user control must have contextual help.
13. Every relative timeline series must visibly identify its reference/baseline and units.
14. Active hard-rejection limits shown on a graph must reflect live engine settings.
15. Remote/mobile integrations are versioned and read-only before any control contract is considered.
16. QSM remote secrets are never returned in snapshots or sent to the browser.
17. Mobile preview must remain display-only, bounded, in-memory and independent from FITS storage.
18. Remote monitoring must remain functional when OpenAstro SERVER NVMe or Media USB is absent.

---

# NEXT STEP

Finish the versioned **1.1.0.0** CI/release, then field-test the OpenAstro `NINA` page with a real sequence. After monitoring is proven stable, consider optional remote alerting and only then a separately versioned, explicitly safety-reviewed control surface.
