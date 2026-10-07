using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Tunnela.Core;

/// <summary>Local syntax checks only: never resolves names or changes networking.</summary>
public static class RoutingSyntax
{
    public static bool IsRule(string? value)
    {
        if (!IsClean(value)) return false;
        if (value!.Contains('/')) return IsCidr(value);
        if (value.StartsWith("*:", StringComparison.Ordinal)) return IsPort(value[2..]);
        if (IsIp(value)) return true;
        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            if (end < 0 || !IsIpv6(value[1..end])) return false;
            return end == value.Length - 1 ||
                   (value[end + 1] == ':' && IsPort(value[(end + 2)..]));
        }
        if (value.Contains(':')) return IsAddressWithPort(value);
        return value.StartsWith("*.", StringComparison.Ordinal)
            ? IsDomain(value[2..])
            : IsDomain(value);
    }

    public static bool IsCidr(string? value)
    {
        if (!IsClean(value)) return false;
        var parts = value!.Split('/');
        return parts.Length == 2 && IsIp(parts[0]) &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) &&
               prefix >= 0 && prefix <= (parts[0].Contains(':') ? 128 : 32);
    }

    public static bool IsAddressWithPort(string? value)
    {
        if (!IsClean(value)) return false;
        if (value!.StartsWith('['))
        {
            var end = value.IndexOf(']');
            return end > 0 && IsIpv6(value[1..end]) && end + 1 < value.Length &&
                   value[end + 1] == ':' && IsPort(value[(end + 2)..]);
        }
        var split = value.LastIndexOf(':');
        if (split < 1 || value[..split].Contains(':') || !IsPort(value[(split + 1)..])) return false;
        return IsHost(value[..split]);
    }

    public static bool IsHost(string? value) => IsIp(value) || IsDomain(value);

    public static bool IsDomain(string? value)
    {
        if (!IsClean(value) || value!.Contains('*') || value.Contains(':') || value.Contains('/')) return false;
        // Do not mistake a malformed dotted IPv4 literal for a DNS name.
        if (value.All(c => char.IsAsciiDigit(c) || c == '.')) return IsIp(value);
        try
        {
            var ascii = new IdnMapping().GetAscii(value.EndsWith('.') ? value[..^1] : value);
            if (ascii.Length is 0 or > 253) return false;
            return ascii.Split('.').All(label => label.Length is > 0 and <= 63 &&
                char.IsAsciiLetterOrDigit(label[0]) && char.IsAsciiLetterOrDigit(label[^1]) &&
                label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
        }
        catch (ArgumentException) { return false; }
    }

    public static bool IsDnsUpstream(string? value)
    {
        if (!IsClean(value)) return false;
        if (IsIp(value) || IsAddressWithPort(value)) return true;
        if (value!.StartsWith("sdns://", StringComparison.Ordinal))
            return value.Length > 7 && value[7..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("tcp" or "tls" or "https" or "quic" or "h3") ||
            !IsHost(uri.Host.Trim('[', ']')) || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 ||
            uri.Port is 0 or > 65535) return false;
        return uri.Scheme is "https" or "h3" || (uri.AbsolutePath is "" or "/" && uri.Query.Length == 0);
    }

    public static bool IsPortList(string? value)
    {
        if (value == "") return true;
        if (!IsClean(value)) return false;
        return value!.Split(',').All(item =>
        {
            var ports = item.Split(':');
            if (ports.Length == 1) return IsPort(ports[0]);
            return ports.Length == 2 && IsPort(ports[0]) && IsPort(ports[1]) &&
                   int.Parse(ports[0], CultureInfo.InvariantCulture) <= int.Parse(ports[1], CultureInfo.InvariantCulture);
        });
    }

    private static bool IsClean(string? value) => !string.IsNullOrEmpty(value) &&
        value.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c));

    private static bool IsPort(string value) => int.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535;

    private static bool IsIpv6(string value) => !value.Contains('%') &&
        IPAddress.TryParse(value, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6;

    private static bool IsIp(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('%') || !IPAddress.TryParse(value, out var ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return !value.Contains('[') && !value.Contains(']');
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part => part.Length is > 0 and <= 3 &&
            part.All(char.IsAsciiDigit) && (part.Length == 1 || part[0] != '0') &&
            byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }
}
