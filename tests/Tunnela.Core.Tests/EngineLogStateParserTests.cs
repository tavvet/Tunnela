using Tunnela.Contracts;
using Tunnela.Service;

namespace Tunnela.Core.Tests;

public sealed class EngineLogStateParserTests
{
    [Theory]
    [InlineData("VPN_SS_DISCONNECTED", TunnelState.Disconnected)]
    [InlineData("VPN_SS_CONNECTING", TunnelState.Connecting)]
    [InlineData("VPN_SS_CONNECTED", TunnelState.Connected)]
    [InlineData("VPN_SS_WAITING_RECOVERY", TunnelState.Reconnecting)]
    [InlineData("VPN_SS_RECOVERING", TunnelState.Reconnecting)]
    [InlineData("VPN_SS_WAITING_FOR_NETWORK", TunnelState.Reconnecting)]
    public void RecognizesPinnedEngineStateEmission(string state, TunnelState expected) =>
        Assert.Equal(expected, EngineLogStateParser.Parse($"06.10.2026 20:00:00.123456 INFO  [123] VPNCORE raise_state: [1] {state}\r"));

    [Theory]
    [InlineData("")]
    [InlineData("The endpoint connected successfully")]
    [InlineData("VPN_SS_CONNECTED")]
    [InlineData("06.10.2026 20:00:00.123456 INFO  [123] UPSTREAM raise_state: [1] VPN_SS_CONNECTED")]
    [InlineData("06.10.2026 20:00:00.123456 INFO  [123] VPNCORE read_settings: [1] VPN_SS_CONNECTED")]
    [InlineData("06.10.2026 20:00:00.123456 INFO  [123] VPNCORE raise_state: [1] VPN_SS_DISCONNECTED extra text")]
    [InlineData("06.10.2026 20:00:00.123456 DEBUG [123] VPNCORE raise_state: [1] VPN_SS_CONNECTED")]
    [InlineData("06.10.2026 20:00:00.123456 INFO  [123] vpncore raise_state: [1] VPN_SS_CONNECTED")]
    [InlineData("06.10.2026 20:00:00.123456 INFO  [123] VPNCORE raise_state: [1] VPN_SS_RECOVERING connected")]
    public void UnrelatedMessagesCannotAssertVpnConnectivity(string line) => Assert.Null(EngineLogStateParser.Parse(line));

    [Fact]
    public void RejectsOversizedMessages() =>
        Assert.Null(EngineLogStateParser.Parse(new string('x', 4096) + "[VPNCORE] raise_state VPN_SS_CONNECTED"));

    [Fact]
    public void FutureStateIsUnknownRatherThanPreviousConnectedState() =>
        Assert.Equal(TunnelState.Unknown, EngineLogStateParser.Parse("06.10.2026 20:00:00.123456 INFO  [123] VPNCORE raise_state: [1] VPN_SS_FUTURE"));
}
