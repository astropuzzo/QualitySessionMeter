# QualitySessionMeter

**Real-time subframe quality control and session diagnostics for N.I.N.A.**

QualitySessionMeter (QSM) evaluates LIGHT frames as they are acquired. It combines exposure-specific guiding analysis with rolling, same-context image-signal baselines, then presents a **Quality score (0–100)** while independently applying explicit hard rejection rules.

> **QSM never deletes rejected images in its normal workflow.** It can monitor only, keep a rejected file in place, prefix it with `BAD_`, or move it into a `Rejected` subfolder when the frame is eligible for file mutation.

## Publication status

QSM is currently in **pre-store validation** and is **not yet an official N.I.N.A. catalog plugin**.

Current compatibility target:

- N.I.N.A. **3.3 NIGHTLY #057** (`3.3.0.1057`);
- `NINA.Plugin 3.3.0.1057-nightly`;
- .NET **10** / `net10.0-windows7.0`;
- Windows.

Two packages are generated during pre-store development:

- **field-test-with-synthetic-lab** — the package to use during current validation; includes Synthetic Lab;
- **production** — generated in parallel to continuously prove that the future catalog package builds without Synthetic Lab/test UI.

The official N.I.N.A. package will use the production source set only. The exact official publishing procedure and release blockers are documented in **[docs/PUBLISHING.md](docs/PUBLISHING.md)**.

## What QSM is designed to catch

Long imaging sequences can contain individual subs damaged by temporary conditions that normal sequence control may not detect reliably, including:

- wind gusts and tracking disturbances;
- prolonged or severe guide excursions;
- cloud/transparency loss;
- haze/fog and abrupt sky-background changes.

QSM deliberately does **not** make the current hard-rejection decision from HFR/FWHM/eccentricity alone. Scalar star-shape metrics can remain deceptively similar on some visibly wind-damaged point–streak–point stars. QSM therefore prioritizes exposure guiding behaviour and robust session-relative image statistics.

## Decision model: score and rejection are separate

A central design rule is:

```text
Quality score != reject switch
```

The 0–100 Quality score summarizes measured channels for a human operator. Hard rules remain independent:

```text
ANY enabled hard rule fails -> REJECTED
```

A numerically reasonable Quality score cannot override a configured hard-limit failure.

Frame states are:

- `LEARNING` — same-context baseline is still being established;
- `ACCEPTED` — no enabled hard rule failed;
- `WARNING` — no hard rule failed, but overall quality is degraded;
- `REJECTED` — at least one enabled hard rule failed;
- `ERROR` — QSM could not assess the frame safely.

See **[docs/METRICS.md](docs/METRICS.md)** for the normative definition of every metric, summary value and baseline rule.

## Measured channels

### Exposure Guide RMS

QSM uses guide samples whose timestamps fall inside the actual camera exposure and computes an exposure-specific RMS in arcseconds.

Default maximum:

```text
1.50"
```

### Sustained guide excursion

QSM measures how long the **total vector guide-error magnitude** remains above the configured threshold. A single isolated spike has sustained duration zero and cannot satisfy the duration rule by itself.

Defaults:

```text
Threshold: 2.00"
Duration:  2.0 s
```

### Hard guide excursion

A sufficiently severe individual total-error excursion can reject a frame even when it is too short for the sustained-duration rule.

Default:

```text
5.00"
```

### Stars Δ vs rolling baseline

Detected stars are compared with the clean rolling baseline for the same target/filter/exposure/gain/binning/camera context.

Default maximum loss:

```text
-35%
```

### Background Δ vs rolling baseline

The image background is compared with its own same-context rolling baseline.

Defaults:

```text
Maximum increase: +30%
Maximum decrease: -30%
```

Only clean `LEARNING` and `ACCEPTED` frames train the baseline. `WARNING`, `REJECTED` and `ERROR` frames do not, preventing degraded conditions from becoming the new definition of normal.

## N.I.N.A. Imaging panel

The dockable QSM panel provides:

- current Quality and Confidence;
- frame state and explicit rejection reason;
- probable-cause diagnostics;
- exposure Guide RMS and guide-pattern diagnostics;
- Stars Δ and Background Δ against rolling baselines;
- captured / usable / rejected / acceptance summaries;
- Session Quality and Session Confidence;
- a **three-band multichannel timeline**:
  - Quality + Confidence;
  - exposure Guide RMS and current RMS limit;
  - Stars Δ + Background Δ, rolling zero baseline and active limits;
