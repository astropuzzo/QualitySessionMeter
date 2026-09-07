# OpenAstro Mobile Bridge

QualitySessionMeter can optionally expose its existing read-only mobile snapshot to a trusted LAN/Tailscale companion such as OpenAstro Control.

## Security model

The HTTP bridge is disabled unless `QSM_REMOTE_TOKEN` is present in the N.I.N.A. process environment.

```text
QSM_REMOTE_TOKEN=<long-random-secret>
QSM_REMOTE_PORT=18973        # optional; default 18973
QSM_REMOTE_BIND=*            # optional; default all interfaces
```

V1 is deliberately read-only. It exposes:

```text
GET /healthz
GET /api/v1/snapshot
```

`/api/v1/snapshot` requires either:

```text
X-QSM-Token: <token>
```

or:

```text
Authorization: Bearer <token>
```

No threshold changes, file actions, sequencer actions, or other write operations are accepted by this bridge.

## Snapshot contents

The payload is the same plain-object contract used by the in-process `QualitySessionMeter.ApiV1` broker integration and includes:

- full-session summary counters;
- QSM mode/settings relevant to display;
- current frame;
- latest 160 frames for charting;
- Quality / Confidence / Guide RMS;
- stars/background rolling baselines and deltas;
- acquisition context (target, filter, exposure, gain, binning, camera);
- cause/reason, prediction and environmental hints;
- canonical chart-series colors.

The token itself is never returned by QSM.

## Recommended topology

Do not expose the QSM port directly to the public Internet. The intended topology is:

```text
phone -> existing authenticated OpenAstro HTTPS panel -> ASIAIR proxy -> QSM on trusted LAN/Tailscale
```

OpenAstro then becomes the single remote-facing surface while N.I.N.A./QSM remains private.
