# QualitySessionMeter settings guide

This document explains what each setting does, when it matters, and what it does **not** do.

A core rule applies throughout QSM:

```text
Quality score != hard rejection
```

The displayed 0–100 Quality score is diagnostic. A frame is rejected when an enabled rule remains failed after the optional stellar second pass. The score itself never clears a rejection.

All configurable QSM settings live in **N.I.N.A. Plugin Options**. The Imaging dock is operational only: it shows session state, metrics, history/review tools and, when Suggest Only has a proposal ready, compact **Apply / Ignore** actions. There is no second QSM settings/control-center panel.

## General

### Enable QualitySessionMeter

Master switch for frame assessment and QSM sequencer helpers.

- **ON:** eligible LIGHT frames are evaluated by QSM.
- **OFF:** QSM does not assess new frames.

The optional Web Dashboard and the separate OpenAstro companion API have their own enable/configuration rules.

### Frames to Monitor

QSM intentionally exposes only two analysis scopes:

- **Advanced Sequencer LIGHTs (default):** every LIGHT produced by N.I.N.A.'s Advanced Sequencer.
- **All saved LIGHTs:** Advanced Sequencer LIGHTs plus manual/external LIGHT saves visible to N.I.N.A.

The former controlled-block-only monitoring mode has been removed. Profiles that stored that legacy value are automatically migrated to **Advanced Sequencer LIGHTs**.

This setting controls which LIGHTs QSM assesses. File rename/move safety remains independently protected by acquisition provenance and the **Monitor Only** setting.

### Monitor Only

Safety switch for file handling.

- **ON:** QSM still evaluates, classifies, counts and reports frames, but never performs its rename/move action.
- **OFF:** the configured **Rejected File Action** may be performed on an eligible rejected frame.

Recommended during first real-sky testing.

### Baseline Window

Maximum number of recent clean samples retained for each independent imaging context:

```text
target + filter + exposure + gain + binning + camera
```

A larger window is more stable but follows genuine long-term changes more slowly.

### Minimum Learning Frames

Number of clean same-context frames required before relative star/background baselines are considered mature.

Until enough samples exist, the context remains **LEARNING**.

## Guiding

### Reject on Exposure RMS

Enables the exposure-RMS hard rejection rule.

QSM uses guide samples whose timestamps fall inside the actual camera exposure. This is different from the Web Dashboard's rolling 20-second live RMS display.

### Max Exposure Guide RMS

Maximum allowed total guide RMS for the completed exposure.

Example:

```text
Configured limit: 1.60 arcsec
Measured exposure RMS: 1.72 arcsec
Result: GUIDE_RMS hard rule fails -> REJECTED
```

Lower values are stricter. There is no universal correct value: image scale, seeing, guider cadence and mount behaviour matter.

### Reject Sustained Excursion

Catches guiding disturbances that remain too far from the lock position for too long.

A single short spike is not enough to satisfy the sustained-duration rule.

### Excursion Threshold

Total guide-error magnitude above which the sustained-excursion timer starts.

Crossing this threshold alone does not reject a frame. The condition must also persist for **Minimum Excursion Duration**.

### Minimum Excursion Duration

Continuous time above **Excursion Threshold** required before the sustained-excursion hard rule fails.

Higher values ignore short disturbances more aggressively.

### Reject Hard Excursion

Emergency peak-error rule.

A sufficiently severe instantaneous guide excursion can reject a frame even when it is too brief to satisfy the sustained-duration rule.

### Hard Excursion

Peak total guide-error magnitude used by the hard-excursion rule.

It is intentionally separate from RMS: RMS describes the exposure-wide spread, while Hard Excursion catches a severe individual departure.

## Image Signal

### Reject Relative Star Loss

Compares detected star count against the mature rolling clean baseline for the same imaging context.

QSM does not use HFR/FWHM/eccentricity alone as hard rejection criteria in this release.

### Maximum Star Loss

Maximum allowed percentage drop in detected stars relative to the mature baseline.

Example:

```text
Limit: 35%
Measured star delta: -40%
Result: STAR_COUNT_DROP hard rule fails
```

### Reject Background Deviation

Compares image-background median with the mature same-context baseline.

Brightening and darkening are evaluated independently.

### Maximum Background Increase

Maximum allowed positive background change.