- vertical WARNING / REJECTED / ERROR markers with cause codes;
- recent-frame history;
- rejected-frame review/open-in-N.I.N.A. workflow;
- session artifact/report access.

The custom timeline follows the active N.I.N.A. theme for structural colors and reserves/clips its legend lane so labels cannot be drawn over the plot.

## Web Dashboard

QSM can optionally serve a self-contained **read-only Web Dashboard** directly from the Windows computer running N.I.N.A.

Default behaviour:

- Web Dashboard: **OFF**;
- port: `18974`;
- password: **optional and OFF by default**;
- no cloud account;
- no router/UPnP changes;
- no automatic public-Internet exposure.

When enabled, the plugin settings display a copyable URL such as:

```text
http://192.168.1.50:18974/
```

The address resolver prefers an active physical Ethernet/Wi-Fi RFC1918 LAN address with a real IPv4 gateway and rejects common VPN/tunnel/virtual adapters for the displayed convenience URL.

The dashboard includes:

- current-frame diagnostics;
- a **latest real saved LIGHT preview**;
- session counters and metric definitions;
- the same three-band multichannel session timeline used conceptually by the N.I.N.A. panel;
- live guiding with:
  - signed RA/DEC error around zero;
  - a separate total-error-magnitude plot;
  - true sustained/hard excursion thresholds drawn only where they are semantically valid;
  - real sample timestamps and per-sample hover/touch details.

The latest-LIGHT preview is generated from the image payload N.I.N.A. already provides at save time. QSM clones/freezes that image and JPEG-encodes a bounded preview in memory; it does **not** reopen or modify the acquisition FITS/XISF. One shared preview cache feeds both the universal dashboard and the OpenAstro companion bridge, so both surfaces show the same latest real LIGHT.

Synthetic Lab deliberately has no real camera image pixels and therefore cannot fabricate an astronomical preview. During a synthetic-only session the preview remains unavailable unless a real LIGHT has already been captured during the current plugin lifetime; the next real saved LIGHT refreshes it automatically.

The rolling 20-second dashboard RMS is a **live diagnostic window**. The completed-frame Guide RMS remains exposure-specific and is the value used by the exposure-RMS hard rule.

For access outside a trusted LAN, use an existing VPN or HTTPS reverse proxy. Directly forwarding the plain HTTP dashboard port to the public Internet is not a supported deployment model.

See **[SECURITY.md](SECURITY.md)** for the complete network/security model.

## OpenAstro / companion integration

The existing OpenAstro companion bridge remains separate from the universal Web Dashboard.

- it is opt-in;
- it uses a dedicated tokenized read-only API;
- it preserves the existing `/api/v1/...` contract;
- it serves the same shared in-memory latest-LIGHT preview as the universal dashboard;
- enabling the normal Web Dashboard does **not** connect an installation to the maintainer's server or to another user's server.

This separation allows the same QSM build to support both ordinary LAN/VPN users and advanced companion-server integrations without maintaining divergent plugin builds.

## Rejected-file safety and review

Available file-handling modes are:

```text
Monitor Only
Keep in place
Prefix BAD_
Move to Rejected subfolder
```

Safety rules:

- QSM does not delete rejected images;
- file operations occur only after N.I.N.A. has completed the save;
- rename/move operations are collision-safe;
- `Monitor Only` suppresses every physical file action;
- V3 provenance/source policy protects manual or unknown frames from automatic mutation when they are not eligible;
- QSM-applied `BAD_` / `Rejected` actions can be physically undone without rewriting the original automatic verdict in the session history.

## Sequencer integration

QSM includes V3 sequence-control primitives for accepted-frame acquisition, including valid-frame progress tracking and Smart Recovery behaviour.

A critical compatibility rule for a published plugin is that serialized sequencer type names/namespaces must remain stable. QSM therefore treats its public sequencer types as compatibility-sensitive API once officially released.

## Session artifacts

QSM stores session diagnostics under:

```text
%LOCALAPPDATA%\NINA\QualitySessionMeter\Sessions\YYYY-MM-DD_HH-mm-ss\
```

Artifacts include structured frame/session data and human-readable reports such as:

- `frames.csv`;
- `events.csv`;
- `session.json`;
- `quality.svg`;
- `report.html`.

Reports can contain filenames, target/filter/camera metadata and diagnostics. Review them before sharing publicly.

## Pre-store installation for testing

