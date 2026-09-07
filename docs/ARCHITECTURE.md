# QualitySessionMeter — V1 Architecture

This document describes the implemented V1 architecture and the boundaries that future V2/V3 work must preserve.

## Goals

QualitySessionMeter is a real-time quality-control layer for N.I.N.A. LIGHT acquisitions. V1 must evaluate completed exposures without interfering with N.I.N.A.'s normal image-save pipeline and without deleting user data.

The architecture is deliberately split into acquisition observation, metric collection, baseline management, scoring/rejection, persistence, file handling, and UI. This prevents later V2/V3 sequence-control work from coupling directly to low-level image analysis.

## Runtime flow

```text
N.I.N.A. exposure
      |
      v
Guide samples collected continuously
      |
      v
Image save/finalization event
      |
      +--> image statistics (median)
      +--> star detection analysis (DetectedStars)
      +--> metadata / context
      |
      v
QualitySessionRuntime
      |
      +--> GuideCollector: samples in exposure interval
      +--> BaselineEngine: matching context baseline
      +--> QualityEngine: scores + hard rules + cause
      |
      v
FrameQualityResult
      |
      +--> SessionStore (CSV / JSON / SVG)
      +--> RejectedFileService (only when enabled)
      +--> dockable UI / timeline
```

## N.I.N.A. integration

V1 targets:

```text
NINA.Plugin 3.2.0.9001
.NET 8 / net8.0-windows7.0
```

The plugin observes N.I.N.A.'s image-save lifecycle and guider mediator rather than replacing the normal sequencer.

Image evaluation is performed after N.I.N.A. has produced the image statistics and star-detection result required by the plugin. Rejected-file actions only occur after the file has been saved.

## Core components

### `QualitySessionMeterPlugin`

Plugin bootstrap and composition root.

Responsibilities:

- expose plugin identity and metadata;
- receive N.I.N.A. services through MEF;
- create the persistent settings accessor;
- create/start the shared `QualitySessionRuntime`;
- expose settings/options resources;
- ensure monitoring continues even when the dockable panel is not visible.

The runtime must never depend on the dockable UI existing.

### `QualitySessionRuntime`

The central coordinator.

Responsibilities:

- subscribe to image-save and guiding events;
- determine whether an image is a LIGHT frame;
- derive an `AcquisitionContext`;
- determine the real exposure interval;
- ask `GuideCollector` for samples belonging to that interval;
- gather star count and median/background statistics;
- query the `BaselineEngine`;
- invoke the `QualityEngine`;
- persist the result;
- update the baseline only when allowed;
- perform the configured rejected-file action;
- publish frame/session updates to the UI.

The runtime owns orchestration, not scoring mathematics.

### `GuideCollector`

Collects guider samples independently of image finalization.

Each sample stores at least:

- timestamp;
- RA error;
- DEC error;
- total error in arcseconds.

The total guide error is derived using the guider pixel scale. For an exposure, only samples whose timestamps fall inside the exposure window are evaluated.

Outputs include:

- exposure RMS;
- maximum excursion;
- longest continuous/sustained interval above the configured excursion threshold;
- whether a hard excursion occurred.

This time-windowed design is essential: a guide event immediately before or after the exposure must not contaminate the frame result.

### `BaselineEngine`

Maintains rolling robust references for image-level metrics.

Current V1 baseline values:

- detected star count;
- median/background level.

Baseline key (`AcquisitionContext`) isolates incompatible frames by acquisition characteristics such as:

- target;
- filter;
- exposure duration;
- gain;
- binning;
- camera.

The baseline statistic is the median of the current rolling window.

Default window:

```text
8 eligible frames
```

Default minimum learning population:

```text
4 frames
```

Baseline update eligibility is intentionally strict:

```text
LEARNING clean frame -> eligible
ACCEPTED frame       -> eligible
WARNING              -> not eligible
REJECTED             -> not eligible
ERROR                -> not eligible
```

This prevents abnormal conditions from redefining normal conditions.

### `QualityEngine`

Pure decision/scoring layer.

Inputs include:

- current metrics;
- matching baseline snapshot;
- guiding summary;
- user thresholds/settings.

Outputs include:

