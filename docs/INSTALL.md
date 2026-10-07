# Installing Tunnela on Windows x64

Tunnela is an independent wrapper for the TrustTunnel engine. Installation uses a single NSIS 3.13 EXE package: `.artifacts\installer\Tunnela-0.1.0-preview.3-Setup-x64.exe`. This is an unsigned preview. Build results and verification status are recorded separately in [BUILD-STATUS.md](BUILD-STATUS.md); these instructions alone do not establish release readiness. The user performs installation, UAC approval, VPN operation, and reboot tests.

Tunnela has its own names and directories. An existing TrustTunnel client, its service, and its data are not automatically migrated, deleted, or stopped. You can inspect the Tunnela interface without connecting alongside an existing client. Before a real network test, manually disconnect the existing VPN and confirm that its tunnel has stopped; simultaneous VPN connections are not tested.

## Moving from the development installation to the EXE package

Follow these steps after confirming the package build. You do not need to remove the existing Tunnela installation or reimport its profiles before updating.

1. Save profile changes. In Tunnela, choose **Disconnect** and wait for confirmation. Then choose **Close interface…** from the tray. Close every Tunnela GUI instance, including copies started from the working directory. Closing only the interface does not disconnect the VPN, so the order matters.
2. For a first installation, identify the ordinary Windows account that will control Tunnela. Run `whoami` in a normal PowerShell session to obtain its name. The installer asks for an explicit account name in `DOMAIN\user` or `COMPUTER\user` format. If UAC uses another administrator's credentials, the controller must still be your ordinary account. Updating an existing installation retains its saved controller SID.
3. Run `Tunnela-0.1.0-preview.3-Setup-x64.exe` and approve UAC. The package contains the application, service, .NET runtime, TrustTunnel engine, and Wintun; installation requires neither component downloads nor a separate .NET installation.
4. Files are installed under `%ProgramFiles%\Tunnela`. The `Tunnela` service (`Tunnela VPN Service`) runs as LocalSystem, uses **Automatic** startup, and starts idle, waiting for a command. Installation and Windows startup do not automatically connect the VPN.
5. Open Tunnela from the Start menu under the ordinary controller account, without elevation. Expect an available service and existing profiles from `%LOCALAPPDATA%\Tunnela\profiles.dat`. No profile connects automatically. Check the selected server and rules before connecting manually.
6. After the basic check, manually disconnect the VPN and restart Windows. Expect the service to start automatically and remain idle; after opening the GUI from Start, the same user should retain control, and the VPN should remain disconnected until requested.

The installer must refuse to continue while a Tunnela GUI or its own tunnel process is running, **before stopping the service or replacing files**. Close Tunnela normally and retry. It must not force the application to close, disconnect the VPN, or stop another TrustTunnel client. User profiles are preserved on update and uninstall; data from an earlier product is not automatically migrated.

## Building the EXE installer

The NSIS **3.13** compiler is kept inside the project at `.tools\nsis-3.13\makensis.exe`. No global NSIS installation, Visual Studio, or C++ toolchain is needed. Run from a normal PowerShell session in the repository root:

```powershell
.\scripts\bootstrap-sdk.ps1
.\scripts\bootstrap-installer.ps1
.\scripts\build-installer.ps1
```

The first script prepares the local .NET 10.0.401 SDK; this step is required in a fresh checkout. The second prepares the pinned portable NSIS compiler. `build-installer.ps1` checks the compiler, automatically prepares the pinned engine files through `prepare-engine.ps1`, and builds `.artifacts\installer\Tunnela-0.1.0-preview.3-Setup-x64.exe`. It uses fresh, isolated publish output to avoid replacing a running GUI copy in the main publish directory.

The package includes Tunnela's MIT license and licenses for TrustTunnel, Wintun, Tomlyn, and the included .NET runtime components. Tunnela's license is also copied to `licenses/TUNNELA_LICENSE.txt` in standalone Desktop and Service build/publish output. Preparing the SDK, compiler, and engine, and restoring build dependencies may require internet access; installing the completed EXE does not download components.

