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

### Reference Window

Maximum number of recent accepted frames kept in the star/background reference of each independent imaging context:

```text
target + filter + exposure + gain + binning + camera
```

A larger window is more stable but follows genuine long-term changes more slowly.

### Frames to Validate Reference

Number of frames needed to validate the star/background reference of a new target and setup (default 4).

Until then frames are **PROVISIONAL**. When enough of them agree, they are accepted, outliers are rejected, and only the agreeing frames form the reference. See [METRICS.md](METRICS.md#reference-validation).

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

<a id="stellar-second-pass"></a>
## Stellar Analysis

**Enable Stellar Analysis** measures the central raw field of every monitored LIGHT. Confirmed stellar damage can reject a frame even with normal guiding. Disable this option to retain guide, count and background rules without image verification.

| Setting | Default | Meaning |
| --- | --- | --- |
| Maximum eccentricity | 0.60 | Reject when the shape limit is reached in at least 60% of measured stars. Rescue requires eccentricity below 0.55 (a 0.05 margin). |
| Maximum tail | 2% | Asymmetric median-profile wings relative to the central peak. |
| Maximum secondary peak | 8% | Repeated close secondary peak. |
| Maximum distant image | 0.5% | Repeated asymmetric signal in the extended profile, supported by at least 60% of sampled stars and above the measured noise threshold. |
| Minimum reliable stars (fixed) | 20 | Minimum count; distribution and valid-measurement fraction must also pass. |
| Target stars | 100 | Upper target for the central sample. |
| Verify Star-count Flags | On | Match stellar identities to previous clean references before clearing or confirming a count-loss flag. |
| Maximum Sudden Signal Loss | 50% | Rejects loss beyond this limit relative to the recent expected signal; also confirms an existing count flag when count verification is enabled. |
| Minimum Session Signal | 35% | Minimum relative to the first three eligible matched references in the same context and pier side. Configurable from 5–80%; this is not an absolute SNR limit. |

**Reject Stellar Signal Loss** (default On) enables direct measured loss, combined sudden loss and the session minimum. **Sudden Signal and Star Loss** defaults to 35% below the recent expected level, with star-count loss of at least `max(10%, half the configured star-count loss limit)`. The sky may brighten, darken or stay unchanged. The direct loss limit defaults to 50%. Both rules require at least 20 matched stars against two prior clean references. A robust scatter allowance must still cross the threshold before rejection.

With timestamps, the star/background reference uses the median of the three most recent eligible samples, or a coherent fitted trend. Matched photometry follows a log-linear trend in past clean frames when sufficiently consistent; otherwise it uses their rolling median. Fits require R² at least 0.85, log scatter at most 0.025, and bounded rates. Extrapolation through a rejected gap stops after two usual frame intervals (at least five minutes for photometry). Unexpected loss of at least 20%, together with at least 10% fewer stars or 3% higher background, keeps the reference unchanged even when the frame is only a warning. This prevents a moderate suspicious frame from weakening the clean reference while reserving automatic rejection for larger losses.

References are separated by imaging context and pier side and retained for at most six hours. Count recovery still requires references no older than two hours and successful shape evidence. Photometry can remain available when shape classification is inconclusive. Missing shape evidence cannot clear a guide rejection. Gradual cloud attenuation and altitude-related changes can resemble one another; these rules measure relative image quality, not a calibrated weather probability.

The central sample is bounded to 1024 × 1024 pixels. Four outer samples are bounded to 384 × 384 each. Bayer data uses aligned 2 × 2 cell averages; measurement decisions use linear pixels. A valid core fit supplements the moment estimate where aperture truncation would underestimate elongation.

Guide-reject candidates, confirmed core damage and star-count drops trigger extended verification. It examines the outer samples and a median profile with radius 32–64 sampled pixels (48 without a known image scale). Each analysis stage has a 1.5-second processing budget. Insufficient evidence or a timeout cannot clear an existing rejection.

Moderate guide recovery requires the eccentricity margin and clean core checks, no measured compromised outer region, and a completed extended check when attempted. The original moderate bounds are: RMS at or below its enabled maximum, peak below `max(6 arcsec, 2 × hard limit)`, and longest sustained excursion below `max(configured duration, 10% of exposure)`.

Recovery beyond those bounds additionally requires:

- at least four of five regions verified, none compromised, and every verified region below the eccentricity limit;
- tail and close secondary peak below half their limits, with no confirmed distant image;
- known image scale and the recorded guide peak contained within the searched area;
- RMS at or below three times its enabled limit;
- longest sustained excursion below `max(configured duration, 25% of exposure)` when that rule is enabled.

These are limits on recovery, not new guide-rejection thresholds. A guide flag remains if this proof is missing.

Signal verification requires at least 20 matched stars and two clean prior references no more than 120 minutes old. References are separated by target, filter, exposure, gain, binning, camera, image geometry and pier side. Only baseline-eligible accepted or learning frames enter the bounded reference window. With signal rejection enabled, a measured signal loss of at least 10% combined with a count decrease of 10% or background rise of 2% marks the frame for review. Fewer stars (−10%) together with a brighter sky (+3%) also protects the reference, even if matched flux is stable. Such frames remain usable below reject thresholds but cannot train either baseline. This prevents a slow deterioration from redefining normal conditions. References still expire, remain bounded, and are reset by context/session changes. Future frames are never used. A count flag can be cleared only with clean extended shape evidence and flux loss no greater than `min(20%, configured signal-loss limit − 5 percentage points)`. With signal rejection enabled, count rescue also requires background rise below 3%. Background rejection remains independent.

The verdict is finalized before a file action. QSM does not rename historical files during replay. JSON, CSV and reports retain the measurements, applied limits and cleared flags. Native and browser inspectors show central and extended proof; display contrast enhances faint wings without altering measurements. Missing measurements display as unavailable. FWHM is reported in original pixels and relative to matched prior references; its change contributes to the diagnostic score, not an independent reject rule.

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

## Session reports (1.4.2)

**Save Location** defaults to the existing local NINA/QualitySessionMeter/Sessions directory. **Custom folder** creates QSM/session-date below the selected folder. **Beside the LIGHT folder** uses the parent of a LIGHT or LIGHTS directory in the first saved image's path; without such a directory it uses the image directory. Choose the custom location when a naming layout differs.

The destination is fixed for the active QSM session. Changes apply after Reset Session or the next N.I.N.A. run. Options shows the actual path. If the selected folder is unavailable when the session starts, reports fall back to the default local folder with a visible warning. A later write failure is shown without changing an image verdict. Existing reports are not moved.
