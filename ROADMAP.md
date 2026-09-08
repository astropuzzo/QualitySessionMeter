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

`1.3.0.5` is a **pre-store field-test candidate**, not yet an official N.I.N.A. manifest submission.

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

## WPF / AvalonDock dispatcher safety

A real N.I.N.A. 3.3.0.1057 field-test log exposed a cross-dispatcher WPF crash while AvalonDock materialized QSM templates. The failure was caused by data-binding shared `SolidColorBrush` resources to host theme brushes: the binding made those WPF `Freezable` objects non-freezable and unsafe across dispatchers.

Release requirements:

- shared QSM fallback brushes must remain immutable/frozen;
- no `BindingOperations.SetBinding` or equivalent dynamic binding may be attached to shared brush `Color` properties;
- active N.I.N.A. theme resources are resolved only on the actual loaded UI element through `SetResourceReference(...)` on its owning dispatcher;
- production and official-release CI reject regression to the unsafe shared-brush pattern;
- final store readiness still requires a real N.I.N.A./AvalonDock smoke test because a standalone visual harness cannot reproduce every host dispatcher/lifecycle interaction.

## Native N.I.N.A. UI consistency

- Plugin Options use a flat N.I.N.A.-style layout instead of custom card/GroupBox presentation.
- TextBlock text and TextBox/PasswordBox/ComboBox content are explicitly left-aligned where QSM presents ordinary option values; host styles must not silently center them.
- Structural foreground/background/border resources follow the active N.I.N.A. theme through loaded-element resource references.
- Synthetic Lab field-test chrome must also follow dark/light host structural resources without mutable shared brushes.
- Dark/light readability is a release gate, not a cosmetic follow-up.
- Every non-obvious user-editable option must provide contextual help explaining:
  - what is measured;
  - unit/range;
  - whether lower/higher is stricter;
  - whether the option affects hard rejection, diagnostic scoring, filesystem handling or sequencer behavior;
  - relevant safety constraints.
- Adaptive Calibration settings are exposed in Plugin Options, including its bounded automatic safety caps.

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
- a readable adaptive **Frame #** axis using the same chronological QSM `FrameIndex` as the frame tables/tooltips;
- the Frame # row must not overlap the EVENTS badges/legend;
- exact-value hover/tooltips;
- legend lane clipped independently from the plot;
- graph labels/reference labels must not overlap data on narrow dock layouts.

A decorative single-series substitute is not acceptable.

## Live guiding dashboard

The Web Dashboard live guiding graph must not imply that the rolling live signal is the same quantity used for exposure rejection.

Required model:

- signed **RA** and **DEC** errors centered on zero;
- **Total error magnitude** shown separately;
- real sample timestamps / rolling lookback window;
- RA RMS, DEC RMS, total rolling RMS and maximum excursion;
- sustained/hard excursion limits shown only against total magnitude, because the engine applies those rules to the total guide-error magnitude;
- per-sample hover details;
- explicit note that exposure classification uses exposure-window guide metrics, not merely the dashboard rolling 20-second diagnostic.

## Latest LIGHT preview

- the universal dashboard and OpenAstro use one shared in-memory latest-LIGHT preview cache;
- preview generation is display-only and must never alter or reopen acquisition FITS/XISF files for classification/file handling;
- generation ordering prevents an older asynchronous encode from replacing a newer LIGHT;
- the browser uses preview generation/ETag semantics rather than blindly replacing the `<img>` on a timer;
- a temporary reconnect/404/fetch error must not erase the last already-valid LIGHT from the operator's screen;
- Synthetic Lab does not fabricate astronomical image pixels; a real LIGHT is required to validate the true preview path.

## Web Dashboard security / network model

The browser dashboard remains an optional read-only service independent from the OpenAstro integration bridge.

Required behavior:

- disabled by default;
- no UPnP, router forwarding, DNS or outbound tunnel configuration;
- preferred displayed URL uses the physical private LAN IPv4 and ignores common VPN/virtual adapters;
- URL is selectable and has one-click clipboard copy;
- password is optional, not mandatory;
- when enabled, password storage uses salted PBKDF2-HMAC-SHA256 rather than clear text;
- password/auth/port/profile reconfiguration invalidates existing browser sessions;
- bounded request/header/body sizes;
- bounded concurrent clients;
- request timeout;
- login rate limiting;
- HttpOnly + SameSite session cookie;
- CSP, nosniff, frame-deny, referrer, permissions and same-origin headers;
- all session-derived text inserted into HTML must be escaped or assigned through text-safe DOM APIs;
- no remote threshold/file/sequencer control routes;
- direct public-Internet HTTP exposure is unsupported; remote access is expected through a trusted VPN or HTTPS reverse proxy.

