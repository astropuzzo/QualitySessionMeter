# QualitySessionMeter Web Dashboard

QualitySessionMeter includes an optional self-contained, read-only browser dashboard for users who want to monitor a N.I.N.A. session from a phone, tablet, laptop, remote desktop host, or another machine on the same reachable network.

The dashboard is independent from the existing OpenAstro integration bridge.

## Design goals

- no separate server is required;
- no OpenAstro installation is required;
- no cloud account is required;
- the dashboard is OFF by default;
- enabling it starts a local HTTP server on the N.I.N.A. PC;
- default dashboard port: `18974`;
- the port is configurable from the QSM plugin settings;
- password protection is optional and OFF by default;
- QSM never opens router ports, configures UPnP, or exposes the dashboard to the Internet automatically;
- all dashboard operations are read-only.

## Normal local use

Enable **Web Dashboard** in:

```text
N.I.N.A. -> Plugins -> QualitySessionMeter -> Web Dashboard
```

QSM shows the address to open from another device, for example:

```text
http://192.168.1.50:18974/
```

The remote device must already be able to reach the N.I.N.A. computer. Typical cases are:

- the same home/observatory LAN;
- an existing WireGuard/Tailscale/ZeroTier-style VPN;
- an existing reverse proxy or remote server architecture managed by the user.

QSM does not attempt to solve external network reachability itself.

## Optional password

`Require password` is optional.

When it is OFF, opening the dashboard URL immediately shows the current QSM state.

When it is ON, a password can be set or replaced in the plugin settings. QSM stores only:

- a random salt;
- a PBKDF2-SHA256 derived password hash.

The clear-text password is not persisted in the plugin settings.

After a successful login, the dashboard receives a random in-memory session cookie. Sessions expire and disappear when QSM/N.I.N.A. is restarted.

Because the built-in dashboard uses plain HTTP, password mode is intended for trusted LAN/VPN use. If a user intentionally publishes the dashboard through the public Internet, TLS/HTTPS should terminate at their existing reverse proxy or VPN layer.

## Dashboard contents

The browser view uses the same read-only QSM snapshot model as the companion integrations and currently shows:

- captured / usable / rejected counters;
- acceptance rate;
- session Quality and Confidence;
- current frame status and Quality;
- target/filter;
- exposure Guide RMS;
- star-count deviation;
- background deviation;
- probable cause / rejection reason;
- current filename;
- latest saved LIGHT preview;
- live guiding summary and chart;
- Quality timeline;
- recent-frame table.

The dashboard polls only local QSM endpoints:

```text
GET  /
POST /dashboard/login
POST /dashboard/logout
GET  /dashboard/healthz
GET  /dashboard/api/snapshot
GET  /dashboard/api/preview.jpg
```

No threshold changes, file actions, sequencer actions, or other control endpoints exist.

## OpenAstro compatibility

The existing OpenAstro bridge remains separate and unchanged.

It still uses the legacy/advanced environment configuration:

```text
QSM_REMOTE_TOKEN=<long-random-secret>
QSM_REMOTE_PORT=18973
QSM_REMOTE_BIND=*
```

and exposes:

```text
GET /healthz
GET /api/v1/snapshot
GET /api/v1/preview.jpg
```

The `/api/v1/*` endpoints continue to require the existing `X-QSM-Token` or Bearer token.

Therefore an installation can use any of these configurations:

```text
QSM only
QSM + local Web Dashboard
QSM + OpenAstro bridge
QSM + local Web Dashboard + OpenAstro bridge
```

No separate QSM build is needed for OpenAstro.

## Port separation

The two servers use different defaults intentionally:

```text
Web Dashboard: 18974
OpenAstro API:  18973
```

This preserves existing OpenAstro deployments and prevents a newly enabled browser dashboard from colliding with the established companion API listener.

## Firewall note

QSM binds the enabled dashboard on the N.I.N.A. computer so reachable LAN/VPN devices can connect. Windows Firewall or third-party firewall rules can still block inbound connections. QSM does not silently create or elevate firewall rules.
