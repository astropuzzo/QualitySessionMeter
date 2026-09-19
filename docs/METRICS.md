# QualitySessionMeter metric reference

This document defines the meaning of the values shown by QualitySessionMeter (QSM). The N.I.N.A. dock, Web Dashboard, reports and remote snapshot should use the same terminology and semantics.

## Decision model

QSM deliberately separates **display scores** from **hard rejection rules**.

- `Quality` is a 0–100 composite presentation score.
- **Evidence strength** (stored as `ConfidenceScore` for compatibility) describes completeness/agreement of the available diagnostics; it is not a calibrated probability.
- `ACCEPTED`, `WARNING`, `REJECTED`, `LEARNING` and `ERROR` are frame states.
- A frame is `REJECTED` when at least one enabled rule remains failed after the optional stellar second pass. A high Quality score does not override a failed hard rule.
- A `WARNING` frame is kept: it has reduced diagnostic quality, inconclusive/borderline stellar evidence, or a guide/count flag cleared by stellar verification.

This separation is intentional. It prevents a good value in one channel from numerically hiding a severe failure in another.

## Frame states

| State | Meaning | Counts as usable? | Trains clean baseline? |
|---|---|---:|---:|
| `LEARNING` | Same-context baseline is still being established. | No | Yes |
| `ACCEPTED` | No enabled hard rule failed. | Yes | Yes |
| `WARNING` | Kept for review: low score or stellar-confirmed guide false positive. | Yes | No |
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

The hard-excursion rule flags total guide-error magnitude. In 1.4.1 a failure can be cleared by stellar verification only within the evidence and safety bounds described in SETTINGS.md. A sufficiently large individual excursion can reject a frame even when it is too short to meet the sustained-duration rule.

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

In 1.4, `Quality = 0.75 × mean(available channel scores) + 0.25 × min(available channel scores)`. Guide RMS, transparency and background retain their channel mappings. Stability becomes `clamp(100 − 300 × longest sustained excursion / exposure duration, 0, 100)` when excursion diagnostics are enabled. A lone spike no longer zeroes that component. This duration is the longest run, not total disturbed time.

For measured LIGHTs the stellar channel is also included: `clamp(100 − max(180 × max(0, axisRatio − 1.10), 2200 × max(0, tailStrength − 0.003)), 0, 100)`. It is a heuristic diagnostic, not a calibrated image-quality estimate. A secondary peak can confirm rejection even if its composite score is high. The score and the rule result are separate.

In 1.4.1, a cleared guide flag removes guide/stability penalties from the score; a cleared count flag removes its count penalty. Available matched flux contributes `clamp(100 × relative flux, 0, 100)`. FWHM ratio contributes `clamp(100 − 100 × max(0, ratio − 1.1), 0, 100)`. These remain diagnostics; they cannot override remaining hard rules.

The former Worst-channel Influence option is retired. Disabling stellar verification keeps the 1.4 score and strict guide rejection; it does not restore the 1.3 score. Historical V1/V2/V3 fixtures explicitly select their legacy scoring only in development builds.

Rejected frames show **STAR DAMAGE** or **LIMIT EXCEEDED**; warnings show **KEPT FOR REVIEW**. These explain disposition rather than disguising it as an aesthetic grade. Other assessed frames retain these score labels:

| Score | Label |
|---:|---|
| 90–100 | EXCELLENT |
| 80–89 | GOOD |
| 65–79 | FAIR |
| 50–64 | POOR |
| 0–49 | BAD |

## Evidence strength 0–100

The existing completeness, baseline maturity, threshold separation and channel agreement diagnostics are retained. In 1.4 the displayed score is capped at 85 with usable stellar evidence and 65 without it. These caps deliberately avoid false certainty; they are not empirically calibrated probabilities. `85 / 100` does not mean an 85% probability of a bad photograph. A low score is not itself a reject rule.

## Session summary values

The Web Dashboard and N.I.N.A. dock use these definitions:

- **Captured** — number of QSM frame results in the current session, including `LEARNING` and `ERROR`.
- **Usable** — `ACCEPTED + WARNING`.
- **Rejected** — number of `REJECTED` frames.
- **Acceptance** — `Usable / (Usable + Rejected) × 100`. `LEARNING` and `ERROR` are deliberately excluded from the denominator.
- **Usable frame quality** — mean Quality of usable frames.
- **Mean evidence strength** — mean finite Confidence of usable frames.

## Probable cause

Probable cause is a diagnostic interpretation of measured evidence, for example:

- guiding/tracking disturbance;
- cloud/transparency loss;
- background/haze/sky-brightness event.

It is explanatory metadata. It does **not** replace the explicit rejection reason(s), threshold values or frame state.

## Stellar shape, tails and repeated peaks

Optional stellar analysis measures central raw stars on every monitored LIGHT. Guide, shape and star-count suspects also receive an extended profile and outer-region check. Eccentricity alone can miss a round core with a faint distant image. Matched flux uses only prior clean same-context, same-pier-side references. See [SETTINGS.md](SETTINGS.md#stellar-second-pass) for exact thresholds, rescue safety bounds, computational limits and visual-proof interpretation.

## Measured sky and signal loss (1.4.1.1)

Matched stellar flux is also an enabled hard-rule input. A loss above 35% (default) can reject independently of the star-count rule. A loss of at least 20% together with brighter sky (+3%) and fewer stars (at least half the star-loss limit, minimum 10%) triggers the combined rule. All thresholds and switches are described in [SETTINGS.md](SETTINGS.md#stellar-second-pass).

This is an image-degradation diagnosis; it is not proof of a particular weather condition. The intensity is measured above local background in matched stellar apertures, not inferred from a stretched preview. Moderately degraded usable frames are excluded from reference training. The reference age limit is 120 minutes in this candidate; context and pier-side isolation remain enforced.