The existing tokenized OpenAstro bridge remains separate and backward-compatible.

## Online visual regression

QSM now has an online visual-regression layer that does not require the maintainer's local PC or a local N.I.N.A. installation for routine UI iteration.

- `QualitySessionMeter.VisualHarness` renders the actual WPF ResourceDictionaries/DataTemplates and custom timeline control on a GitHub-hosted Windows runner using deterministic stress-test data.
- dark and light host resource palettes are exercised;
- Options, Control Center, main QSM panel, Synthetic Lab and wide/narrow timelines are rendered to PNG;
- Playwright extracts/renders the actual embedded Web Dashboard HTML against deterministic snapshot/guiding/latest-LIGHT responses at desktop, tablet and mobile viewports;
- `.github/workflows/visual-preview.yml` uploads the complete screenshot set as a PR artifact;
- the visual harness is explicitly excluded from the QSM plugin source/resource set and cannot ship in the production DLL;
- visual CI has already detected issues (Synthetic Lab light-theme chrome and Frame #/EVENTS overlap) before another real-host installation.

Visual CI is a release-quality gate for layout/theme/responsiveness, but it is not a substitute for the final N.I.N.A./AvalonDock lifecycle smoke test.

## Documentation / reviewer readiness

Required before official manifest submission:

- `README.md` matches the actual .NET/N.I.N.A. target and current package names;
- `docs/METRICS.md` is the authoritative metric/reference document;
- `SECURITY.md` documents the Web Dashboard trust model and reporting path;
- assembly metadata contains stable GUID, author/company, license, repository, homepage, tags, minimum N.I.N.A. version, short description and long description;
- `CHANGELOG.md` describes the current release line;
- material AI assistance is disclosed when submitting the official manifest, with the human maintainer retaining responsibility for review, testing, security, privacy, licensing, provenance and maintenance;
- actual screenshots/featured image are added before store submission; generated/mock UI must not be presented as evidence of host behavior.

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
online WPF visual preview generation
online Web Dashboard desktop/tablet/mobile preview generation
```

Static release-quality gates must catch regressions such as:

- old simplified browser chart returning;
- custom GroupBox/card Plugin Options returning;
- unsafe shared WPF brush binding returning;
- Options text/content losing left alignment;
- dashboard address losing explicit OneWay read-only binding;
- missing LAN resolver;
- missing timeline clipping or Frame # axis;
- Frame #/EVENTS overlap detectable in online visual review;
- missing generation-aware preview behavior;
- missing HTML escaping;
- missing browser security headers.

## Real-host gates

Do **not** declare store readiness until these are observed in actual N.I.N.A.:

1. plugin loads cleanly on N.I.N.A. 3.3 NIGHTLY #057 and reproducing the prior Imaging-layout navigation no longer triggers the cross-thread `SolidColorBrush`/AvalonDock crash;
2. Plugin Options render correctly in the active dark theme and option labels/values are left-aligned/readable;
3. Control Center text/controls have readable contrast;
4. multichannel timeline labels, Frame # axis, event markers and reference text do not overlap;
5. Synthetic Lab is present in the field-test package and drives the real dockable UI;
6. browser dashboard opens from another LAN device;
7. displayed dashboard URL selects the intended physical LAN address with VPN enabled;
8. Copy button places the full URL on the Windows clipboard;
9. password OFF allows direct access;
10. password ON accepts correct credentials, rejects invalid credentials and revokes sessions after password/config changes;
11. live guiding graph receives real N.I.N.A./guider samples and uses correct RA/DEC/total semantics;
12. latest real LIGHT preview updates, remains stable through temporary browser reconnect and does not write preview files to disk;
13. OpenAstro integration still works unchanged and displays the same shared preview source;
14. shutdown/profile switch/plugin teardown produces no listener/client exceptions;
15. rejected-frame review and undo remain collision-safe with real FITS/XISF files.

---

# STORE SUBMISSION GATES

Official N.I.N.A. publication remains blocked until the real-host gates above are complete.

When ready:

1. freeze a production commit/tag;
2. build the **production package only** for store distribution;
3. generate the manifest from that immutable final DLL/archive;
4. include checksum generated from the final archive;
5. validate the manifest against the official N.I.N.A. schema;
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

Use the online visual-regression artifact to review dark/light WPF and desktop/tablet/mobile Web Dashboard output without requiring a local N.I.N.A. install for every UI iteration. Fix any visual regression on PR #13, rerun both normal and visual CI on the exact final head, then merge/publish `1.3.0.5` as the next pre-store field-test package. Perform one final real-host N.I.N.A./AvalonDock + real LIGHT + live guider/OpenAstro smoke test only after the online visual candidate is approved. Official manifest/image submission remains blocked until that final host test passes.
