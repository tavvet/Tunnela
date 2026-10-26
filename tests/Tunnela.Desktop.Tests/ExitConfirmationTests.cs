using Tunnela.Contracts;

namespace Tunnela.Desktop.Tests;

/// <summary>Decides whether "Disconnect and exit" may close without a warning. No service is contacted.</summary>
public sealed class ExitConfirmationTests
{
    [Theory]
    [InlineData(TunnelState.Disconnected)]
    [InlineData(TunnelState.Error)] // For example, after the engine exited unexpectedly or failed to start.
    public void AcceptedDisconnectWithoutProcessConfirmsNoTunnel(TunnelState state) =>
        Assert.True(UiModel.ConfirmsNoTunnel(Response(success: true, state, processId: null)));

    [Theory]
    [InlineData(TunnelState.Unknown)]
    [InlineData(TunnelState.Connecting)]
    [InlineData(TunnelState.Connected)]
    [InlineData(TunnelState.Reconnecting)]
    [InlineData(TunnelState.Disconnecting)]
    public void ActiveOrUncertainStateIsNotConfirmed(TunnelState state) =>
        Assert.False(UiModel.ConfirmsNoTunnel(Response(success: true, state, processId: null)));

    [Theory]
    [InlineData(TunnelState.Disconnected)]
    [InlineData(TunnelState.Error)]
    public void RunningProcessIsNeverConfirmedAsStopped(TunnelState state) =>
        Assert.False(UiModel.ConfirmsNoTunnel(Response(success: true, state, processId: 1234)));

    [Theory]
    [InlineData(TunnelState.Disconnected)]
    [InlineData(TunnelState.Error)]
    public void RejectedDisconnectIsNotConfirmed(TunnelState state) =>
        Assert.False(UiModel.ConfirmsNoTunnel(Response(success: false, state, processId: null)));

    private static ServiceResponse Response(bool success, TunnelState state, int? processId) => new()
    {
        Success = success,
        Snapshot = new TunnelSnapshot { State = state, ProcessId = processId },
    };
}
