# QualitySessionMeter

**Real-time subframe quality control for N.I.N.A.**

QualitySessionMeter watches LIGHT frames as they are acquired and assigns every exposure a **Quality Score from 0 to 100**, while independently applying user-configurable hard rejection rules for guiding instability, sudden transparency loss, and background changes.

The core idea is simple: a camera exposure being completed does not automatically mean that the exposure is useful.

> **No rejected frame is ever deleted.** QualitySessionMeter can monitor only, keep rejected files in place, prefix them with `BAD_`, or move them into a `Rejected` subfolder.

## Status

- **1.2.0.0 / V3 + review workflow + OpenAstro monitor:** current candidate line. Rejected real frames can be opened directly in N.I.N.A. Image for inspection and QSM-applied `BAD_`/`Rejected` file moves can be physically undone without rewriting the original automatic verdict. The remote snapshot now exposes filenames and richer per-frame diagnostics for the OpenAstro monitor.
- Runtime target: **N.I.N.A. 3.3 NIGHTLY #057** (`3.3.0.1057`) / `NINA.Plugin 3.3.0.1057-nightly`.
- Target framework: **.NET 10** / `net10.0-windows7.0`.
- Default operating mode: **OFF until explicitly enabled**; when enabled, **Monitor Only** remains the safe default for file handling.
- Production package excludes Synthetic Lab code and UI; the field-test/development package retains the complete lab for deterministic host validation.
- CI: Windows build + full V1/V2/V3 synthetic regression + production/field-test packaging gates through GitHub Actions.
- OpenAstro remote monitor bridge: tokenized read-only snapshot, true live guider telemetry and in-memory latest-LIGHT JPEG preview.
- See [ROADMAP.md](ROADMAP.md) for the exact release-hardening state and safety constraints.

## Why this plugin exists

Long astrophotography sequences regularly contain individual subs damaged by conditions that are temporary enough to escape normal sequence logic:

- wind gusts and short mount excursions;
- clouds crossing the field;
- haze or fog;
- sudden transparency loss;
- rapid sky-background brightening or darkening.

HFR, FWHM, and eccentricity are deliberately **not used as V1 rejection criteria**. In real acquisition tests, visibly wind-damaged stars with a point–streak–point profile can still report deceptively similar HFR or eccentricity values. QualitySessionMeter therefore focuses on temporal guiding behaviour and robust session-relative image statistics.

## V1 quality signals

### Exposure guiding RMS

Guide samples falling inside the actual exposure interval are collected and converted to arcseconds using the guider pixel scale. Their combined error is used to calculate an exposure-specific RMS.

Default hard limit:

```text
Maximum exposure RMS: 1.50"
```

### Sustained guide excursion

A single noisy guide sample should not automatically destroy a sub. QualitySessionMeter therefore measures **how long** the total guide error remains above a configured threshold.

Default:

```text
Excursion threshold: 2.00"
Minimum duration:     2.0 s
```

### Hard guide excursion

A sufficiently severe guide excursion can reject a frame even if it is short.

Default:

```text
Hard excursion: 5.00"
```

### Relative star-count loss

The current detected-star count is compared with a rolling robust baseline built from previous good frames in the same acquisition context.

Default:

```text
Maximum star loss: 35%
```

A sudden 40–50% drop is a strong indicator of a cloud or major transparency event.

### Relative background deviation

The image median/background is compared with its own rolling baseline. Both brightening and darkening can be rejected.

Default:

```text
Maximum increase: +30%
Maximum decrease: -30%
```

This helps catch illuminated clouds, haze, fog, or other abrupt sky-background changes.

## Adaptive baseline

QualitySessionMeter does **not** use the first frame as a permanent reference.

The V1 baseline uses a rolling median of previous clean frames:

```text
Window:                  8 frames
Minimum learning frames: 4
Statistic:               median
```

Baselines are isolated by:

- target;
- filter;
- exposure duration;
- gain;
- binning;
- camera.

A 300-second OIII frame therefore never becomes the reference for a 60-second Luminance frame.

### Baseline contamination protection

Only clean `LEARNING` frames and `ACCEPTED` frames update the baseline.

`REJECTED`, `WARNING`, and `ERROR` frames do not.

This prevents a cloud event from gradually becoming the plugin's new definition of normal conditions.

## Quality Meter 0–100

The Quality Meter is a first-class V1 feature, not a later add-on.

Available sub-scores are:

- **Guiding Quality**
- **Guiding Stability**
- **Transparency Quality**
- **Background Quality**

The total is intentionally **not a plain arithmetic mean**. A disastrous value in one dimension must not be hidden by three excellent values.

V1 uses:

```text
Overall Quality =
    WorstMetric × 0.70
  + AverageMetric × 0.30
```

The worst-metric weight is configurable.

Labels:

