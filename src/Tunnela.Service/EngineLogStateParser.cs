using Tunnela.Contracts;
using System.Text.RegularExpressions;

namespace Tunnela.Service;

/// <summary>
/// Parses only the pinned 1.1.7 state-emission sites, never arbitrary occurrences of "connected".
/// This is a log compatibility boundary, not a structured engine API or a connectivity probe.
/// </summary>
public static partial class EngineLogStateParser
{
    public static TunnelState? Parse(string line)
    {
        if (line.Length > 4096) return null;
        var match = StateLine().Match(line);
        if (match.Success)
        {
            return match.Groups[1].Value switch
            {
                "VPN_SS_DISCONNECTED" => TunnelState.Disconnected,
                "VPN_SS_CONNECTING" => TunnelState.Connecting,
                "VPN_SS_CONNECTED" => TunnelState.Connected,
                "VPN_SS_WAITING_RECOVERY" or "VPN_SS_RECOVERING" or "VPN_SS_WAITING_FOR_NETWORK" => TunnelState.Reconnecting,
                _ => TunnelState.Unknown,
            };
        }
        return null;
    }

    // NativeLibsCommon 8.1.52 logger.cpp emits date, time, six microsecond digits,
    // padded INFO, thread ID, logger name; infolog prefixes __func__, log_vpn prefixes VPN ID.
    [GeneratedRegex(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}\.\d{6} INFO\s+\[\d+\] VPNCORE raise_state: \[\d+\] (VPN_SS_[A-Z_]+)\r?$", RegexOptions.CultureInvariant)]
    private static partial Regex StateLine();
}
