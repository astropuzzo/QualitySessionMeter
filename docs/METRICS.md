# QualitySessionMeter metric reference

This document defines the meaning of the values shown by QualitySessionMeter (QSM). The N.I.N.A. dock, Web Dashboard, reports and remote snapshot should use the same terminology and semantics.

## Decision model

QSM deliberately separates **display scores** from **hard rejection rules**.

- `Quality` is a 0–100 composite presentation score.
- `Confidence` estimates how reliable/complete the current assessment is.
- `ACCEPTED`, `WARNING`, `REJECTED`, `LEARNING` and `ERROR` are frame states.
- A frame is `REJECTED` when at least one enabled hard rule fails. A high Quality score does not override a failed hard rule.
- A `WARNING` frame did not fail a hard rule but has degraded overall quality.

This separation is intentional. It prevents a good value in one channel from numerically hiding a severe failure in another.

## Frame states

| State | Meaning | Counts as usable? | Trains clean baseline? |
|---|---|---:|---:|
| `LEARNING` | Same-context baseline is still being established. | No | Yes |
| `ACCEPTED` | No enabled hard rule failed. | Yes | Yes |
| `WARNING` | No enabled hard rule failed, but overall quality is degraded. | Yes | No |
| `REJECTED` | One or more enabled hard rules failed. | No | No |
| `ERROR` | QSM could not assess the frame safely. | No | No |

File handling is a separate concern. In **Monitor Only** mode a rejected frame is reported but not renamed or moved.

## Exposure Guide RMS

**Unit:** arcseconds (″)  
**Direction:** lower is better.

Guide samples whose timestamps fall inside the actual camera exposure are converted to arcseconds and combined into the exposure-specific total guide error. QSM computes the RMS of that total error over the exposure.

This is the value used by the optional `Maximum RMS` hard rule.

The Web Dashboard also shows a **rolling 20-second live RMS**. That live value is diagnostic and must not be confused with the exposure-specific RMS used to classify a completed subframe.

## Sustained guide excursion

**Unit:** arcseconds (threshold) + seconds (duration).

QSM evaluates the **total vector guide error magnitude**, not one signed RA/DEC axis in isolation. An excursion becomes sustained only when consecutive valid guide samples remain above the configured threshold for at least the configured duration.

A single isolated spike has sustained duration zero and cannot satisfy a duration-based rejection rule by itself.

## Hard guide excursion

**Unit:** arcseconds.

The hard-excursion rule is an emergency ceiling on total guide-error magnitude. A sufficiently large individual excursion can reject a frame even when it is too short to meet the sustained-duration rule.

Because excursion thresholds apply to total magnitude, the Web Dashboard draws them on the **Total error magnitude** guide plot, not over the signed RA/DEC plot.

## Stars Δ

**Unit:** percent relative to rolling baseline.

`Stars Δ` compares the current detected-star count with the mature clean-frame baseline for the same imaging context.

- `0%` = current count equals the rolling baseline.
- negative = fewer stars than baseline.
- positive = more stars than baseline.

When the star-count hard rule is enabled, rejection occurs below the configured negative loss threshold (for example, `-35%`).

QSM does not treat a universal absolute star count as meaningful because target, filter, exposure, gain, binning and camera strongly affect the detected count.

## Background Δ

**Unit:** percent relative to rolling baseline.

`Background Δ` compares the image background statistic with the mature clean-frame baseline for the same imaging context.

- `0%` = current background equals baseline.
- positive = brighter background.
- negative = darker background.

Separate increase and decrease limits are supported. The diagnostic probable-cause layer may associate large positive deviations with haze, illuminated cloud or sky brightening, but the measured deviation and configured hard rule remain the actual rejection evidence.

## Baseline isolation

A clean baseline is separated by:

- target;
- filter;
- exposure duration;
- gain;
- binning;
- camera.

A baseline from one acquisition context must never silently become the reference for another context.

Only `LEARNING` and `ACCEPTED` frames update the clean baseline. `WARNING`, `REJECTED` and `ERROR` frames do not, preventing a degraded period from becoming QSM's new definition of normal.

## Quality 0–100

Quality combines available channel sub-scores while deliberately weighting the weakest channel more heavily than the arithmetic mean.

The configurable **Worst Metric Weight** affects this display score only. It does not change the semantics of the independent hard rules.

Current labels:

| Score | Label |
|---:|---|
| 90–100 | EXCELLENT |
| 80–89 | GOOD |
| 65–79 | FAIR |
| 50–64 | POOR |
| 0–49 | BAD |

## Confidence 0–100

Confidence represents how trustworthy/complete the current assessment is, considering the evidence available to the engine. It is not a replacement for Quality and is not itself a hard rejection switch.

A low-confidence result should be interpreted as "QSM has less reliable evidence for this assessment", not automatically as "bad frame".

## Session summary values

The Web Dashboard and N.I.N.A. dock use these definitions:

- **Captured** — number of QSM frame results in the current session, including `LEARNING` and `ERROR`.
- **Usable** — `ACCEPTED + WARNING`.
- **Rejected** — number of `REJECTED` frames.
- **Acceptance** — `Usable / (Usable + Rejected) × 100`. `LEARNING` and `ERROR` are deliberately excluded from the denominator.
- **Session Q** — mean Quality of usable frames.
- **Session Confidence** — mean finite Confidence of usable frames.

## Probable cause

Probable cause is a diagnostic interpretation of measured evidence, for example:

- guiding/tracking disturbance;
- cloud/transparency loss;
- background/haze/sky-brightness event.

It is explanatory metadata. It does **not** replace the explicit rejection reason(s), threshold values or frame state.

## HFR / FWHM / eccentricity

QSM does not use HFR, FWHM or eccentricity as the primary hard-rejection criteria in the current decision model. They can be useful image diagnostics, but real acquisition tests showed that some visibly wind-damaged point–streak–point stars can retain deceptively similar scalar shape metrics.

The plugin therefore prioritizes exposure guiding behaviour and robust session-relative image statistics for automatic rejection.
