# Changelog

All notable QualitySessionMeter changes are summarized here. Detailed pre-store development history is also available from the repository's GitHub Releases and merged pull requests.

## [1.3.1.0] - 2026-09-10

### First N.I.N.A. catalog candidate

- Froze the first production catalog candidate after the completed 1.3.0.x real-host field-test cycle.
- Confirmed **Plugin Options** as the only QSM configuration surface; the duplicate development-era **QSM Control Center** dockable was removed.
- Kept the normal Imaging dock focused on live telemetry, multichannel timeline, session events/history, rejected-frame review and session actions.
- Preserved Adaptive Calibration **Suggest Only** through a compact Apply/Ignore row that appears in the normal QSM dock only when a real proposal exists.
- Finalized **Frames to Monitor** with exactly two public scopes: **Advanced Sequencer LIGHTs** and **All saved LIGHTs**. The former controlled-block-only scope is removed.
- Finalized dispatcher-safe WPF theming after real N.I.N.A./AvalonDock cross-thread failures discovered during field testing.
- Finalized the canonical three-band timeline with Quality/Confidence, exposure Guide RMS, Stars Δ/Background Δ, active limits, events and adaptive **Frame #** axis.
- Finalized the read-only local Web Dashboard, including physical-LAN address selection, optional password protection, live RA/DEC/total guiding diagnostics, generation-aware latest-LIGHT preview and mobile/desktop layouts.
- Preserved the separate tokenized read-only OpenAstro companion API and shared latest-LIGHT preview cache.
- Added current N.I.N.A. catalog artwork and assembly metadata for featured image and screenshots.
- Hardened the official tag-driven release pipeline to build only the production source set, exclude Synthetic Lab/test harnesses, run V1/V2/V3 regression, validate WPF dispatcher boundaries, generate the manifest from the immutable DLL and validate it against `isbeorn/nina.plugin.manifests`.
- Material AI assistance is disclosed for the catalog submission; `astropuzzo` remains the accountable human maintainer.

## [1.3.0.9] - 2026-09-10

### Single configuration surface

- Removed the separate exported QSM Control Center dockable and its ResourceDictionary.
- Moved the only operational action worth retaining from that panel—Adaptive Calibration Apply/Ignore—into the main QSM monitoring dock and show it only while a proposal exists.
- Removed dead duplicate Options presentation resources.
- Kept configuration exclusively under the plugin's Options page.

## [1.3.0.8] - 2026-09-10

### Monitoring-scope and Options simplification

- Removed the former **QSM-controlled blocks only** monitoring mode from the model, policy and UI.
- Reduced **Frames to Monitor** to Advanced Sequencer LIGHTs or All saved LIGHTs.
- Added automatic migration of the legacy pre-store numeric scope to Advanced Sequencer LIGHTs.
- Reduced Options visual density, increased spacing and moved detailed explanations to contextual help/tooltips.

## [1.3.0.5–1.3.0.7] - 2026-09-09/10

### N.I.N.A. UI and dispatcher hardening

- Fixed real-host `XamlParseException`/cross-dispatcher failures caused first by mutable bound `SolidColorBrush` resources and then by shared keyed WPF Styles.
- Frozen shared fallback brushes and moved live host-theme resolution onto the materialized element's owning dispatcher.
- Added independent-STA dispatcher regression testing.
- Corrected Options alignment, dark/light foregrounds and native N.I.N.A. theme behavior.
- Added online WPF/browser visual-regression rendering.
- Added the adaptive Frame # axis and layout guards preventing event/legend/plot overlap.

## [1.3.0.4] - 2026-09-08

### Shared latest-LIGHT preview and catalog pipeline

- Added one in-memory latest-LIGHT JPEG cache shared by the universal Web Dashboard and OpenAstro companion bridge.
- Ensured preview generation never reopens or modifies the acquisition FITS/XISF and never participates in frame classification.
- Added generation ordering/ETag metadata so an older encode cannot replace a newer preview.
- Added the separate official N.I.N.A. release workflow and publishing documentation.

## [1.3.0.0–1.3.0.3] - 2026-09-08

### Universal Web Dashboard and release hardening

- Added the optional local read-only browser dashboard, disabled by default.
- Added physical LAN address preference, one-click URL copy and optional password authentication.
- Added browser/server security controls, safe HTML rendering and session invalidation on relevant configuration changes.
- Replaced simplified charts with the same multichannel measurement model used by QSM in N.I.N.A.
- Reworked live guiding into signed RA/DEC plus separate total-error diagnostics.
- Added authoritative metric/security documentation and release-quality CI gates.

## [1.2.0.0] - 2026-09-08

- Added rejected-frame visual review in N.I.N.A., collision-safe reversible `BAD_`/Rejected-folder actions and richer companion telemetry.

## [1.1.x] - 2026-09-08

- Added the tokenized read-only OpenAstro companion bridge.
- Completed V3 acquisition provenance, Valid Frame Target integration, Smart Recovery behavior and production/field-test Synthetic Lab isolation.

## [1.0.x] - 2026-09-07

- Established the V1/V2 frame-quality engine, hard rejection rules, confidence/session intelligence, multichannel diagnostics, reporting and deterministic synthetic regression foundation.
