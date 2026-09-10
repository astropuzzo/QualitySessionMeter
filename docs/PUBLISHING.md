# Publishing QualitySessionMeter to the N.I.N.A. plugin catalog

This is the release procedure for the official production package. The upstream catalog is `isbeorn/nina.plugin.manifests`; its current README/schema are authoritative if requirements change.

## First catalog release

```text
QualitySessionMeter:       1.3.1.0
N.I.N.A. minimum version: 3.3.0.1057
Manifest path:            manifests/Q/QualitySessionMeter/3.3.0.1057/1.3.1.0/manifest.json
Plugin GUID:              bf861692-b3de-4fdc-8a74-d2b97434f49d
```

The GUID is permanent and must never be changed for later releases.

## Validation status

The 1.3.0.x pre-store line was used for iterative field testing. Before the 1.3.1.0 catalog freeze the maintainer confirmed the current plugin loads and operates correctly in the real N.I.N.A. host after the final UI cleanup. Automated release gates additionally cover production/field-test source isolation, V1/V2/V3 deterministic regression, WPF independent-STA dispatcher safety, Web Dashboard security contracts and online WPF/browser visual regression.

Synthetic Lab is a development/field-test facility and is excluded from the official production package.

## Store artwork

The official manifest uses current release-source assets under:

```text
docs/store/featured.png
docs/store/nina-panel.png
docs/store/web-dashboard.png
```

`nina-panel.png` comes from the actual current QSM WPF monitoring DataTemplate and `web-dashboard.png` from the actual embedded dashboard rendered in Chromium. `featured.png` is based on the same QSM meter geometry used by the plugin icon.

The assembly metadata keys are:

- `FeaturedImageURL`
- `ScreenshotURL`
- `AltScreenshotURL`

## AI-assisted development disclosure

Material AI assistance was used during QSM development. N.I.N.A. currently permits AI-assisted development but requires disclosure and an accountable human maintainer.

The upstream manifest PR must state that `astropuzzo` is the accountable human maintainer and accepts responsibility for understanding, testing, security, privacy, licensing, provenance, debugging and maintenance of the submitted plugin.

## Official release workflow

`.github/workflows/nina-release.yml` runs only for a four-part tag **without** a leading `v`.

For `1.3.1.0` it:

1. verifies tag/project version equality;
2. verifies the official source set excludes Synthetic Lab, visual/test harnesses and the removed duplicate QSM Control Center;
3. verifies required store metadata/artwork and Web Dashboard/WPF safety contracts;
4. runs the independent-STA WPF dispatcher check;
5. builds the immutable production DLL;
6. runs full V1/V2/V3 deterministic regression;
7. packages the production files;
8. downloads the current upstream `CreateManifest.ps1`;
9. creates the archive + manifest from the final DLL;
10. places the manifest into a checkout of `isbeorn/nina.plugin.manifests` and validates it with `npm install` + `node gather.js`;
11. publishes the exact ZIP + manifest to the GitHub release;
12. optionally creates the manifest branch/PR automatically when the maintainer fork and `PAT` secret are available.

The DLL is never rebuilt after the manifest/checksum pair is generated.

## One-time upstream setup

The only account-level setup not stored in this repository is:

1. fork `isbeorn/nina.plugin.manifests` into the `astropuzzo` account, keeping the repository name `nina.plugin.manifests`;
2. if automatic submission is desired, add a repository secret `PAT` to `astropuzzo/QualitySessionMeter` with permission to push to that fork and create the upstream PR.

If the fork/PAT are absent, the official release still produces a fully validated manifest. It can then be committed manually to the fork and submitted upstream.

## Create the official release

Only after the release-preparation PR has been merged into `main`, create and push:

```text
1.3.1.0
```

Do not use `v1.3.1.0`: the `v...` prefix is reserved for pre-store field-test releases.

## Upstream pull request

The generated manifest belongs at:

```text
manifests/Q/QualitySessionMeter/3.3.0.1057/1.3.1.0/manifest.json
```

The PR should state that the manifest was generated from the immutable production DLL, validated with the official manifest pipeline, and include the required AI-assistance disclosure.

If upstream review requires a plugin binary/code change, do not modify the already-checksummed 1.3.1.0 asset. Create a higher plugin version and regenerate the manifest/checksum.

## After merge

After `isbeorn/nina.plugin.manifests` merges the PR, N.I.N.A.'s catalog pipeline makes QSM discoverable for compatible N.I.N.A. installations. Future updates follow the same higher-version manifest process. QSM must not implement a parallel self-updater; the N.I.N.A. Plugin Manager is the normal public update channel.
