using Tunnela.Desktop.Localization;
using Forms = System.Windows.Forms;

namespace Tunnela.Desktop;

/// <summary>Owns the native tray icon and menu. The window schedules callbacks on its WPF dispatcher.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly System.Drawing.Icon _icon;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripItem _open;
    private readonly Forms.ToolStripMenuItem _connection;
    private readonly Forms.ToolStripMenuItem _exit;
    private readonly Forms.ToolStripItem _closeInterface;

    public TrayController(Action open, Action toggleConnection, Action exit, Action closeInterface)
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Tunnela.ico"))?.Stream
            ?? throw new InvalidOperationException(Text.Get("IconMissing"));
        using var sourceIcon = new System.Drawing.Icon(iconStream);
        // The clone remains owned by the tray after the resource stream is closed.
        _icon = (System.Drawing.Icon)sourceIcon.Clone();
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = Text.Get("TrayUnknown"), Visible = true };
        _menu = new Forms.ContextMenuStrip();
        _open = _menu.Items.Add(Text.Get("TrayOpen"), null, (_, _) => open());
        _connection = new Forms.ToolStripMenuItem(Text.Get("Connect"));
        _connection.Click += (_, _) => toggleConnection();
        _menu.Items.Add(_connection);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _exit = new Forms.ToolStripMenuItem(Text.Get("TrayExit"));
        _exit.Click += (_, _) => exit();
        _menu.Items.Add(_exit);
        _closeInterface = _menu.Items.Add(Text.Get("TrayCloseInterface"), null, (_, _) => closeInterface());
        _tray.ContextMenuStrip = _menu;

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
        };
        _tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
        };
        _tray.BalloonTipClicked += (_, _) => open();
    }

    public void Update(string statusTitle, string connectionText, bool canToggleConnection, bool canExit)
    {
        string tooltip = "Tunnela — " + statusTitle;
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        _connection.Text = connectionText;
        _open.Text = Text.Get("TrayOpen");
        _closeInterface.Text = Text.Get("TrayCloseInterface");
        _exit.Text = Text.Get("TrayExit");
        _connection.Enabled = canToggleConnection;
        _exit.Enabled = canExit;
    }

    public void ShowBackgroundHint() =>
        _tray.ShowBalloonTip(3000, Text.Get("BackgroundTitle"), Text.Get("BackgroundHint"), Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
