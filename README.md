# Tunnela

**English** · [Русский](README.ru.md)

A Windows desktop app for connecting to your own [TrustTunnel](https://github.com/TrustTunnel/TrustTunnel) server.

Tunnela adds a compact interface, a system tray menu, and a Windows service to the official TrustTunnel CLI. Import a configuration, choose how to route your traffic, and connect. The VPN server and connection credentials are supplied by you.

**Status:** early preview. The installer is currently unsigned. See [verification status](docs/BUILD-STATUS.md) for tested scenarios and remaining limitations.

## Features

- **Server profiles:** create and edit connections, import supported TOML configurations or static `tt://` links.
- **Split tunneling:** route all traffic except selected destinations, or route only selected destinations through the VPN. Rules support domains, IP addresses, CIDR subnets, and supported port expressions.
- **Connection settings:** HTTP/2 or HTTP/3, DNS servers, IPv6 support, and server certificates.
- **Background connection:** a separate Windows service manages the tunnel; the tray menu provides connection and exit controls.
- **Clear status:** service availability, a running client process, and a confirmed VPN connection are shown separately.
- **Local profiles:** saved credentials and preferences are encrypted with Windows DPAPI for your Windows account.
- **English and Russian:** English is the default. Change **Settings → Interface language** at any time; the choice applies immediately and is saved automatically.
- **Diagnostics:** view service events and export a report that excludes profile configurations and credentials.

## Getting started

You need Windows 10 or later on x64, administrator rights to install the service, and a working TrustTunnel server configuration. The installed app includes its .NET runtime and VPN engine; no separate .NET installation is needed.

Look for packaged builds in [Releases](https://github.com/tavvet/Tunnela/releases). If no installer is available, use the [build instructions](#build-from-source) below.

1. If updating Tunnela, disconnect its VPN and close the app through the tray menu before running the installer.
2. Run the setup EXE. On first installation, confirm the Windows account that will control the VPN. Updates preserve this account and your saved profiles.
3. Open **Tunnela** from the Start menu as your ordinary Windows user.
4. In **Servers**, choose **Import TOML**, **Paste configuration**, or **Add**. Review the settings and select **Save**.
5. Adjust the selected profile's rules in **Routing**, if needed.
6. Before connecting, manually disconnect any other VPN and wait for its tunnel to stop. Open **Connection**, select your server, and choose **Connect**.

The service starts with Windows but waits for a manual connection command. Closing the window to the tray keeps the tunnel running. Use **Disconnect and exit** to disconnect and quit; **Close interface…** closes only the GUI.

For installation, updates, and removal, see [INSTALL.md](docs/INSTALL.md). Uninstalling Tunnela preserves saved profiles. An existing TrustTunnel installation and its data are not automatically migrated or removed.

## Current limitations

- Routing by application is not supported by this CLI integration.
- Subscription links, automatic updates, and automatic VPN connection at startup are not implemented.
- The CLI's kill switch does not provide a guaranteed persistent block after a client crash or service stop. Failure scenarios still require further testing.
- Routing rules belong to individual server profiles. There are no shared routing presets yet.

Tunnela manages one device-wide tunnel. Concurrent VPN connections are outside the supported test plan. See the [manual test plan](docs/TESTING.md) for details.

## Build from source

Development and builds run on Windows. The stack is C# / .NET 10, WPF, and a separate Windows service. The scripts use a project-local SDK; Visual Studio and C++ tools are not required.

In PowerShell, clone the repository and build the installer:

```powershell
git clone https://github.com/tavvet/Tunnela.git
cd Tunnela
.\scripts\bootstrap-sdk.ps1
.\scripts\build-installer.ps1
```

The installer build runs isolated tests, prepares the pinned TrustTunnel CLI **1.1.7** and Wintun **0.14.1**, and uses a project-local NSIS **3.13** compiler. The SDK version is pinned in [global.json](global.json). The first build needs internet access to download dependencies; installation from the completed EXE works offline.

The current output is:

```text
.artifacts\installer\Tunnela-0.1.0-preview.2-Setup-x64.exe
```

To build, test, and publish the desktop and service separately:

```powershell
.\scripts\build.ps1 -Publish
```

Outputs are placed in `.artifacts\publish\tunnela\desktop` and `.artifacts\publish\tunnela\service`. These commands do not install the application, start a service, or connect a VPN. Toolchains, dependencies, and build output stay in ignored project directories.

## Documentation and project structure

| Location | Contents |
| --- | --- |
| [Installation](docs/INSTALL.md) | Setup, updates, local builds, and removal |
| [Architecture](docs/ARCHITECTURE.md) | Process boundaries, service permissions, and data handling |
| [Testing](docs/TESTING.md) | Manual application and network checks |
| [Build status](docs/BUILD-STATUS.md) | Automated results and user-confirmed behavior |
| [`src/Tunnela.Desktop`](src/Tunnela.Desktop) | WPF interface, tray, localization, and encrypted profile storage |
| [`src/Tunnela.Service`](src/Tunnela.Service) | Windows service and TrustTunnel process management |
| [`src/Tunnela.Core`](src/Tunnela.Core) | Import, validation, routing, and configuration generation |
| [`src/Tunnela.Contracts`](src/Tunnela.Contracts) | Shared models and service protocol |
| [`tests`](tests) | Isolated application and installer tests |

Detailed documentation is currently in English. When reporting a problem, include the app version, steps to reproduce it, and a reviewed diagnostic report. Do not post passwords, complete connection links, or original TOML files.

## Upstream projects

Tunnela is an independent wrapper. TrustTunnel remains the VPN engine, and Wintun provides the Windows tunnel driver. Third-party licenses are included in the installer.

- [TrustTunnel](https://github.com/TrustTunnel/TrustTunnel)
- [TrustTunnel Client](https://github.com/TrustTunnel/TrustTunnelClient)
- [Wintun](https://www.wintun.net/)

## License

Tunnela's own code is distributed under the [MIT License](LICENSE). Third-party components retain their respective licenses; their notices are included in the installer alongside Tunnela's license.
