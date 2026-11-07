# Tunnela 0.1.0-preview.6 — draft prerelease notes

**Preparation only. This file does not publish or authorize a release.** Confirm
the final source commit, successful CI run and matching assets before copying
these notes into a GitHub prerelease. Do not label this as a stable release.

## Windows x64 preview

Tunnela provides a Windows interface, tray controls and a separate Windows
service for the official TrustTunnel CLI. It requires Windows 10 or later on
x64, administrator rights to install the service, and your own TrustTunnel
server configuration. The installer includes .NET 10, TrustTunnel CLI 1.1.7 and
Wintun 0.14.1; no separate runtime installation is needed.

Features include local server profiles, TOML/static `tt://` import, domain/IP/CIDR
routing rules, English/Russian UI, diagnostics, and optional GUI startup in the
tray. The service waits for a manual connection command.

This preview includes engine-supervisor cleanup/ownership improvements and the
Windows CI pipeline for tested, self-contained installer builds. The CI changes
do not rewrite the application or change its C# 14 / .NET 10 target.

## Important limitations

**Use at your own risk.** The installer has no Authenticode signature. Windows
may display **Unknown publisher**, and Microsoft Defender SmartScreen may warn
about or block it. The supplied SHA-256 detects file changes relative to the
recorded checksum; it is not a signature or independent publisher authentication.

- The kill switch does not guarantee a persistent network block after a client
  crash or service stop. Do not rely on this preview for such a guarantee.
- Per-application routing, subscription links, automatic updates and automatic
  VPN connection at startup are not implemented. Routing rules belong to each
  profile; shared presets are not available.
- Only one device-wide tunnel is supported. Disconnect any other VPN manually
  before connecting Tunnela. Do not test simultaneous VPN connections.
- Final installation, failure recovery, cross-user permissions, route/DNS cleanup
  and all kill-switch cases are not fully established by automated tests.

## Verification

Windows CI has passed a Release build with zero warnings/errors, 152 isolated
.NET tests and 83 isolated installer assertions, and compiled the unsigned
installer. See [build status](BUILD-STATUS.md) for the exact verified revisions
and runs. Before publication, link the successful run for the final release commit.

The owner reports successful personal use, prolonged operation and sleep
transitions. The exact tested installer has not been identified, so this report
must not be presented as manual certification of a particular downloadable asset
or all scenarios in the [manual test plan](TESTING.md).

## Installation and updates

Use the release's `Tunnela-0.1.0-preview.6-Setup-x64.exe` together with its matching
`.sha256` and `.build.json` files once they have been published. Before an update,
disconnect Tunnela and close its interface through the tray. Uninstallation
preserves saved profiles. Existing TrustTunnel installations are not migrated or
removed automatically. See [installation instructions](INSTALL.md).

## Publication checklist (remove from published notes)

1. Obtain explicit owner authorization for merging PR #1 and publishing the
   prerelease. No tag, release or merge has been created by these notes.
2. Select the exact source commit and its successful CI build. Prefer a build of
   the approved final branch commit, not an unidentified PR merge artifact.
3. Download the EXE, checksum and build metadata from that one build. Verify the
   archive digest and EXE checksum, and retain the source commit/run in the notes.
4. Identify the owner's tested installer by checksum or clearly retain the
   distinction between user-reported experience and exact-asset verification.
5. Publish as a GitHub **prerelease**, with the three matching assets and the
   limitations above. Do not imply code signing or stable-release certification.
