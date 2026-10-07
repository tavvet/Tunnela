using Tunnela.Contracts;
using Tunnela.Core;

namespace Tunnela.Core.Tests;

public sealed class TomlProfileCodecTests
{
    private const string Endpoint = """
        hostname = "vpn.example.com"
        addresses = ["192.0.2.1:443"]
        username = "test-user"
        password = "test-password"
        """;

    [Fact]
    public void RoundtripPreservesEscapedCredentialsAndRouting()
    {
        var original = ProfileValidatorTests.ValidProfile() with
        {
            Username = "user\"with\\slashes",
            Password = "pass\"\\\nстрока\tvalue",
            RoutingMode = RoutingMode.Selective,
            Rules = ["example.com", "*.example.org", "192.0.2.0/24", "[::1]:443", "*:80"],
            DnsUpstreams = ["tls://1.1.1.1"],
            CustomSni = "sni.example.org",
            AntiDpi = true,
            ClientRandom = "abcd/ffff",
            PostQuantumGroupEnabled = false,
            TcpEarlyAckEnabled = true,
            PreresolveEnabled = false,
            PreresolveMaxQueries = 75,
            ScannablePorts = "443,8080:8090",
            KillSwitchEnabled = false,
            KillSwitchAllowPorts = [8080],
            IncludedRoutes = [],
            ExcludedRoutes = ["192.168.0.0/16"],
            MtuSize = 1280,
            ChangeSystemDns = false,
            BoundInterface = "Ethernet 2",
            TcpReceiveBufferSize = 262144,
            TcpSendBufferSize = 131072,
            DeviceName = "TrustTunnel Test"
        };
        var encoded = TomlProfileCodec.Export(original);
        var restored = TomlProfileCodec.Import(encoded, original.Name);
        Assert.Equal(original.Username, restored.Username);
        Assert.Equal(original.Password, restored.Password);
        Assert.Equal(original.Rules, restored.Rules);
        Assert.Equal(original.DnsUpstreams, restored.DnsUpstreams);
        // Deterministic re-export catches loss of supported configuration, including listener settings.
        Assert.Equal(encoded, TomlProfileCodec.Export(restored));
        Assert.Contains("skip_verification = false", encoded);
    }

    [Fact]
    public void ImportsFlatEndpointAndAppliesConservativeDefaults()
    {
        var profile = TomlProfileCodec.Import(Endpoint);
        Assert.Equal("vpn.example.com", profile.Name);
        Assert.Equal(RoutingMode.General, profile.RoutingMode);
        Assert.True(profile.KillSwitchEnabled);
        Assert.Empty(profile.Rules);
        Assert.Contains("0.0.0.0/0", profile.IncludedRoutes);
    }

    [Fact]
    public void ImportsLegacySniWithoutDroppingIt()
    {
        var profile = TomlProfileCodec.Import(Endpoint.Replace("vpn.example.com", "vpn.example.com|front.example.com"));
        Assert.Equal("vpn.example.com", profile.Hostname);
        Assert.Equal("front.example.com", profile.CustomSni);
    }

    [Theory]
    [InlineData("skip_verification = true")]
    [InlineData("unrecognized_setting = true")]
    [InlineData("has_ipv6 = \"false\"")]
    [InlineData("addresses = [1, 2, 3]")]
    [InlineData("certificate = \"invalid-certificate\"")]
    public void RejectsUnsafeOrUnrepresentableEndpointWithoutReturningPartialProfile(string extra)
    {
        Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import(Endpoint + Environment.NewLine + extra));
    }

    [Fact]
    public void RejectsSocksAndUnknownListenerOptions()
    {
        var exported = TomlProfileCodec.Export(ProfileValidatorTests.ValidProfile());
        Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import(exported + "\n[listener.socks]\naddress = '127.0.0.1:1080'\n"));
        Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import(exported + "\n[listener.custom]\nenabled = true\n"));
    }

    [Fact]
    public void RejectsConflictingDnsAndPreservesLegacyDns()
    {
        var exported = TomlProfileCodec.Export(ProfileValidatorTests.ValidProfile());
        Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import("dns_upstreams = ['8.8.8.8']\n" + exported));
        var noEndpointDns = exported.Replace("dns_upstreams = []", "");
        Assert.Equal(["8.8.8.8"], TomlProfileCodec.Import("dns_upstreams = ['8.8.8.8']\n" + noEndpointDns).DnsUpstreams);
    }

    [Fact]
    public void MalformedInputCannotLeakParserSourceInErrors()
    {
        var exception = Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import("password = \"PRIVATE_SECRET"));
        Assert.DoesNotContain("PRIVATE_SECRET", exception.ToString());
        Assert.Null(exception.InnerException);
        var unknown = Assert.Throws<ProfileImportException>(() => TomlProfileCodec.Import(Endpoint + "\nPRIVATE_SECRET = 1"));
        Assert.DoesNotContain("PRIVATE_SECRET", unknown.ToString());
    }
}
