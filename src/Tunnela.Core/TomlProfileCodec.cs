using Tunnela.Contracts.Localization;
using Tomlyn;
using Tomlyn.Model;
using Tunnela.Contracts;

namespace Tunnela.Core;

/// <summary>Imports endpoint exports or supported v1.1.7 TUN client configurations without discarding settings.</summary>
public static class TomlProfileCodec
{
    private static readonly string[] EndpointKeys = ["hostname", "addresses", "custom_sni", "has_ipv6", "username", "password",
        "client_random", "skip_verification", "certificate", "upstream_protocol", "anti_dpi", "dns_upstreams", "name"];

    public static ServerProfile Import(string text, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1_048_576)
            throw new ProfileImportException(Messages.Get("Import.SizeInvalid"));
        TomlTable root;
        try
        {
            if (Toml.Parse(text).HasErrors) throw new ProfileImportException(Messages.Get("Import.TomlSyntaxInvalid"));
            root = Toml.ToModel(text);
        }
        catch (ProfileImportException) { throw; }
        catch (Exception)
        {
            // Parser diagnostics can contain passwords and source excerpts.
            throw new ProfileImportException(Messages.Get("Import.TomlReadFailed"));
        }

        var fullConfig = root.ContainsKey("endpoint");
        var endpoint = fullConfig ? Table(root, "endpoint", required: true) : root;
        OnlyKnownKeys(endpoint, EndpointKeys);
        if (Boolean(endpoint, "skip_verification", false))
            throw new ProfileImportException(Messages.Get("Import.CertificateVerificationDisabled"));

        var defaults = new ServerProfile();
        var hostname = String(endpoint, "hostname", required: true);
        var sni = String(endpoint, "custom_sni");
        if (hostname.Contains('|'))
        {
            var parts = hostname.Split('|');
            if (parts.Length != 2 || sni.Length > 0)
                throw new ProfileImportException(Messages.Get("Import.SniAmbiguous"));
            hostname = parts[0];
            sni = parts[1];
        }
        var profile = defaults with
        {
            Name = name ?? String(endpoint, "name", hostname),
            Hostname = hostname,
            CustomSni = sni,
            Addresses = Strings(endpoint, "addresses", required: true),
            Username = String(endpoint, "username", required: true),
            Password = String(endpoint, "password", required: true),
            HasIpv6 = Boolean(endpoint, "has_ipv6", true),
            CertificatePem = String(endpoint, "certificate"),
            UpstreamProtocol = String(endpoint, "upstream_protocol", "http2"),
            AntiDpi = Boolean(endpoint, "anti_dpi", false),
            ClientRandom = String(endpoint, "client_random"),
            DnsUpstreams = Strings(endpoint, "dns_upstreams")
        };

