# QualitySessionMeter

**Real-time subframe quality control, session diagnostics and quality-aware acquisition for N.I.N.A.**

QualitySessionMeter (QSM) evaluates LIGHT frames as they are acquired. It combines exposure-specific guiding analysis with rolling same-context image-signal baselines, presents a diagnostic **Quality score (0–100)** and independently applies explicit hard rejection rules.

> **QSM never deletes rejected images in its normal workflow.**

## Release status

`1.3.1.1` is the first official N.I.N.A. catalog candidate after the completed 1.3.0.x pre-store field-test cycle.

Compatibility floor:

- N.I.N.A. **3.3.0.1057 / NIGHTLY #057**;
- `NINA.Plugin 3.3.0.1057-nightly`;
- .NET **10** / `net10.0-windows7.0`;
- Windows x64.

The official catalog package uses the **production source set only**. Synthetic Lab and test harnesses are excluded from the public package and remain development/field-test tooling.

## Decision model

QSM deliberately separates the human-readable score from hard frame rejection:

```text
Quality score != reject switch
ANY enabled hard rule fails -> REJECTED
```

Frame states are `LEARNING`, `ACCEPTED`, `WARNING`, `REJECTED` and `ERROR`.

The current hard-rule channels are:

- exposure-window Guide RMS;
- sustained total guide-error excursion;
- hard peak total guide-error excursion;
- relative star-count loss versus the mature same-context clean baseline;
- background increase/decrease versus the mature same-context clean baseline.

QSM does not use HFR/FWHM/eccentricity alone as a hard rejection criterion in this release.

Baselines are isolated by:

```text
target + filter + exposure + gain + binning + camera
```

Only clean baseline-eligible data teaches the reference; degraded/rejected/error frames do not redefine normal conditions. See **[docs/METRICS.md](docs/METRICS.md)** for the normative metric definitions.

## N.I.N.A. integration

QSM has one configuration surface: **Plugin Options**. The duplicate development-era Control Center was removed before the first catalog release.

The normal Imaging dock is operational/observational and provides:

- current Quality, Confidence and frame status;
- explicit rejection reason and probable-cause diagnostics;
- exposure Guide RMS and guide-pattern diagnostics;
- Stars Δ and Background Δ versus rolling baselines;
- captured / usable / rejected / acceptance summaries;
- Session Quality and Session Confidence;
- a three-band multichannel timeline with active limits, event markers and adaptive **Frame #** axis;
- session events and frame history;
- rejected-frame review in N.I.N.A. Image;
- reversible QSM-applied `BAD_` / `Rejected` file actions;
- session report/folder controls;
- an Apply/Ignore action only when **Adaptive Calibration → Suggest Only** has a real proposal ready.

### Frames to Monitor

The public setting intentionally has only two scopes:

1. **Advanced Sequencer LIGHTs** — default;
2. **All saved LIGHTs** — also includes manual/external LIGHT saves visible to N.I.N.A.

The former controlled-block-only monitoring mode has been removed.

## Advanced Sequencer

QSM includes quality-aware sequence primitives such as:

- **QSM Valid Frame Target** — tracks usable correlated frames toward a requested target;
- **QSM Smart Recovery Gate** — optional between-exposure recovery behavior after persistent degradation.

Smart Recovery never aborts an active exposure. Public sequencer type names are treated as compatibility-sensitive once released.

## Rejected-file safety

Available behavior includes Monitor Only, Keep in Place, Prefix `BAD_`, and Move to a sibling `Rejected` folder.

Safety rules:

- QSM never deletes rejected images;
- physical actions occur only after save completion and when provenance is eligible;
- Monitor Only suppresses physical rename/move actions;
- collision protection prevents overwriting an existing target filename;
- undoing a QSM file action changes only physical disposition and does not rewrite the original automatic verdict or sequence accounting.

## Read-only Web Dashboard

QSM can optionally serve a self-contained browser dashboard directly from the Windows PC running N.I.N.A.

Defaults:

```text
Web Dashboard: OFF
Port:          18974
Password:      optional / OFF
```

It requires no cloud account and QSM performs no UPnP, router forwarding, DNS or outbound-tunnel configuration. The settings page displays a selectable/copyable LAN URL and prefers a physical private Ethernet/Wi-Fi IPv4 address over common VPN/virtual adapters.

The dashboard includes:

