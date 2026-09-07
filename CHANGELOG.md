# Changelog

All notable changes to QualitySessionMeter are documented here.

## [0.1.0.1] - 2026-09-07

### N.I.N.A. 3.3 NIGHTLY #057 field-test compatibility

- Retargeted the current field-test build to `.NET 10` and `NINA.Plugin 3.3.0.1057-nightly` for N.I.N.A. `3.3.0.1057`.
- Fixed plugin startup failure caused by manually re-loading an already exported WPF `ResourceDictionary` from both dockable constructors (`Cannot re-initialize ResourceDictionary instance`). Dockable icons are now created directly in code.
- Aligned `MinimumApplicationVersion` metadata with the tested 3.3 nightly build.
- User verified that build `0.1.0.1` loads successfully inside N.I.N.A. 3.3 NIGHTLY #057.

### Synthetic Lab development harness

- Added an isolated `QSM Synthetic Lab` dockable for deterministic testing inside the real N.I.N.A. host without camera hardware or real image files.
- Synthetic mode ignores live `ImageSaved` callbacks, uses its own baseline/session store and never invokes real rejected-file actions.
- Added a frame-level PASS/FAIL oracle using the same production `QualityEngine`, `BaselineEngine`, `GuideMetricsCalculator` and `SessionStore` code paths.
- User executed the original canonical in-host suite successfully: `PASS 29/29`.
- Synthetic testing exposed and fixed a real persistence bug: `session.json` could fail on `NaN` values during LEARNING. Unavailable floating-point values are now serialized as JSON `null`.
- Added isolated CI tests for `BAD_` prefix and `Rejected` subfolder actions using temporary fake files.

### Multi-profile synthetic expansion

The development harness is being expanded beyond isolated edge cases to whole-night quality profiles:

- Canonical regression;
- Excellent night;
- Average night;
- Poor night;
- Severe / disaster night;
- Deterioration + recovery.

CI now exercises every profile and fails if any frame does not match its expected state/rejection reasons.

### Release policy

The Synthetic Lab is a **development/pre-release harness**. It must not be exposed as a normal feature in the final public V3 package. Automated headless synthetic regression remains part of CI even when the public release artifact excludes the Synthetic Lab UI.

---

## [0.1.0] - 2026-09-07

### V1 — initial implementation

#### Added

- Initial N.I.N.A. plugin bootstrap, originally developed against `.NET 8` and `NINA.Plugin 3.2.0.9001` before the current 3.3 nightly field target was adopted.
- Dockable Quality Session Meter panel for the Imaging workspace.
- Real-time **Quality Score 0–100** for every LIGHT exposure.
- Independent sub-scores for:
  - guiding quality;
  - guiding stability;
  - transparency / relative star count;
  - background stability.
- Worst-metric-dominant overall score:
  - configurable worst-metric weight;
  - default `0.70` worst / `0.30` average.
- Hard rejection rules independent from the Quality Score.
- Exposure-specific guiding analysis from timestamped `IGuiderMediator.GuideEvent` samples.
- Guide RMS rejection.
- Sustained guide-excursion rejection with configurable duration.
- Hard guide-excursion rejection.
- Adaptive star-count baseline using rolling median of previous clean frames.
- Adaptive background-median baseline using rolling median of previous clean frames.
- Separate baseline buckets for target, filter, exposure, gain, binning and camera.
- Baseline contamination protection: rejected and warning frames do not update the reference.
- Initial `LEARNING` phase.
- Basic probable-cause classification for wind/guiding, clouds/transparency and background/haze events.
- Safe rejected-frame handling:
  - Monitor Only;
  - keep in place;
  - `BAD_` prefix;
  - move to `Rejected` subfolder.
- No automatic frame deletion.
- Persistent session output:
  - `frames.csv`;
  - `session.json`;
  - `quality.svg`.
- Live Quality timeline and per-frame history table.
- Session counters for captured, usable and rejected frames.
- Acceptance-rate and accepted-frame session-quality indicators.
- GitHub Actions Windows build and Release artifact publishing.

#### Hardened before field testing

- Enabled quality metrics with unavailable data now produce an explicit `ERROR` / unassessed frame instead of being silently treated as a perfect or accepted measurement.
- `MinimumLearningFrames` can no longer exceed the rolling baseline window.
- The hard guide-excursion threshold can no longer be configured below the normal excursion threshold.
- Sustained guide duration now requires consecutive confirmed above-threshold samples; a single guide spike has `0 s` sustained duration and cannot trigger the duration rule by itself.
- Session reset is serialized against frame processing and no longer clears guide samples belonging to a potentially active exposure.
- A physical frame receives one stable frame index even if analysis throws; error logging no longer skips an index.
- Frame timestamps now use the exposure-start timestamp where available.
- Session folders use millisecond-resolution, collision-safe names; JSON now keeps a stable `createdUtc` plus `updatedUtc`.
- CSV now persists analysis/file-action error messages.
- Rejected-file move/prefix failures are no longer silent after retries; the failure is recorded while the original frame remains preserved.
- SVG timelines explicitly distinguish analysis-error frames.
- Analysis and file-action error details are surfaced in the dashboard reason text.
- Repository, package and plugin assembly licensing metadata aligned with the existing Apache-2.0 license.

#### Deliberately excluded from rejection logic

- HFR
- FWHM
- eccentricity

These metrics were excluded after real-world observations showed they can remain deceptively similar on visibly wind-damaged point–streak–point stellar profiles.

### Validation

The initial V1 compiled successfully on GitHub Actions Windows against `NINA.Plugin 3.2.0.9001`. Current field-test development has moved to N.I.N.A. 3.3 NIGHTLY #057 as documented above and in `ROADMAP.md`.

### Field validation strategy

Continue to use Monitor Only for first real-sky validation. Synthetic testing reduces software risk but does not replace hardware/sky validation of guide sample timing, star detection behavior, threshold realism, or real N.I.N.A. save/file-action interactions.
