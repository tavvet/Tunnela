using Microsoft.Win32;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop.Tests;

/// <summary>
/// Uses a private key under HKCU\Software\Tunnela.Tests in place of HKCU, deleted afterwards. The real
/// Run key is never read or written, and no window or dispatcher is created.
/// </summary>
public sealed class StartupSettingTests : IDisposable
{
    private readonly string _testKeyPath = @"Software\Tunnela.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly UiModel _model = new();
    private readonly List<Action> _posted = [];

    public StartupSettingTests()
    {
        Text.SetLanguage("en");
        _root = Registry.CurrentUser.CreateSubKey(_testKeyPath, writable: true);
    }

    [Fact]
    public void InstalledCopyWritesTheEntryAndDefersTheReread()
    {
        var setting = Create(_root, WindowsStartup.InstalledExecutablePath);

        _model.StartWithWindows = true; // As the checkbox binding does.

        Assert.True(WindowsStartup.IsEnabled(_root, WindowsStartup.InstalledExecutablePath));
        Assert.Equal(Text.Get("StartupEnabled"), _model.Notice);
        Assert.Single(_posted);
        RunPosted();
        Assert.True(_model.StartWithWindows);
        Assert.Equal(Text.Get("StartupHint"), _model.StartupHint);

        _model.StartWithWindows = false;
        Assert.False(WindowsStartup.IsEnabled(_root, WindowsStartup.InstalledExecutablePath));
        Assert.Equal(Text.Get("StartupDisabled"), _model.Notice);
        RunPosted();
        Assert.False(_model.StartWithWindows);
        GC.KeepAlive(setting);
    }

    [Fact]
    public void FailedWriteIsReportedAndTheCheckboxIsResetAfterTheBindingUpdate()
    {
        using var readOnlyRoot = Registry.CurrentUser.OpenSubKey(_testKeyPath, writable: false)!;
        var setting = Create(readOnlyRoot, WindowsStartup.InstalledExecutablePath);

        _model.StartWithWindows = true;

        Assert.Equal(Text.Get("StartupChangeFailed"), _model.Notice);
        // Not changed while the binding is still updating its source...
        Assert.True(_model.StartWithWindows);
        // ...but reset once the posted re-read runs.
        RunPosted();
        Assert.False(_model.StartWithWindows);
        Assert.False(WindowsStartup.IsEnabled(_root, WindowsStartup.InstalledExecutablePath));
        GC.KeepAlive(setting);
    }

    [Fact]
    public void CopyOutsideTheInstallationCannotRegisterItself()
    {
        var setting = Create(_root, @"G:\work\.artifacts\publish\tunnela\desktop\Tunnela.Desktop.exe");

        Assert.False(_model.CanChangeStartWithWindows);
        Assert.Equal(Text.Get("StartupHintNotInstalled"), _model.StartupHint);

        _model.StartWithWindows = true; // Even if the disabled checkbox were changed.
        Assert.Equal(Text.Get("StartupChangeFailed"), _model.Notice);
        using (var run = _root.OpenSubKey(WindowsStartup.RunKeyPath))
            Assert.Null(run?.GetValue(WindowsStartup.ValueName));
        RunPosted();
        Assert.False(_model.StartWithWindows);
        GC.KeepAlive(setting);
    }

    [Fact]
    public void EntryTurnedOffByWindowsStaysOnWithAnExplanation()
    {
        WindowsStartup.SetEnabled(_root, WindowsStartup.InstalledExecutablePath, enabled: true);
        using (var approved = _root.CreateSubKey(WindowsStartup.ApprovedKeyPath))
            approved.SetValue(WindowsStartup.ValueName, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);

        var setting = Create(_root, WindowsStartup.InstalledExecutablePath);

        Assert.True(_model.StartWithWindows);
        Assert.True(_model.CanChangeStartWithWindows);
        Assert.Equal(Text.Get("StartupHintBlocked"), _model.StartupHint);
        Assert.Empty(_posted); // Reading the state never writes or schedules anything.
        GC.KeepAlive(setting);
    }

    private StartupSetting Create(RegistryKey root, string processPath)
    {
        var setting = new StartupSetting(_model, root, processPath, _posted.Add);
        setting.Refresh();
        return setting;
    }

    private void RunPosted()
    {
        var actions = _posted.ToList();
        _posted.Clear();
        foreach (var action in actions) action();
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\Tunnela.Tests", writable: true);
        if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
            Registry.CurrentUser.DeleteSubKey(@"Software\Tunnela.Tests", throwOnMissingSubKey: false);
    }
}
