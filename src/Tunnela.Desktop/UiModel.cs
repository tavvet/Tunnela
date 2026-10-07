using Tunnela.Desktop.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tunnela.Contracts;

namespace Tunnela.Desktop;

internal abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Notify(name); return true;
    }
}

internal sealed class ProfileEditor : ObservableModel
{
    public ServerProfile Source { get; private set; }
    public Guid Id => Source.Id;
    private string _name, _hostname, _addresses, _username, _password, _dns, _rules, _certificate, _protocol;
    private bool _ipv6, _killSwitch, _selective;
    public bool IsDirty { get; private set; }
    public string DisplayName => (string.IsNullOrWhiteSpace(Name) ? Text.Get("NewConnection") : Name) + (IsDirty ? " *" : "");
    public string Name { get => _name; set { if (Set(ref _name, value)) { Dirty(); Notify(nameof(DisplayName)); } } }
    public string Hostname { get => _hostname; set { if (Set(ref _hostname, value)) Dirty(); } }
    public string Addresses { get => _addresses; set { if (Set(ref _addresses, value)) Dirty(); } }
    public string Username { get => _username; set { if (Set(ref _username, value)) Dirty(); } }
    public string Password { get => _password; set { if (Set(ref _password, value)) Dirty(); } }
    public string Dns { get => _dns; set { if (Set(ref _dns, value)) Dirty(); } }
    public string Rules { get => _rules; set { if (Set(ref _rules, value)) Dirty(); } }
    public string Certificate { get => _certificate; set { if (Set(ref _certificate, value)) Dirty(); } }
    public string Protocol { get => _protocol; set { if (Set(ref _protocol, value)) Dirty(); } }
    public bool Ipv6 { get => _ipv6; set { if (Set(ref _ipv6, value)) Dirty(); } }
    public bool KillSwitch { get => _killSwitch; set { if (Set(ref _killSwitch, value)) Dirty(); } }
    public bool Selective { get => _selective; set { if (Set(ref _selective, value)) { Dirty(); Notify(nameof(General)); Notify(nameof(RoutingHint)); } } }
    public bool General { get => !Selective; set { if (value) Selective = false; } }
    public string RoutingHint => Selective ? Text.Get("RoutingSelectiveHint") : Text.Get("RoutingGeneralHint");

    public ProfileEditor(ServerProfile profile, bool dirty = false)
    {
        Source = profile; _name = profile.Name; _hostname = profile.Hostname; _addresses = string.Join(Environment.NewLine, profile.Addresses);
        _username = profile.Username; _password = profile.Password; _dns = string.Join(Environment.NewLine, profile.DnsUpstreams);
        _rules = string.Join(Environment.NewLine, profile.Rules); _certificate = profile.CertificatePem; _protocol = profile.UpstreamProtocol;
        _ipv6 = profile.HasIpv6; _killSwitch = profile.KillSwitchEnabled; _selective = profile.RoutingMode == RoutingMode.Selective; IsDirty = dirty;
    }
    private void Dirty() { IsDirty = true; Notify(nameof(IsDirty)); Notify(nameof(DisplayName)); }
    private static List<string> Lines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    public ServerProfile ToProfile() => Source with { Name = Name.Trim(), Hostname = Hostname.Trim(), Addresses = Lines(Addresses), Username = Username, Password = Password, DnsUpstreams = Lines(Dns), Rules = Lines(Rules), CertificatePem = Certificate.Trim(), UpstreamProtocol = Protocol, HasIpv6 = Ipv6, KillSwitchEnabled = KillSwitch, RoutingMode = Selective ? RoutingMode.Selective : RoutingMode.General };
    public void MarkSaved(ServerProfile profile) { Source = profile; IsDirty = false; Notify(nameof(IsDirty)); Notify(nameof(DisplayName)); }
    public void RefreshLanguage() { Notify(nameof(DisplayName)); Notify(nameof(RoutingHint)); }
}

