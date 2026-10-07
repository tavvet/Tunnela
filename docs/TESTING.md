# Manual Tunnela testing

The user performs application, service, and real VPN tests. Automated builds and logic tests do not establish that network integration works. Follow the checks in order; if a step fails, stop at that step and save a diagnostic report. Results from the earlier build before the rename do not verify the current Tunnela revision; see [BUILD-STATUS.md](BUILD-STATUS.md) for current status.

Tunnela must not migrate, remove, or stop an existing TrustTunnel client or its data. You may inspect the interface without VPN operation alongside that client. Before the first Tunnela connection, manually disconnect the existing VPN and verify that its tunnel has stopped. Testing two simultaneous VPN connections is outside this plan.

## Already confirmed by the user

The user reported successful manual checks on their computer: profile import, service installation, connection, disconnection and reconnection, selected split-routing rules, and recovery after switching Wi-Fi off/on and after sleep/resume. The tray fix, accepted compact layout, and readable text after the revised drop-down fix were also confirmed.

For EXE package `0.1.0-preview.1`, the user confirmed repeat installation, the Tunnela Start menu entry, and launch and operation after restarting Windows. They then confirmed refusal to continue installation while the GUI was open with the VPN disconnected, without disrupting the application. A separate uninstall/reinstall test also passed: profiles/settings were preserved, and the connection worked. The user additionally checked the Services console and confirmed that uninstalling the application removes the Tunnela service. Refusal with an active tunnel and closed GUI remains unconfirmed.

On October 7, 2026, after receiving the language acceptance steps, the user reported that the localized version works. This records a successful basic manual language check on their computer, without treating every detailed step or edge case as individually confirmed.

These reports do not mean every item below has passed: crashes, all kill-switch modes, every network protocol and rule combination, cleanup of every route/DNS setting, and access between different Windows users have not been verified separately. The scenarios below remain the detailed acceptance and regression plan, including language combinations and failure cases not covered by the user's report.

UI labels below use the English interface. Equivalent Russian labels apply when Russian is selected.

## Interface without a VPN

1. Start `.artifacts\publish\tunnela\desktop\Tunnela.Desktop.exe` without elevation and without installing the Tunnela service. Expect the Tunnela name and a clear message that its own service is unavailable, with no Connected state and no connection to the earlier client's service.
2. Inspect Connection, Servers, Routing, Settings, and Diagnostics. Check the compact layout at 100% and 150% scaling, Tab navigation, readable text, and visible hover, focus, and selection states.
3. Create a profile with empty fields and try to save. Expect an explanation of the missing information.
4. Import your exported TOML or a static `tt://` link, verify the name, server address, DNS, and rules, then save. Import must not automatically connect the VPN.
5. Try an invalid configuration, a SOCKS configuration, and a subscription link. Expect an unsupported-format message; previously saved profiles must remain intact.
6. Edit a profile, navigate to another page, and return. The draft must remain. Closing the application with unsaved changes must produce a warning.
7. Restart the application. Saved settings should load from `%LOCALAPPDATA%\Tunnela\profiles.dat`, and the password must not appear in diagnostics. The earlier client's profiles must remain unchanged.
8. Check closing to the tray, reopening, and launching the EXE again. Two independent windows that overwrite the same settings must not appear.

### Interface language

The user reported that the language update works on October 7, 2026, after receiving the acceptance steps. Keep the following steps for detailed regression checks; that report does not separately confirm every combination or save-failure scenario. Perform UI-only checks without connecting a VPN; the last step can accompany a later, user-controlled connection test.

Application dialog text follows the selected language. Native Windows file-picker controls and standard confirmation button labels follow the Windows display language; these are not expected to switch with the application preference.

1. On first launch, expect English. Also check an upgrade with preferences created before the language setting existed: missing language must default to English while preserving profiles and settings. Do not edit the encrypted `profiles.dat` file to simulate this case.
2. In Settings, select Russian. Expect the current page, navigation, buttons, state labels, tray menu, and subsequently opened dialogs to change immediately, without restarting the GUI. Check a validation message and an exit/unsaved-changes dialog, then cancel the exit. Technical identifiers and user-entered profile names must stay unchanged.
3. Close the interface normally and reopen it. Russian must remain selected. Switch back to English and repeat the check. Existing profiles, passwords, rules, selected server, and unsaved edits during the switch must not be reset.
4. During a separately authorized manual Tunnela connection test, switch languages while connected. First disconnect any other VPN as required above. Expect the displayed connection state and explanatory text to use the selected language without disconnecting, reconnecting, or restarting the service or tunnel. Check the tray labels as well. Language choice must not change the actual connection state.

