# Tunnela 0.1.0-preview.7

Windows x64 prerelease of the independent desktop interface for TrustTunnel.
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

See [build status](BUILD-STATUS.md) and the successful CI run linked in the
published release for exact source and automated results. A successful build
and isolated tests do not establish real-world installation, UI or VPN safety.

The owner reports successful personal use, prolonged operation and sleep
transitions. The tested installer checksum is unknown; this is not a claim that
the exact preview.7 release asset was manually tested or that every scenario in
the [manual test plan](TESTING.md) passed.

## Installation and updates

Download `Tunnela-0.1.0-preview.7-Setup-x64.exe` together with its matching
`.sha256` and `.build.json` assets. Verify the checksum before use. Before an
update, disconnect Tunnela and close its interface through the tray. The service
waits for a manual connection command. Uninstallation preserves saved profiles.
Existing TrustTunnel installations are not migrated or removed automatically.
See [installation instructions](INSTALL.md).
