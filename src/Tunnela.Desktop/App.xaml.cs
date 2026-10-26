using Tunnela.Desktop.Localization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Security.Principal;

namespace Tunnela.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showRequest;
    private RegisteredWaitHandle? _showRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        var trayStart = WindowsStartup.IsTrayStart(e.Args);
        Text.SetLanguage("en");
        Tunnela.Contracts.ProfileCollection? collection = null;
        try { collection = new ProfileStore().Load(); Text.SetLanguage(collection.Preferences.Language); }
        catch { /* MainWindow preserves an unreadable store and disables saving. */ }
        base.OnStartup(e);
        using var identity = WindowsIdentity.GetCurrent();
        // Created before the mutex, so a later instance in this session always finds the running one's event.
        // Signalling it only asks that instance to show its window; it carries no other command.
        // If the name is unusable (for example, held by an object this user cannot open), only window
        // activation is lost; the single-instance mutex below still applies.
        var showRequestCreated = true;
        try
        {
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset,
                SingleInstance.ShowRequestName(identity.User?.Value), out showRequestCreated);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            _showRequest = null;
        }
        _mutex = new Mutex(true, SingleInstance.MutexName(identity.User?.Value), out _ownsMutex);
        if (!_ownsMutex)
        {
            switch (SingleInstance.ForSecondInstance(trayStart, _showRequest is not null, showRequestCreated))
            {
                case SecondInstanceAction.ActivateRunningCopy:
                    SingleInstance.AllowRunningCopyToTakeForeground();
                    _showRequest!.Set();
                    break;
                case SecondInstanceAction.ShowAlreadyRunningMessage:
                    System.Windows.MessageBox.Show(Text.Get("AlreadyRunning"), "Tunnela", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
            Shutdown();
            return;
        }
        var window = new MainWindow(collection);
        MainWindow = window;
        if (_showRequest is not null)
            _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showRequest, (_, _) => window.RequestShow(),
                null, Timeout.Infinite, executeOnlyOnce: false);
        if (trayStart) window.StartInTray();
        else window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showRegistration?.Unregister(null);
        _showRequest?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
