# QualitySessionMeter metric reference

This document defines the meaning of the values shown by QualitySessionMeter (QSM). The N.I.N.A. dock, Web Dashboard, reports and remote snapshot should use the same terminology and semantics.

## Decision model

QSM deliberately separates **display scores** from **hard rejection rules**.

- `Quality` is a 0–100 composite presentation score.
- **Evidence strength** (stored as `ConfidenceScore` for compatibility) describes completeness/agreement of the available diagnostics; it is not a calibrated probability.
- `ACCEPTED`, `WARNING`, `REJECTED`, `LEARNING` and `ERROR` are frame states. The interface shows them as **ACCEPTED**, **ACCEPTED (REVIEW)**, **REJECTED**, **PROVISIONAL** and **NOT ASSESSED**; exports keep the state codes.
- A frame is `REJECTED` when at least one enabled rule remains failed after the optional stellar second pass. A high Quality score does not override a failed hard rule.
- A `WARNING` frame is accepted with reservation: it has reduced diagnostic quality, inconclusive/borderline stellar evidence, or a guide/count flag cleared by stellar verification. It counts as valid.
- A `LEARNING` frame is provisional. It passed every rule QSM could evaluate, but star count and background cannot be judged before a reference exists. It is settled when the reference is validated (see below).

This separation is intentional. It prevents a good value in one channel from numerically hiding a severe failure in another.

## Frame states

| State (shown as) | Meaning | Counts as valid? | Forms the reference? |
|---|---|---:|---:|
| `LEARNING` (PROVISIONAL) | Reference for this context not validated yet. | Not until settled | Only after validation |
| `ACCEPTED` (ACCEPTED) | No enabled rule failed. | Yes | Yes |
| `WARNING` (ACCEPTED (REVIEW)) | Accepted with a review finding or a quality score below 65. | Yes | No |
| `REJECTED` (REJECTED) | One or more enabled rules failed. | No | No |
| `ERROR` (NOT ASSESSED) | Required analysis data was missing. | No | No |

### Reference validation

Star count and background are relative measurements, so the first frames of a target and setup cannot be judged when they arrive. They are provisional until **Frames to Validate Reference** (default 4) exist:

1. The star reference is the median of the better half of the provisional frames. Cloud or haze only removes stars, so a minority of degraded frames cannot pull it down. The background reference is the median of the frames that pass the star-count limit.
2. Every provisional frame is re-evaluated against that reference. Frames that fail are rejected and never enter the reference. If too few frames agree, the outliers are rejected at once and the rest stay provisional until enough agree.
3. If the whole start was degraded, the frames agree with each other and are accepted. When a later run of **Frames to Validate Reference** consecutive frames shows so many more stars, without a brighter sky, that the reference itself would fail the star-count rule, the reference is rebuilt from that run. Earlier frames of the context are re-checked and rejected if they fail. This happens only while the context has at most twice the **Reference Window** accepted frames; later, earlier verdicts stand and the rolling reference follows the change.

Retrospective verdicts update the panel, reports, `frames.csv`, the Valid Frame Target count and, outside Monitor Only, the rejected-file action. The decision text ends with "Decided after the session reference was validated."

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

Only validated and `ACCEPTED` frames update the reference. `WARNING`, `REJECTED` and `ERROR` frames do not, preventing a degraded period from becoming QSM's new definition of normal.

## Quality 0–100

Quality combines available channel sub-scores while deliberately weighting the weakest channel more heavily than the arithmetic mean.

In 1.4, `Quality = 0.75 × mean(available channel scores) + 0.25 × min(available channel scores)`. Guide RMS, transparency and background retain their channel mappings. Stability becomes `clamp(100 − 300 × longest sustained excursion / exposure duration, 0, 100)` when excursion diagnostics are enabled. A lone spike no longer zeroes that component. This duration is the longest run, not total disturbed time.

