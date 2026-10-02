# N.I.N.A. catalog publishing

## Catalog identity

- Minimum N.I.N.A.: 3.3.0.1057 (.NET 10).
- Permanent GUID: bf861692-b3de-4fdc-8a74-d2b97434f49d.
- Manifest: manifests/Q/QualitySessionMeter/3.3.0.1057/{version}/manifest.json.
- First catalog entry, 1.4.0.2: accepted September 16 in https://github.com/isbeorn/nina.plugin.manifests/pull/689.

Updates keep the existing plugin identity and compatibility floor. Each version requires its own upstream manifest update. Published archives and manifests must remain unchanged.

## Release procedure

1. Increase the four-part version, finish the dated CHANGELOG.md entry and pass branch CI. Keep verification records separate from release notes.
2. Merge the reviewed source and push its matching numeric tag without a leading v. Only the tag-driven nina-release.yml workflow publishes official packages; main-branch builds produce CI artifacts only.
3. The workflow requires a dated, nonempty changelog entry, publishes that entry as release notes, builds and preserves the production DLL, runs release gates, and generates the archive and manifest using the upstream script. Development harnesses are excluded.
4. Validate the manifest with upstream gather.js. Download the published archive and verify its checksum, plugin GUID, version, minimum application version and production assembly before submitting the catalog update. Never rebuild or replace checksummed assets.
5. Submit the exact manifest to isbeorn/nina.plugin.manifests. Keep the prior version. A GitHub release alone does not make an update available in the Plugin Manager: upstream acceptance and feed publication are required.

The optional Actions secret PAT permits the workflow to push a manifest branch to the maintainer fork and open its upstream PR. Authenticated manual submission is also available. Never place token values in source, logs or documentation. No separate plugin self-updater is used.

## Validation and rollback

Keep verification records separate from public release notes. Retain the prior release and N.I.N.A. profile; close N.I.N.A. before restoring a prior DLL. Do not silently replace a catalog archive to roll back a version.

## Maintainer responsibility

Material AI assistance was used during development. astropuzzo is the accountable human maintainer and retains responsibility for understanding, testing, security, privacy, licensing, provenance, debugging and maintenance. Include this disclosure and accurate validation scope in the upstream submission.

Official requirements: https://github.com/isbeorn/nina.plugin.manifests.