        if (fullConfig)
        {
            OnlyKnownKeys(root, ["loglevel", "vpn_mode", "killswitch_enabled", "killswitch_allow_ports", "post_quantum_group_enabled",
                "exclusions_tcp_early_ack_enabled", "exclusions_preresolve_enabled", "exclusions_preresolve_max_queries",
                "exclusions_scannable_ports", "exclusions", "dns_upstreams", "endpoint", "listener"]);
            var legacyDns = Strings(root, "dns_upstreams");
            if (legacyDns.Count > 0)
            {
                if (endpoint.ContainsKey("dns_upstreams") && !legacyDns.SequenceEqual(profile.DnsUpstreams))
                    throw new ProfileImportException(Messages.Get("Import.DnsAmbiguous"));
                profile = profile with { DnsUpstreams = legacyDns };
            }
            var routingMode = String(root, "vpn_mode", "general") switch
            {
                "general" => RoutingMode.General,
                "selective" => RoutingMode.Selective,
                _ => throw new ProfileImportException(Messages.Get("Import.RoutingModeInvalid"))
            };
            var listener = Table(root, "listener", required: true);
            if (listener.ContainsKey("socks"))
                throw new ProfileImportException(Messages.Get("Import.SocksUnsupported"));
            OnlyKnownKeys(listener, ["tun"]);
            var tun = Table(listener, "tun", required: true);
            OnlyKnownKeys(tun, ["bound_if", "included_routes", "excluded_routes", "mtu_size", "tcp_recv_buf_size", "tcp_send_buf_size",
                "change_system_dns", "device_name", "use_existing"]);
            if (Boolean(tun, "use_existing", false))
                throw new ProfileImportException(Messages.Get("Import.ExistingTunUnsupported"));
            profile = profile with
            {
                RoutingMode = routingMode,
                Rules = Strings(root, "exclusions"),
                LogLevel = String(root, "loglevel", "info"),
                KillSwitchEnabled = Boolean(root, "killswitch_enabled", true),
                KillSwitchAllowPorts = Integers(root, "killswitch_allow_ports"),
                PostQuantumGroupEnabled = Boolean(root, "post_quantum_group_enabled", true),
                TcpEarlyAckEnabled = Boolean(root, "exclusions_tcp_early_ack_enabled", false),
                PreresolveEnabled = Boolean(root, "exclusions_preresolve_enabled", true),
                PreresolveMaxQueries = Integer(root, "exclusions_preresolve_max_queries", 50),
                ScannablePorts = String(root, "exclusions_scannable_ports", defaults.ScannablePorts),
                BoundInterface = String(tun, "bound_if"),
                IncludedRoutes = Strings(tun, "included_routes", defaults.IncludedRoutes),
                ExcludedRoutes = Strings(tun, "excluded_routes", defaults.ExcludedRoutes),
                MtuSize = Integer(tun, "mtu_size", 1350),
                TcpReceiveBufferSize = Integer(tun, "tcp_recv_buf_size", 0),
                TcpSendBufferSize = Integer(tun, "tcp_send_buf_size", 0),
                ChangeSystemDns = Boolean(tun, "change_system_dns", true),
                DeviceName = String(tun, "device_name")
            };
        }
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0) throw new ProfileImportException(string.Join(Environment.NewLine, errors));
        return profile;
    }

    /// <summary>Returns sensitive text. Callers must protect any destination and never log the result.</summary>
    public static string Export(ServerProfile profile)
    {
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0) throw new ProfileImportException(string.Join(Environment.NewLine, errors));
        var endpoint = new TomlTable
        {
            ["hostname"] = profile.Hostname,
            ["addresses"] = Array(profile.Addresses),
            ["custom_sni"] = profile.CustomSni,
            ["has_ipv6"] = profile.HasIpv6,
            ["username"] = profile.Username,
            ["password"] = profile.Password,
            ["client_random"] = profile.ClientRandom,
            ["skip_verification"] = false,
            ["certificate"] = profile.CertificatePem,
            ["dns_upstreams"] = Array(profile.DnsUpstreams),
            ["upstream_protocol"] = profile.UpstreamProtocol,
            ["anti_dpi"] = profile.AntiDpi
        };
        var tun = new TomlTable
        {
            ["bound_if"] = profile.BoundInterface,
            ["included_routes"] = Array(profile.IncludedRoutes),
            ["excluded_routes"] = Array(profile.ExcludedRoutes),
            ["mtu_size"] = (long)profile.MtuSize,
            ["tcp_recv_buf_size"] = (long)profile.TcpReceiveBufferSize,
            ["tcp_send_buf_size"] = (long)profile.TcpSendBufferSize,
            ["change_system_dns"] = profile.ChangeSystemDns,
            ["device_name"] = profile.DeviceName,
            ["use_existing"] = false
        };
        var root = new TomlTable
        {
            ["loglevel"] = profile.LogLevel,
            ["vpn_mode"] = profile.RoutingMode == RoutingMode.Selective ? "selective" : "general",
            ["killswitch_enabled"] = profile.KillSwitchEnabled,
            ["killswitch_allow_ports"] = Array(profile.KillSwitchAllowPorts.Select(port => (long)port)),
            ["post_quantum_group_enabled"] = profile.PostQuantumGroupEnabled,
            ["exclusions_tcp_early_ack_enabled"] = profile.TcpEarlyAckEnabled,
            ["exclusions_preresolve_enabled"] = profile.PreresolveEnabled,
            ["exclusions_preresolve_max_queries"] = (long)profile.PreresolveMaxQueries,
            ["exclusions_scannable_ports"] = profile.ScannablePorts,
            ["exclusions"] = Array(profile.Rules),
            ["endpoint"] = endpoint,
            ["listener"] = new TomlTable { ["tun"] = tun }
        };
        return Toml.FromModel(root);
    }

    private static void OnlyKnownKeys(TomlTable table, string[] keys)
    {
        if (table.Keys.Any(key => !keys.Contains(key, StringComparer.Ordinal)))
            throw new ProfileImportException(Messages.Get("Import.UnsupportedSettings"));
    }

    private static TomlTable Table(TomlTable source, string key, bool required = false)
    {
        if (!source.TryGetValue(key, out var value))
        {
            if (!required) return new TomlTable();
            throw new ProfileImportException(Messages.Get("Import.SectionMissing", key));
        }
        return value as TomlTable ?? throw InvalidType(key);
    }

    private static string String(TomlTable source, string key, string fallback = "", bool required = false)
    {
        if (!source.TryGetValue(key, out var value))
        {
            if (!required) return fallback;
            throw new ProfileImportException(Messages.Get("Import.ParameterMissing", key));
        }
        return value as string ?? throw InvalidType(key);
    }

    private static bool Boolean(TomlTable source, string key, bool fallback) =>
        !source.TryGetValue(key, out var value) ? fallback : value is bool boolean ? boolean : throw InvalidType(key);

    private static int Integer(TomlTable source, string key, int fallback) =>
        !source.TryGetValue(key, out var value) ? fallback : value is long number && number is >= int.MinValue and <= int.MaxValue
            ? (int)number : throw InvalidType(key);

    private static List<string> Strings(TomlTable source, string key, List<string>? fallback = null, bool required = false)
    {
        if (!source.TryGetValue(key, out var value))
        {
            if (!required) return fallback is null ? [] : [.. fallback];
            throw new ProfileImportException(Messages.Get("Import.ParameterMissing", key));
        }
        if (value is not TomlArray array || array.Any(item => item is not string)) throw InvalidType(key);
        return array.Cast<string>().ToList();
    }

    private static List<int> Integers(TomlTable source, string key)
    {
        if (!source.TryGetValue(key, out var value)) return [];
        if (value is not TomlArray array || array.Any(item => item is not long number || number is < int.MinValue or > int.MaxValue))
            throw InvalidType(key);
        return array.Cast<long>().Select(number => (int)number).ToList();
    }

    private static TomlArray Array<T>(IEnumerable<T> values) where T : notnull
    {
        var array = new TomlArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    private static ProfileImportException InvalidType(string key) => new(Messages.Get("Import.ParameterTypeInvalid", key));
}
