# Project instructions

## Product

- Build Tunnela, an independent Windows GUI for TrustTunnel for ordinary users. Development and the target OS are Windows.
- Use Tunnela for product, project, namespace, executable, service, IPC, and data-directory names. Retain TrustTunnel for the upstream VPN engine and its formats, links, releases, and version.
- Keep the Tunnela installation isolated: `%ProgramFiles%\Tunnela`, SCM service `Tunnela` (`Tunnela VPN Service`), pipe `Tunnela.Control.v1`, `%LOCALAPPDATA%\Tunnela\profiles.dat`, and `%ProgramData%\Tunnela\Service`. Publish under `.artifacts\publish\tunnela`.
- The service uses runtime adapter name `Tunnela` and log level `info`; preserve the original imported profile values in storage and export, and document the runtime overrides.
- Renaming the wrapper does not authorize migration, deletion, or stopping of any previous installation, service, process, or user data.
- Use C# / .NET 10, WPF, and a separate Windows service. Keep GUI, contracts, core logic, and the TrustTunnel integration separate.
- Keep dependencies small. Prefer a project-local SDK to a global toolchain installation. Do not install Visual Studio or C++ tooling unless needed and explicitly agreed.
- Prefer compact controls and spacing while keeping configuration forms readable. Hover and keyboard focus must not change control geometry; keep visible focus and scrolling for small windows.
- The user accepted the current compact window layout. Preserve it unless asked to change it; tray behavior and functional validation remain separate work.
- Preserve the distinction between service availability, process state, and an actually established VPN connection.
- Routing by domains, IP addresses, and CIDR is in scope. Per-application routing is not supported by the selected CLI integration.

## User testing boundary

- The user performs final application and VPN tests personally. Give clear, numbered instructions and expected results.
- UI-only preview may run separately alongside an existing client. Before a real Tunnela network test, instruct the user to disconnect the existing VPN manually and confirm its tunnel has stopped. Do not test simultaneous VPN connections.
- Do not launch the desktop app, install/start/stop a Windows service, connect a VPN, change routes/DNS/firewall rules, or trigger UAC to test the product yourself.
- Building, static analysis, and isolated automated tests that do not change the host network are allowed.
- Never describe a successful build or unit test as proof that VPN connectivity, cleanup, sleep/resume, or kill switch behavior works on the user's machine.
- Record unverified integration behavior and known limitations in the repository documentation.

## Engineering

- Never log credentials, full imported connection links, or generated TOML containing secrets.
- Do not accept arbitrary executable paths or shell commands over the service API.
- Use explicit permissions for service IPC and files containing credentials.
- Pin dependency and engine versions; preserve existing user data on failed import or save.