For measured LIGHTs the stellar channel is also included: `clamp(100 − max(180 × max(0, axisRatio − 1.10), 2200 × max(0, tailStrength − 0.003)), 0, 100)`. It is a heuristic diagnostic, not a calibrated image-quality estimate. A secondary peak can confirm rejection even if its composite score is high. The score and the rule result are separate.

In 1.4.1, a cleared guide flag removes guide/stability penalties from the score; a cleared count flag removes its count penalty. Available matched flux contributes `clamp(100 × relative flux, 0, 100)`. FWHM ratio contributes `clamp(100 − 100 × max(0, ratio − 1.1), 0, 100)`. These remain diagnostics; they cannot override remaining hard rules.

The former Worst-channel Influence option is retired. Disabling stellar verification keeps the 1.4 score and strict guide rejection; it does not restore the 1.3 score. Historical V1/V2/V3 fixtures explicitly select their legacy scoring only in development builds.

Rejected frames show **REJECTED**, frames accepted for review **REVIEW**, provisional frames **PROVISIONAL** and unassessed frames **NOT ASSESSED** under the score, so a disposition is never disguised as an aesthetic grade. Other frames use these score labels:

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

- **Captured** — number of QSM frame results in the current session, including provisional and unassessed frames.
- **Accepted** — `ACCEPTED + WARNING`.
- **Rejected** — number of `REJECTED` frames.
- **Acceptance rate** — `Accepted / (Accepted + Rejected) × 100`. Provisional and unassessed frames are excluded until they have a verdict.
- **Mean quality** — mean Quality of accepted frames.
- **Mean evidence** — mean finite evidence strength of accepted frames.

## Failed rules and channels

Each rule has a stable code in exports and a name in the interface:

| Code | Name | Channel (marker) |
|---|---|---|
| `GUIDE_RMS` | Guide RMS above limit | Guiding (G) |
| `SUSTAINED_GUIDE_EXCURSION` | Sustained guide excursion | Guiding (G) |
| `HARD_GUIDE_EXCURSION` | Guide excursion peak | Guiding (G) |
| `STAR_SHAPE_CONFIRMED` | Distorted star shapes | Star shape (S) |
| `STAR_COUNT_DROP` | Fewer stars than reference | Transparency (T) |
| `STELLAR_FLUX_LOSS` | Stellar signal loss | Transparency (T) |
| `SKY_SIGNAL_LOSS` | Signal and star loss | Transparency (T) |
| `LOW_SESSION_SIGNAL` | Signal below session minimum | Transparency (T) |
| `BACKGROUND_HIGH` / `BACKGROUND_LOW` | Brighter / darker sky background | Sky background (B) |

The `ProbableCause` field holds the failed channels in words, for example "Guiding + Transparency". QSM names what it measured; it does not claim wind, cloud or haze. Only when a weather device is connected does the environmental hint relate a failure to measured wind, cloud cover or humidity. Session events group consecutive abnormal frames by the same channels.

## Stellar shape, tails and repeated peaks

Optional stellar analysis measures central raw stars on every monitored LIGHT. Guide, shape and star-count suspects also receive an extended profile and outer-region check. Eccentricity alone can miss a round core with a faint distant image. Matched flux uses only prior clean same-context, same-pier-side references. See [SETTINGS.md](SETTINGS.md#stellar-second-pass) for exact thresholds, rescue safety bounds, computational limits and visual-proof interpretation.

## Measured sky and signal loss (1.4.1.1)

Matched stellar flux is also an enabled hard-rule input. A loss above 35% (default) can reject independently of the star-count rule. A loss of at least 20% together with brighter sky (+3%) and fewer stars (at least half the star-loss limit, minimum 10%) triggers the combined rule. All thresholds and switches are described in [SETTINGS.md](SETTINGS.md#stellar-second-pass).

This is an image-degradation diagnosis; it is not proof of a particular weather condition. The intensity is measured above local background in matched stellar apertures, not inferred from a stretched preview. Moderately degraded usable frames are excluded from reference training. The reference age limit is 120 minutes in this candidate; context and pier-side isolation remain enforced.
