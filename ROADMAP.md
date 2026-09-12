# QualitySessionMeter Roadmap

QualitySessionMeter (QSM) is a N.I.N.A. quality-control plugin with three production layers:

- **V1** — per-frame measurements, independent hard rejection rules and 0–100 diagnostic Quality scoring;
- **V2** — confidence, temporal/session intelligence, event grouping and reporting;
- **V3** — quality-aware Advanced Sequencer tools, including Valid Frame Target and Smart Recovery.

Remote/mobile observability is an additional read-only layer and must never weaken decision, provenance or file-safety behavior.

## Current release state — 2026-09-10

```text
Official catalog candidate: 1.3.1.1
N.I.N.A.:                  3.3.0.1057 / NIGHTLY #057
NINA.Plugin:               3.3.0.1057-nightly
.NET:                      10
Windows:                   x64
```

The 1.3.0.x line was used for pre-store field validation. Real-host validation on N.I.N.A. has now been completed successfully by the maintainer. `1.3.1.1` is the first official N.I.N.A. catalog candidate.

## Public product model

QSM exposes one configuration surface only: **Plugin Options**. The former duplicate QSM Control Center dockable has been removed.

The normal Imaging dock is operational/observational only: current-frame status, session metrics, multichannel timeline, events/history, rejected-frame review, report/session actions and, when a real Suggest Only calibration proposal exists, the explicit Apply/Ignore action.

**Frames to Monitor** intentionally has only two choices:

1. Advanced Sequencer LIGHTs — default;
2. All saved LIGHTs.

The former controlled-block-only monitoring scope was removed from both the public model and source policy.

## Release invariants

### Frame decisions

- Quality 0–100 is diagnostic; it cannot override a hard rejection.
- Exposure guiding uses samples inside the actual exposure interval.
- Sustained excursion and hard excursion operate on total guide-error magnitude.
- Star-count and background rules use mature rolling clean baselines isolated by target/filter/exposure/gain/binning/camera.
- REJECTED/WARNING/ERROR frames never contaminate the clean baseline.
- Missing required analysis fails safe rather than silently accepting a frame.
- HFR/FWHM/eccentricity are not hard rejection inputs in this release.

### Files and Advanced Sequencer

- QSM never deletes rejected images in the normal workflow.
- Rename/move actions require eligible provenance and Monitor Only OFF.
- Undo BAD changes only physical file disposition; the original automatic verdict/history remains intact.
- Valid Frame Target counts correlated usable frames and fails safe when classification is missing.
- Smart Recovery runs only between exposures and never aborts an active exposure.

### UI and WPF safety

- Plugin Options follow the N.I.N.A. host theme and remain left-aligned/readable.
- Shared WPF fallback brushes are immutable/frozen; no bound mutable Freezable crosses AvalonDock dispatcher boundaries.
- Shared keyed Styles are prohibited in dispatcher-sensitive exported QSM ResourceDictionaries.
- The multichannel timeline exposes Quality+Confidence, Guide RMS, Stars Δ+Background Δ, active limits, event markers and adaptive Frame # ticks.
- Online visual regression renders actual QSM WPF templates and the actual embedded Web Dashboard at every UI release candidate.

### Web Dashboard

- Disabled by default and read-only.
- No UPnP, port forwarding, DNS configuration or outbound tunnel creation.
- Physical private LAN IPv4 is preferred for the displayed URL; common VPN/virtual adapters are ignored.
- Dashboard URL is selectable/copyable.
- Password is optional; when enabled it uses salted PBKDF2-HMAC-SHA256 derived storage.
- Browser sessions are revoked on relevant auth/server configuration changes.
- CSP, request limits, login throttling and text-safe rendering are enforced.
- Latest LIGHT preview is generated in memory from the N.I.N.A. ImageSaved payload and shared with the OpenAstro companion bridge.
- Direct public-Internet HTTP exposure is unsupported; use a trusted VPN or HTTPS reverse proxy for remote access.

## Packaging

The official catalog package is built with:

```text
QsmDevelopmentBuild=false
```

Therefore Synthetic Lab and test-only UI are excluded. The interactive Synthetic Lab remains available only in pre-store/development field-test packages; deterministic SyntheticCheck remains permanent CI coverage.

## Official release pipeline

A four-part tag without a leading `v` starts `.github/workflows/nina-release.yml`.

For `1.3.1.1` the workflow must:

1. verify tag/project version equality;
2. verify production source-set isolation and absence of the removed Control Center;
3. run release/security/UI static gates;
4. run the independent-STA WPF dispatcher test;
5. build the immutable production DLL;
6. run full V1/V2/V3 regression;
7. create the production archive and manifest from the final DLL;
8. validate the generated manifest against the current official `isbeorn/nina.plugin.manifests` repository with `npm install` + `node gather.js`;
9. publish ZIP + exact manifest on the immutable GitHub release;
10. optionally submit the manifest automatically when the maintainer fork/PAT are configured.

The stable plugin GUID is `bf861692-b3de-4fdc-8a74-d2b97434f49d` and must never change across updates.

## N.I.N.A. catalog submission

The first official manifest belongs at:

```text
manifests/Q/QualitySessionMeter/3.3.0.1057/1.3.1.1/manifest.json
```

Material AI assistance used during development must be disclosed in the upstream pull request. The human maintainer remains accountable for understanding, testing, security, privacy, licensing, provenance, debugging and maintenance.

After upstream merge, normal discovery and updates are handled by the N.I.N.A. Plugin Manager. QSM does not implement a separate self-updater.

## Next work after 1.3.1.1

Do not expand scope before the first catalog release stabilizes. Post-release work should prioritize bug fixes, compatibility with newer N.I.N.A. builds, localization/accessibility improvements and regression coverage. New decision rules require explicit design and dedicated synthetic + real-sky validation.