For the current validation cycle, use the latest release asset whose name contains:

```text
field-test-with-synthetic-lab
```

1. Close N.I.N.A.
2. Remove the previous QSM test DLL/package from the active plugin folder to avoid loading an old copy.
3. Extract the field-test package under the active N.I.N.A. plugin directory. For the current plugin API floor this is normally below:

```text
%LOCALAPPDATA%\NINA\Plugins\3.0.0\
```

N.I.N.A. loads plugin DLLs from that version folder and one plugin subdirectory level below it.
4. Start N.I.N.A. and confirm the loaded QSM version in the plugin/log output.
5. Keep **Monitor Only** enabled for real-sky validation until file handling has been intentionally tested.
6. Use **Synthetic Lab** for deterministic regression scenarios before testing destructive-adjacent workflows such as rename/move/restore.
7. For Web Dashboard preview validation, save at least one **real LIGHT**; Synthetic Lab alone cannot create an image preview.

The exact versioned plugin folder is controlled by N.I.N.A.'s plugin API floor and can change in a future N.I.N.A. major/plugin-API revision.

## Recommended starting values

These are conservative starting points, **not universal astrophotography constants**:

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
Worst metric weight:        0.70
File mode:                  Monitor Only
```

Image scale, focal length, seeing, guider cadence, filters, mount behaviour and sky conditions all affect sensible limits.

## Build from source

Requirements:

- Windows build/runtime environment;
- .NET **10 SDK**;
- compatible N.I.N.A. 3.3 host for runtime validation.

Production build:

```powershell
dotnet restore QualitySessionMeter.csproj
dotnet build QualitySessionMeter.csproj -c Release -p:QsmDevelopmentBuild=false
```

Field-test build with Synthetic Lab:

```powershell
dotnet build QualitySessionMeter.csproj -c Release -p:QsmDevelopmentBuild=true
```

The project currently references:

```text
NINA.Plugin 3.3.0.1057-nightly
```

## CI / release gates

Every release candidate is expected to pass:

- production source-set isolation (Synthetic Lab excluded);
- field-test source-set completeness (Synthetic Lab included);
- production build;
- field-test build;
- full deterministic V1/V2/V3 synthetic regression;
- UI/dashboard static release-contract gates;
- pre-store packaging of both source sets.

Additional host validation is still required before an official catalog submission; CI does not substitute for visual/runtime testing inside N.I.N.A.

## Official N.I.N.A. catalog publishing

The official plugin manager is driven by the central `isbeorn/nina.plugin.manifests` repository. QSM now includes a separate tag-driven workflow, `.github/workflows/nina-release.yml`, for the official production release path.

The official flow is:

1. complete every host/runtime/artwork blocker in **[docs/PUBLISHING.md](docs/PUBLISHING.md)**;
2. create an immutable **four-part tag without a leading `v`**, matching the version in `QualitySessionMeter.csproj`;
3. the workflow builds the production source set only and runs the complete regression suite;
4. it generates the archive and `manifest.json` from that final DLL;
5. it validates the generated manifest against the current official manifest repository;
6. it creates the GitHub release with the exact ZIP + manifest/checksum pair;
7. if the maintainer fork and `PAT` secret are configured, it opens the upstream manifest PR automatically; otherwise the validated manifest can be submitted manually.

The DLL is never rebuilt after manifest/checksum generation.

Store metadata is kept in assembly metadata so the official manifest generator can consume it directly rather than relying on hand-edited manifest values. Final `FeaturedImageURL`, `ScreenshotURL` and `AltScreenshotURL` are intentionally added only after current final release screenshots are captured and committed under `docs/store/`; outdated field-test screenshots are not published as store artwork.

Once an approved manifest is merged upstream, future QSM versions are surfaced through N.I.N.A.'s Plugin Manager. QSM does not implement a separate self-updater.

## Development disclosure and maintenance responsibility

QSM has used **material AI-assisted development**. This is disclosed explicitly in preparation for the current N.I.N.A. manifest-repository policy.

The repository owner is the accountable human maintainer. An official manifest should be submitted only after the human maintainer has personally reviewed, understood and tested the release candidate and is prepared to explain, debug and maintain the implementation, including its security/privacy/licensing/provenance implications.

## Security

Read **[SECURITY.md](SECURITY.md)** before exposing the Web Dashboard outside a trusted local network.

## License

Apache License 2.0. See [LICENSE](LICENSE).