- current-frame diagnostics;
- latest real saved LIGHT preview;
- session summaries;
- the multichannel timeline with Frame # references;
- live guiding with signed RA/DEC error plus separate total error magnitude;
- rolling RA/DEC/Total RMS and maximum excursion;
- metric definitions and recent frame data.

The latest LIGHT preview is generated in memory from the image payload N.I.N.A. already supplies at save time. QSM does not reopen or modify the acquisition FITS/XISF to produce the preview. The same preview cache is shared with the optional OpenAstro companion API.

The browser dashboard is **read-only**. Optional password storage uses salted PBKDF2-HMAC-SHA256 derived data. Direct public-Internet exposure of the plain HTTP listener is unsupported; use a trusted VPN or HTTPS reverse proxy for remote access. See **[SECURITY.md](SECURITY.md)**.

## OpenAstro companion API

The OpenAstro integration remains separate from the universal dashboard and preserves its tokenized read-only `/api/v1/...` contract. Installing QSM does not connect another user to the maintainer's server or to any other QSM installation.

## Session artifacts

Session diagnostics are stored under:

```text
%LOCALAPPDATA%\NINA\QualitySessionMeter\Sessions\YYYY-MM-DD_HH-mm-ss\
```

Typical artifacts include `frames.csv`, `events.csv`, `session.json`, `quality.svg` and a self-contained `report.html`. Reports can contain filenames and imaging metadata; review them before sharing publicly.

## Recommended starting values

These are conservative starting points, not universal astrophotography constants:

```text
Baseline window:            8 clean frames
Minimum learning frames:    4
Guide RMS maximum:          1.50"
Excursion threshold:        2.00"
Excursion minimum duration: 2.0 s
Hard excursion:             5.00"
Maximum star loss:          35%
Background increase:        30%
Background decrease:        30%
Worst-channel influence:    0.70
File handling:              Monitor Only for first validation
```

Image scale, seeing, guider cadence, mount behavior, filters and sky conditions determine sensible limits for a particular setup. Full setting semantics are in **[docs/SETTINGS.md](docs/SETTINGS.md)**.

## Build from source

Requirements: Windows, .NET 10 SDK and a compatible N.I.N.A. 3.3 host for runtime validation.

Production build:

```powershell
dotnet restore QualitySessionMeter.csproj
dotnet build QualitySessionMeter.csproj -c Release -p:QsmDevelopmentBuild=false
```

Development/field-test build with Synthetic Lab:

```powershell
dotnet build QualitySessionMeter.csproj -c Release -p:QsmDevelopmentBuild=true
```

The production project references `NINA.Plugin 3.3.0.1057-nightly`.

## Release quality gates

The official release pipeline verifies:

- production source-set isolation;
- absence of Synthetic Lab/test harnesses and the removed Control Center;
- Web Dashboard security/preview contracts;
- WPF/AvalonDock dispatcher-safety contracts;
- independent-STA template validation;
- full deterministic V1/V2/V3 SyntheticCheck regression;
- final catalog artwork/metadata;
- manifest generation from the immutable final DLL;
- manifest validation against the current `isbeorn/nina.plugin.manifests` repository.

Online visual regression also renders the actual QSM WPF templates and embedded Web Dashboard on GitHub-hosted runners. Real N.I.N.A. host testing was completed during the 1.3.0.x field-test cycle before the 1.3.1.1 catalog freeze.

## N.I.N.A. catalog and updates

The official catalog is driven by `isbeorn/nina.plugin.manifests`. QSM's `.github/workflows/nina-release.yml` creates the production archive, checksum and manifest from a four-part official tag without a leading `v`, validates it with the upstream manifest pipeline and can submit it upstream after the maintainer fork is available.

After the manifest is accepted, discovery and future compatible updates are handled by the **N.I.N.A. Plugin Manager**. QSM does not implement a separate self-updater.

See **[docs/PUBLISHING.md](docs/PUBLISHING.md)** for the exact publication procedure.

## AI-assisted development disclosure

QualitySessionMeter has used **material AI-assisted development**. The repository owner is the accountable human maintainer and retains responsibility for understanding, testing, security, privacy, licensing, provenance, debugging and maintenance. This is disclosed in the official manifest submission in accordance with the current N.I.N.A. repository policy.

## License

Apache License 2.0. See [LICENSE](LICENSE).