internal sealed class UiModel : ObservableModel
{
    public ObservableCollection<ProfileEditor> Profiles { get; } = [];
    public ObservableCollection<DiagnosticEntry> Logs { get; } = [];
    private ProfileEditor? _selected;
    private int _page;
    private string _notice = "", _statusTitle = Text.Get("CheckingService"), _statusMessage = Text.Get("GettingStatus"), _process = Text.Get("Unknown"), _engine = Text.Get("Unknown");
    private bool _busy, _serviceAvailable, _minimizeToTray = true;
    private string _language = "en";
    private bool _hasStatus;
    private string? _unavailableMessageKey;
    public string Language { get => _language; set => Set(ref _language, Text.NormalizeLanguage(value)); }
    public ProfileEditor? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (_selected is not null) _selected.PropertyChanged -= SelectedChanged;
            _selected = value;
            if (_selected is not null) _selected.PropertyChanged += SelectedChanged;
            Notify(); Notify(nameof(HasProfile)); Notify(nameof(SelectedName));
        }
    }
    private void SelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileEditor.DisplayName)) Notify(nameof(SelectedName));
    }
    public bool HasProfile => Selected is not null;
    public string SelectedName => Selected?.DisplayName ?? Text.Get("SelectServerHint");
    public int Page { get => _page; set => Set(ref _page, value); }
    public string Notice { get => _notice; set => Set(ref _notice, value); }
    public string StatusTitle { get => _statusTitle; set => Set(ref _statusTitle, value); }
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }
    public string ProcessText { get => _process; set => Set(ref _process, value); }
    public string EngineText { get => _engine; set => Set(ref _engine, value); }
    public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) Notify(nameof(CanInteract)); } }
    public bool CanInteract => !IsBusy;
    public bool ServiceAvailable { get => _serviceAvailable; set { if (Set(ref _serviceAvailable, value)) Notify(nameof(ServiceText)); } }
    public string ServiceText => ServiceAvailable ? Text.Get("Available") : Text.Get("Unavailable");
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }
    public TunnelSnapshot Snapshot { get; private set; } = new() { State = TunnelState.Unknown };
    public string ConnectText => Snapshot.ProcessId.HasValue || Snapshot.State is TunnelState.Connected or TunnelState.Connecting or TunnelState.Reconnecting or TunnelState.Disconnecting ? Text.Get("Disconnect") : Text.Get("Connect");
    public string ActiveProfileText => Snapshot.ProfileId.HasValue && Snapshot.ProcessId.HasValue ? Text.Get("ActiveProfile", Snapshot.ProfileName ?? Text.Get("Unnamed")) : "";
    public void Apply(TunnelSnapshot snapshot)
    {
        Snapshot = snapshot; ServiceAvailable = true; _hasStatus = true; _unavailableMessageKey = null;
        StatusTitle = snapshot.State switch { TunnelState.Disconnected => Text.Get("VpnDisconnected"), TunnelState.Connecting => Text.Get("Connecting"), TunnelState.Connected => Text.Get("VpnConnected"), TunnelState.Reconnecting => Text.Get("Reconnecting"), TunnelState.Disconnecting => Text.Get("Disconnecting"), TunnelState.Error => Text.Get("AttentionRequired"), _ => Text.Get("StatusUnknown") };
        StatusMessage = Text.FromService(snapshot.Message, snapshot.MessageCode);
        ProcessText = snapshot.ProcessId.HasValue ? Text.Get("ProcessRunning") : Text.Get("ProcessStopped");
        EngineText = snapshot.EngineAvailable ? $"TrustTunnel {snapshot.EngineVersion}" : Text.Get("EngineMissing");
        Notify(nameof(ConnectText)); Notify(nameof(ActiveProfileText));
    }
    public void Unavailable(string? messageKey = null)
    {
        _hasStatus = true; _unavailableMessageKey = messageKey;
        Snapshot = new TunnelSnapshot { State = TunnelState.Unknown };
        ServiceAvailable = false; StatusTitle = Text.Get("ServiceUnavailable");
        StatusMessage = Text.Get(messageKey ?? "ServiceUnavailableHint");
        ProcessText = Text.Get("Unknown"); EngineText = Text.Get("Unknown"); Notify(nameof(ConnectText)); Notify(nameof(ActiveProfileText));
    }

    public void RefreshLanguage()
    {
        if (!_hasStatus)
        {
            StatusTitle = Text.Get("CheckingService"); StatusMessage = Text.Get("GettingStatus");
            ProcessText = EngineText = Text.Get("Unknown");
        }
        else if (ServiceAvailable) Apply(Snapshot);
        else Unavailable(_unavailableMessageKey);
        foreach (var profile in Profiles) profile.RefreshLanguage();
        Notify(nameof(SelectedName)); Notify(nameof(ServiceText)); Notify(nameof(ConnectText)); Notify(nameof(ActiveProfileText));
    }
}
