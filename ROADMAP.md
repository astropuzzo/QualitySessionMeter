# QualitySessionMeter Roadmap

QualitySessionMeter is a N.I.N.A. quality-control plugin built around three stable layers:

- **V1** — per-frame measurement, hard rejection rules and Quality scoring;
- **V2** — confidence, temporal/session intelligence and reporting;
- **V3** — quality-aware Advanced Sequencer acquisition.

Remote/mobile observability is layered on top and must never weaken the core decision, provenance or file-safety model.

This document is the current operational handoff/source of truth. Detailed historical changes belong in `CHANGELOG.md`.

---

# CURRENT HANDOFF STATE — 2026-09-08

## Runtime target

```text
N.I.N.A. 3.3 NIGHTLY #057
Application: 3.3.0.1057
NINA.Plugin: 3.3.0.1057-nightly
.NET: 10
Windows: x64
```

## Current release line

```text
latest published pre-store test: 1.3.0.4
current hardening candidate:      1.3.0.5
working PR:                       #13
```

`1.3.0.5` is a **pre-store field-test hotfix candidate**, not yet an official N.I.N.A. manifest submission.

Field-test packaging policy remains:

```text
field-test / current validation
  QsmDevelopmentBuild=true
  Synthetic Lab INCLUDED

production / future N.I.N.A. store
  QsmDevelopmentBuild=false
  Synthetic Lab EXCLUDED
```

Both variants must build from the same source commit and pass the same production source-set isolation gate.

---

# 1.3.0.5 RELEASE-HARDENING SCOPE

## WPF / AvalonDock stability

A real N.I.N.A. 3.3.0.1057 host log exposed a store-blocking crash during Imaging-layout materialization:

```text
XamlParseException
StaticResourceHolder
The calling thread cannot access this object because a different thread owns it
Cannot access Freezable 'System.Windows.Media.SolidColorBrush' across threads because it cannot be frozen
```

Root cause: QSM 1.3.0.4 data-bound shared `SolidColorBrush.Color` resources to host theme brushes. A bound WPF `Freezable` cannot be frozen, so AvalonDock can later fail when materializing a shared template from another dispatcher.

Required 1.3.0.5 behavior:

- no `BindingOperations.SetBinding` against shared QSM `SolidColorBrush` resources;
- fallback shared brushes are immutable/frozen;
- N.I.N.A. theme resources are resolved on the actual loaded UI element using `SetResourceReference(...)` on the owning UI dispatcher;
- official and pre-store CI fail if the unsafe brush-binding pattern returns;
- real-host dock/layout load and reload must be tested before store submission.

## Native N.I.N.A. UI consistency

- Plugin Options use a flat N.I.N.A.-style layout instead of custom card/GroupBox presentation.
- Text, TextBox content, PasswordBox content, ComboBox content and ComboBox items are explicitly left-aligned where appropriate; host styles must not silently recenter QSM values.
- Structural foreground/background/border resources follow the active N.I.N.A. theme without sharing mutable brushes across dispatchers.
- Dark-mode readability is a release gate, not a cosmetic follow-up.
- Control Center cards use N.I.N.A. background/border/foreground resources rather than hard-coded dark chrome combined with inherited host text colors.
- Every non-obvious user-editable option provides contextual help describing measurement, units/range, strictness direction, behavior and relevant safety constraints.
- Adaptive Calibration settings remain exposed, including bounded automatic safety caps.

## Canonical multichannel timeline

The N.I.N.A. timeline and browser timeline must express the same measurement model:

1. **Quality + Confidence** — 0–100;
2. **Guide RMS** — arcsec, lower is better, with active RMS limit;
3. **Stars Δ + Background Δ** — percentages relative to the rolling same-context clean-frame baseline.

Required presentation semantics:

- visible units;
- visible `0% rolling baseline`;
- active hard limits shown where applicable;
- rejected/warning/error event markers;
- `G/S/B/!` event semantics;
- exact-value hover/tooltips;
- clipped legend lane independent from the plot;
- graph labels/reference labels must not overlap data on narrow dock layouts;
- **adaptive Frame # axis** using the same chronological QSM `FrameIndex` used by Recent frames and tooltips;
- first and latest frame remain identifiable while tick density adapts to available width.

A decorative single-series substitute or an unindexed plot is not acceptable.

