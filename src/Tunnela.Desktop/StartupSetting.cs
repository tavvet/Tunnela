using Microsoft.Win32;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop;

/// <summary>
/// Keeps the Settings option and the per-user Run entry in step. The registry is the only stored state;
/// a change is applied immediately, independently of the Save button.
/// </summary>
internal sealed class StartupSetting
{
    private readonly UiModel _model;
    private readonly RegistryKey _userRoot;
    private readonly string? _processPath;
    private readonly Action<Action> _post;
    private bool _syncing;

    /// <param name="post">
    /// Runs work after the current dispatcher operation. The re-read after a change must not happen inside the
    /// checkbox binding's own source update, or the checkbox can keep showing a value that was not applied.
    /// </param>
    public StartupSetting(UiModel model, RegistryKey userRoot, string? processPath, Action<Action> post)
    {
        _model = model;
        _userRoot = userRoot;
        _processPath = processPath;
        _post = post;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiModel.StartWithWindows) && !_syncing) Apply();
        };
    }

    /// <summary>Copies the registry state into the model. Call again whenever the window is shown.</summary>
    public void Refresh()
    {
        var installed = WindowsStartup.IsInstalledCopy(_processPath);
        bool enabled = false, blocked = false;
        if (installed)
        {
            try
            {
                enabled = WindowsStartup.IsEnabled(_userRoot, WindowsStartup.InstalledExecutablePath);
                blocked = enabled && WindowsStartup.IsBlockedByWindows(_userRoot);
            }
            catch { /* An unreadable entry is shown as off; enabling rewrites it. */ }
        }
        var hint = !installed ? "StartupHintNotInstalled" : blocked ? "StartupHintBlocked" : "StartupHint";
        _syncing = true;
        try { _model.SetStartupState(enabled, installed, hint); }
        finally { _syncing = false; }
    }

    private void Apply()
    {
        var enable = _model.StartWithWindows;
        try
        {
            if (!WindowsStartup.IsInstalledCopy(_processPath)) throw new InvalidOperationException();
            WindowsStartup.SetEnabled(_userRoot, WindowsStartup.InstalledExecutablePath, enable);
            _model.Notice = Text.Get(enable ? "StartupEnabled" : "StartupDisabled");
        }
        catch
        {
            _model.Notice = Text.Get("StartupChangeFailed");
        }
        _post(Refresh);
    }
}