Large positive changes can accompany illuminated cloud, haze, moonlight or other sky-brightness changes. QSM's hard decision is based on the measured deviation, not on a guessed cause.

### Maximum Background Decrease

Maximum allowed negative background change.

Again, probable-cause text is diagnostic; the hard decision uses the measured numerical deviation.

## Adaptive Calibration

Adaptive Calibration is optional threshold assistance. It learns only from stable, high-quality **ACCEPTED** frames in the same imaging context.

Rejected, warning and error frames do not train it.

### Calibration Mode

#### Off

No automatic calibration assistance. Current thresholds remain manual.

#### Suggest Only — recommended

QSM calculates a bounded recommendation. When a proposal is ready, a compact **Adaptive Calibration Suggestion** row appears in the normal QualitySessionMeter Imaging dock with **Apply** and **Ignore** actions.

Nothing changes until the user explicitly applies the suggestion. The row is hidden when there is no proposal, and it contains no configuration controls.

This is the recommended mode while validating a setup because the proposed limits remain visible and reviewable without duplicating Plugin Options.

#### Automatic

When a stable calibration window is available, QSM applies a bounded recommendation automatically for that imaging context.

Automatic calibration is constrained by the **Auto Safety** caps below. Those caps are limits on what the calibrator may set; they are not rejection thresholds by themselves.

### Stable Calibration Window

Number of stable accepted same-context frames required before a calibration proposal becomes available.

Calibration samples must be high quality and sufficiently confident; unstable/rejected data is excluded.

### Auto Safety caps

These values define the loosest limits Automatic calibration is permitted to generate/apply:

- **Auto Safety: Max RMS**
- **Auto Safety: Max Excursion**
- **Auto Safety: Max Hard Excursion**
- **Auto Safety: Max Star Loss**
- **Auto Safety: Max Background**

They exist to prevent automatic learning from silently creating excessively permissive rejection thresholds.

## Stellar Second Pass

**Verify Guide Rejections with Stars** is ON by default. Only exposures that fail an enabled guide rule are analyzed. OFF keeps the guide rules strict; the 1.4 diagnostic score is unchanged.

QSM copies at most a 1024 × 1024 central field while N.I.N.A. still owns the raw pixels (4 MiB per pending sample). Complete Bayer 2 × 2 cells are averaged for color cameras. After save, candidate-only analysis runs on a worker before any rejected-file action. It does not read the whole FITS file back or use a stretched/JPEG preview.

| Setting | Default | Meaning |
|---|---:|---|
| Stars to Measure | 100 | Target, adjustable 20–200. At least 20 reliable isolated stars spread over three of nine central-field cells are required. |
| Maximum Eccentricity | 0.60 | `sqrt(1 − (b/a)²)` from background-subtracted core moments. 0 is round; 1 is a line. Equivalent default axis ratio `a/b = 1.25`. At least 60% of the sample must reach this limit to confirm common elongation. |
| Maximum Asymmetric Tail | 2% | Maximum opposite-side excess in the median, normalized stellar profile, at radii 5–10 sampled pixels. |
| Maximum Repeated Secondary Peak | 8% | Detached secondary local maximum at radii 3–10 sampled pixels, relative to stellar peak, surviving the median profile. |

These are this algorithm's measurements, not interchangeable with another application's fitted PSF eccentricity. Focus, undersampling, optics, crowding and narrowband signal can affect reliability. The small central field does not certify corner quality or defects elsewhere.

If measured shapes are within all tolerances and the guide failure is within the rescue safety bounds, the guide reasons move to the audit/review history. The frame is **WARNING / kept**, or **LEARNING** while its signal baseline is immature. A separate star-count/background failure still rejects it. Rescued warning frames do not train the clean baseline or adaptive calibration. Baseline-eligible learning frames retain the existing learning behavior.

Unavailable pixels, too few reliable stars, inadequate field coverage, rejection of most candidate objects, or a 1.5-second analysis budget overrun cannot rescue a guide rejection. Shape damage confirms the rejection. With each corresponding guide rule enabled, these safety bounds also retain rejection even if the central cores look round:

- exposure RMS above its configured maximum;
- peak at or above `max(6 arcsec, 2 × hard-excursion limit)`;
- sustained duration at or above `max(configured duration, 10% of exposure length)`.

