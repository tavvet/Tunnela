# Tunnela Windows service integration candidate

The user has reported successful installation, connection, disconnection, reconnection, selected
routing rules, recovery after toggling Wi-Fi, and sleep/resume on their computer. These reports
do not cover every network, crash, cleanup, or access-control scenario described below. Build and
isolated parser tests do not verify host integration. See [build status](../../docs/BUILD-STATUS.md)
and the [manual test plan](../../docs/TESTING.md) for the scope of confirmed and pending checks.
The user performs application, service, and VPN tests after reviewing the installation steps.

## Process and connection state

The service runs as LocalSystem, accepts only the documented JSON commands, and starts the
fixed `engine/trusttunnel_client.exe` from its installed directory. It does not register that
CLI as a second Windows service. Installation configuration pins engine version `1.1.7` and
SHA-256 values for both the executable and adjacent `wintun.dll`. The EXE packaging flow checks
fixed hashes for the official release archive and both binaries. Runtime checks verify consistency
with the protected installation manifest; hashes alone do not independently prove publisher authenticity.

Tunnela uses its own SCM name `Tunnela` (display name `Tunnela VPN Service`), pipe
`Tunnela.Control.v1`, and installation directory `%ProgramFiles%/Tunnela`. Its helper is
`service/Tunnela.Service.exe` and GUI is `desktop/Tunnela.Desktop.exe`. The install/uninstall
scripts only manage these identities and never migrate, stop or delete an existing official
TrustTunnel installation or an earlier prototype installation. Upstream CLI and Wintun
filenames and version pins are unchanged.

The service owns the runtime adapter name: it exports a copy of the selected profile with
`device_name = "Tunnela"` and `loglevel = "info"`. The imported `device_name` and log level stay
unchanged in the stored profile and normal profile exports; they are not used for the service's
temporary runtime TOML. This prevents the empty-name upstream fallback from selecting
`TrustTunnel (<hostname>)`. In v1.1.7 the Wintun adapter GUID is derived from the namespace
`VpnLibsTunnels` and the adapter name, so the Tunnela name also produces its own deterministic
adapter GUID. The engine opens an existing adapter with that name first. This name separation
does not establish that two VPN clients can run concurrently: routes, DNS and firewall behaviour
remain shared host concerns and concurrent operation is not supported or tested here.

CLI process existence is reported as `ProcessId`. A connection is reported as `Connected` only
after a recognised `VPNCORE raise_state` INFO line containing `VPN_SS_CONNECTED`. All six
upstream states are recognised, including recovery and waiting for the network. A terminal
disconnected event immediately removes the Connected state, even if the process still exists.
An unrecognised state token or a lost output stream produces Unknown. An initial connection
unconfirmed for 30 seconds becomes Unknown; the service does not kill or restart the engine.

These log emissions are not a versioned IPC API. Their actual appearance and timely delivery
must be verified with the pinned Windows binary. If the engine's logger changes, the parser
must be reviewed before its version/hash pins are updated. There is no independent traffic,
DNS, public-IP or endpoint health probe in this candidate.

Inspected source:

