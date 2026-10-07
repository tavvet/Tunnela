using Tunnela.Contracts.Localization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Tunnela.Contracts;

namespace Tunnela.Core;

public static class ProfileValidator
{
    /// <summary>Checks syntax without resolving hosts, contacting a server, or exposing input in errors.</summary>
    public static IReadOnlyList<string> Validate(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name)) errors.Add(Messages.Get("Validation.NameRequired"));
        if (!RoutingSyntax.IsHost(profile.Hostname)) errors.Add(Messages.Get("Validation.HostnameInvalid"));
        if (profile.CustomSni is null || (profile.CustomSni.Length > 0 && !RoutingSyntax.IsHost(profile.CustomSni)))
            errors.Add(Messages.Get("Validation.SniInvalid"));
        if (profile.Addresses is null || profile.Addresses.Count == 0)
            errors.Add(Messages.Get("Validation.AddressRequired"));
        else if (profile.Addresses.Any(address => !RoutingSyntax.IsAddressWithPort(address)))
            errors.Add(Messages.Get("Validation.AddressInvalid"));
        if (string.IsNullOrEmpty(profile.Username)) errors.Add(Messages.Get("Validation.UsernameRequired"));
        if (string.IsNullOrEmpty(profile.Password)) errors.Add(Messages.Get("Validation.PasswordRequired"));
        if (profile.UpstreamProtocol is not ("http2" or "http3")) errors.Add(Messages.Get("Validation.ProtocolInvalid"));
        if (!Enum.IsDefined(profile.RoutingMode)) errors.Add(Messages.Get("Validation.RoutingModeInvalid"));
        if (profile.Rules is null) errors.Add(Messages.Get("Validation.RulesMissing"));
        else for (var index = 0; index < profile.Rules.Count; index++)
            if (!RoutingSyntax.IsRule(profile.Rules[index]))
                errors.Add(Messages.Get("Validation.RuleInvalid", index + 1));
        if (profile.DnsUpstreams is null || profile.DnsUpstreams.Any(value => !RoutingSyntax.IsDnsUpstream(value)))
            errors.Add(Messages.Get("Validation.DnsInvalid"));
        if (profile.IncludedRoutes is null || profile.IncludedRoutes.Any(value => !RoutingSyntax.IsCidr(value)) ||
            profile.ExcludedRoutes is null || profile.ExcludedRoutes.Any(value => !RoutingSyntax.IsCidr(value)))
            errors.Add(Messages.Get("Validation.RoutesInvalid"));
        if (profile.KillSwitchAllowPorts is null || profile.KillSwitchAllowPorts.Any(port => port is <= 0 or > 65535))
            errors.Add(Messages.Get("Validation.AllowedPortsInvalid"));
        if (profile.PreresolveMaxQueries < 0) errors.Add(Messages.Get("Validation.PreresolveCountInvalid"));
        if (!RoutingSyntax.IsPortList(profile.ScannablePorts)) errors.Add(Messages.Get("Validation.ScannablePortsInvalid"));
        if (profile.MtuSize is < 576 or > 65535) errors.Add(Messages.Get("Validation.MtuInvalid"));
        if (profile.TcpReceiveBufferSize < 0 || profile.TcpSendBufferSize < 0)
            errors.Add(Messages.Get("Validation.TcpBufferInvalid"));
        if (profile.LogLevel is not ("info" or "debug" or "trace")) errors.Add(Messages.Get("Validation.LogLevelInvalid"));
        if (profile.BoundInterface is null || profile.BoundInterface.Any(char.IsControl) ||
            profile.DeviceName is null || profile.DeviceName.Any(char.IsControl))
            errors.Add(Messages.Get("Validation.InterfaceNameInvalid"));
        if (!ValidClientRandom(profile.ClientRandom)) errors.Add(Messages.Get("Validation.ClientRandomInvalid"));
        if (!ValidCertificateChain(profile.CertificatePem)) errors.Add(Messages.Get("Validation.CertificateInvalid"));
        return errors;
    }

    private static bool ValidClientRandom(string? value)
    {
        if (value == "") return true;
        if (value is null) return false;
        var parts = value.Split('/');
        return parts.Length is 1 or 2 && parts.All(part => part.Length is > 0 and <= 64 &&
            part.Length % 2 == 0 && part.All(char.IsAsciiHexDigit)) &&
            (parts.Length == 1 || parts[0].Length == parts[1].Length);
    }

    private static bool ValidCertificateChain(string? value)
    {
        if (value == "") return true;
        if (value is null) return false;
        try
        {
            var remainder = value.AsSpan().Trim();
            var count = 0;
            while (PemEncoding.TryFind(remainder, out var fields))
            {
                var start = fields.Location.Start.GetOffset(remainder.Length);
                if (!remainder[..start].IsWhiteSpace() || !remainder[fields.Label].SequenceEqual("CERTIFICATE")) return false;
                using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(remainder[fields.Base64Data].ToString()));
                count++;
                remainder = remainder[fields.Location.End.GetOffset(remainder.Length)..].Trim();
            }
            return count > 0 && remainder.IsEmpty;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
