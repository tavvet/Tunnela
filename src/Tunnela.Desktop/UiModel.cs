using System.Collections.ObjectModel;
using System.ComponentModel;
using Tunnela.Contracts;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop;

internal sealed class UiModel : ObservableModel
{
    public ObservableCollection<ProfileEditor> Profiles { get; } = [];
    public ObservableCollection<DiagnosticEntry> Logs { get; } = [];

    private ProfileEditor? _selected;
    private int _page;
    private string _notice = "";
    private string _statusTitle = Text.Get("CheckingService");
    private string _statusMessage = Text.Get("GettingStatus");
    private string _process = Text.Get("Unknown");
    private string _engine = Text.Get("Unknown");
    private bool _busy;
    private bool _serviceAvailable;
    private bool _minimizeToTray = true;
    private string _language = "en";
    private bool _hasStatus;
    private string? _unavailableMessageKey;

    public string Language
    {
        get => _language;
        set => Set(ref _language, Text.NormalizeLanguage(value));
    }

    public ProfileEditor? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            if (_selected is not null)
            {
                _selected.PropertyChanged -= SelectedChanged;
            }

            _selected = value;
            if (_selected is not null)
            {
                _selected.PropertyChanged += SelectedChanged;
            }

            Notify();
            Notify(nameof(HasProfile));
            Notify(nameof(SelectedName));
        }
    }

    private void SelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileEditor.DisplayName))
        {
            Notify(nameof(SelectedName));
        }
    }

    public bool HasProfile => Selected is not null;
    public string SelectedName => Selected?.DisplayName ?? Text.Get("SelectServerHint");

    public int Page
    {
        get => _page;
        set => Set(ref _page, value);
    }

    public string Notice
    {
        get => _notice;
        set => Set(ref _notice, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        set => Set(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public string ProcessText
    {
        get => _process;
        set => Set(ref _process, value);
    }

    public string EngineText
    {
        get => _engine;
        set => Set(ref _engine, value);
    }

    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                Notify(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public bool ServiceAvailable
    {
        get => _serviceAvailable;
        set
        {
            if (Set(ref _serviceAvailable, value))
            {
                Notify(nameof(ServiceText));
            }
        }
    }

    public string ServiceText => ServiceAvailable ? Text.Get("Available") : Text.Get("Unavailable");

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => Set(ref _minimizeToTray, value);
    }

    public TunnelSnapshot Snapshot { get; private set; } = new() { State = TunnelState.Unknown };

    public string ConnectText =>
        Snapshot.ProcessId.HasValue || Snapshot.State is TunnelState.Connected or TunnelState.Connecting
            or TunnelState.Reconnecting or TunnelState.Disconnecting
            ? Text.Get("Disconnect")
            : Text.Get("Connect");

    public string ActiveProfileText => Snapshot.ProfileId.HasValue && Snapshot.ProcessId.HasValue
        ? Text.Get("ActiveProfile", Snapshot.ProfileName ?? Text.Get("Unnamed"))
        : "";

    public void Apply(TunnelSnapshot snapshot)
    {
        Snapshot = snapshot;
        ServiceAvailable = true;
        _hasStatus = true;
        _unavailableMessageKey = null;

        StatusTitle = snapshot.State switch
        {
            TunnelState.Disconnected => Text.Get("VpnDisconnected"),
            TunnelState.Connecting => Text.Get("Connecting"),
            TunnelState.Connected => Text.Get("VpnConnected"),
            TunnelState.Reconnecting => Text.Get("Reconnecting"),
            TunnelState.Disconnecting => Text.Get("Disconnecting"),
            TunnelState.Error => Text.Get("AttentionRequired"),
            _ => Text.Get("StatusUnknown")
        };
        StatusMessage = Text.FromService(snapshot.Message, snapshot.MessageCode);
        ProcessText = snapshot.ProcessId.HasValue ? Text.Get("ProcessRunning") : Text.Get("ProcessStopped");
        EngineText = snapshot.EngineAvailable ? $"TrustTunnel {snapshot.EngineVersion}" : Text.Get("EngineMissing");
        Notify(nameof(ConnectText));
        Notify(nameof(ActiveProfileText));
    }

    public void Unavailable(string? messageKey = null)
    {
        _hasStatus = true;
        _unavailableMessageKey = messageKey;
        Snapshot = new TunnelSnapshot { State = TunnelState.Unknown };
        ServiceAvailable = false;
        StatusTitle = Text.Get("ServiceUnavailable");
        StatusMessage = Text.Get(messageKey ?? "ServiceUnavailableHint");
        ProcessText = Text.Get("Unknown");
        EngineText = Text.Get("Unknown");
        Notify(nameof(ConnectText));
        Notify(nameof(ActiveProfileText));
    }

    public void RefreshLanguage()
    {
        if (!_hasStatus)
        {
            StatusTitle = Text.Get("CheckingService");
            StatusMessage = Text.Get("GettingStatus");
            var unknown = Text.Get("Unknown");
            EngineText = unknown;
            ProcessText = unknown;
        }
        else if (ServiceAvailable)
        {
            Apply(Snapshot);
        }
        else
        {
            Unavailable(_unavailableMessageKey);
        }

        foreach (var profile in Profiles)
        {
            profile.RefreshLanguage();
        }

        Notify(nameof(SelectedName));
        Notify(nameof(ServiceText));
        Notify(nameof(ConnectText));
        Notify(nameof(ActiveProfileText));
    }
}
