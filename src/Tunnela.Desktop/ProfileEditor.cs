using Tunnela.Contracts;
using Tunnela.Desktop.Localization;

namespace Tunnela.Desktop;

internal sealed class ProfileEditor : ObservableModel
{
    // Keep the complete imported/saved baseline, including fields this editor does not expose.
    // ToProfile overlays only editable fields; MarkSaved advances the baseline after a successful save.
    public ServerProfile Source { get; private set; }
    public Guid Id => Source.Id;

    private string _name;
    private string _hostname;
    private string _addresses;
    private string _username;
    private string _password;
    private string _dns;
    private string _rules;
    private string _certificate;
    private string _protocol;
    private bool _ipv6;
    private bool _killSwitch;
    private bool _selective;

    public bool IsDirty { get; private set; }

    public string DisplayName =>
        (string.IsNullOrWhiteSpace(Name) ? Text.Get("NewConnection") : Name) + (IsDirty ? " *" : "");

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                Dirty();
                Notify(nameof(DisplayName));
            }
        }
    }

    public string Hostname
    {
        get => _hostname;
        set
        {
            if (Set(ref _hostname, value))
            {
                Dirty();
            }
        }
    }

    public string Addresses
    {
        get => _addresses;
        set
        {
            if (Set(ref _addresses, value))
            {
                Dirty();
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (Set(ref _username, value))
            {
                Dirty();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (Set(ref _password, value))
            {
                Dirty();
            }
        }
    }

    public string Dns
    {
        get => _dns;
        set
        {
            if (Set(ref _dns, value))
            {
                Dirty();
            }
        }
    }

    public string Rules
    {
        get => _rules;
        set
        {
            if (Set(ref _rules, value))
            {
                Dirty();
            }
        }
    }

    public string Certificate
    {
        get => _certificate;
        set
        {
            if (Set(ref _certificate, value))
            {
                Dirty();
            }
        }
    }

    public string Protocol
    {
        get => _protocol;
        set
        {
            if (Set(ref _protocol, value))
            {
                Dirty();
            }
        }
    }

    public bool Ipv6
    {
        get => _ipv6;
        set
        {
            if (Set(ref _ipv6, value))
            {
                Dirty();
            }
        }
    }

    public bool KillSwitch
    {
        get => _killSwitch;
        set
        {
            if (Set(ref _killSwitch, value))
            {
                Dirty();
            }
        }
    }

    public bool Selective
    {
        get => _selective;
        set
        {
            if (Set(ref _selective, value))
            {
                Dirty();
                Notify(nameof(General));
                Notify(nameof(RoutingHint));
            }
        }
    }

    public bool General
    {
        get => !Selective;
        set
        {
            if (value)
            {
                Selective = false;
            }
        }
    }

    public string RoutingHint => Selective
        ? Text.Get("RoutingSelectiveHint")
        : Text.Get("RoutingGeneralHint");

    public ProfileEditor(ServerProfile profile, bool dirty = false)
    {
        Source = profile;
        _name = profile.Name;
        _hostname = profile.Hostname;
        _addresses = string.Join(Environment.NewLine, profile.Addresses);
        _username = profile.Username;
        _password = profile.Password;
        _dns = string.Join(Environment.NewLine, profile.DnsUpstreams);
        _rules = string.Join(Environment.NewLine, profile.Rules);
        _certificate = profile.CertificatePem;
        _protocol = profile.UpstreamProtocol;
        _ipv6 = profile.HasIpv6;
        _killSwitch = profile.KillSwitchEnabled;
        _selective = profile.RoutingMode == RoutingMode.Selective;
        IsDirty = dirty;
    }

    private void Dirty()
    {
        IsDirty = true;
        Notify(nameof(IsDirty));
        Notify(nameof(DisplayName));
    }

    private static List<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    public ServerProfile ToProfile() => Source with
    {
        Name = Name.Trim(),
        Hostname = Hostname.Trim(),
        Addresses = Lines(Addresses),
        Username = Username,
        Password = Password,
        DnsUpstreams = Lines(Dns),
        Rules = Lines(Rules),
        CertificatePem = Certificate.Trim(),
        UpstreamProtocol = Protocol,
        HasIpv6 = Ipv6,
        KillSwitchEnabled = KillSwitch,
        RoutingMode = Selective ? RoutingMode.Selective : RoutingMode.General
    };

    public void MarkSaved(ServerProfile profile)
    {
        Source = profile;
        IsDirty = false;
        Notify(nameof(IsDirty));
        Notify(nameof(DisplayName));
    }

    public void RefreshLanguage()
    {
        Notify(nameof(DisplayName));
        Notify(nameof(RoutingHint));
    }
}
