# Changelog

All notable changes to QualitySessionMeter are documented here.

## [1.0.0.1] - 2026-09-07

### UI / usability hotfix

Immediate testing of the public V3 build inside N.I.N.A. exposed presentation defects that were invisible to headless regression tests.

- Fixed N.I.N.A. toggle styling swallowing `CheckBox.Content`, which produced bare `ON/OFF` switches with no visible explanation beside them.
- Rebuilt the QSM Control Center switch rows with explicit left-side labels/help and right-side switches, independent of the host CheckBox content template.
- Constrained and reorganized Control Center layout so settings no longer stretch awkwardly across very wide Imaging panels.
- Replaced the legacy Plugin Options template at runtime with a labeled/help-rich version that preserves every existing binding while rendering switch labels explicitly.
- Added detailed contextual tooltips throughout user-editable settings. Tooltips explain what each control measures, what changing it does, safe/default usage, and whether it affects the displayed Quality score, hard rejection, file handling or Advanced Sequencer control.
- Added help to V3 Advanced Sequencer items:
  - `QSM Valid Frame Target` explains valid-vs-captured semantics, warning counting, LEARNING/ERROR/REJECTED behavior and fail-safe matching;
  - `QSM Smart Recovery Gate` explains between-exposure recovery behavior and Control Center dependency.
- Replaced emoji rejection glyphs with deterministic text cause codes to avoid Windows font/fallback alignment problems:
  - `G` guiding/tracking;
  - `S` stars/transparency/cloud;
  - `B` background/haze/sky brightness;
  - `!` analysis error;
  - `?` unmapped cause.
- Reworked live timeline event rendering so each badge is centered on the same X coordinate as its frame marker.
- Dense timelines keep every vertical event line while suppressing overlapping badge text rather than drawing unreadable collisions.
- Added timeline hover help: normal hover explains all channels/cause codes; hovering a warning/rejection/error marker exposes exact frame number, interpreted cause and raw reason text.
- Bumped plugin/file/assembly version to `1.0.0.1`.

### Validation status

- Host screenshot defects reproduced/understood from N.I.N.A. `3.3 NIGHTLY #057`.
- Windows CI, production/field-test packaging and full V1/V2/V3 synthetic regression are required before this hotfix is merged/released.
- A fresh real-host screenshot is required after rebuilding before the presentation fix is considered verified.

---

## [1.0.0.0] - 2026-09-07

### V3 — Quality-Controlled Acquisition

- Added explicit global QSM OFF/ON behavior. OFF means no analysis, no valid-frame control and no file action.
- Added acquisition source scoping with safe defaults for QSM-controlled blocks, Advanced Sequencer LIGHTs and opt-in All LIGHT monitoring.
- File mutation is stricter than monitoring: manual/external LIGHTs are never renamed/moved, and transient sequencer-running evidence alone cannot authorize mutation.
- Added native Advanced Sequencer `QSM Valid Frame Target` without altering N.I.N.A.'s built-in `Take Exposure` counter.
- `ACCEPTED` advances valid progress; `WARNING` advances by default but is configurable; `REJECTED`, `ERROR` and `LEARNING` do not advance it.
- Missing classification stops the controlled loop fail-safe instead of capturing indefinitely.
- Added deterministic valid-frame accounting, duplicate-frame protection, persisted condition progress and source/file-action regression oracles.
- Added adaptive calibration modes: OFF, Suggest Only and bounded Automatic with explicit safety ceilings.
- Added predictive degradation warnings as a diagnostic channel separate from hard rejection.
- Added optional N.I.N.A. weather/environment correlation for cloud cover, humidity, wind, SQM, temperature and dew point when available.
- Added opt-in `QSM Smart Recovery Gate`, which acts only between exposures after persistent degradation and never aborts an active shutter.
- Added production `QSM Control Center` for activation, scope, adaptive calibration, prediction/environment and Smart Recovery settings.
- Added explicit production vs field-test packaging: production excludes the in-host Synthetic Lab dockable/resources; field-test retains them.
- CI builds both package variants, runs the full V1/V2/V3 synthetic regression and creates the public production release from `main`.
- Bumped plugin version to `1.0.0.0` for N.I.N.A. `3.3 NIGHTLY #057`, `.NET 10`.