- individual sub-scores;
- overall Quality Score 0–100;
- quality label;
- hard-rule failures;
- frame status;
- probable cause.

The engine deliberately keeps **hard rejection** separate from **quality scoring**.

A frame is rejected when any enabled hard rule fails, regardless of the aggregate score.

### `RejectedFileService`

Runs only after classification and only if Monitor Only is disabled.

Supported V1 actions:

```text
Keep in place
Prefix BAD_
Move to Rejected subfolder
```

Rules:

- never delete a rejected image;
- never overwrite an existing file;
- preserve the original filename where possible;
- treat file-operation errors as plugin errors, not as permission to delete/replace user data.

### `SessionStore`

Persistent per-session telemetry.

Current outputs:

```text
frames.csv
session.json
quality.svg
```

Default root:

```text
%LOCALAPPDATA%\NINA\QualitySessionMeter\Sessions\
```

`frames.csv` is intended for direct inspection/data-analysis tooling.

`session.json` is the structured source for future V2 event grouping and advanced reporting.

`quality.svg` is a persistent quick-look Quality timeline.

## Models

### `AcquisitionContext`

Defines which frames may share a baseline.

It must remain value-comparable and deterministic. Future context fields may be added if real-world tests show a metric depends strongly on them, but reducing isolation requires care because cross-context contamination is worse than slower learning.

### `GuideSample`

Timestamped guide data used to reconstruct what happened during one exposure.

### `FrameQualityResult`

The durable result produced for each evaluated LIGHT frame.

Conceptually contains:

```text
identity / filename / timestamp
acquisition context
raw image metrics
baseline values
relative deviations
guide summary
sub-scores
overall Quality Score
quality label
status
hard rejection reasons
probable cause
```

### `FrameQualityStatus`

V1 states:

```text
LEARNING
ACCEPTED
WARNING
REJECTED
ERROR
```

`WARNING` is a low aggregate quality result without a hard-rule violation. It is intentionally not baseline-eligible.

## UI architecture

The WPF dockable panel is a view over the shared runtime state.

It displays:

- current Quality Score and label;
- status/reasons/probable cause;
- guiding RMS;
- maximum guide excursion;
- detected stars and relative change;
- median/background and relative change;
- captured/usable/rejected counts;
- acceptance rate;
- accepted-frame session quality;
- live timeline;
- frame history;
- session folder/reset actions.

The UI must not contain the authoritative classification logic.

## Settings

V1 settings are persisted through N.I.N.A.'s plugin options accessor.

Groups:

- General / enable / Monitor Only;
- baseline window and learning count;
- guide RMS;
- sustained excursion threshold + duration;
- hard excursion;
- star-count loss;
- background increase/decrease;
- worst-metric score weight;
- rejected-file action.

## Failure behaviour

QualitySessionMeter follows a fail-safe policy.

If analysis fails:

```text
frame -> ERROR / unassessed
```

The plugin must not:

- delete the frame;
- invent a rejection result;
- corrupt N.I.N.A. sequence state;
- block normal image persistence because the Quality Meter failed.

## V2 extension boundary

V2 should build on persisted time-series data and `FrameQualityResult` objects rather than altering the V1 acquisition pipeline.

Expected additions:

- confidence model;
- event grouping;
- guide-pattern analysis;
- slow-trend compensation;
- ranking;
- HTML report.

## V3 extension boundary

V3 introduces sequence-control behaviour and therefore must remain a separate layer above V1 classification.

Critical invariant:

```text
QualityEngine decides whether a frame is valid.
Sequence controller decides whether valid count has reached the target.
```

For Valid Frame Target mode:

```text
CapturedCount++ for every completed exposure
AcceptedCount++ only for ACCEPTED frames
RejectedCount++ only for REJECTED frames

sequence complete when AcceptedCount >= RequestedValidFrames
```

Do not implement V3 by decrementing or mutating N.I.N.A.'s normal captured counter after the fact. The intended architecture is an explicit quality-aware sequence item/controller with separate physical and valid counters.

## Build validation

GitHub Actions builds the project on Windows using .NET 8.

A change that affects runtime code, XAML, project files, or package references is not considered integrated until the Windows CI build succeeds.