## Latest LIGHT preview

The universal Web Dashboard and OpenAstro bridge share the same in-memory latest-LIGHT preview cache.

Required behavior:

- only real saved `LIGHT` images update the preview;
- preview encoding is display-only and never participates in quality decisions;
- no preview image file is written to disk;
- the browser uses snapshot preview generation/version metadata and does **not** blindly replace the `<img>` on a timer;
- the browser downloads a JPEG only when the preview generation changes;
- a temporary 404/reconnect/update failure keeps the last valid LIGHT visible rather than replacing it with a broken image;
- Synthetic Lab cannot fabricate astronomical pixels and therefore cannot create a new preview by itself.

## Live guiding dashboard

The Web Dashboard live guiding graph must not imply that the rolling live signal is the same quantity used for exposure rejection.

Required model:

- signed **RA** and **DEC** errors centered on zero;
- **Total error magnitude** shown separately;
- real sample timestamps / rolling lookback window;
- RA RMS, DEC RMS, total rolling RMS and maximum excursion;
- sustained/hard excursion limits shown only against total magnitude;
- per-sample hover details;
- explicit distinction between rolling 20-second diagnostics and exposure-window guide metrics used for frame classification.

## Web Dashboard security / network model

The browser dashboard remains an optional read-only service independent from the OpenAstro integration bridge.

Required behavior:

- disabled by default;
- no UPnP, router forwarding, DNS or outbound tunnel configuration;
- preferred displayed URL uses the physical private LAN IPv4 and ignores common VPN/virtual adapters;
- URL is selectable and has one-click clipboard copy;
- password is optional, not mandatory;
- password storage uses salted PBKDF2-HMAC-SHA256 rather than clear text;
- password/auth/port/profile reconfiguration invalidates existing browser sessions;
- bounded request/header/body sizes and concurrent clients;
- request timeout and login rate limiting;
- HttpOnly + SameSite session cookie;
- CSP, nosniff, frame-deny, referrer, permissions and same-origin headers;
- session-derived text inserted into HTML is escaped or assigned through text-safe DOM APIs;
- no remote threshold/file/sequencer control routes;
- direct public-Internet HTTP exposure is unsupported; remote access is expected through a trusted VPN or HTTPS reverse proxy.

The existing tokenized OpenAstro bridge remains separate and backward-compatible.

---

# RELEASE GATES FOR 1.3.0.5

## Automated gates

All must pass from the exact candidate commit:

```text
production source-set isolation
production build
production package
field-test build with Synthetic Lab
field-test package
full V1/V2/V3 SyntheticCheck
UI/release-quality static gates
WPF Freezable/dispatcher safety gate
frame-axis presence gate
preview-generation behavior gate
```

Static release-quality gates must catch regressions such as:

- old simplified `qualityCanvas` browser chart returning;
- missing Frame # axis in either N.I.N.A. or browser timeline;
- custom GroupBox/card Plugin Options returning;
- centered text/value content returning in QSM Options;
- shared `SolidColorBrush` binding returning;
- dashboard address losing explicit OneWay read-only binding;
- missing LAN resolver;
- missing timeline clipping;
- blind periodic preview reload returning;
- missing HTML escaping;
- missing browser security headers.

## Real-host gates

Do **not** declare store readiness until these are observed in actual N.I.N.A.:

1. plugin loads cleanly on N.I.N.A. 3.3 NIGHTLY #057;
2. Imaging/AvalonDock layout initializes without `XamlParseException`/Freezable cross-thread errors;
3. reopening/resizing/reloading QSM dockables does not reproduce the crash;
4. Plugin Options render correctly in the active dark theme;
5. all option labels/values are left-aligned where intended and readable;
6. Control Center text/controls have readable contrast;
7. multichannel timeline labels, markers and reference text do not overlap;
8. Frame # axis is readable and correctly correlates plotted points with Recent frames rows;
9. Synthetic Lab is present in the field-test package and drives the real dockable UI;
10. browser dashboard opens from another LAN device;
11. displayed dashboard URL selects the intended physical LAN address with VPN enabled;
12. Copy button places the full URL on the Windows clipboard;
13. password OFF allows direct access;
14. password ON accepts correct credentials, rejects invalid credentials and revokes sessions after password/config changes;
15. live guiding graph receives real N.I.N.A./guider samples and uses correct RA/DEC/total semantics;
16. latest real LIGHT preview updates on the browser and OpenAstro from the shared cache;
17. reconnect/temporary preview failure keeps the previous valid preview visible;
18. OpenAstro integration still works unchanged;
19. shutdown/profile switch/plugin teardown produces no listener/client exceptions;
20. rejected-frame review and undo remain collision-safe with real FITS/XISF files.