### Release validation

- PR V3 Windows CI: PASS.
- Production source-set/build/package: PASS.
- Field-test source-set/build/package: PASS.
- Full V1/V2/V3 synthetic regression: PASS.
- Main Windows CI after merge: PASS.
- Public GitHub release `v1.0.0.0`: published successfully.
- Known non-blocking nightly warning remains `NU1603` because `NINA.Image 3.3.0.1057-nightly` requests `NINA.Accord.Imaging >= 3.5.3-alpha` and NuGet resolves `3.5.3`.

---

## [0.2.0.1] - 2026-09-07

### Rejected-frame timeline visibility

- Added explicit rejected-frame markers to both the live N.I.N.A. multichannel timeline and the generated HTML report.
- Every rejected frame receives a strong vertical marker/band at its exact timeline position.
- Added compact cause badges using a shared visual vocabulary.
- Added `RejectionVisual` as the single presentation mapping used by both WPF and HTML so the two timelines cannot silently diverge.
- HTML rejected-frame markers include hover tooltips with frame number, probable cause and raw reason text.
- HTML frame-history Cause cells show the same compact cause mapping.
- Marker rendering is presentation-only and does not alter Quality, Confidence, baseline state or hard reject decisions.
- Removed nullable-annotation warnings previously emitted by `HtmlReportWriter` while nullable annotations are disabled for the project.
- Bumped the V2 host-test build to `0.2.0.1`.

---

## [0.2.0.0] - 2026-09-07

### V2 — Smart Quality Analysis field-test candidate

- Added per-frame **Confidence Score** independent from Quality and hard ACCEPT/REJECT decisions.
- Confidence is derived from data completeness, baseline maturity, threshold separation and independent-channel agreement.
- Missing analysis data produces zero confidence in the quality judgment; LEARNING confidence is capped until the adaptive reference matures.
- Added **Session Event Grouping** with event type, start/end, affected frames, severity, mean/peak confidence and primary cause.
- Added `events.csv` and event persistence inside `session.json`.
- Added **Guide Pattern Analysis** with diagnostic classes including stable, isolated spike, sustained excursion, oscillation, drift, wind-like and irregular behavior.
- Added conservative **Slow Trend Analysis** for star/background behavior with expected value, rate, R² and residual diagnostics.
- Hard V1 star/background rejection still uses the robust rolling reference; trend analysis does not normalize away a slowly worsening cloud event.
- Added a V2 **multichannel timeline** covering Quality, Confidence, Guide RMS, stars and background deviations.
- Added accepted-frame **Best / Worst ranking**.
- Added self-contained interactive `report.html` with session summary, timeline, events, cause distribution, ranking and frame history.
- Expanded the main N.I.N.A. dockable into a scrollable V2 dashboard.
- Synthetic Lab exposes selectable whole-night profiles for Canonical, Excellent, Average, Poor, Severe/Disaster and Deterioration + Recovery sessions.
- Added deterministic CI oracles for Confidence, Event Grouping, Guide Patterns and Slow Trend behavior on top of the existing 108-frame whole-night regression.
- Fixed misleading presentation during adaptive learning: LEARNING frames display `Quality — / LEARNING`; analysis errors display `Quality — / UNASSESSED`.
- Bumped field-test version to `0.2.0.0` for N.I.N.A. `3.3 NIGHTLY #057` / `NINA.Plugin 3.3.0.1057-nightly` / `.NET 10`.

### Validation

- plugin build;
- 6 whole-night profiles / 108 frames / 0 failures;
- Confidence oracle;
- Event Grouping oracle;
- Guide Pattern oracle;
- Trend oracle;
- CSV/JSON/SVG/HTML persistence;
- isolated `BAD_` and move-to-`Rejected` file-action tests.

---

## [0.1.0.1] - 2026-09-07

### N.I.N.A. 3.3 NIGHTLY #057 field-test compatibility

