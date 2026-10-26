# Tunnela architecture

Tunnela is an independent Windows wrapper for the TrustTunnel engine. The GUI and service run in separate processes. The service manages one machine-wide tunnel. The connection does not belong to the GUI window and must not stop when the window closes to the tray.

| Project | Responsibility |
| --- | --- |
| Tunnela.Contracts | Profiles, states, and version 1 IPC commands and responses |
| Tunnela.Core | Profile and rule validation, TOML/link import, configuration generation |
| Tunnela.Desktop | WPF, tray, encrypted user storage, IPC client |
| Tunnela.Service | Installation validation, IPC server, CLI process, limited diagnostics |
| Tunnela.Core.Tests | Isolated tests without launching the application or changing the network |
| Tunnela.Desktop.Tests | Model, localization, report formatting, and in-memory WPF control checks |
| Tunnela.Service.Tests | Pipe accept loop on private local pipes and protected-folder checks in a temporary directory |

## Reading the C# code

Start with these files when following a user action through the application:

| Entry point | What to read there |
| --- | --- |
| [App.xaml.cs](../src/Tunnela.Desktop/App.xaml.cs) | GUI startup, loading preferences, and choosing the initial language |
| [MainWindow.xaml.cs](../src/Tunnela.Desktop/MainWindow.xaml.cs) | User actions, service requests, profile saves, and the asynchronous exit flow |
| [UiModel.cs](../src/Tunnela.Desktop/UiModel.cs) / [ProfileEditor.cs](../src/Tunnela.Desktop/ProfileEditor.cs) | Bindable connection state and editable drafts; `Source` retains fields the form does not expose |
| [TrayController.cs](../src/Tunnela.Desktop/TrayController.cs) | Native icon/menu ownership; callbacks are scheduled by the window on its WPF dispatcher |
| [DiagnosticReportBuilder.cs](../src/Tunnela.Desktop/DiagnosticReportBuilder.cs) | Report formatting and literal redaction for saved profiles and drafts, without file or service access |
| [ServiceClient.cs](../src/Tunnela.Desktop/ServiceClient.cs) / [ControlPipeWorker.cs](../src/Tunnela.Service/ControlPipeWorker.cs) | The two ends of the authenticated, bounded IPC exchange |
| [EngineSupervisor.cs](../src/Tunnela.Service/EngineSupervisor.cs) | Serialized connect/disconnect commands and state updates from process observers |
| [ConsoleEngineProcess.cs](../src/Tunnela.Service/ConsoleEngineProcess.cs) | Native process launch, ownership records, and the one-shot graceful stop helper |
| [Models.cs](../src/Tunnela.Contracts/Models.cs) | Shared data and wire contracts, including routing rules and the different roles of diagnostic and localization codes |

For a connection request, follow `ToggleConnectionAsync` → `ServiceClient.SendAsync` → `ControlPipeWorker` → `EngineSupervisor` → `ConsoleEngineProcess`. Polling and recognized engine messages update the displayed state separately; successful process creation alone does not establish a VPN connection.

For profile edits, `ProfileEditor.ToProfile` applies the draft to its saved `Source` using a record copy. `MainWindow.Persist` updates the in-memory saved collection only after `ProfileStore.Save` commits the encrypted replacement. The form's unsaved draft remains separate until then.

## Names and installation isolation

| Component | Name or location |
| --- | --- |
| Solution | `Tunnela.slnx` |
| Published output | `.artifacts\publish\tunnela\desktop` and `.artifacts\publish\tunnela\service` |
| Installed GUI | `%ProgramFiles%\Tunnela\desktop\Tunnela.Desktop.exe` |
| Installed service | `%ProgramFiles%\Tunnela\service\Tunnela.Service.exe` |
| SCM service name | `Tunnela`, display name `Tunnela VPN Service` |
| Named pipe | `Tunnela.Control.v1` |
| Runtime network adapter name | `Tunnela` |
| User profiles | `%LOCALAPPDATA%\Tunnela\profiles.dat` |
| Service data | `%ProgramData%\Tunnela\Service` |

Project names and namespaces use the `Tunnela` prefix. TrustTunnel remains the name of the upstream engine, its configuration format, links, and version. Earlier installations and data are not migrated automatically; renaming does not stop existing services or processes. The interface can be inspected alongside an existing client. Before a Tunnela network test, the user must manually disconnect the existing VPN. Concurrent tunnels are not supported or tested.

## Processes and privileges

The GUI runs as an ordinary user. The `Tunnela` service runs as LocalSystem; its files are under `%ProgramFiles%\Tunnela` and writable only by administrators and SYSTEM. A specific controller SID is selected during installation. Communication uses the local `Tunnela.Control.v1` named pipe with an explicit ACL, fixed commands, and bounded message size. Before sending a profile, the GUI compares the pipe server PID with the running `Tunnela` service PID from Windows Service Control Manager.

