# Windows continuous integration

[Windows build](https://github.com/tavvet/Tunnela/actions/workflows/windows-build.yml)
checks pushes to `main` and `codex/**`, pull requests targeting `main`, and manual
runs from the Actions tab. It uses a fresh Windows 2025 runner, Windows PowerShell
5.1, and the project-local SDK selected by `global.json`.

The job calls the same `scripts/build-installer.ps1` used locally. It restores and
builds the solution with warnings as errors, runs all isolated .NET tests and
installer assertions, then downloads the hash-verified pinned engine and NSIS,
publishes the self-contained Windows x64 applications, and compiles the unsigned
installer. No desktop app, installer, Windows service or VPN is launched.

## Downloading a build

1. Open a successful workflow run for the intended commit.
2. Download `Tunnela-win-x64-<commit SHA>` from **Artifacts** and extract it.
3. Compare `Get-FileHash .\Tunnela-*-Setup-x64.exe -Algorithm SHA256` with the
   adjacent `.sha256` file before use. The `.build.json` file records packaging
   hashes and the engine version. A checksum detects corruption; it is not a
   publisher signature.
4. Follow [installation instructions](INSTALL.md) and the user's
   [manual test plan](TESTING.md). CI does not verify connectivity, rendered UI,
   sleep/resume, cleanup, or kill-switch behavior.

Artifacts expire after 14 days. TRX test reports are uploaded even when a later
step fails; a restore/build failure can occur before any report exists. Packaging
or download failures leave the workflow failed and do not upload an installer.

The workflow has read-only repository permissions, uses commit-pinned actions,
and does not need repository secrets. It never creates tags, publishes GitHub
Releases, merges changes, signs binaries, or deploys an installation. Release
publication remains a separate deliberate action after reviewing the exact
commit, CI results and manual checks. A workflow definition is not evidence of a
successful run: inspect its run status before distributing its output.

## SDK and language maintenance

The code already targets .NET 10 / C# 14, with nullable analysis, deterministic
builds and warnings treated as errors in `Directory.Build.props`. This pipeline
does not change runtime behavior or downgrade the language to an older version.
Update `global.json` to select a new stable .NET 10 SDK; `bootstrap-sdk.ps1` reads
that version by default, avoiding a second hard-coded default. Its explicit
`-Version` override remains available for SDK bootstrap diagnostics. Bootstrap
still verifies the archive against Microsoft's release metadata before extraction.

For local TRX reports, run:

```powershell
.\scripts\build.ps1 -TestResultsDirectory "$PWD\.artifacts\test-results"
```
