# Publishing QualitySessionMeter to the N.I.N.A. plugin catalog

This file is the release procedure for the **official production package**. It is intentionally separate from QSM's pre-store `vX.Y.Z.W` field-test releases.

The upstream catalog is `isbeorn/nina.plugin.manifests`. Its manifest README is the authority if this document ever conflicts with upstream requirements.

## Release blockers

Do not create an official four-part release tag until all of the following are true:

- production and field-test CI are green;
- full V1/V2/V3 deterministic regression is green;
- clean install and upgrade have been tested in the supported N.I.N.A. host;
- the N.I.N.A. panel has been checked in the normal dark theme and night/red theme;
- Web Dashboard has been tested from another LAN/VPN device with password OFF and ON;
- the displayed dashboard address selects the physical LAN adapter when a VPN is active;
- the latest real saved LIGHT preview appears in both the universal Web Dashboard and the OpenAstro/companion panel;
- PHD2 disconnect/reconnect and live guiding have been tested;
- preview failure cannot affect image saving or QSM classification;
- rejected-file rename/move/restore collision cases have been tested;
- plugin unload/reload and N.I.N.A. shutdown are clean;
- the production package contains no Synthetic Lab/test-only UI;
- final store artwork/screenshots are current and representative;
- the human maintainer has personally reviewed, understood and tested the release code and accepts responsibility for security, privacy, licensing, provenance, debugging and maintenance.

## AI-assisted development disclosure

N.I.N.A. currently allows AI-assisted development but requires material AI use to be disclosed and requires an accountable human maintainer.

QualitySessionMeter has used material AI assistance. `astropuzzo` is the accountable human maintainer. Creating the official release tag and submitting its manifest should only happen after the maintainer has personally completed the release blockers above.

## Store artwork

The N.I.N.A. manifest supports these optional presentation fields:

- `Descriptions.FeaturedImageURL` — plugin logo/featured artwork;
- `Descriptions.ScreenshotURL` — primary screenshot;
- `Descriptions.AltScreenshotURL` — secondary screenshot.

For QSM, keep final public assets under `docs/store/` with stable names:

```text
docs/store/featured.png
docs/store/nina-panel.png
docs/store/web-dashboard.png
```

After those files are final, add their stable raw GitHub URLs as `AssemblyMetadata` entries in `Properties/AssemblyInfo.cs` using the keys `FeaturedImageURL`, `ScreenshotURL`, and `AltScreenshotURL`, then bump the plugin version before creating the official tag. Do not publish outdated screenshots from a field-test UI.

## One-time GitHub setup

1. Fork `https://github.com/isbeorn/nina.plugin.manifests` into the `astropuzzo` account and keep the fork name exactly `nina.plugin.manifests`.
2. In `astropuzzo/QualitySessionMeter`, open **Settings → Actions → General → Workflow permissions** and select **Read and write permissions**.
3. Create a GitHub Personal Access Token that can write to the `astropuzzo/nina.plugin.manifests` fork and create a pull request.
4. Add that token to `astropuzzo/QualitySessionMeter` as an Actions repository secret named exactly `PAT`.

The `PAT` is only used by the optional manifest-publishing job. Never commit it to source control.

If the fork or `PAT` is missing, the official release workflow still builds, validates and publishes the ZIP + manifest. Only the automatic upstream manifest PR is skipped; it can be submitted manually.

## Versioning convention

Pre-store test releases use a leading `v`, for example:

```text
v1.3.0.4
```

Official catalog releases use the upstream-recommended four-part tag **without** a leading `v`:

```text
1.3.0.4
```

Before tagging, `QualitySessionMeter.csproj` must contain the exact same four-part `Version`, `AssemblyVersion`, and `FileVersion`.

## What the official workflow does

`.github/workflows/nina-release.yml` runs only for a tag matching `X.Y.Z.W` and then:

1. verifies that the tag equals the version in `QualitySessionMeter.csproj`;
2. verifies that the production source set excludes Synthetic Lab;
3. checks the release/security/dashboard contracts;
4. builds the production DLL with `QsmDevelopmentBuild=false`;
5. runs the complete V1/V2/V3 deterministic regression suite;
6. assembles the production package;
7. downloads the current upstream `CreateManifest.ps1`;
8. generates the archive and manifest from the **final already-built DLL**;
9. validates that manifest by placing it into a checkout of `isbeorn/nina.plugin.manifests` and running `npm install` + `node gather.js`;
10. creates a GitHub release containing both the ZIP and generated manifest;
11. if the fork + `PAT` exist, creates a manifest branch in the fork and opens the upstream pull request automatically.

The DLL is not rebuilt after manifest generation. This preserves the installer checksum expected by N.I.N.A.

## First official submission

After all blockers are complete:

```bash
git checkout main
git pull
git tag 1.3.0.4
git push origin 1.3.0.4
```

Use the actual final version number, not the example above.

Then open the GitHub Actions run named **NINA catalog release** and confirm that `build-validate-release` succeeded.

If automatic publishing prerequisites are configured, the workflow opens the PR against `isbeorn/nina.plugin.manifests` automatically. Review that PR and respond to maintainers if they request changes.

If automatic publishing is not configured, download `QualitySessionMeter.<version>.manifest.json` from the GitHub release and place it in the fork at:

```text
manifests/Q/QualitySessionMeter/3.3.0.1057/<plugin-version>/manifest.json
```

Run the manifest repository validation locally:

```bash
npm install
node gather.js
```

Then push the fork branch and open a pull request against `isbeorn/nina.plugin.manifests:main`.

## After upstream merge

Once the manifest PR is merged, the N.I.N.A. catalog pipeline publishes it to the plugin repository used by N.I.N.A. The plugin becomes discoverable/installable for compatible N.I.N.A. versions.

Future updates use the same process with a higher QSM version. N.I.N.A.'s Plugin Manager selects the highest compatible manifest, so QSM does **not** need to implement its own updater.

Do not promise or implement a silent self-updater inside QSM. The N.I.N.A. Plugin Manager is the update channel.
