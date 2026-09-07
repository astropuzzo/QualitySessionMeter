# OpenAstro Mobile Bridge

QualitySessionMeter can optionally expose its read-only mobile state to a trusted LAN/Tailscale companion such as OpenAstro Control on a modified ASIAIR.

## Security model

The HTTP bridge is disabled unless `QSM_REMOTE_TOKEN` is present in the N.I.N.A. process environment.

```text
QSM_REMOTE_TOKEN=<long-random-secret>
QSM_REMOTE_PORT=18973        # optional; default 18973
QSM_REMOTE_BIND=*            # optional; default all interfaces
```

The bridge is deliberately read-only. It exposes:

```text
GET /healthz
GET /api/v1/snapshot
GET /api/v1/preview.jpg
```

The two `/api/v1/*` routes require either:

```text
X-QSM-Token: <token>
```

or:

```text
Authorization: Bearer <token>
```

No threshold changes, file actions, sequencer actions, or other write operations are accepted.

## Snapshot contents

The payload is the same plain-object contract used by the in-process `QualitySessionMeter.ApiV1` broker integration and includes:

- full-session summary counters;
- QSM mode/settings relevant to display;
- current frame;
- latest 160 frames for session charting;
- Quality / Confidence / exposure-integrated Guide RMS;
- stars/background rolling baselines and deltas;
- acquisition context (target, filter, exposure, gain, binning, camera);
- cause/reason, prediction and environmental hints;
- canonical chart-series colors;
- `guidingLive`: the latest 20 seconds of real N.I.N.A. `GuideEvent` samples, converted to arcseconds using the guider pixel scale, with RA/DEC/total RMS, max excursion and up to 120 timestamped RA/DEC points.

`guidingLive` is not reconstructed from completed exposures: it comes from the same live guider stream QSM uses internally for exposure analysis.

## Latest LIGHT preview

`/api/v1/preview.jpg` returns the most recently saved N.I.N.A. LIGHT as a display-only JPEG:

- source is the processed `BitmapSource` supplied by N.I.N.A.'s `ImageSaved` event;
- maximum width 1280 px;
- JPEG quality 82;
- encoded in memory;
- no FITS file is re-read;
- no preview file is written to disk;
- failure to generate a preview is best-effort and never affects image save/classification.

This keeps the remote channel light enough for mobile access and independent of OpenAstro's SERVER NVMe / Media USB.

## Recommended topology

Do not expose the QSM port directly to the public Internet. The intended topology is:

```text
phone -> existing authenticated OpenAstro HTTPS panel -> ASIAIR proxy -> QSM on trusted LAN/Tailscale
```

OpenAstro is the single remote-facing surface while N.I.N.A./QSM remains private.
