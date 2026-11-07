# Tunnela 0.1.0-preview.7

Experimental Windows x64 prerelease for limited evaluation, not a stable or broadly validated release, of the independent desktop interface for TrustTunnel.
Requires Windows 10 or later, administrator rights to install the service, and
your own TrustTunnel server configuration. The installer includes .NET 10,
TrustTunnel CLI 1.1.7 and Wintun 0.14.1.

## Changes from preview.6

- Windows CI builds the application, runs isolated tests and compiles the
  self-contained installer. Test reports, checksums and build metadata are saved.
- SDK bootstrap reads its pinned version from `global.json`.
- NSIS download handling tolerates an unavailable mirror while preserving the
  required archive SHA-256. No hash verification is disabled.
- English and Russian READMEs explain unsigned-installer warnings and use at
  your own risk. Build-status documentation separates current and historical
  verification.

Application functionality is unchanged from preview.6. This release does not
claim new VPN behavior or a migration of existing TrustTunnel installations.

## Important limitations

**Use at your own risk.** The installer has no Authenticode signature. Windows
may display **Unknown publisher**, and Microsoft Defender SmartScreen may warn
about or block it. A SHA-256 checksum detects changes relative to the recorded
checksum; it is not a signature or independent publisher authentication.

- A documented service-crash window between saving the engine ownership record
  and resuming its main thread may leave a suspended engine process. Automatic
  resumption is not implemented. This remains unresolved and must be addressed
  before broad distribution; prolonged ordinary use and sleep transitions do
  not cover this failure window. See [architecture notes](https://github.com/tavvet/Tunnela/blob/4a1ea1e27327ce4e3ebc71d66d68d740b67ab2d0/docs/ARCHITECTURE.md#state-and-shutdown).
- The kill switch does not guarantee a persistent network block after a client
  crash or service stop. Do not rely on this preview for that guarantee.
- Per-application routing, subscription links, automatic updates and automatic
  VPN connection at startup are not implemented. Routing rules belong to each
  profile; shared presets are unavailable.
- Only one device-wide tunnel is supported. Manually disconnect any other VPN
  before connecting Tunnela. Concurrent VPN connections are not supported.
- Full failure recovery, cross-user permissions, route/DNS cleanup and every
  kill-switch scenario have not been established by automated tests.

## Verification scope

Built from commit `4a1ea1e27327ce4e3ebc71d66d68d740b67ab2d0` on GitHub's Windows 2025 runner. [CI run 42386681233](https://github.com/tavvet/Tunnela/actions/runs/42386681233) passed: Release build with **0 warnings and 0 errors**, **152 .NET tests** (79 Core, 62 Desktop, 11 Service; none skipped), **83 isolated installer assertions**, and NSIS installer compilation.

Installer SHA-256: `9531294c8ee4573f7f1e0703b34f8ba825083422830998fddac483ef39459461`.
The EXE was checked against the matching checksum and build metadata before upload. A successful build and isolated tests do not establish real-world installation, UI or VPN safety.

The owner reports successful personal use, prolonged operation and sleep
transitions. The tested installer checksum is unknown; this is not a claim that
the exact preview.7 release asset was manually tested or that every scenario in
the [manual test plan](https://github.com/tavvet/Tunnela/blob/4a1ea1e27327ce4e3ebc71d66d68d740b67ab2d0/docs/TESTING.md) passed.

## Installation and updates

Download `Tunnela-0.1.0-preview.7-Setup-x64.exe` together with its matching
`.sha256` and `.build.json` assets. Verify the checksum before use. Before an
update, disconnect Tunnela and close its interface through the tray. The service
waits for a manual connection command. Uninstallation preserves saved profiles.
Existing TrustTunnel installations are not migrated or removed automatically.
See [installation instructions](https://github.com/tavvet/Tunnela/blob/4a1ea1e27327ce4e3ebc71d66d68d740b67ab2d0/docs/INSTALL.md).
