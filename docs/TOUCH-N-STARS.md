# Touch 'n' Stars integration

QualitySessionMeter 1.0.0.2 introduces the QSM side of a read-only mobile integration contract for Touch 'n' Stars (TNS).

## Goal

Expose the live QSM dashboard on a phone/tablet as a native responsive Touch 'n' Stars plugin page without screen scraping, without a direct DLL dependency, and without opening an additional HTTP port from QualitySessionMeter.

## Architecture

```text
QualitySessionMeter (N.I.N.A. plugin)
        |
        | IMessageBroker — process-local
        v
Touch 'n' Stars N.I.N.A. plugin/server
        |
        | /api/qsm/snapshot
        v
Touch 'n' Stars Vue/Pinia plugin page
        |
        v
phone / tablet / browser
```

TNS already owns the network/API/mobile layer. QSM only owns its domain data. The boundary is N.I.N.A.'s `IMessageBroker`, so the two plugins do not need to reference each other's assemblies.

## QSM broker contract — v1

Request topic:

```text
QualitySessionMeter.ApiV1.RequestSnapshot
```

Response topic:

```text
QualitySessionMeter.ApiV1.Snapshot
```

The response `CorrelationId` equals the request `MessageId`.

The response content is composed only of plain dictionaries/lists/scalars so a companion plugin can serialize it directly. No QSM CLR model types are required on the TNS side.

### Snapshot payload

Top-level fields:

- `contractVersion` — currently `1`;
- `available` — whether the QSM runtime is initialized;
- `readOnly` — always `true` in contract v1;
- `generatedUtc`;
- `mode`;
- `summary`;
- `settings`;
- `series` — display metadata including the canonical QSM line colors;
- `currentFrame`;
- `frames` — up to the latest 160 assessed frames.

Per-frame payload includes:

- frame index/time/status;
- Quality and Confidence;
- Guide RMS;
- current star count, rolling star baseline and star delta %;
- current background, rolling background baseline and background delta %;
- target/filter;
- interpreted cause and raw reason;
- source/QSM-controlled state;
- predictive/environment hints.

Unavailable floating-point values are emitted as `null`, never JSON NaN/Infinity.

## Canonical mobile series

The snapshot advertises the same visual vocabulary as the desktop dashboard:

| Series | Color | Unit / reference |
|---|---|---|
| Quality | `#8AB4F8` | score 0–100 |
| Confidence | `#C58AF9` | 0–100% |
| Guide RMS | `#81C995` | arcsec, absolute |
| Stars Δ | `#FDD663` | % versus rolling clean-frame baseline |
| Background Δ | `#F28B82` | % versus rolling clean-frame baseline |

A TNS chart should show the same 0% baseline and live threshold lines as the N.I.N.A. dashboard.

## Expected TNS backend adapter

The TNS server should expose one read-only endpoint initially:

```text
GET /api/qsm/snapshot
```

Suggested flow:

1. create an `IMessage` with topic `QualitySessionMeter.ApiV1.RequestSnapshot`;
2. subscribe/wait for `QualitySessionMeter.ApiV1.Snapshot`;
3. match `CorrelationId` against the request `MessageId`;
4. return the response `Content` as JSON;
5. use a short timeout and return an unavailable/offline response if QSM is not installed/running.

Polling every ~1 second while the QSM page is visible is sufficient for v1. A later version may add push updates if TNS wants to bridge broker messages to its existing real-time transport.

## Expected TNS frontend plugin

TNS's source plugin architecture can host QSM as a normal responsive plugin:

```text
src/plugins/quality-session-meter/
  plugin.json
  index.js
  components/
  store/
  views/
```

The plugin-local Pinia store should poll `/api/qsm/snapshot` only while its route is active. The view should prioritize mobile readability:

1. current Quality / Confidence / status;
2. valid/usable/rejected counters;
3. responsive multichannel timeline with touchable frame inspection;
4. current RMS, stars/background values and rolling baselines;
5. active limits and concise cause/event history.

Touch interaction should replace mouse-only hover: tapping/dragging on the timeline selects the nearest frame and opens its exact values/baselines/limits.

## Safety boundary

Contract v1 is intentionally **read-only**.

It does not allow a phone client to:

- toggle QSM OFF/ON;
- change rejection thresholds;
- change Monitor Only/file handling;
- reset a session;
- alter Valid Frame Target progress;
- control Smart Recovery.

Those operations can be designed later as a separately versioned command contract with explicit validation and safety semantics. Mobile visibility does not need write authority.

## Status in 1.0.0.2

- QSM broker responder: implemented;
- snapshot DTO/serialization-safe payload: implemented;
- desktop/mobile shared color/reference metadata: implemented;
- Touch 'n' Stars backend endpoint: requires a TNS-side adapter;
- Touch 'n' Stars Vue/Pinia page: requires a TNS-side plugin contribution.

This separation is intentional: QualitySessionMeter can ship its stable contract independently while TNS integrates it through its own server and mobile UI layers.
