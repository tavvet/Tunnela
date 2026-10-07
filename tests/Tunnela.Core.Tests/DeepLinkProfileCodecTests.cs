using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Tunnela.Core;
using Tunnela.Contracts.Localization;

namespace Tunnela.Core.Tests;

public sealed class DeepLinkProfileCodecTests
{
    private static readonly (ulong Tag, byte[] Value)[] Required =
    [
        (1, Utf8("vpn.example.com")),
        (2, Utf8("192.0.2.1:443")),
        (5, Utf8("test-user")),
        (6, Utf8("test-password"))
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ImportsStaticVersionsAndRepeatedAddresses(int version)
    {
        var fields = Required.Concat(new (ulong, byte[])[]
        {
            (0, VarInt((ulong)version)),
            (2, Utf8("[2001:db8::1]:8443")),
            (3, Utf8("front.example.com")),
            (4, [0]),
            (9, [2]),
            (10, [1]),
            (11, Utf8("abcd/ffff")),
            (12, Utf8("Сервер в Европе")),
            (13, Strings("tls://1.1.1.1", "https://dns.example.com/dns-query"))
        });
        var profile = DeepLinkProfileCodec.Import(Link(fields));
        Assert.Equal("Сервер в Европе", profile.Name);
        Assert.Equal("front.example.com", profile.CustomSni);
        Assert.Equal(["192.0.2.1:443", "[2001:db8::1]:8443"], profile.Addresses);
        Assert.False(profile.HasIpv6);
        Assert.Equal("http3", profile.UpstreamProtocol);
        Assert.True(profile.AntiDpi);
        Assert.Equal("abcd/ffff", profile.ClientRandom);
        Assert.Equal(["tls://1.1.1.1", "https://dns.example.com/dns-query"], profile.DnsUpstreams);
    }

    [Fact]
    public void VersionZeroWithoutVersionTagAndUnknownExtensionsRemainReadable()
    {
        var fields = Required.Concat(new (ulong, byte[])[]
        {
            (1UL << 40, new byte[200]),
            (5, Utf8("replacement-user")),
            (12, Utf8(new string('x', 200)))
        });
        var profile = DeepLinkProfileCodec.Import(Link(fields));
        Assert.Equal("replacement-user", profile.Username);
        Assert.Equal(200, profile.Name.Length);
    }

    [Fact]
    public void ConvertsConcatenatedDerCertificatesToPemWithoutChangingTrust()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=vpn.example.com", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var der = certificate.Export(X509ContentType.Cert);
        var fields = Required.Append((8UL, der.Concat(der).ToArray()));
        var profile = DeepLinkProfileCodec.Import(Link(fields));
        Assert.Equal(2, profile.CertificatePem.Split("-----BEGIN CERTIFICATE-----").Length - 1);
        var exported = TomlProfileCodec.Export(profile);
        Assert.Equal(profile.CertificatePem, TomlProfileCodec.Import(exported).CertificatePem);
    }

    [Fact]
    public void RejectsSubscriptionsEvenWhenStaticFallbackExists()
    {
        var link = Link(Required.Concat(new (ulong, byte[])[] { (0, [2]), (14, Utf8("https://secret:password@example.com/sub")) }));
        var error = Assert.Throws<ProfileImportException>(() => DeepLinkProfileCodec.Import(link));
        Assert.Equal(Messages.Get("Link.SubscriptionUnsupported"), error.Message);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.DoesNotContain(link, error.ToString());
    }

    [Fact]
    public void RejectsUnsafeFlagsFutureVersionsAndMalformedValueTypes()
    {
        foreach (var field in new (ulong, byte[])[] { (7, [1]), (0, [3]), (4, [2]), (9, [1, 0]), (5, [0xc3, 0x28]), (8, [0x30, 0x42]) })
            Assert.Throws<ProfileImportException>(() => DeepLinkProfileCodec.Import(Link(Required.Append(field))));
    }

    [Fact]
    public void RejectsTruncatedVarintsAndLengths()
    {
        foreach (var payload in new byte[][] { [0x40], [1, 10, 1], [1, 0x40], [1, 0xc0, 0, 0, 0, 0, 0, 0, 1] })
            Assert.Throws<ProfileImportException>(() => DeepLinkProfileCodec.Import(Base64Link(payload)));
    }

    [Theory]
    [InlineData("tt://?")]
    [InlineData("tt://?bad+encoding")]
    [InlineData("tt://?A")]
    [InlineData("https://example.com/")]
    public void RejectsMalformedLinks(string link) =>
        Assert.Throws<ProfileImportException>(() => DeepLinkProfileCodec.Import(link));

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string Link(IEnumerable<(ulong Tag, byte[] Value)> fields) => Base64Link(fields
        .SelectMany(field => VarInt(field.Tag).Concat(VarInt((ulong)field.Value.Length)).Concat(field.Value)).ToArray());

    private static string Base64Link(byte[] bytes) => "tt://?" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Strings(params string[] values) => values.SelectMany(value =>
    {
        var bytes = Utf8(value);
        return VarInt((ulong)bytes.Length).Concat(bytes);
    }).ToArray();

    private static byte[] VarInt(ulong value)
    {
        var length = value < 64 ? 1 : value < 16384 ? 2 : value < (1UL << 30) ? 4 : 8;
        var bytes = new byte[length];
        for (var index = length - 1; index >= 0; index--)
        {
            bytes[index] = (byte)(value & 0xff);
            value >>= 8;
        }
        bytes[0] |= (byte)(length == 1 ? 0 : length == 2 ? 0x40 : length == 4 ? 0x80 : 0xc0);
        return bytes;
    }
}