### Drop-down text colors

The user confirmed the fix in `.artifacts\publish\tunnela\desktop-select-fix\Tunnela.Desktop.exe`. To repeat that historical check, close the previous GUI through **Close interface…**, then start that copy. Replacing only the GUI does not require sending a VPN disconnect command. Use the current build when checking the new language changes.

1. On Connection, check the selected server and expanded list items for readable text, including hover state.
2. Repeat on Routing. Check keyboard navigation through the list and selection with Enter.
3. On Servers, expand advanced settings and check HTTP/2 and HTTP/3 in both the field and its menu. You do not need to change or save the working transport for this check.

### Tray regression check

The user accepted the window layout and confirmed correct tray behavior after the fixes. That is not separate confirmation of every edge case. For regression testing:

1. Close the window with its close button. One left click on the Tunnela icon should restore it. Repeated and double left clicks must leave only one open window. Restoring an already maximized window must not reset its size.
2. Right click should open the menu. **Open Tunnela** should restore the window; right click alone should not open it.
3. Without an installed/running service, the connect action is disabled. Choose **Disconnect and exit**: the window should appear, and the unavailable-service message should offer to close only the interface without waiting tens of seconds. **No** leaves the application open; **Yes** closes it and removes the icon.
4. Start again, edit a profile without saving, and choose **Close interface…**. A visible confirmation above the window should warn about unsaved changes. Cancel preserves the draft; confirmation exits the GUI without sending a VPN disconnect command.
5. Repeat exit with minimize-to-tray disabled: the window close button should start normal exit without hanging or opening duplicate dialogs.
6. During testing with the installed service, check **Close interface…** while a command is pending. The GUI should exit after confirmation; a command already accepted by the service may complete later. This action does not establish that the VPN disconnected. Check its state separately.

## Service without a connection

1. Install the service following [INSTALL.md](INSTALL.md). The `Tunnela` service (`Tunnela VPN Service`) should be running and waiting for a command; `%ProgramFiles%\Tunnela\service\engine\trusttunnel_client.exe` must not start before connection. A process belonging to an existing client is not Tunnela's process.
2. Open the installed GUI without elevation. It should show the service and engine availability without requesting UAC for each action.
3. Inspect versions and the state message in Diagnostics. Export a report and confirm it contains no password, original `tt://` link, or full TOML.

## Connection and disconnection

1. Manually disconnect the existing VPN and confirm its tunnel has stopped. Then record the ordinary network state. These commands are read-only:

```powershell
Get-NetIPConfiguration
Get-DnsClientServerAddress
Get-Service Tunnela
```

2. Connect to your server in Tunnela. Expect Connecting followed by a state confirmed by the engine. Process or service existence alone is not success. Do not connect the earlier VPN at the same time.
3. Check for the `Tunnela` adapter, access to a normal website, DNS, and your actual public address using your preferred method. Record whether IPv6 works if enabled in the profile.
4. Close the window to the tray and reopen it. Connection state should remain unchanged.
5. Choose **Disconnect**. Expect graceful CLI exit, removal of its PID, and restoration of ordinary network access. Compare DNS and interfaces with the baseline.
6. Repeat connection and disconnection several times. Processes, routes, and adapters must not accumulate.
7. Check the tray's **Disconnect and exit** action. If disconnection fails, the application must not hide the failure or claim that the VPN is off.

## Routing

1. Select the mode for all traffic except rule matches and add one test domain or IP. After reconnecting, that address should use the direct route while other traffic uses the VPN.
2. Select the mode for only addresses matching rules. Only the selected address should use the VPN.
3. Test a domain, its subdomains, and CIDR separately. Use server logs or appropriate network diagnostics to confirm routing: successfully loading a page does not prove which route it took.
4. Repeat the domain test with ordinary DNS and with the browser's secure DNS if you use it. Record differences; combinations of DoH, QUIC, ECH, and shared IP addresses need separate checks.

