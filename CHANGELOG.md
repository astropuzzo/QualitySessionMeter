# Changelog

All notable changes to QualitySessionMeter are documented here.

The current `1.3.x` line is still a **pre-store field-test line**. During this phase GitHub releases intentionally contain both a field-test package with Synthetic Lab and a production package without Synthetic Lab. The official N.I.N.A. manifest will be generated only after real-host validation is complete.

---

## [1.3.0.5] - 2026-09-08

### WPF/AvalonDock crash hotfix and UI alignment

- Fixed an unexpected N.I.N.A. Imaging-layout crash caused by data-binding shared QSM `SolidColorBrush` resources to host theme brushes. A bound `SolidColorBrush` is a non-freezable WPF `Freezable`; AvalonDock can later materialize a dock template on another dispatcher, producing `XamlParseException`, `StaticResourceHolder` failures and cross-thread `SolidColorBrush` access errors.
- Removed cross-thread brush bindings entirely. Shared fallback brushes are now immutable/frozen and therefore safe to cross dispatcher boundaries.
- Theme following is now applied with `SetResourceReference(...)` on the actual loaded UI element, on its owning UI dispatcher. This preserves live N.I.N.A. theme resources without sharing mutable `Freezable` objects between threads.
- Fixed Plugin Options controls whose boxes were left-positioned but whose text/content could still inherit centered alignment from N.I.N.A. styles. TextBlock text, TextBox content, PasswordBox content, ComboBox content and ComboBox items are explicitly left-aligned while preserving native control styles.
- Fixed inconsistent foreground colors in Plugin Options by resolving `ButtonForegroundBrush` on loaded controls rather than relying on inherited/hard-coded text colors.
- Fixed Control Center hard-coded dark card chrome versus inherited host foreground colors. Cards/borders now resolve N.I.N.A. background/border resources on the UI dispatcher; ordinary text uses the host foreground resource and QSM accent text uses the host primary resource.
- Synthetic Lab now follows light/dark host structural resources through loaded-element resource references while keeping all shared fallback brushes frozen.
- Added an adaptive `Frame #` axis to the N.I.N.A. multichannel timeline and the Web Dashboard using the same chronological QSM `FrameIndex` shown by Recent Frames and hover diagnostics.
- Separated the Frame # row from the EVENTS legend/badge row so axis text cannot overlap the event explanation on narrow timelines.
- Web Dashboard timeline hover now includes filename as well as frame number/state/metrics.
- Replaced blind periodic browser preview reloads with preview-generation-aware updates; the last valid LIGHT remains visible through temporary reconnect/fetch errors.

### Automated visual regression

- Added `QualitySessionMeter.VisualHarness`, a Windows/WPF visual test host that renders the actual QSM DataTemplates and `QualityTimelineControl` with deterministic stress-test data without requiring a local N.I.N.A. installation.
- Added a Playwright renderer that extracts the actual embedded Web Dashboard HTML and renders desktop, tablet and mobile layouts against deterministic snapshot/guiding/preview responses.
- Added `.github/workflows/visual-preview.yml`: every PR can now generate dark/light WPF screenshots and desktop/tablet/mobile browser screenshots entirely on GitHub-hosted Windows runners.
- The visual harness is explicitly excluded from the plugin compile/resource source set and can never ship in the production DLL.
- Automated visual inspection already caught and drove fixes for Synthetic Lab light-theme chrome and Frame #/EVENTS overlap before host installation.
- These renders are visual-regression evidence only; final store readiness still requires one real N.I.N.A./AvalonDock smoke test because a standalone harness cannot reproduce every host lifecycle/dispatcher interaction.

---

## [1.3.0.4] - 2026-09-08

### Unified latest-LIGHT preview and official N.I.N.A. publication path

- Added a single shared in-memory latest-LIGHT preview cache used by both the universal Web Dashboard and the existing tokenized OpenAstro/companion bridge.
- A real saved LIGHT is cloned/frozen from N.I.N.A.'s `ImageSaved` payload, then JPEG-encoded asynchronously so preview encoding is isolated from the acquisition/save path.
- Preview generation is display-only: it never reads, writes, renames, moves or deletes the acquisition FITS/XISF file and never participates in frame classification.
- Added generation ordering so an older, slower asynchronous JPEG encode cannot overwrite a newer preview.
- Added preview generation/version metadata, capture timestamp, image ID and ETag support to the universal dashboard routes.
- OpenAstro keeps its existing `/api/v1/...` routes and token authentication; only the internal preview source is now shared, preventing the two dashboards from diverging.
- Synthetic Lab still contains no real camera image pixels by design, so it cannot manufacture an astronomical preview. The cached preview updates on the next real saved LIGHT.