The verdict is finalized **before** `BAD_` or a move is applied. The second pass does not retrospectively rename historical files. The existing manual restore action remains available for historical rejections.

The dock shows a recent-check list and the latest measured proof directly. Hover a timeline frame or evidence cell for the median profile and six individual stars. The browser supports timeline hover and expandable row evidence; reports preserve the same proof. The display stretch enhances faint wings; all decisions use linear pixels. Flux (background/noise-subtracted core sum) is recorded as a diagnostic only: it is not compared between unmatched stars or used as a universal mass-loss rejection rule.

Session JSON records measurements, applied shape limits, preview PNG, original guide reasons, the final decision, and up to 512 guide samples. CSV adds numeric measurements and decision provenance. The small proof is sufficient for review, not a replacement for the original science image.

## Session & File Handling

### Rejected File Action

Used only when:

1. a frame is eligible for QSM file mutation;
2. it is REJECTED;
3. Monitor Only is OFF.

Choices:

- **Keep in Place:** no filesystem change.
- **Prefix BAD_:** rename with `BAD_` prefix.
- **Move to Rejected Folder:** move to a sibling `Rejected` folder.

QSM does not delete rejected images.

### Predictive Warnings

Enables advisory early-warning diagnostics derived from recent trends.

Predictive warnings do not reject a frame by themselves.

### Use environmental data for hints

Internal setting name: `EnvironmentalCorrelationEnabled`.

If N.I.N.A. has a connected weather/environment source, QSM records available values such as:

- cloud cover;
- humidity;
- wind speed/gust;
- sky quality;
- temperature;
- dew point.

QSM can then add explanatory hints, for example:

- wind/gust data may support a guiding-disturbance interpretation;
- cloud cover may support a transparency-loss interpretation;
- high humidity may support haze/fog interpretation;
- temperature close to dew point is reported as relevant context.

**Environmental data is not an independent hard rejection rule in this release.**

## Retired Smart Recovery

QSM 1.4 removes Smart Recovery controls and the toolbox item. It never inserts recovery waits between exposures. Old saved sequences can deserialize the existing item type; it completes immediately and can be removed from the sequence.

## Enable Smart Recovery Gate

Allows the Smart Recovery Gate to react after a configured streak of sequence-frame degradation.

### Reject / Error Streak

Number of consecutive **REJECTED** or **ERROR** results associated with that sequence loop required before the recovery gate activates.

Accepted/warning frames break the streak.

### Recovery Wait

Cooldown inserted between exposures after persistent degradation.

### Healthy Guide Samples to Resume

After the cooldown, number of consecutive healthy guide samples required before the gate reports recovery and permits the next exposure.

If QSM cannot obtain a reliable guide-recovery signal before the safety timeout, the gate deliberately allows one probe exposure rather than blocking the sequence forever.

Example:

```text
Reject/Error streak: 3
Recovery wait: 120 s
Healthy samples: 3
```

After three consecutive REJECTED/ERROR results associated with the loop, QSM waits 120 seconds. It then waits for three consecutive healthy guide samples before the next exposure. If recovery cannot be confirmed before the safety timeout, one probe exposure is allowed.

## Web Dashboard

### Enable Web Dashboard

Starts QSM's local, read-only HTTP dashboard on the Windows computer running N.I.N.A.

QSM does not configure UPnP, port forwarding, DNS or an outbound tunnel.

### Port

TCP port used by the local Web Dashboard. Default: `18974`.

### Dashboard Address

Convenience LAN address detected for the N.I.N.A. computer.

QSM prefers active physical Ethernet/Wi-Fi RFC1918 addresses and excludes common VPN/virtual adapters when selecting the displayed address.

### Require Password

Optional browser authentication.

- **OFF:** any device that can reach the dashboard listener can open it directly.
- **ON:** the configured dashboard password is required.

QSM stores a salted PBKDF2-HMAC-SHA256 derived password hash, not the clear-text password.

The dashboard itself uses plain HTTP. Password authentication therefore does not turn direct public-Internet exposure into a supported deployment. Use a trusted LAN/VPN or an HTTPS reverse proxy for remote access.

## OpenAstro companion API

The OpenAstro integration is separate from the normal Web Dashboard. It keeps its dedicated tokenized, read-only `/api/v1/...` contract so existing OpenAstro installations remain compatible.
