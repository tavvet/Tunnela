using Tunnela.Desktop.Localization;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Tunnela.Contracts;
using Tunnela.Core;
using Forms = System.Windows.Forms;

namespace Tunnela.Desktop;

public partial class MainWindow : Window
{
    private readonly UiModel _model = new();
    private readonly ProfileStore _store = new();
    private readonly ServiceClient _client = new();
    private readonly SemaphoreSlim _serviceGate = new(1, 1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.ToolStripMenuItem _trayConnection;
    private readonly Forms.ToolStripMenuItem _trayExit;
    private readonly Forms.ToolStripItem _trayOpen;
    private readonly Forms.ToolStripItem _trayCloseInterface;
    private ProfileCollection _collection = new();
    private List<DiagnosticEntry> _logEntries = [];
    private bool _changingLanguage;
    private TaskCompletionSource? _exitPromptClosed;
    private bool _storageReadOnly, _syncPassword, _allowClose, _exitPending, _exitPromptOpen, _closed, _trayHintShown, _initialized;

    public MainWindow(ProfileCollection? initialCollection)
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width - 32);
        MinHeight = Math.Min(MinHeight, workArea.Height - 32);
        Width = Math.Min(Width, workArea.Width - 32);
        Height = Math.Min(Height, workArea.Height - 32);
        DataContext = _model;
        _model.PropertyChanged += Model_PropertyChanged;
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Tunnela.ico"))?.Stream
            ?? throw new InvalidOperationException(Text.Get("IconMissing"));
        using var sourceIcon = new System.Drawing.Icon(iconStream);
        _trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = Text.Get("TrayUnknown"), Visible = true };
        _trayMenu = new Forms.ContextMenuStrip();
        _trayOpen = _trayMenu.Items.Add(Text.Get("TrayOpen"), null, (_, _) => QueueTrayAction(ShowFromTray));
        _trayConnection = new Forms.ToolStripMenuItem(Text.Get("Connect"));
        _trayConnection.Click += (_, _) => QueueTrayAction(async () => { ShowFromTray(); await ToggleConnectionAsync(); });
        _trayMenu.Items.Add(_trayConnection);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayExit = new Forms.ToolStripMenuItem(Text.Get("TrayExit"));
        _trayExit.Click += (_, _) => QueueTrayAction(async () => await ExitAsync());
        _trayMenu.Items.Add(_trayExit);
        _trayCloseInterface = _trayMenu.Items.Add(Text.Get("TrayCloseInterface"), null, (_, _) => QueueTrayAction(CloseInterface));
        _tray.ContextMenuStrip = _trayMenu;
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) QueueTrayAction(ShowFromTray); };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) QueueTrayAction(ShowFromTray); };
        _tray.BalloonTipClicked += (_, _) => QueueTrayAction(ShowFromTray);
        UpdateTray();
        LoadProfiles(initialCollection);
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { if (_initialized) return; _initialized = true; _timer.Start(); await RefreshAsync(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _lifetime.Cancel();
            _tray.Visible = false;
            _tray.Dispose();
            _trayMenu.Dispose();
            _trayIcon.Dispose();
            _lifetime.Dispose();
            System.Windows.Application.Current.Shutdown();
        };
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (e.PropertyName == nameof(UiModel.Language) && _initialized && !_changingLanguage) ChangeLanguage();
        if (e.PropertyName is nameof(UiModel.IsBusy) or nameof(UiModel.ConnectText) or nameof(UiModel.ServiceAvailable)) UpdateTray();
        if (e.PropertyName == nameof(UiModel.Selected))
        {
            _syncPassword = true;
            ProfilePassword.Password = _model.Selected?.Password ?? "";
            _syncPassword = false;
        }
    }

    private void LoadProfiles(ProfileCollection? initialCollection)
    {
        try
        {
            var collection = initialCollection ?? throw new InvalidDataException();
            var editors = collection.Profiles.Select(p => new ProfileEditor(p)).ToList();
            _collection = collection;
            foreach (var editor in editors) _model.Profiles.Add(editor);
            _model.Selected = _model.Profiles.FirstOrDefault(p => p.Id == collection.Preferences.SelectedProfileId) ?? _model.Profiles.FirstOrDefault();
            _model.MinimizeToTray = collection.Preferences.MinimizeToTray;
            _model.Language = Text.NormalizeLanguage(collection.Preferences.Language);
            _model.Notice = _model.Profiles.Count == 0 ? Text.Get("AddServerHint") : Text.Get("ProfilesLoaded");
        }
        catch
        {
            _storageReadOnly = true;
            _model.Notice = Text.Get("ProfilesReadFailed");
        }
    }

    private void ChangeLanguage()
    {
        // Save only the language preference; switching language must not commit profile
        // drafts or other unsaved settings, and it never issues a service command.
        _changingLanguage = true;
        try
        {
            if (!Persist(_collection with { Preferences = _collection.Preferences with { Language = _model.Language } }))
            {
                _model.Language = Text.Language;
                return;
            }
            Text.SetLanguage(_model.Language);
            _model.RefreshLanguage();
            RefreshLogLanguage();
            UpdateTray();
            _model.Notice = Text.Get("LanguageSaved");
        }
        finally { _changingLanguage = false; }
    }

    private static string ErrorText(ServiceResponse response, string fallbackKey) =>
        response.Error is { } error ? Text.FromService(error, response.ErrorCode) : Text.Get(fallbackKey);

    // Post after the native tray/menu callback returns. Async handlers then use WPF's dispatcher context.
    private void QueueTrayAction(Action action)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => { if (!_closed && !_exitPromptOpen) action(); }));
    }

    private void ShowFromTray()
    {
        if (_closed) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private async Task RefreshAsync()
    {
        if (_closed || _allowClose || _exitPending || !await _serviceGate.WaitAsync(0)) return;
        try
        {
            var response = await _client.SendAsync("status", cancellationToken: _lifetime.Token);
            if (_closed) return;
            _model.Apply(response.Snapshot);
            if (!response.Success) _model.Notice = ErrorText(response, "StatusFailed");
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (ServiceUnavailableException exception) { if (!_closed) _model.Unavailable(exception.MessageKey); }
        catch { if (!_closed) _model.Unavailable(); }
        finally { UpdateTray(); _serviceGate.Release(); }
    }

    private void UpdateTray()
    {
        if (_closed) return;
        string status = "Tunnela — " + _model.StatusTitle;
        _tray.Text = status.Length > 63 ? status[..63] : status;
        _trayConnection.Text = _model.ConnectText;
        _trayOpen.Text = Text.Get("TrayOpen");
        _trayCloseInterface.Text = Text.Get("TrayCloseInterface");
        _trayExit.Text = Text.Get("TrayExit");
        _trayConnection.Enabled = !_model.IsBusy && !_exitPending && _model.ServiceAvailable;
        _trayExit.Enabled = !_model.IsBusy && !_exitPending;
    }

    private async Task RunServiceActionAsync(Func<Task> action)
    {
        if (_closed || _model.IsBusy) return;
        _model.IsBusy = true;
        await _serviceGate.WaitAsync();
        try { if (!_closed) await action(); }
        catch (OperationCanceledException) when (_closed) { }
        catch (ServiceUnavailableException exception)
        {
            if (_closed) return;
            _model.Unavailable(exception.MessageKey);
            _model.Notice = _model.StatusMessage;
        }
        catch (UnauthorizedAccessException)
        {
            if (_closed) return;
            _model.Unavailable();
            _model.Notice = Text.Get("ServiceVerificationFailed");
        }
        catch
        {
            if (_closed) return;
            _model.Unavailable();
            _model.Notice = Text.Get("ServiceNoResponse");
        }
        finally { UpdateTray(); _model.IsBusy = false; _serviceGate.Release(); }
    }

    private async Task ToggleConnectionAsync()
    {
        if (_closed || _exitPending || _model.IsBusy) return;
        await RunServiceActionAsync(async () =>
        {
            var actual = await _client.SendAsync("status", cancellationToken: _lifetime.Token);
            if (_closed) return;
            _model.Apply(actual.Snapshot);
            if (!actual.Success) { _model.Notice = ErrorText(actual, "ServiceNotReady"); return; }
            bool active = actual.Snapshot.ProcessId.HasValue || actual.Snapshot.State is TunnelState.Connected or TunnelState.Connecting or TunnelState.Reconnecting or TunnelState.Disconnecting;
            if (!active && actual.Snapshot.State == TunnelState.Unknown) { _model.Notice = Text.Get("RefreshUnknownStatus"); return; }
            ServerProfile? profile = null;
            if (!active)
            {
                if (_model.Selected is null) { _model.Page = 1; _model.Notice = Text.Get("SelectServer"); return; }
                if (!TrySaveSelected()) return;
                profile = _model.Selected.Source;
            }
            _model.Notice = active ? Text.Get("WaitingDisconnect") : Text.Get("WaitingConnect");
            var result = await _client.SendAsync(active ? "disconnect" : "connect", profile, _lifetime.Token);
            if (_closed) return;
            _model.Apply(result.Snapshot);
            _model.Notice = result.Success ? (active ? Text.Get("DisconnectCompleted") : Text.Get("ConnectRequested")) : ErrorText(result, "CommandRejected");
        });
    }

    private bool Persist(ProfileCollection next)
    {
        if (_storageReadOnly) { _model.Notice = Text.Get("StorageReadOnly"); return false; }
        try { _store.Save(next); _collection = next; return true; }
        catch { _model.Notice = Text.Get("SaveFailed"); return false; }
    }

    private AppPreferences CurrentPreferences() => _collection.Preferences with { SelectedProfileId = _model.Selected?.Id, MinimizeToTray = _model.MinimizeToTray, Language = _model.Language };

    private bool TrySaveSelected()
    {
        if (_model.Selected is not { } selected) return false;
        ServerProfile profile;
        try
        {
            profile = selected.ToProfile();
            var errors = Text.InCurrentLanguage(() => ProfileValidator.Validate(profile));
            if (errors.Count > 0) { _model.Notice = string.Join(" ", errors); return false; }
            byte[] command = JsonSerializer.SerializeToUtf8Bytes(new ServiceRequest { Command = "connect", Profile = profile });
            try
            {
                if (command.Length + 1 > ServiceProtocol.MaxMessageBytes)
                { _model.Notice = Text.Get("ProfileTooLarge"); return false; }
            }
            finally { CryptographicOperations.ZeroMemory(command); }
        }
        catch { _model.Notice = Text.Get("InvalidProfile"); return false; }
        var profiles = _collection.Profiles.Where(p => p.Id != profile.Id).Append(profile).ToList();
        if (!Persist(_collection with { Profiles = profiles, Preferences = CurrentPreferences() })) return false;
        selected.MarkSaved(profile);
        _model.Notice = Text.Get("ProfileSaved");
        return true;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var draft = new ProfileEditor(new ServerProfile { Name = Text.Get("NewConnection") }, true);
        _model.Profiles.Add(draft); _model.Selected = draft;
        _model.Notice = Text.Get("FillServerHint");
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySaveSelected();
    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        if (Persist(_collection with { Preferences = CurrentPreferences() })) _model.Notice = Text.Get("SettingsSaved");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_model.Selected is not { } selected) return;
        if (_model.Snapshot.ProfileId == selected.Id && (_model.Snapshot.ProcessId.HasValue || _model.Snapshot.State is TunnelState.Connecting or TunnelState.Connected or TunnelState.Reconnecting))
        { _model.Notice = Text.Get("DisconnectBeforeDelete"); return; }
        if (System.Windows.MessageBox.Show(this, Text.Get("DeleteProfilePrompt"), Text.Get("DeleteProfileTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var remaining = _collection.Profiles.Where(p => p.Id != selected.Id).ToList();
        if (!Persist(_collection with { Profiles = remaining, Preferences = CurrentPreferences() with { SelectedProfileId = remaining.FirstOrDefault()?.Id } })) return;
        _model.Profiles.Remove(selected); _model.Selected = _model.Profiles.FirstOrDefault();
        _model.Notice = Text.Get("ProfileDeleted");
    }

    private void ImportText(string text, string? name = null)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > ServiceProtocol.MaxMessageBytes) { _model.Notice = Text.Get("ConfigurationTooLarge"); return; }
            var parsed = Text.InCurrentLanguage(() => text.TrimStart().StartsWith("tt://", StringComparison.OrdinalIgnoreCase)
                ? DeepLinkProfileCodec.Import(text)
                : TomlProfileCodec.Import(text, name));
            var imported = parsed with { Id = Guid.NewGuid() };
            var errors = Text.InCurrentLanguage(() => ProfileValidator.Validate(imported));
            if (errors.Count > 0) { _model.Notice = Text.Get("ImportFailedPrefix") + string.Join(" ", errors); return; }
            var draft = new ProfileEditor(imported, true);
            _model.Profiles.Add(draft); _model.Selected = draft; _model.Page = 1;
            _model.Notice = Text.Get("ImportDraft");
        }
        catch (ProfileImportException exception) { _model.Notice = Text.Get("ImportFailedPrefix") + exception.Message + Text.Get("ProfilesUnchangedSuffix"); }
        catch { _model.Notice = Text.Get("ImportUnsupported"); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = Text.Get("ImportFileTitle"), Filter = Text.Get("ImportFileFilter"), CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            using var file = new FileStream(picker.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > ServiceProtocol.MaxMessageBytes) { _model.Notice = Text.Get("FileTooLarge"); return; }
            using var reader = new StreamReader(file, Encoding.UTF8, true);
            ImportText(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(picker.FileName));
        }
        catch { _model.Notice = Text.Get("FileReadFailed"); }
    }

    private void PasteImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportWindow { Owner = this };
        if (dialog.ShowDialog() == true) ImportText(dialog.ConfigurationText);
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncPassword && _model.Selected is { } selected) selected.Password = ProfilePassword.Password;
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await ToggleConnectionAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task ReadLogsAsync()
    {
        await RunServiceActionAsync(async () =>
        {
            var response = await _client.SendAsync("logs", cancellationToken: _lifetime.Token);
            if (_closed) return;
            _model.Apply(response.Snapshot);
            if (!response.Success) { _model.Notice = ErrorText(response, "LogsUnavailable"); return; }
            _logEntries = response.Logs.TakeLast(250).ToList();
            RefreshLogLanguage();
            _model.Notice = Text.Get("LogsUpdated");
        });
    }
    private async void Logs_Click(object sender, RoutedEventArgs e) => await ReadLogsAsync();

    private void RefreshLogLanguage()
    {
        _model.Logs.Clear();
        foreach (var entry in _logEntries)
            _model.Logs.Add(entry with { Message = Redact(Text.FromService(entry.Message, entry.MessageCode)) });
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = Text.Get("ExportTitle"), Filter = Text.Get("ExportFilter"), FileName = $"Tunnela-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var report = new StringBuilder();
            report.AppendLine(Text.Get("ReportTitle"));
            report.AppendLine(Text.Get("ReportDate", DateTimeOffset.Now.ToString("O")));
            report.AppendLine(Text.Get("ReportService", _model.ServiceText));
            report.AppendLine(Text.Get("ReportState", _model.StatusTitle));
            report.AppendLine(Text.Get("ReportProcess", _model.ProcessText));
            report.AppendLine(Text.Get("ReportEngine", _model.EngineText));
            report.AppendLine(Text.Get("ReportPrivacy"));
            report.AppendLine();
            // Only structured service diagnostics are accepted; raw engine output is never requested.
            foreach (var entry in _model.Logs) report.AppendLine($"{entry.Timestamp:O} [{entry.Level}] {Redact(entry.Message)}");
            File.WriteAllText(picker.FileName, report.ToString(), new UTF8Encoding(false));
            _model.Notice = Text.Get("ReportSaved");
        }
        catch { _model.Notice = Text.Get("ReportSaveFailed"); }
    }

    private string Redact(string message)
    {
        foreach (var profile in _collection.Profiles.Concat(_model.Profiles.Select(p => p.ToProfile())))
        {
            var privateValues = new[] { profile.Password, profile.Username, profile.Hostname, profile.Name, profile.CertificatePem }.Concat(profile.Addresses);
            foreach (var value in privateValues.Where(v => !string.IsNullOrEmpty(v))) message = message.Replace(value, Text.Get("Redacted"), StringComparison.OrdinalIgnoreCase);
        }
        return message;
    }

    private async Task ExitAsync()
    {
        if (_closed || _exitPromptOpen) return;
        ShowFromTray();
        if (_exitPending || _model.IsBusy) { _model.Notice = Text.Get("CommandInProgress"); return; }
        _exitPending = true;
        UpdateTray();
        try
        {
            if (HasUnsavedChanges && !ConfirmExit(Text.Get("UnsavedExitPrompt"), MessageBoxImage.Question)) return;
            _model.Notice = Text.Get("DisconnectBeforeExit");
            bool disconnected = false;
            await RunServiceActionAsync(async () =>
            {
                var response = await _client.SendAsync("disconnect", cancellationToken: _lifetime.Token);
                if (_closed) return;
                _model.Apply(response.Snapshot);
                disconnected = response.Success && response.Snapshot.State == TunnelState.Disconnected && response.Snapshot.ProcessId is null;
                if (!disconnected) _model.Notice = ErrorText(response, "DisconnectNotConfirmed");
            });
            if (_closed) return;
            if (_exitPromptClosed is { } prompt) await prompt.Task;
            if (_closed) return;
            if (disconnected) { CloseApplication(); return; }
            var message = !_model.ServiceAvailable
                ? Text.Get("UnavailableExitPrompt")
                : Text.Get("UnconfirmedExitPrompt");
            if (ConfirmExit(message, MessageBoxImage.Warning)) CloseApplication();
        }
        finally { _exitPending = false; UpdateTray(); }
    }

    private bool HasUnsavedChanges => _model.Profiles.Any(p => p.IsDirty) || _model.MinimizeToTray != _collection.Preferences.MinimizeToTray;

    private bool ConfirmExit(string message, MessageBoxImage image)
    {
        if (_closed || _exitPromptOpen) return false;
        ShowFromTray();
        _exitPromptOpen = true;
        var promptClosed = _exitPromptClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { return System.Windows.MessageBox.Show(this, message, Text.Get("ExitTitle"), MessageBoxButton.YesNo, image, MessageBoxResult.No) == MessageBoxResult.Yes; }
        finally
        {
            _exitPromptOpen = false;
            _exitPromptClosed = null;
            promptClosed.TrySetResult();
        }
    }

    private void CloseInterface()
    {
        if (_closed || _exitPromptOpen) return;
        string message = Text.Get("CloseInterfacePrompt");
        if (_model.IsBusy || _exitPending) message += Text.Get("PendingCommandSuffix");
        if (HasUnsavedChanges) message += Text.Get("UnsavedChangesSuffix");
        if (ConfirmExit(message, MessageBoxImage.Warning)) CloseApplication();
    }

    private void CloseApplication()
    {
        if (_closed) return;
        _allowClose = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_exitPromptOpen || _exitPending) return;
        if (_model.MinimizeToTray)
        {
            Hide();
            if (!_trayHintShown) { _tray.ShowBalloonTip(3000, Text.Get("BackgroundTitle"), Text.Get("BackgroundHint"), Forms.ToolTipIcon.Info); _trayHintShown = true; }
            return;
        }
        // Let WPF finish the cancelled Closing event before showing a dialog or closing again.
        QueueTrayAction(async () => await ExitAsync());
    }
}