- Retargeted the current field-test build to `.NET 10` and `NINA.Plugin 3.3.0.1057-nightly` for N.I.N.A. `3.3.0.1057`.
- Fixed plugin startup failure caused by manually re-loading an already exported WPF `ResourceDictionary` from both dockable constructors (`Cannot re-initialize ResourceDictionary instance`). Dockable icons are created directly in code.
- Aligned `MinimumApplicationVersion` metadata with the tested 3.3 nightly build.
- User verified that build `0.1.0.1` loads successfully inside N.I.N.A. 3.3 NIGHTLY #057.

### Synthetic Lab development harness

- Added an isolated `QSM Synthetic Lab` dockable for deterministic testing inside the real N.I.N.A. host without camera hardware or real image files.
- Synthetic mode ignores live `ImageSaved` callbacks, uses its own baseline/session store and never invokes real rejected-file actions.
- Added a frame-level PASS/FAIL oracle using the same production `QualityEngine`, `BaselineEngine`, `GuideMetricsCalculator` and `SessionStore` code paths.
- User executed the original canonical in-host suite successfully: `PASS 29/29`.
- Synthetic testing exposed and fixed a persistence bug: `session.json` could fail on `NaN` values during LEARNING. Unavailable floating-point values are serialized as JSON `null`.
- Added isolated CI tests for `BAD_` prefix and `Rejected` subfolder actions using temporary fake files.

### Multi-profile synthetic expansion

- Canonical regression;
- Excellent night;
- Average night;
- Poor night;
- Severe / disaster night;
- Deterioration + recovery.

CI exercises every profile and fails if any frame does not match its expected state/rejection reasons.

---

## [0.1.0] - 2026-09-07

### V1 — initial implementation

#### Added

- Initial N.I.N.A. plugin bootstrap, originally developed against `.NET 8` and `NINA.Plugin 3.2.0.9001` before the current 3.3 nightly field target was adopted.
- Dockable Quality Session Meter panel for the Imaging workspace.
- Real-time **Quality Score 0–100** for every LIGHT exposure.
- Independent sub-scores for guiding quality, guiding stability, transparency/relative star count and background stability.
- Worst-metric-dominant overall score with configurable weighting.
- Hard rejection rules independent from the Quality Score.
- Exposure-specific guiding analysis from timestamped `IGuiderMediator.GuideEvent` samples.
- Guide RMS, sustained guide-excursion and hard guide-excursion rejection.
- Adaptive star-count and background-median rolling baselines separated by target, filter, exposure, gain, binning and camera.
- Baseline contamination protection: rejected and warning frames do not update the reference.
- Initial `LEARNING` phase.
- Basic probable-cause classification for wind/guiding, clouds/transparency and background/haze events.
- Safe rejected-frame handling: Monitor Only, keep in place, `BAD_` prefix, or move to `Rejected` subfolder.
- No automatic frame deletion.
- Persistent session output: `frames.csv`, `session.json`, `quality.svg`.
- Live Quality timeline and per-frame history table.
- Session counters and acceptance/session-quality indicators.
- GitHub Actions Windows build and Release artifact publishing.

#### Hardened before field testing

- Missing required enabled analysis data produces explicit `ERROR / UNASSESSED` instead of false acceptance.
- `MinimumLearningFrames` cannot exceed the rolling baseline window.
- Hard guide-excursion threshold cannot be configured below the normal excursion threshold.
- Sustained guide duration requires consecutive confirmed above-threshold samples; one spike has `0 s` sustained duration.
- Session reset is serialized against frame processing.
- Physical frames retain stable frame indices even on analysis failure.
- Frame timestamps use exposure-start timestamp where available.
- Session folders use collision-safe millisecond-resolution names.
- JSON keeps stable `createdUtc` plus `updatedUtc` and supports unavailable floating-point values.
- CSV persists analysis/file-action error messages.
- File move/prefix failures remain non-destructive and are surfaced to the user.
- SVG timelines distinguish analysis-error frames.
- Repository/package/plugin licensing metadata aligned with Apache-2.0.

#### Deliberately excluded from rejection logic

- HFR
- FWHM
- eccentricity

These metrics were excluded after real-world observations showed they can remain deceptively similar on visibly wind-damaged point–streak–point stellar profiles.