- [CLI signal handling and state callback, v1.1.7](https://github.com/TrustTunnel/TrustTunnelClient/blob/v1.1.7/trusttunnel/src/trusttunnel_client.cpp)
- [All state emissions, v1.1.7](https://github.com/TrustTunnel/TrustTunnelClient/blob/v1.1.7/core/src/vpn_fsm.cpp)
- [VPNCORE logger and log_vpn macro](https://github.com/TrustTunnel/TrustTunnelClient/blob/v1.1.7/core/src/vpn_manager.h)
- [Wintun adapter name and derived GUID](https://github.com/TrustTunnel/TrustTunnelClient/blob/v1.1.7/net/src/os_tunnel_win.cpp)
- [Logger framing, v8.1.52](https://github.com/AdguardTeam/NativeLibsCommon/blob/v8.1.52/common/logger.cpp)
- [Function-name prefix in logging macros](https://github.com/AdguardTeam/NativeLibsCommon/blob/v8.1.52/common/include/common/logger.h)

## Graceful stop and remaining integration checks

The engine is launched suspended into its own hidden console with redirected stdout/stderr.
The service writes a protected ownership record before resuming the process. A separate,
short-lived copy of the service executable attaches to that console and sends CTRL_C_EVENT.
It validates LocalSystem identity, the random session token, PID, process creation time and
the fixed executable path. It accepts no caller-supplied PID or executable path.

CTRL_BREAK_EVENT is not used: the pinned CLI registers SIGINT and SIGTERM, not SIGBREAK.
Successful signal delivery is not successful disconnect. The service waits for process exit
for at most 15 seconds. On timeout it retains the ownership record, reports Unknown and does
not force termination. The helper persists a signal-attempt marker before sending CTRL_C.
A retry, even after a service restart, waits for exit without sending another CTRL_C, since the
CLI signal handler resets its handlers to default. If signal delivery itself fails after marking
the attempt, automatic retransmission is deliberately disabled and the state remains Unknown.

There is no Job Object with kill-on-close and no forced termination of a running engine.
`TerminateProcess` is used only if creation fails while the new process is still suspended,
before any engine code has executed. On service failure an engine may outlive its supervisor.
A later service startup recognises the protected ownership record, reports Unknown and allows
graceful disconnect; it cannot regain that process's previous output stream. An engine without
a verifiable ownership record is never signalled automatically or replaced by a second engine.

Normal application window closure does not stop the service or VPN. Normal service shutdown
requests the same bounded graceful stop. Abrupt OS shutdown, service failure, stop timeouts,
route/DNS cleanup and kill-switch behaviour require explicit user testing. A process exit alone
does not prove that all Windows network state was restored.

## IPC, files and diagnostics

The pipe has explicit SYSTEM, Administrators and configured controller-account permissions.
NETWORK is denied. The controller cannot create another server instance. A first-instance guard
and a continuously retained server handle protect the pipe name. At most eight requests run
concurrently; connect/disconnect are serialized. Frames are one UTF-8 JSON message plus newline,
limited to 256 KiB, with a three-second read deadline. Only `status`, `logs`, `connect`, and
`disconnect` exist. Version mismatch and unknown commands are rejected.

All executable paths, runtime paths and helper arguments are chosen by the service. The service
refuses interactive/uninstalled startup. It requires a protected Program Files installation,
trusted owners, no writable non-administrator ACLs and no reparse points in protected paths.
`service-install.json` contains `ControllerSid`, `EngineVersion`, `EngineSha256`, `WintunSha256`.

The active runtime TOML necessarily contains plaintext engine credentials. It lives under
`%ProgramData%/Tunnela/Service` with an explicit SYSTEM/Administrators-only ACL and is
removed after process exit. Administrators and SYSTEM remain able to read it. Do not put that
file in diagnostics or support bundles. Crash remnants are checked on service startup.

Raw engine output is bounded in memory one line at a time and discarded after state recognition.
The GUI receives only fixed, sanitised lifecycle messages in a bounded 150-entry memory queue;
it does not receive credentials, full imported links, generated TOML or raw native errors.
Consequently this candidate has limited detailed failure diagnostics. No traffic counters,
per-application routing, automatic process retries, boot auto-connect or process adoption with
verified VPN state is implemented.

## User verification checklist

1. Install the published candidate using the documented installer and controller account.
   Confirm that the ordinary GUI can obtain service status without UAC and that a different
   unconfigured ordinary account cannot control the pipe.
2. Connect a known working profile. Process existence should appear before Connected;
   Connected must follow a recognised engine state line. Verify real traffic separately.
3. Disconnect. The GUI should wait for process exit, then show Disconnected with no ProcessId.
   Verify DNS/routes and ordinary Internet access yourself. A timeout must show Unknown,
   leave the process intact and never falsely claim a successful disconnect.
4. Repeat with invalid credentials and an unavailable endpoint. Confirm no Connected state,
   bounded memory/diagnostics, and no secret material in the GUI log.
5. While connected, close/reopen the GUI, change networks, suspend/resume, and test the selected
   kill-switch and routing rules. Record each result separately; the existing user reports cover
   selected routing rules, Wi-Fi recovery, and sleep/resume, not every combination or kill-switch case.
6. Test service shutdown/restart and a stop timeout separately. A recovered process must appear
   as Unknown and prevent a second connection until it exits. Avoid process termination during
   routine testing because that bypasses the intended cleanup path.