These commands build the package without installing it. The user checks installation and updates using [TESTING.md](TESTING.md#installer-acceptance).

## Building the application for development

Run from a normal PowerShell session in the repository root:

```powershell
.\scripts\bootstrap-sdk.ps1
.\scripts\build.ps1 -Publish
```

The first script downloads .NET 10.0.401 x64 and checks SHA-512 against Microsoft metadata. The SDK is stored in `.tools\dotnet`. The second restores packages, builds the projects, runs isolated tests, and publishes the application with .NET into `.artifacts\publish\tunnela\desktop` and `.artifacts\publish\tunnela\service`. No global tools are installed. See [BUILD-STATUS.md](BUILD-STATUS.md) for verification of the current revision.

If PowerShell policy prevents local scripts from running, use a separate process without changing the persistent policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Publish
```

## Inspecting the interface without the service

Start it manually:

```powershell
.\.artifacts\publish\tunnela\desktop\Tunnela.Desktop.exe
```

You can inspect pages, create a profile, import configuration, and edit rules. If the Tunnela service is not installed, communication with it will be unavailable. Choosing **Connect** must not report a false success. This preview does not require stopping an existing VPN.

English is the default interface language, including when an existing preferences file has no language field. Russian can be selected in Settings; the choice takes effect immediately and is stored in encrypted user preferences. Switching the language must not reconnect the tunnel or alter profiles. On October 7, 2026, the user reported that the localized version works after receiving the acceptance steps. See the [language checks](TESTING.md#interface-language) for detailed regression scenarios and the limits of that confirmation.

Application dialog text follows the selected language. Native Windows file-picker controls and standard confirmation button labels follow the Windows display language.

Tunnela profiles are stored in `%LOCALAPPDATA%\Tunnela\profiles.dat`, encrypted with DPAPI and tied to the current Windows account. Copying that file to another computer or account is not a profile migration method. Earlier user storage is not automatically read or changed; the user imports the required connection. Full configurations and connection links contain secrets.

## Preparing the engine

The engine is prepared automatically when building the EXE package. To prepare it separately for a development installation, run from normal PowerShell:

```powershell
.\scripts\prepare-engine.ps1
```

The script uses the Windows x64 CLI **v1.1.7** archive from the local cache or downloads it from the [official TrustTunnel release](https://github.com/TrustTunnel/TrustTunnelClient/releases/tag/v1.1.7). Before extraction, it checks the pinned SHA-256 of the archive, then checks fixed SHA-256 values for `trusttunnel_client.exe` and the included `wintun.dll`. Arbitrary files with the same names are not accepted. A separate Wintun download is not required.

Both binaries, `TRUSTTUNNEL_LICENSE.txt`, `WINTUN_LICENSE.txt`, and `provenance.json` with sources and checksums are saved in `runtime\engine`. Preparation does not execute the CLI; the archive's `setup_wizard.exe` is not included in the package. The engine version is pinned because connection-state recognition depends on its log format.

Pinned SHA-256 values:

- `trusttunnel_client-v1.1.7-windows-x86_64.zip`: `aff81ff4ebcc00d9ede7f29d0fbb56d277d784168080513074ffb8f357ad3796` ([release](https://github.com/TrustTunnel/TrustTunnelClient/releases/tag/v1.1.7)).
- `trusttunnel_client.exe`: `86a5992d4f841dcf605da374d2d74bab035fb2c0d9b32719bdd9f80cfdd56c68`.
- `wintun.dll`: `e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce`.

Manual preparation is an alternative for development: download the same official archive, verify its SHA-256, and extract `trusttunnel_client.exe`, `wintun.dll`, `LICENSE.txt`, and `WINTUN_LICENSE.txt` into `runtime\engine`, renaming `LICENSE.txt` to `TRUSTTUNNEL_LICENSE.txt`. Verify both binary checksums against the list above. When building the EXE, `prepare-engine.ps1` still verifies the pinned archive and generates `provenance.json`. The `runtime\engine` directory is excluded from Git.

Do not install the CLI's own built-in service: this application uses its separate `Tunnela` service with display name `Tunnela VPN Service`.

If the source TOML is in a protected directory belonging to an existing client, the non-elevated GUI may be unable to read it. Copy the file from an elevated PowerShell session into an accessible directory, then import the copy through **Servers → Import TOML**. Do not change the original file or its ACL. The copy contains credentials; after successful import and profile save, it can be deleted. Do not add these files to Git or include them in diagnostic reports.

## Installing the service with PowerShell for development

This is the earlier development installation method. Use the migration steps above to test the new EXE package.

Close the Tunnela window and tray application. Open PowerShell **as administrator**, change to the repository root, and run:

```powershell
.\scripts\install-dev.ps1 -EngineDirectory .\runtime\engine
```

The script copies the build to `%ProgramFiles%\Tunnela\desktop` and `%ProgramFiles%\Tunnela\service`, applies file permissions, registers the `Tunnela` service as LocalSystem, and starts it idle. The service executable is `Tunnela.Service.exe`. Its startup type is Manual. Installation does not connect the VPN. Installations and services with earlier names are unchanged.

When connecting, the service uses network adapter name `Tunnela` even if the imported profile has another `device_name`. It also uses log level `info` to recognize state transitions. The original values remain in the profile and its normal export; overrides apply only to the runtime configuration. You must still disconnect the existing VPN before a Tunnela network test.

Control is granted to the SID of the user running the installer. If elevation uses a different account, first obtain your ordinary account's SID with `whoami /user` and pass it explicitly:

```powershell
.\scripts\install-dev.ps1 -EngineDirectory .\runtime\engine -ControllerSid 'S-1-5-21-...'
```

Replace the placeholder with the actual SID. After installation, start the GUI **without elevation**:

```powershell
& "$env:ProgramFiles\Tunnela\desktop\Tunnela.Desktop.exe"
```

If the service was installed by the earlier `install-dev.ps1` with Manual startup, start it manually from elevated PowerShell after a reboot. This does not apply after installing the new EXE package, which uses Automatic startup:

```powershell
Start-Service Tunnela
```

## Uninstalling the EXE installation

After successful basic checks, if needed, disconnect Tunnela, wait for the tunnel to stop, and close the GUI through the tray. Uninstall Tunnela through the Windows installed-apps list. Expect only the Tunnela program, shortcuts, and service to be removed; `%LOCALAPPDATA%\Tunnela\profiles.dat` remains. On reinstallation, select the same ordinary Windows account so the GUI can read its DPAPI profiles. Exporting passwords is not required for this step.

## Updating and removing the development installation

First disconnect the VPN in Tunnela and close Tunnela through the tray menu. Rebuild and run `install-dev.ps1` again. If graceful shutdown fails, the installer must abort the update. Only the Tunnela installation is updated; the earlier wrapper is not migrated. This development installer does not implement transactional update rollback.

To remove it from elevated PowerShell:

```powershell
.\scripts\uninstall-dev.ps1
```

The script stops and removes only the `Tunnela` service and `%ProgramFiles%\Tunnela` directory. It must not continue removal while a Tunnela tunnel process remains. Encrypted user profiles are preserved. An earlier installation, its services, and its data are not removed.

The service temporarily creates `%ProgramData%\Tunnela\Service\active.toml` with the plaintext credentials required by the CLI. Access is limited to SYSTEM and administrators. Normal service/tunnel shutdown cleans up its own runtime files. The earlier service-data directory is not used. Crash behavior requires separate verification.
