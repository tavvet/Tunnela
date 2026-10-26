using System.Runtime.InteropServices;

namespace Tunnela.Desktop;

internal enum SecondInstanceAction
{
    /// <summary>A sign-in start while Tunnela already runs: nothing to show.</summary>
    ExitSilently,

    /// <summary>Ask the running copy in this session to show its window, then exit.</summary>
    ActivateRunningCopy,

    /// <summary>The running copy cannot be reached from this session; explain instead.</summary>
    ShowAlreadyRunningMessage,
}

/// <summary>Names and decisions for the one-GUI-per-user rule. The mutex name is unchanged from earlier releases.</summary>
internal static class SingleInstance
{
    private const int AllowAnyProcess = -1; // ASFW_ANY

    internal static string MutexName(string? userSid) => "Global\\Tunnela.Desktop." + userSid;

    /// <summary>Per session and user, so only a copy in the same session can be asked to show its window.</summary>
    internal static string ShowRequestName(string? userSid) => "Local\\Tunnela.Desktop.Show." + userSid;

    /// <param name="trayStart">This start came from the sign-in entry (<c>--tray</c>).</param>
    /// <param name="showRequestAvailable">The show event could be created or opened.</param>
    /// <param name="showRequestCreatedHere">
    /// This process created the event, so no copy in this session owns it: the running copy is in another session.
    /// </param>
    internal static SecondInstanceAction ForSecondInstance(bool trayStart, bool showRequestAvailable, bool showRequestCreatedHere) =>
        trayStart ? SecondInstanceAction.ExitSilently
        : showRequestAvailable && !showRequestCreatedHere ? SecondInstanceAction.ActivateRunningCopy
        : SecondInstanceAction.ShowAlreadyRunningMessage;

    /// <summary>
    /// The process the user just started may bring a window to the front; the running copy may not. Pass that
    /// right on before signalling, otherwise Windows only flashes the running copy's taskbar button.
    /// </summary>
    internal static void AllowRunningCopyToTakeForeground() => _ = AllowSetForegroundWindow(AllowAnyProcess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
