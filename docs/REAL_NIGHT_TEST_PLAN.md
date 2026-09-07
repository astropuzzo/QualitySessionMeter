# QualitySessionMeter — V1 Real-Night Test Plan

A successful Windows build proves that the plugin compiles against the selected N.I.N.A. API. It does **not** prove that thresholds and timing behave correctly on a real acquisition system.

The first night should therefore be run in **Monitor Only**.

## Objectives

Validate that V1:

1. observes every intended LIGHT exposure exactly once;
2. associates guider samples with the correct exposure interval;
3. reads star count and median/background correctly;
4. creates and isolates baselines correctly;
5. detects obvious wind/cloud events;
6. avoids false rejection during normal variation;
7. persists accurate CSV/JSON/SVG output;
8. never moves/renames files while Monitor Only is enabled;
9. remains stable over a long sequence.

## Recommended initial settings

```text
Monitor Only                 ON
Baseline window              8
Minimum learning frames      4

Max exposure RMS             1.50"
Excursion threshold          2.00"
Minimum excursion duration   2.0 s
Hard excursion               5.00"

Maximum star loss            35%
Background increase          +30%
Background decrease          -30%
Worst-metric weight          0.70
```

These values are intentionally conservative starting points.

## Test A — Normal stable sequence

Run at least 10–20 LIGHT exposures in reasonably stable conditions.

Expected:

- first frames show `LEARNING`;
- baseline becomes available after the configured learning count;
- later normal frames become `ACCEPTED`;
- star/background baselines change gradually rather than jumping;
- no rejected frame enters the baseline;
- Quality timeline updates once per exposure.

Record:

```text
normal guide RMS range
normal maximum excursion range
normal star-count variation (%)
normal background variation (%)
normal Quality Score range
```

This defines the system's real noise floor.

## Test B — Dither boundary

Observe several normal dithers between exposures.

Goal: verify that guider activity occurring outside the exposure interval does not contaminate the completed sub.

Expected:

- dither movement outside `[ExposureStart, ExposureEnd]` must not cause a frame rejection;
- guiding samples should reflect only the exposure window.

This is a critical timing test.

## Test C — Natural wind event

If wind occurs naturally, note the approximate timestamp and compare it with the plugin log.

Expected for a meaningful gust:

- increased maximum excursion and/or sustained excursion;
- potentially increased exposure RMS;
- explicit guide-related reject reason if threshold crossed;
- probable cause such as `WIND / GUIDING DISTURBANCE` when image statistics remain near baseline.

Do not intentionally touch or strike the telescope/mount for this test.

## Test D — Cloud/transparency event

When a cloud or thin veil crosses the target, compare visible conditions with:

```text
DetectedStars
StarBaseline
StarDeviationPct
BackgroundMedian
BackgroundBaseline
BackgroundDeviationPct
```

Expected:

- significant cloud should usually reduce detected stars;
- illuminated cloud/haze may also increase background;
- hard star/background threshold failures should be explicit;
- rejected cloud frames must not shift the baseline toward cloudy values.

## Test E — Filter change

Acquire at least two materially different filters, for example L and narrowband.

Expected:

- filter change creates/uses a separate acquisition context;
- a low-star narrowband frame is not compared against the Luminance star-count baseline;
- each context performs its own learning.

## Test F — Exposure/gain/binning change

Change one acquisition parameter during controlled testing.

Expected:

- incompatible acquisition settings do not share the same image baseline;
- new context begins learning independently.

## Test G — Monitor Only safety

With frames that the plugin classifies as `WOULD REJECT`:

Expected:

- original files remain at their original paths;
- filenames remain unchanged;
- CSV/JSON still record the rejection classification and reason.

This must pass before testing active file handling.

## Test H — Active file handling

Only after Monitor Only behaviour is trusted, test on a short disposable/test sequence.

### Prefix mode

Expected:

```text
original rejected file -> BAD_<original filename>
```

No accepted file should be renamed.

### Move mode

Expected:

```text
rejected file -> Rejected\<original filename>
```

Requirements:

- no overwrite if filename already exists;
- accepted files remain untouched;
- sequence acquisition continues normally.

## Test I — N.I.N.A. restart / plugin lifecycle

Restart N.I.N.A. and verify:

- plugin loads normally;
- settings persist;
- a new session output folder is valid;
- runtime works even if the dockable panel is never opened;
- opening/closing the dockable panel does not duplicate subscriptions or frame records.

## Test J — Long run

Run a realistic multi-hour sequence.

Check:

- one log row per evaluated LIGHT exposure;
- no unbounded UI slowdown;
- no duplicate frames;
- quality SVG remains valid;
- session JSON remains readable;
- guide sample buffer remains bounded/managed;
- no interference with N.I.N.A. acquisition.

## Threshold calibration method

After the first stable night, use the persisted CSV rather than intuition alone.

For each metric calculate/inspect the typical accepted distribution.

Suggested principle:

```text
normal variation << rejection threshold
```

Example:

If stable star-count variation is normally only ±4–6%, a 35% threshold is very conservative and may later be tightened.

If guide RMS normally ranges 0.7–1.1", a 1.5" threshold may be reasonable; if normal guiding is already 1.3–1.6", it clearly is not.

Do not calibrate from one single frame.

## Pass criteria for V1 field validation

Before treating the plugin as ready for active rejection handling:

- zero unexpected file modifications in Monitor Only;
- no dither-induced false rejects;
- correct exposure-to-guide timing;
- separate baselines confirmed across filters/settings;
- obvious cloud/wind cases detected in telemetry;
- baseline remains stable through rejected events;
- no duplicate frame processing;
- no N.I.N.A. sequence disruption;
- persisted logs match what happened during acquisition.

## Data to retain after the first test night

Keep:

```text
frames.csv
session.json
quality.svg
N.I.N.A. log covering the session
PHD2 guide log if available
notes/timestamps for visible clouds or wind events
```

Those files are the preferred evidence for tuning V1 thresholds and designing the V2 event detector.
