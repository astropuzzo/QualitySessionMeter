# N.I.N.A. catalog publishing

## Current submission

- Plugin: QualitySessionMeter 1.4.0.2
- Minimum N.I.N.A.: 3.3.0.1057 (.NET 10)
- Permanent GUID: bf861692-b3de-4fdc-8a74-d2b97434f49d
- Manifest: manifests/Q/QualitySessionMeter/3.3.0.1057/1.4.0.2/manifest.json
- Upstream request: https://github.com/isbeorn/nina.plugin.manifests/pull/689
- Release: https://github.com/astropuzzo/QualitySessionMeter/releases/tag/v1.4.0.2

The pending first submission replaces 1.3.1.1 with 1.4.0.2. Approval and feed
publication by N.I.N.A. maintainers are still required. Older release archives remain
available; replacing a pending manifest does not delete or rebuild them.

## Promoting the existing 1.4.0.2 package

This release promotes the existing `v1.4.0.2` tag rather than rebuilding its DLL.
The immutable production assembly comes from commit
`2cd4345ff05afe11265275114e694ff25c7d62dd`, whose build, dispatcher and visual CI passed.
The catalog ZIP updates only documentation and package naming. The original candidate
asset remains unchanged. `tools/CreateManifest.ps1` from the official manifest repository
generates the manifest/archive from that preserved DLL. The checksum is verified
against the downloaded final archive before the pending upstream PR is updated.
Do not push a second numeric 1.4.0.2 tag: that would trigger a different binary build.

Validation scope and remaining field coverage are in [1.4-VALIDATION.md](1.4-VALIDATION.md).
Do not claim an unrecorded live rejection, file rename or complete acquisition night.

## Future releases

Keep the GUID. Increase the four-part version and validate the new release.
The `.github/workflows/nina-release.yml` workflow uses numeric tags without `v` to
build/preserve production files, run release gates, generate an official manifest,
validate it with upstream `gather.js`, and publish the exact archive/manifest pair.
Never rebuild or replace checksummed release assets after manifest generation.

The workflow can also push a manifest branch to the maintainer's
`nina.plugin.manifests` fork and open an upstream PR. It requires the repository
Actions secret named `PAT`, a GitHub Personal Access Token with permission for those
operations. That secret is currently absent; manual submission through the maintainer
account remains available. Do not put token values in source, logs or documentation.

A GitHub release alone does not create a Plugin Manager update. The higher compatible
version must appear in the published N.I.N.A. manifest feed. QSM uses that mechanism,
with no parallel self-updater.

## Maintainer responsibility

Material AI assistance was used during development. astropuzzo is the accountable
human maintainer and retains responsibility for understanding, testing, security,
privacy, licensing, provenance, debugging and maintenance. Include this disclosure
in the upstream submission.

Official requirements: https://github.com/isbeorn/nina.plugin.manifests
