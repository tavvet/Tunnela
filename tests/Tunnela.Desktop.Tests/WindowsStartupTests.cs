using System.Globalization;
using System.IO;
using Microsoft.Win32;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop.Tests;

/// <summary>
/// Registry checks use a private key under HKCU\Software\Tunnela.Tests that stands in for HKCU and is
/// deleted afterwards. The real Run and StartupApproved keys are never read or written.
/// </summary>
public sealed class WindowsStartupTests : IDisposable
{
    private const string Executable = @"C:\Program Files\Tunnela\desktop\Tunnela.Desktop.exe";
    private readonly string _testKeyPath = @"Software\Tunnela.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _previousDefault = CultureInfo.DefaultThreadCurrentUICulture;

    public WindowsStartupTests() => _root = Registry.CurrentUser.CreateSubKey(_testKeyPath, writable: true);

    [Theory]
    [InlineData(new[] { "--tray" }, true)]
    [InlineData(new[] { "--TRAY" }, true)]
    [InlineData(new[] { "--other", "--tray" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "tray", "/tray", "--tray=1" }, false)]
    public void TrayStartNeedsTheExactArgument(string[] arguments, bool expected) =>
        Assert.Equal(expected, WindowsStartup.IsTrayStart(arguments));

    [Fact]
    public void OnlyTheInstalledCopyMayRegisterItself()
    {
        Assert.True(WindowsStartup.IsInstalledCopy(WindowsStartup.InstalledExecutablePath));
        Assert.True(WindowsStartup.IsInstalledCopy(WindowsStartup.InstalledExecutablePath.ToUpperInvariant()));
        Assert.False(WindowsStartup.IsInstalledCopy(@"G:\work\.artifacts\publish\tunnela\desktop\Tunnela.Desktop.exe"));
        Assert.False(WindowsStartup.IsInstalledCopy(null));
    }

    [Fact]
    public void InstalledPathIsInProgramFiles()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Equal(Path.Combine(programFiles, "Tunnela", "desktop", "Tunnela.Desktop.exe"), WindowsStartup.InstalledExecutablePath);
    }

    [Fact]
    public void CommandQuotesThePathAndAddsTheTrayArgument() =>
        Assert.Equal("\"C:\\Program Files\\Tunnela\\desktop\\Tunnela.Desktop.exe\" --tray", WindowsStartup.CommandFor(Executable));

    [Fact]
    public void CommandStartsWithTheQuotedExecutableAsTheUninstallerExpects() =>
        // installer-actions.ps1 removes a Tunnela entry by this prefix only, whatever arguments follow.
        Assert.StartsWith("\"" + WindowsStartup.InstalledExecutablePath + "\"",
            WindowsStartup.CommandFor(WindowsStartup.InstalledExecutablePath));

    [Fact]
    public void EnablingWritesOnlyTheTunnelaValue()
    {
        using (var run = _root.CreateSubKey(WindowsStartup.RunKeyPath))
            run.SetValue("OtherApp", "\"C:\\Other\\app.exe\"");

        WindowsStartup.SetEnabled(_root, Executable, enabled: true);

        Assert.True(WindowsStartup.IsEnabled(_root, Executable));
        using var check = _root.OpenSubKey(WindowsStartup.RunKeyPath)!;
        Assert.Equal(WindowsStartup.CommandFor(Executable), check.GetValue(WindowsStartup.ValueName));
        Assert.Equal(RegistryValueKind.String, check.GetValueKind(WindowsStartup.ValueName));
        Assert.Equal("\"C:\\Other\\app.exe\"", check.GetValue("OtherApp"));
    }

    [Fact]
    public void DisablingRemovesOnlyTheTunnelaValueAndToleratesAMissingEntry()
    {
        WindowsStartup.SetEnabled(_root, Executable, enabled: false); // Nothing registered yet.
        WindowsStartup.SetEnabled(_root, Executable, enabled: true);
        using (var run = _root.OpenSubKey(WindowsStartup.RunKeyPath, writable: true)!)
            run.SetValue("OtherApp", "\"C:\\Other\\app.exe\"");

        WindowsStartup.SetEnabled(_root, Executable, enabled: false);

        Assert.False(WindowsStartup.IsEnabled(_root, Executable));
        using var check = _root.OpenSubKey(WindowsStartup.RunKeyPath)!;
        Assert.Null(check.GetValue(WindowsStartup.ValueName));
        Assert.Equal("\"C:\\Other\\app.exe\"", check.GetValue("OtherApp"));
    }

    [Fact]
    public void EntryForAnotherCopyOrWithoutTheTrayArgumentReadsAsOff()
    {
        Assert.False(WindowsStartup.IsEnabled(_root, Executable)); // No Run key at all.
        using var run = _root.CreateSubKey(WindowsStartup.RunKeyPath);

        run.SetValue(WindowsStartup.ValueName, "\"G:\\copy\\Tunnela.Desktop.exe\" --tray");
        Assert.False(WindowsStartup.IsEnabled(_root, Executable));

        run.SetValue(WindowsStartup.ValueName, $"\"{Executable}\"");
        Assert.False(WindowsStartup.IsEnabled(_root, Executable));

        run.SetValue(WindowsStartup.ValueName, WindowsStartup.CommandFor(Executable.ToUpperInvariant()));
        Assert.True(WindowsStartup.IsEnabled(_root, Executable));
    }

    [Theory]
    [InlineData(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 7, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 6, 0, 0, 0 }, false)]
    [InlineData(new byte[0], false)]
    public void WindowsStartupApprovalIsReadFromTheFirstByte(byte[] state, bool blocked)
    {
        using (var approved = _root.CreateSubKey(WindowsStartup.ApprovedKeyPath))
            approved.SetValue(WindowsStartup.ValueName, state, RegistryValueKind.Binary);

        Assert.Equal(blocked, WindowsStartup.IsBlockedByWindows(_root));
    }

    [Fact]
    public void MissingApprovalMeansNotBlocked() => Assert.False(WindowsStartup.IsBlockedByWindows(_root));

    [Fact]
    public void StartupHintFollowsTheInterfaceLanguage()
    {
        Text.SetLanguage("en");
        var model = new UiModel();
        model.SetStartupState(enabled: true, canChange: true, hintKey: "StartupHintBlocked");
        Assert.True(model.StartWithWindows);
        Assert.True(model.CanChangeStartWithWindows);
        var english = model.StartupHint;

        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Text.SetLanguage("ru");
        model.RefreshLanguage();

        Assert.Contains(nameof(UiModel.StartupHint), changed);
        Assert.NotEqual(english, model.StartupHint);
        Assert.Equal(Text.Get("StartupHintBlocked"), model.StartupHint);
        Assert.True(model.StartWithWindows); // Changing the language never changes the startup entry.
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\Tunnela.Tests", writable: true);
        if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
            Registry.CurrentUser.DeleteSubKey(@"Software\Tunnela.Tests", throwOnMissingSubKey: false);
        Text.SetLanguage("en");
        CultureInfo.CurrentUICulture = _previous;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefault;
    }
}
