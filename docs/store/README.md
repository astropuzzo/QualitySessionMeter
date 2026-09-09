# N.I.N.A. catalog artwork

These files are the presentation assets referenced by the QSM N.I.N.A. plugin manifest.

- `featured.png` is a raster export of the same meter geometry used by the QSM plugin icon (`QSM_MeterSVG` / `PluginIcon.CreateMeterGeometry`).
- `nina-panel.png` is rendered from the actual QSM WPF `QualitySessionMeterDockable` DataTemplate using the deterministic Windows visual-regression harness.
- `web-dashboard.png` is rendered from the actual embedded QSM Web Dashboard HTML in Chromium with deterministic snapshot, guiding and LIGHT-preview responses.

The screenshots are generated from current source rather than hand-designed mockups. They are presentation/regression evidence and do not replace the required real N.I.N.A./AvalonDock host smoke test before official catalog submission.

Whenever the public UI changes materially, regenerate these assets before the next official catalog release.
