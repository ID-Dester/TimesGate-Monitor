using System.Drawing;
using Forms = System.Windows.Forms;

namespace TimesGateMonitor.App.Infrastructure;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;

    public TrayIconService()
    {
        _pauseItem = new Forms.ToolStripMenuItem("Pause monitoring")
        {
            CheckOnClick = true
        };
        _pauseItem.CheckedChanged += (_, _) => PauseChanged?.Invoke(_pauseItem.Checked);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open TGm", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Reconnect now", null, (_, _) => ReconnectRequested?.Invoke());
        menu.Items.Add("Open logs", null, (_, _) => OpenLogsRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = ExtractIcon(),
            Text = "TGm · Ready",
            Visible = true
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action<bool>? PauseChanged;

    public event Action? ReconnectRequested;

    public event Action? OpenLogsRequested;

    public event Action? ExitRequested;

    public void SetStatus(string status)
    {
        var text = $"TGm · {status}";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }

    private static Icon ExtractIcon()
    {
        var executable = Environment.ProcessPath;
        return executable is not null
            ? Icon.ExtractAssociatedIcon(executable) ?? SystemIcons.Application
            : SystemIcons.Application;
    }
}
