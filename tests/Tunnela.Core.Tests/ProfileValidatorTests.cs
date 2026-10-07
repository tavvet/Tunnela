using Tunnela.Contracts;
using Tunnela.Core;

namespace Tunnela.Core.Tests;

public sealed class ProfileValidatorTests
{
    internal static ServerProfile ValidProfile() => new()
    {
        Name = "Тестовый сервер",
        Hostname = "vpn.example.com",
        Addresses = ["192.0.2.1:443", "[2001:db8::1]:443"],
        Username = "test-user",
        Password = "test-password"
    };

    [Theory]
    [InlineData("example.com")]
    [InlineData("*.example.com")]
    [InlineData("пример.рф")]
    [InlineData("192.0.2.1")]
    [InlineData("192.0.2.1:443")]
    [InlineData("192.0.2.0/24")]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:db8::1")]
    [InlineData("[::1]")]
    [InlineData("[::1]:443")]
    [InlineData("*:80")]
    public void AcceptsDocumentedRoutingRules(string rule) =>
        Assert.Empty(ProfileValidator.Validate(ValidProfile() with { Rules = [rule] }));

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("192.0.2.1:0")]
    [InlineData("192.0.2.1:65536")]
    [InlineData("999.0.0.1")]
    [InlineData("192.0.2.0/33")]
    [InlineData("2001:db8::/129")]
    [InlineData("192.0.2.0/-1")]
    [InlineData("*example.com")]
    [InlineData("[::1]:abc")]
    [InlineData("example .com")]
    [InlineData("*")]
    public void RejectsInvalidRoutingRules(string rule) =>
        Assert.NotEmpty(ProfileValidator.Validate(ValidProfile() with { Rules = [rule] }));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8:53")]
    [InlineData("tcp://8.8.8.8:53")]
    [InlineData("tls://1.1.1.1")]
    [InlineData("https://dns.example.com/dns-query")]
    [InlineData("quic://dns.example.com:8853")]
    public void AcceptsDnsUpstreams(string value) =>
        Assert.Empty(ProfileValidator.Validate(ValidProfile() with { DnsUpstreams = [value] }));

    [Fact]
    public void ValidationMessagesDoNotEchoInvalidInput()
    {
        var errors = ProfileValidator.Validate(ValidProfile() with { Addresses = ["private-secret://bad"], Rules = ["private-secret://bad"] });
        Assert.NotEmpty(errors);
        Assert.DoesNotContain("private-secret", string.Join('\n', errors));
    }

    [Fact]
    public void RequiresExplicitPortAndPreservesEmptySelectiveRules()
    {
        Assert.NotEmpty(ProfileValidator.Validate(ValidProfile() with { Addresses = ["vpn.example.com"] }));
        Assert.Empty(ProfileValidator.Validate(ValidProfile() with { RoutingMode = RoutingMode.Selective, Rules = [] }));
    }
}
