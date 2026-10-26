using System.IO;
using Microsoft.Win32;

namespace Tunnela.Desktop;

/// <summary>
/// Per-user "start with Windows" entry under HKCU Run. The registry value is the only stored state, so
/// Task Manager and Windows Settings changes are respected. The service has its own Automatic SCM start.
/// </summary>
internal static class WindowsStartup
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "Tunnela";

    /// <summary>Starts with only the tray icon and does not open the window.</summary>
    internal const string TrayArgument = "--tray";

    internal static string InstalledExecutablePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tunnela", "desktop", "Tunnela.Desktop.exe");

    internal static bool IsTrayStart(IEnumerable<string> arguments) =>
        arguments.Any(argument => string.Equals(argument, TrayArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Only the installed copy may register itself: a build or preview copy started from another folder
    /// must not become the program that Windows starts at every sign-in.
    /// </summary>
    internal static bool IsInstalledCopy(string? processPath) =>
        processPath is not null && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(InstalledExecutablePath),
            StringComparison.OrdinalIgnoreCase);

    internal static string CommandFor(string executablePath) => $"\"{executablePath}\" {TrayArgument}";

    /// <summary>True only when the entry starts this executable; an entry for another copy reads as off.</summary>
    internal static bool IsEnabled(RegistryKey userRoot, string executablePath)
    {
        using var run = userRoot.OpenSubKey(RunKeyPath);
        return run?.GetValue(ValueName) is string command &&
               string.Equals(command.Trim(), CommandFor(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the user turned the entry off in Task Manager or Windows Settings. Windows stores that
    /// choice in an undocumented binary value; an odd first byte has been observed to mean "disabled".
    /// </summary>
    internal static bool IsBlockedByWindows(RegistryKey userRoot)
    {
        using var approved = userRoot.OpenSubKey(ApprovedKeyPath);
        return approved?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) != 0;
    }

    /// <summary>Writes or removes only the Tunnela value; other startup entries are never touched.</summary>
    internal static void SetEnabled(RegistryKey userRoot, string executablePath, bool enabled)
    {
        using var run = userRoot.CreateSubKey(RunKeyPath, writable: true);
        if (enabled) run.SetValue(ValueName, CommandFor(executablePath), RegistryValueKind.String);
        else run.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
