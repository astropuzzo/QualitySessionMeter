# Security policy and network model

QualitySessionMeter (QSM) is an astrophotography quality-control plugin for N.I.N.A. The normal quality engine does not require a cloud service and does not need Internet access.

## Supported versions

Security fixes are applied to the current release-candidate line until QSM enters the official N.I.N.A. plugin catalog. Once a stable public channel is established, this file will be updated with the supported-version matrix.

## Reporting a vulnerability

Please report suspected security issues privately to the repository maintainer before opening a public issue when disclosure could expose users. Include the affected QSM version, N.I.N.A. version, reproduction steps and the security impact you believe is possible.

Do not include passwords, API tokens, private N.I.N.A. session data or other secrets in a public report.

## Web Dashboard: intended deployment

The optional Web Dashboard is:

- **disabled by default**;
- **read-only**;
- served by the Windows computer running N.I.N.A.;
- intended for a trusted LAN, an existing VPN, or an HTTPS reverse proxy controlled by the user;
- not designed to be exposed directly to the public Internet over its plain HTTP port.

QSM does **not**:

- create a cloud account;
- open or forward router ports;
- configure UPnP/NAT-PMP;
- publish the dashboard address to an external discovery service;
- make outbound connections on behalf of the Web Dashboard;
- expose write/control endpoints through the Web Dashboard.

If remote access is required, use the user's existing VPN or a correctly configured HTTPS reverse proxy. Directly forwarding the dashboard's HTTP port from the public Internet is not a supported or recommended configuration.

## Dashboard authentication

Password protection is optional by design because many users operate N.I.N.A. only inside a private LAN/VPN.

When password protection is enabled:

- QSM stores a salted PBKDF2-derived password hash, not the plaintext password;
- successful login creates a random session token;
- the browser receives the token in an `HttpOnly`, `SameSite=Strict` cookie;
- failed login attempts are rate-limited per remote IP;
- sessions expire and are kept only in memory.

Because the built-in dashboard listener uses HTTP rather than TLS, password-protected access across an untrusted network should still be placed behind a VPN or HTTPS reverse proxy.

## HTTP hardening

The dashboard server applies bounded request sizes, a concurrent-client limit and a per-request timeout. Responses include restrictive browser headers including:

- `Content-Security-Policy`;
- `X-Content-Type-Options: nosniff`;
- `X-Frame-Options: DENY`;
- `Referrer-Policy: no-referrer`;
- `Cross-Origin-Resource-Policy: same-origin`;
- a restrictive `Permissions-Policy`.

Dynamic values such as filenames, target names and diagnostic strings are HTML-escaped before they are inserted into dashboard markup.

## Preview handling

The latest-LIGHT preview is generated in memory from the image data supplied by N.I.N.A. It is a display-only JPEG and is not used by the quality decision engine. Generating or serving the preview must never rename, rewrite or delete the acquisition file.

## Rejected-file safety

QSM has no normal workflow that deletes rejected images.

Possible user-selected actions are:

- monitor/report only;
- keep a rejected file in place;
- prefix an eligible rejected file with `BAD_`;
- move an eligible rejected file into a `Rejected` subfolder.

Mutation is additionally restricted by QSM's acquisition-source/provenance policy. Manual or unknown frames are protected from automatic file mutation unless the source is explicitly considered eligible by the plugin's safety gate.

## OpenAstro / companion API

The legacy/advanced OpenAstro companion bridge is separate from the universal Web Dashboard. It uses its own tokenized read-only API and remains opt-in. The Web Dashboard does not require OpenAstro, and enabling the Web Dashboard does not send data to the maintainer's server or another user's server.

Do not publish an integration token in logs, screenshots, issue reports or source control.

## Privacy

QSM processes acquisition/session information locally. Session reports can contain filenames, target names, camera/filter metadata and quality diagnostics; users should review those files before sharing them publicly.

The plugin should not add telemetry or external data collection without an explicit design review, user-visible disclosure and an opt-in decision.

## Release security checklist

Before an official N.I.N.A. catalog submission, the release candidate should pass at least:

1. production and field-test builds;
2. the complete deterministic V1/V2/V3 synthetic regression suite;
3. clean install and upgrade testing in the supported N.I.N.A. host;
4. Web Dashboard disabled-by-default verification;
5. LAN dashboard test with and without optional password protection;
6. browser markup-injection tests using adversarial filenames/target strings;
7. loss/reconnect tests for PHD2 and Web Dashboard clients;
8. rejected-file collision/restore tests;
9. plugin unload/reload and N.I.N.A. shutdown tests;
10. verification that the production package excludes Synthetic Lab and test-only code.
