namespace Tunnela.Desktop.Tests;

/// <summary>Decisions only; no mutex, event, window or other process is created.</summary>
public sealed class SingleInstanceTests
{
    [Theory]
    // A sign-in start never shows anything, whatever the event state.
    [InlineData(true, true, false, nameof(SecondInstanceAction.ExitSilently))]
    [InlineData(true, true, true, nameof(SecondInstanceAction.ExitSilently))]
    [InlineData(true, false, true, nameof(SecondInstanceAction.ExitSilently))]
    // A Start menu start wakes the running copy when that copy owns the event in this session.
    [InlineData(false, true, false, nameof(SecondInstanceAction.ActivateRunningCopy))]
    // This process created the event: the running copy is in another session and cannot be reached.
    [InlineData(false, true, true, nameof(SecondInstanceAction.ShowAlreadyRunningMessage))]
    // The event could not be opened at all.
    [InlineData(false, false, true, nameof(SecondInstanceAction.ShowAlreadyRunningMessage))]
    [InlineData(false, false, false, nameof(SecondInstanceAction.ShowAlreadyRunningMessage))]
    public void SecondInstanceActsOnlyWhenItCanReachTheRunningCopy(bool trayStart, bool available, bool createdHere,
        string expected) =>
        Assert.Equal(expected, SingleInstance.ForSecondInstance(trayStart, available, createdHere).ToString());

    [Fact]
    public void MutexNameIsUnchangedSoOlderRunningCopiesAreStillDetected() =>
        Assert.Equal(@"Global\Tunnela.Desktop.S-1-5-21-1-2-3-1001", SingleInstance.MutexName("S-1-5-21-1-2-3-1001"));

    [Fact]
    public void ShowRequestIsPerSessionAndPerUser()
    {
        var name = SingleInstance.ShowRequestName("S-1-5-21-1-2-3-1001");
        Assert.StartsWith(@"Local\", name);
        Assert.EndsWith(".S-1-5-21-1-2-3-1001", name);
        Assert.NotEqual(name, SingleInstance.ShowRequestName("S-1-5-21-1-2-3-1002"));
    }
}