### N.I.N.A. catalog release pipeline

- Added `.github/workflows/nina-release.yml`, a separate official-release pipeline triggered only by a four-part tag such as `1.3.0.4` (without the pre-store `v` prefix).
- The official pipeline builds the production source set only, verifies that Synthetic Lab is excluded, runs the complete V1/V2/V3 regression suite and generates the manifest from the final immutable DLL.
- The generated manifest is validated against a checkout of the current `isbeorn/nina.plugin.manifests` repository before the release is published.
- Official release assets contain the archive plus the exact generated manifest/checksum pair.
- Optional automatic manifest submission is supported after the maintainer creates the `astropuzzo/nina.plugin.manifests` fork and configures the `PAT` repository secret; otherwise the manifest remains available for manual submission.
- Added `docs/PUBLISHING.md` with release blockers, AI-assistance disclosure requirements, one-time GitHub setup, store-artwork slots, exact manifest path and manual/automatic publication procedures.
- Official catalog submission remains blocked until real N.I.N.A. host validation and current final store artwork/screenshots are complete.

---

## [1.3.0.3] - 2026-09-08

### N.I.N.A. release-readiness, chart correctness, theme consistency and dashboard hardening

- Reworked Plugin Options to follow the flat/native N.I.N.A. visual language rather than custom card/GroupBox presentation.
- Bound shared QSM structural colors to active N.I.N.A. theme resources so dark/light theme foregrounds, backgrounds and borders remain coherent.
- Increased secondary-text readability and removed hard-coded low-contrast chrome where host theme resources are appropriate.
- Added complete contextual help for the public user-editable settings, including units, valid ranges, decision semantics and safety implications.
- Restored the full **Adaptive Calibration** configuration to Plugin Options:
  - Off / Suggest Only / bounded Automatic modes;
  - stable calibration window;
  - automatic safety caps for RMS, sustained excursion, hard excursion, star loss and background deviation.
- Clarified that adaptive calibration trains only on stable same-context ACCEPTED frames and that Suggest Only never changes thresholds automatically.

### Multichannel timeline

- Removed the browser's simplified single Quality line and replaced it with the same three-band measurement model used by QSM in N.I.N.A.:
  - Quality + Confidence;
  - Guide RMS + active RMS limit;
  - Stars Δ + Background Δ relative to the rolling clean-frame baseline.
- Added visible baseline/threshold semantics, event markers and exact-value hover support.
- Made the custom WPF timeline theme-aware for structural background/border/text colors.
- Added a dedicated clipped legend lane so long labels, marker badges and reference labels cannot draw over the plot on narrow dock layouts.
- Added readable backing behind reference-line labels.

### Live guiding dashboard

- Replaced the low-information guiding graph with a diagnostic representation that separates:
  - signed RA error around zero;
  - signed DEC error around zero;
  - total guide-error magnitude.
- Added real sample-time context, RA/DEC/total RMS, maximum excursion and per-sample hover details.
- Sustained/hard excursion limits are shown against total error magnitude rather than incorrectly implying that those rules apply independently to RA or DEC.
- Explicitly distinguishes rolling live guiding diagnostics from the exposure-window guide metrics used for frame classification.

### Web Dashboard safety and correctness

- Dashboard remains disabled by default and read-only.
- Preserved the separate tokenized OpenAstro companion API unchanged.
- Retained physical-LAN address preference and VPN/virtual-adapter filtering for the displayed browser URL.
- Dashboard address remains selectable and has one-click clipboard copy.
- Optional password remains optional; password material is stored as salted PBKDF2-HMAC-SHA256 derived data, not clear text.
- Reconfiguration now invalidates existing browser sessions and failed-login state so changing/clearing password, toggling auth, switching profile, changing port or disabling/re-enabling the listener creates a fresh security context.
- Fixed a shutdown race by allowing in-flight request tasks to complete their semaphore release after listener cancellation instead of synchronously disposing that semaphore beneath them.
- Added bounded concurrent clients, request lifetime, header/body limits and login throttling.
- Added Content Security Policy, `nosniff`, frame denial, same-origin resource policy, referrer policy and restrictive browser permissions policy.
- Session-derived filename/target/cause content is escaped or written through text-safe DOM operations before browser rendering.

### Documentation / release gates