## Errors and recovery

- Incorrect password: expect an error without the password appearing in logs.
- Unavailable server: a false Connected state must not appear.
- Lost GUI-to-service communication: expect Unknown, not a claim that the VPN is off.
- Sleep/resume and Wi-Fi changes: record the engine's built-in recovery behavior. This version does not implement unlimited automatic wrapper-level restarts.
- Kill switch: test a temporary server outage and an explicit disconnect separately. Do not treat this switch as a guarantee of blocking after a process or service crash.

Perform crash tests only after successful normal shutdown tests and when prepared for temporary loss of connectivity. The application does not perform a global reset of DNS, routes, or other applications' firewall rules.

## Installer acceptance

Package under test: `.artifacts\installer\Tunnela-0.1.0-preview.2-Setup-x64.exe`, an unsigned NSIS 3.13 preview. Begin after its build is confirmed in [BUILD-STATUS.md](BUILD-STATUS.md). Building the package does not establish that acceptance has passed. Only the user performs installation, updates, uninstallation, UAC approval, reboots, and network actions. Manually disconnect the earlier VPN before network steps.

Historical result for `0.1.0-preview.1`: repeat installation, the shortcut, and operation after reboot are confirmed by the user. This covers the basic path in steps 2–4; every detail listed in those steps has not been verified separately. Refusal with an open GUI and disconnected VPN in step 5 is also confirmed. Step 7, uninstall/reinstall with preserved profiles, passed; the user separately verified service removal in the Services console. Step 6, protection of an active tunnel with the GUI closed, remains unconfirmed. The October 7 report that the localized `0.1.0-preview.2` works does not confirm that this full installer plan was repeated; installer failure/recovery and the remaining network cases are still pending.

1. **Prepare the current installation.** Save the profile and settings; note the selected server and a few non-secret parameters. Choose **Disconnect**, wait for confirmation, then choose **Close interface…** from the tray. Close every Tunnela GUI copy; also manually disconnect any earlier VPN before the network test.
2. **Install or move from the development installation.** Run the EXE and approve UAC. On first installation, enter the ordinary controller account in `DOMAIN\user` or `COMPUTER\user` format; if UAC uses a different administrator, do not substitute that administrator for your user. An update should preserve the previous controller SID. Expect `%ProgramFiles%\Tunnela`, a Start menu shortcut, and the `Tunnela` service with Automatic startup. The package must not download .NET or the engine. The earlier TrustTunnel client, its services, and its data remain unchanged.
3. **Check the installed copy.** Open Tunnela from Start without elevation. Expect an available service and the previously selected profile/settings, or an empty list for a first installation without profiles. The VPN must not connect automatically. After checking parameters, connect and disconnect manually; existing credentials should work without reimporting.
4. **Reboot.** With the VPN disconnected, manually restart Windows and open Tunnela from Start under the same controller account. Expect the service to start automatically and remain idle, control without UAC, and no tunnel process before a manual connect command. The saved profile should remain available.
5. **Block an update while the GUI is open.** With the VPN disconnected, leave Tunnela open and run the installer again. Expect a request to close the application before any service stop or file replacement. Installation should become available after closing through the tray.
6. **Block an update while the VPN is active.** Connect only Tunnela and choose **Close interface…**, keeping the tunnel running. Run the installer: it should refuse to continue until the VPN is manually disconnected, without stopping the service, replacing files, or breaking the tunnel. Reopen the GUI, disconnect manually, and close it. Updating should then be available; profiles, settings, and the controller account should be preserved.
7. **Optional uninstall/reinstall check.** Perform this after the basic steps pass. Manually disconnect the VPN, close the GUI, and uninstall Tunnela through the Windows installed-apps list. Expect removal of the program, shortcuts, and service while preserving `%LOCALAPPDATA%\Tunnela\profiles.dat`; the earlier TrustTunnel client must remain untouched. After reinstalling with the same ordinary Windows account, profiles and settings should return. Do not share the password or contents of `profiles.dat`.

## Reporting a problem

Provide the step number, expected and actual behavior, Windows version, error text, and the application's diagnostic report. Do not send a password, full connection link, `profiles.dat`, or runtime `active.toml`.