---

# STORE SUBMISSION GATES

Official N.I.N.A. publication remains blocked until the real-host gates above are complete.

When ready:

1. freeze a production commit/tag;
2. build the **production package only** for store distribution;
3. generate the manifest from that immutable final DLL/archive;
4. include checksum generated from the final archive;
5. validate the manifest against the official N.I.N.A. schema/repository tooling;
6. add real featured/screenshot image URLs to assembly/manifest metadata;
7. disclose material AI-assisted development in the submission;
8. submit the manifest PR to `isbeorn/nina.plugin.manifests`;
9. respond to upstream review without changing the already-checksummed artifact; if code changes are required, create a new version/artifact/manifest.

N.I.N.A. Plugin Manager then handles discovery and compatible update availability from the central manifest repository. QSM must not implement a separate self-updater for the normal public distribution path.

---

# AUTHORITATIVE CORE BEHAVIOR

These constraints remain closed unless deliberately redesigned and regression-tested.

## V1 — per-frame core

- Overall Quality 0–100 is diagnostic and independent from hard ACCEPT/REJECT.
- Exposure-specific guide RMS uses samples inside the exposure interval.
- Sustained excursion requires confirmed consecutive above-threshold samples; an isolated spike does not satisfy a duration rule.
- Hard excursion may reject on a sufficiently large instantaneous total guide error.
- Star-count and background rules use mature same-context rolling clean-frame baselines.
- Baselines are isolated by target/filter/exposure/gain/binning/camera.
- LEARNING and ACCEPTED can train the clean baseline; REJECTED/WARNING/ERROR do not contaminate it.
- Missing required analysis fails safe to ERROR/UNASSESSED rather than silently ACCEPTED.
- HFR/FWHM/eccentricity remain excluded from hard rejection logic in this release line.

## V2 — session intelligence

- Confidence is separate from Quality and from hard ACCEPT/REJECT.
- Session Event Grouping retains raw per-frame evidence.
- Guide-pattern and slow-trend analysis are diagnostic layers.
- Predictive degradation warnings are advisory and do not reject by themselves.
- Environment correlation is explanatory context and not an independent hard rule.
- Session artifacts remain `frames.csv`, `events.csv`, `session.json`, `quality.svg` and self-contained `report.html`.

## V3 — sequencer / file safety

- acquisition provenance is frozen before final image save;
- Valid Frame Target consumes the exact correlated controlled-frame classification;
- ACCEPTED advances valid progress;
- WARNING advances by default when configured to count warnings;
- REJECTED, ERROR and LEARNING do not advance valid progress;
- missing classification fails safe;
- Smart Recovery operates only between exposures and never aborts an active shutter;
- filesystem action is not the source of truth for the quality verdict;
- QSM never deletes rejected images in the normal workflow;
- rejected-file rename/move requires eligible provenance and Monitor Only OFF;
- Undo BAD/restoration changes physical file disposition only and never rewrites the original automatic verdict/sequencer history;
- collision protection must prevent overwriting an existing target filename.

---

# PACKAGING POLICY

Synthetic Lab is intentionally retained during pre-store validation because it exercises the real N.I.N.A. dockable/UI without requiring a camera or real image files.

```text
QsmDevelopmentBuild=true
  Synthetic/**                INCLUDED
  Synthetic dockable/UI       INCLUDED

QsmDevelopmentBuild=false
  Synthetic/**                EXCLUDED
  Synthetic dockable/UI       EXCLUDED
```

Headless SyntheticCheck remains permanent CI coverage even after the public store package stops shipping the interactive Synthetic Lab.

---

# NEXT STEP

Finish the exact `1.3.0.5` CI run on PR #13, merge only if green, publish the pre-store field-test package with Synthetic Lab, then repeat real-host validation focused first on the crash reproduction path, dark-theme alignment/contrast, Frame # axis and latest-LIGHT preview. Only after those tests pass should QSM move to final store artwork/manifest submission.