- Added `docs/METRICS.md` as the authoritative metric/reference document.
- Added `SECURITY.md` describing the supported local/VPN/reverse-proxy trust model and unsupported direct public-Internet exposure.
- Expanded assembly metadata for future official manifest generation, including Company/Author and LongDescription.
- Rebuilt `README.md` around the real .NET 10 / N.I.N.A. 3.3 NIGHTLY #057 target and current field-test/production packaging model.
- Refreshed `ROADMAP.md` to make `1.3.0.3` host validation and official manifest submission gates explicit.
- Added CI release-quality guards against regression to the simplified browser chart, custom Options layout, missing theme binding, missing timeline clipping, missing LAN resolver, missing escaping or missing browser security headers.
- Material AI-assisted development is documented for the eventual official N.I.N.A. submission; the human maintainer remains responsible for review, testing, provenance, licensing, privacy, security and maintenance.

---

## [1.3.0.2] - 2026-09-08

### Web Dashboard Options load hotfix

- Fixed a N.I.N.A. `XamlParseException` caused by the selectable dashboard-address `TextBox` defaulting to a TwoWay binding against the read-only `WebDashboardAddress` property.
- Dashboard-address binding is explicitly OneWay while keeping the text selectable/copyable.
- Field-test package continues to include Synthetic Lab; production package continues to exclude it.

---

## [1.3.0.1] - 2026-09-08

### LAN address selection, clipboard access and pre-store packaging

- Replaced first-IPv4 selection with a LAN resolver that prioritizes active physical Ethernet/Wi-Fi private IPv4 interfaces with useful gateway evidence.
- Ignores common VPN/tunnel/virtual adapters when selecting the address shown to the user, including Tailscale, WireGuard/Wintun, Mullvad/OpenVPN/TAP, ZeroTier and common VM/container adapters.
- Dashboard URL became directly selectable and gained a Copy action for the complete URL.
- Pre-store GitHub releases now ship two explicit packages:
  - `field-test-with-synthetic-lab` for current validation;
  - `production` for validating the future N.I.N.A. store source set.

---

## [1.3.0.0] - 2026-09-08

### Universal local Web Dashboard

- Added an optional self-contained browser dashboard served directly by QSM on the N.I.N.A. PC.
- Dashboard does not require OpenAstro and can be opened from a reachable LAN/VPN browser.
- Added dashboard enable/port/password configuration to the plugin settings.
- Password protection is optional and OFF by default.
- Dashboard routes are read-only and provide session telemetry plus an in-memory latest-LIGHT JPEG preview.
- Kept the existing tokenized OpenAstro bridge separate and backward-compatible.
- No router port, UPnP, DNS or public Internet configuration is performed by QSM.

---

## [1.2.0.0] - 2026-09-08

### Rejected-frame review and rich remote session visibility

- Added a dedicated Rejected Review surface inside the N.I.N.A. dockable with filename, file disposition, Quality, Confidence, Guide RMS, star/background deltas and probable cause.
- Selecting a real rejected row loads its existing FITS/XISF into N.I.N.A. Image for visual inspection.
- Added collision-safe Undo BAD / restore filename for QSM-applied `BAD_` prefixes and `Rejected` folder moves.
- Restoring the physical file deliberately does not rewrite the automatic QSM verdict or retroactively change Advanced Sequencer valid-frame accounting.
- Expanded the read-only OpenAstro snapshot with filenames, file-action state, sequence title, max guide excursion, guide-pattern diagnostics and richer per-frame data.

---

## [1.1.0.2] - 2026-09-08

- Made the active Synthetic Lab store observable through the remote snapshot during field testing without mixing it with the real acquisition store.
- Production remains free of Synthetic Lab implementation and UI.

## [1.1.0.1] - 2026-09-08

- Finalized V3 frozen acquisition provenance, one-shot controlled-frame correlation and source/file-action safety gates.
- Strengthened production-vs-field-test Synthetic Lab isolation and CI regression coverage.

## [1.1.0.0] - 2026-09-08

- Added the optional tokenized read-only OpenAstro/companion HTTP bridge.
- Added snapshot telemetry, true live guider samples and an in-memory latest-LIGHT JPEG preview.
- No remote write/control routes are exposed.

## [1.0.0.2] - 2026-09-07

- Added the self-explanatory multichannel timeline model with Quality, Confidence, Guide RMS, Stars Δ and Background Δ, visible baselines/limits and nearest-frame hover help.
- Added the process-local versioned `QualitySessionMeter.ApiV1` snapshot contract.

## [1.0.0.1] - 2026-09-07

- Fixed initial real-host UI/usability issues, added contextual help and deterministic `G/S/B/!/?` event codes.

## [1.0.0.0] - 2026-09-07

- First V3 release line: real-time quality monitoring, temporal/session intelligence, Advanced Sequencer valid-frame acquisition, provenance-safe file handling and Synthetic Lab validation harness.
