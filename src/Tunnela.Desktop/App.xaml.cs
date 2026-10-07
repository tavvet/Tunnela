using Tunnela.Desktop.Localization;
using System.Threading;
using System.Windows;
using System.Security.Principal;

namespace Tunnela.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Text.SetLanguage("en");
        Tunnela.Contracts.ProfileCollection? collection = null;
        try { collection = new ProfileStore().Load(); Text.SetLanguage(collection.Preferences.Language); }
        catch { /* MainWindow preserves an unreadable store and disables saving. */ }
        base.OnStartup(e);
        using var identity = WindowsIdentity.GetCurrent();
        _mutex = new Mutex(true, "Global\\Tunnela.Desktop." + identity.User?.Value, out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show(Text.Get("AlreadyRunning"), "Tunnela", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        MainWindow = new MainWindow(collection);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
