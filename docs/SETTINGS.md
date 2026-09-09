# QualitySessionMeter settings guide

This document explains what each setting does, when it matters, and what it does **not** do.

A core rule applies throughout QSM:

```text
Quality score != hard rejection
```

The displayed 0–100 Quality score is diagnostic. A frame is rejected only when at least one enabled hard rule fails.

## General

### Enable QualitySessionMeter

Master switch for frame assessment and QSM sequencer control.

- **ON:** eligible LIGHT frames are evaluated by QSM.
- **OFF:** QSM does not assess new frames.

The optional Web Dashboard and the separate OpenAstro companion API have their own enable/configuration rules.

### Frames to Monitor

This was previously labelled **Monitoring Scope**.

It decides which saved LIGHT frames QSM is allowed to assess:

- **QSM-controlled blocks only (safest):** only frames produced inside sequence blocks explicitly controlled by QSM.
- **All Advanced Sequencer LIGHTs:** all LIGHT frames produced by N.I.N.A.'s Advanced Sequencer, including QSM-controlled blocks.
- **All saved LIGHTs (widest):** also includes other/manual/external LIGHT saves visible to N.I.N.A.

This controls **analysis scope**. It does not automatically make every monitored frame eligible for rename/move actions. File mutation is separately protected by QSM's frame-source/provenance policy.

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

QSM calculates a bounded recommendation and displays it in **QSM Control Center**.

Nothing changes until the user explicitly applies the suggestion.

This is the recommended mode while validating a setup because the proposed limits remain visible and reviewable.

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

## Quality & Session Intelligence

### Worst-channel influence

Internal setting name: `WorstMetricWeight`.

It controls only the displayed Overall Quality score.

QSM combines available channel scores as:

```text
Overall Quality = worst channel * weight + average channel score * (1 - weight)
```

With the default `0.70`:

```text
70% = worst available channel
30% = average of all available channels
```

A higher value makes one weak channel drag the displayed Quality score down more strongly.

**It does not change any hard reject threshold and cannot override a hard rejection.**

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

## Smart Recovery (Advanced Sequencer)

Smart Recovery has an effect only when the **QSM Smart Recovery Gate** sequence item is placed after **Take Exposure** inside a QSM-controlled loop.

It never aborts an active exposure.

### Enable Smart Recovery Gate

Allows the Smart Recovery Gate to react after persistent QSM-controlled degradation.

### Reject / Error Streak

Number of consecutive QSM-controlled **REJECTED** or **ERROR** frames required before the recovery gate activates.

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

After three consecutive controlled REJECTED/ERROR frames, QSM waits 120 seconds. It then waits for three consecutive healthy guide samples before the next exposure. If recovery cannot be confirmed before the safety timeout, one probe exposure is allowed.

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