The CLI is not registered as a second Windows service. The service starts the pinned EXE from its own `engine` directory. IPC does not accept executable paths or shell commands. EXE packaging verifies fixed SHA-256 hashes for the official CLI archive, CLI binary, and included Wintun DLL. The protected installation manifest records the binary hashes, which the service checks before use. Hash consistency does not independently prove publisher authenticity.

## State and shutdown

VPN state is derived from known messages emitted by the pinned CLI v1.1.7. This is compatibility with a particular log format, not a stable external API. A running process is not treated as proof of a connection. Unconfirmed state is displayed separately.

At startup, the wrapper sets log level `info`, which is needed to observe states, and the network adapter name `Tunnela`. These are overrides in the service's runtime configuration: the original `LogLevel` and `DeviceName` remain unchanged in stored profiles and normal exports. A separate adapter name does not isolate routes and DNS when two VPN clients run concurrently. The diagnostics page shows wrapper events and recognized state transitions; full technical CLI logs are not retained. More detailed classification of authentication and certificate failures is a later step.

Graceful shutdown uses a service helper that verifies process ownership and sends a console CTRL_C_EVENT. Normal disconnection must not be replaced by forced process termination. The user has confirmed ordinary disconnect/reconnect behavior on their computer. Detailed verification that the CLI receives the signal and restores every route and DNS setting remains a separate Windows integration check.

If this approach fails the remaining integration checks, the official Windows adapter API is an alternative. Its beta status, binary-signing contract, and lack of standalone boot auto-connect must be assessed separately. The UI and profile model can be retained.

A crash window remains between saving the process ownership record and resuming the process's main thread. If the service fails at that exact point, a suspended process may remain. Automatic resumption of such a process is not implemented. This failure scenario needs a separate solution before broad distribution; successful ordinary connection tests do not cover it.

## Starting with Windows

The service starts at boot through its Automatic SCM start type and waits idle. The GUI start at sign-in is a separate per-user choice, stored only as the `Tunnela` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so Task Manager and Windows Settings can turn it off. [WindowsStartup.cs](../src/Tunnela.Desktop/WindowsStartup.cs) reads and writes that single value, and [StartupSetting.cs](../src/Tunnela.Desktop/StartupSetting.cs) keeps the Settings option in step with it; after a change it re-reads the entry only once the checkbox binding has finished updating. Only the installed copy in `%ProgramFiles%\Tunnela\desktop` can register itself, always with that fixed path and `--tray`.

With `--tray`, [App.xaml.cs](../src/Tunnela.Desktop/App.xaml.cs) creates the window without showing it and starts status polling, so the tray reflects the service at once. Each GUI creates a per-session, per-user show event before taking the single-instance mutex. A later start without `--tray` passes its foreground right on with `AllowSetForegroundWindow`, signals that event and exits, and the running copy opens its window; without that right Windows would only flash the taskbar button. A later `--tray` start exits silently. If the running copy is in another Windows session, the "already running" message is shown instead. These decisions are in [SingleInstance.cs](../src/Tunnela.Desktop/SingleInstance.cs). The event carries no command other than showing the window.

The uninstaller backend removes the `Tunnela` Run value for the account that approved UAC and, through `HKEY_USERS`, for the recorded controller SID when that hive is loaded. It compares only the quoted executable path, so the GUI's command arguments can change without breaking cleanup.

## Configuration and secrets

Application profiles are the source of truth; TOML is generated to start the CLI. Unsupported import fields are rejected with an explanation to avoid silently losing settings. Certificate verification is never disabled automatically.

The entire JSON document in user storage is encrypted with DPAPI CurrentUser. The service's temporary TOML contains plaintext secrets because the CLI requires that format; access is limited to SYSTEM and administrators. This distinction is not hidden behind a general claim of encrypted storage.

Interface language is a user preference stored with the encrypted preferences. English is the default, including for existing preferences without a language field. Selecting Russian applies immediately and persists across GUI restarts. Changing the language must not modify profiles, restart the service, or reconnect the tunnel. Manual acceptance of the language changes is tracked separately from earlier application and VPN tests.

Diagnostics must contain only sanitized events. Original TOML and connection links are neither logged nor included in reports.

## Next steps

- Complete the remaining manual shutdown, network restoration, and failure scenarios; preserve the distinction between user-confirmed cases and untested combinations.
- Verify edge cases involving GUI exit, network changes, sleep, and service failure.
- Separate reusable routing profiles from server profiles.
- Sign the installer, verify supplied component signatures, and test recovery from installation and update failures. The current NSIS EXE implements best-effort rollback; that is not a guarantee of recovery under every failure.
- After validating engine behavior, add a consistent auto-connect policy. Starting the GUI in the tray at sign-in exists; it never connects the VPN.

The UI, a successful build, and configuration conversion tests do not replace these checks. See [build status](BUILD-STATUS.md) and [manual testing](TESTING.md) for current results and limitations.