| Score | Label |
|---:|---|
| 90–100 | EXCELLENT |
| 80–89 | GOOD |
| 65–79 | FAIR |
| 50–64 | POOR |
| 0–49 | BAD |

### Quality score is not the reject switch

Hard rules and the Quality Meter are deliberately separate.

For example, a frame can have a numerical score of 64 but still be rejected because the configured maximum guide excursion was exceeded.

```text
ANY enabled hard rule fails -> REJECTED
```

No second failing metric is required.

## Frame states

V1 uses five states:

- `LEARNING` — baseline still being established;
- `ACCEPTED` — no hard rule failed;
- `WARNING` — no hard rule failed, but overall Quality < 65;
- `REJECTED` — at least one enabled hard rule failed;
- `ERROR` — the frame could not be assessed safely.

In Monitor Only mode a rejected result is displayed as **WOULD REJECT** and no file is touched.

## Probable cause

V1 also provides a basic explanation layer. Examples:

```text
Guide excursion severe
Stars normal
Background normal
=> WIND / GUIDING DISTURBANCE
```

```text
Stars strongly reduced
Background increased
Guide normal
=> CLOUD / BRIGHT SKY EVENT
```

```text
Stars strongly reduced
Guide normal
=> CLOUD / TRANSPARENCY LOSS
```

This classification is diagnostic, not a substitute for the hard-rule result.

## Live N.I.N.A. panel

The dockable Imaging panel contains:

- current Quality Score and label;
- ACCEPTED / WARNING / REJECTED / LEARNING state;
- probable cause and rejection reason;
- exposure RMS;
- maximum guide excursion;
- star count and relative deviation;
- background median and relative deviation;
- captured / usable / rejected counters;
- acceptance rate;
- accepted-frame session quality;
- live Quality timeline;
- per-frame history table;
- session-folder shortcut;
- session reset control.

## Rejected-file handling

Available modes:

```text
Monitor Only
Keep in place
Prefix BAD_
Move to Rejected subfolder
```

QualitySessionMeter does not contain a normal workflow that deletes rejected images.

File operations are collision-safe and happen only after N.I.N.A. has completed saving the image.

## Persistent session output

A session folder is created under:

```text
%LOCALAPPDATA%\NINA\QualitySessionMeter\Sessions\YYYY-MM-DD_HH-mm-ss\
```

It contains:

### `frames.csv`

One row per evaluated exposure, including source metrics, baselines, deviations, sub-scores, total quality, status, reasons and probable cause.

### `session.json`

Structured session state and frame results for later tooling and future V2 analysis.

### `quality.svg`

A persistent session Quality timeline that can be opened in any modern browser or vector viewer.

## Installation for testing

V1 is currently a development build rather than an official N.I.N.A. plugin-store release.

1. Open the latest successful GitHub Actions **build** run.
2. Download the `QualitySessionMeter-v1` artifact.
3. Create:

```text
%LOCALAPPDATA%\NINA\Plugins\3.0.0\QualitySessionMeter\
```

4. Copy the artifact contents into that folder.
5. Restart N.I.N.A.
6. Open the QualitySessionMeter plugin settings and leave **Monitor Only** enabled for the first real-night calibration run.

## Recommended first-night settings

```text
Baseline window:            8 good frames
Minimum learning frames:    4

Guide RMS max:              1.50"
Excursion threshold:        2.00"
Excursion minimum duration: 2.0 s
Hard excursion:             5.00"

Maximum star loss:          35%
Background increase:        +30%
Background decrease:        -30%

Worst metric weight:        0.70
Mode:                       Monitor Only
```

These are starting values, not universal astrophotography constants. Mount scale, focal length, seeing, guider cadence, filters and sky quality all affect sensible thresholds.

## Build from source

Requirements:

- Windows;
- .NET 8 SDK;
- N.I.N.A. 3.2-compatible environment for runtime testing.

Build:

```powershell
dotnet restore QualitySessionMeter.csproj
dotnet build QualitySessionMeter.csproj -c Release
```

The project references:

```text
NINA.Plugin 3.2.0.9001
```

GitHub Actions builds the same project on a Windows runner and publishes the Release output as an artifact.

## What V1 intentionally does not do

V1 does not:

- reject on HFR;
- reject on FWHM;
- reject on eccentricity;
- delete rejected files;
- pause or resume the sequence automatically;
- alter sequence progress based on accepted frames.

The last item is a planned V3 capability. In V3 the acquisition goal becomes **N valid frames**, so if 203 frames have physically been captured and 8 were rejected, sequence progress should read `195 / target`, not `203 / target`.

## Documentation

- [Roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Quality algorithm](docs/QUALITY_ALGORITHM.md)
- [Full project specification](docs/PROJECT_SPEC.md)
- [V1 real-night validation plan](docs/REAL_NIGHT_TEST_PLAN.md)

## License

Apache License 2.0. See [LICENSE](LICENSE).
