namespace Tunnela.Contracts;

public enum RoutingMode { General, Selective }
public enum TunnelState { Disconnected, Connecting, Connected, Reconnecting, Disconnecting, Error, Unknown }

public sealed record ServerProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "New connection";
    public string Hostname { get; init; } = "";
    public List<string> Addresses { get; init; } = [];
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool HasIpv6 { get; init; } = true;
    public string CertificatePem { get; init; } = "";
    public string UpstreamProtocol { get; init; } = "http2";
    public List<string> DnsUpstreams { get; init; } = [];
    public RoutingMode RoutingMode { get; init; } = RoutingMode.General;

    /// <summary>
    /// Destinations that bypass VPN in General mode or use VPN in Selective mode.
    /// Both modes store this list in the upstream TOML field named "exclusions".
    /// </summary>
    public List<string> Rules { get; init; } = [];
    public bool KillSwitchEnabled { get; init; } = true;
    public bool TcpEarlyAckEnabled { get; init; }
    public string CustomSni { get; init; } = "";
    public bool AntiDpi { get; init; }
    public string ClientRandom { get; init; } = "";
    public bool PostQuantumGroupEnabled { get; init; } = true;
    public bool PreresolveEnabled { get; init; } = true;
    public int PreresolveMaxQueries { get; init; } = 50;
    public string ScannablePorts { get; init; } = "443,80,8080,8008,853";
    public List<int> KillSwitchAllowPorts { get; init; } = [];
    public List<string> IncludedRoutes { get; init; } = ["0.0.0.0/0", "2000::/3"];
    public List<string> ExcludedRoutes { get; init; } = ["0.0.0.0/8", "10.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16", "224.0.0.0/3"];
    public int MtuSize { get; init; } = 1350;
    public bool ChangeSystemDns { get; init; } = true;
    public string BoundInterface { get; init; } = "";
    public int TcpReceiveBufferSize { get; init; }
    public int TcpSendBufferSize { get; init; }
    public string DeviceName { get; init; } = "";
    public string LogLevel { get; init; } = "info";
}

public sealed record AppPreferences
{
    public string Language { get; init; } = "en";
    public Guid? SelectedProfileId { get; init; }
    public bool MinimizeToTray { get; init; } = true;
}

public sealed record ProfileCollection
{
    public int SchemaVersion { get; init; } = 1;
    public List<ServerProfile> Profiles { get; init; } = [];
    public AppPreferences Preferences { get; init; } = new();
}

public sealed record TunnelSnapshot
{
    public TunnelState State { get; init; } = TunnelState.Disconnected;

    /// <summary>English fallback for the state description; older services may send Russian text.</summary>
    public string Message { get; init; } = "VPN is disconnected.";

    /// <summary>Localization key for Message. Optional so responses from older services remain readable.</summary>
    public string? MessageCode { get; init; }
    public Guid? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public int? ProcessId { get; init; }

    /// <summary>Diagnostic state code, such as StopTimedOut; not a localization key. Null when no error is recorded.</summary>
    public string? ErrorCode { get; init; }
    public bool EngineAvailable { get; init; }
    public string EngineVersion { get; init; } = "1.1.7";
}

/// <summary>A sanitized service event with a fallback message and an optional localization key.</summary>
/// <param name="Timestamp">Time of the event.</param>
/// <param name="Level">Severity of the event.</param>
/// <param name="Message">English fallback text; older services may send Russian text.</param>
/// <param name="MessageCode">Localization key; absent in events from older services.</param>
public sealed record DiagnosticEntry(DateTimeOffset Timestamp, string Level, string Message, string? MessageCode = null);

public sealed record ServiceRequest
{
    public int ProtocolVersion { get; init; } = 1;
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public string Command { get; init; } = "status";
    public ServerProfile? Profile { get; init; }
}

public sealed record ServiceResponse
{
    public int ProtocolVersion { get; init; } = 1;
    public Guid RequestId { get; init; }
    public bool Success { get; init; }

    /// <summary>English fallback for an operation failure; older services may send Russian text.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Localization key for Error, unlike the diagnostic ErrorCode in TunnelSnapshot.
    /// Optional for compatibility with responses from older services.
    /// </summary>
    public string? ErrorCode { get; init; }
    public TunnelSnapshot Snapshot { get; init; } = new();
    public List<DiagnosticEntry> Logs { get; init; } = [];
}

public static class ServiceProtocol
{
    public const string PipeName = "Tunnela.Control.v1";
    public const string ServiceName = "Tunnela";
    public const int MaxMessageBytes = 262144;
}
