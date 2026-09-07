# Changelog

All notable changes to QualitySessionMeter are documented here.

## [0.1.0] - 2026-09-07

### V1 — initial implementation

#### Added

- N.I.N.A. plugin bootstrap targeting `.NET 8` and `NINA.Plugin 3.2.0.9001`.
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

#### Deliberately excluded from rejection logic

- HFR
- FWHM
- eccentricity

These metrics were excluded after real-world observations showed they can remain deceptively similar on visibly wind-damaged point–streak–point stellar profiles.

### Validation

V1 compiled successfully on a GitHub Actions Windows runner against `NINA.Plugin 3.2.0.9001`.

### Next validation stage

Real-night Monitor Only testing is required to tune practical thresholds across different mounts, focal lengths, filters, guide cadences and sky conditions before an official public plugin-store release.
