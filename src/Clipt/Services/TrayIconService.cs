using System.Drawing;
using Clipt.Models;
using WinForms = System.Windows.Forms;

namespace Clipt.Services;

public sealed class TrayIconService : ITrayIconService
{
    private readonly ISettingsService _settingsService;
    private readonly Icon _emptyIcon;
    private readonly Icon _hasDataIcon;
    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ToolStripMenuItem? _startModeItem;
    private bool _disposed;

    public event EventHandler? TrayIconClicked;
    public event EventHandler? OpenFullRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _emptyIcon = TrayIconHelper.CreateEmptyClipboardIcon();
        _hasDataIcon = TrayIconHelper.CreateHasDataIcon();
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_notifyIcon is not null)
            return;

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _emptyIcon,
            Text = "Clipt — Clipboard Inspector",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };

        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    public void UpdateIcon(bool hasClipboardData)
    {
        if (_notifyIcon is null)
            return;

        _notifyIcon.Icon = hasClipboardData ? _hasDataIcon : _emptyIcon;
        _notifyIcon.Text = hasClipboardData
            ? "Clipt — Clipboard has data"
            : "Clipt — Clipboard is empty";
    }

    private WinForms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        var openFullItem = new WinForms.ToolStripMenuItem("Open Full Window");
        openFullItem.Click += (_, _) => OpenFullRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(openFullItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var currentMode = _settingsService.LoadStartupMode();
        _startModeItem = new WinForms.ToolStripMenuItem(FormatStartModeLabel(currentMode));
        _startModeItem.Click += OnStartModeToggle;
        menu.Items.Add(_startModeItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnNotifyIconMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
            TrayIconClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnStartModeToggle(object? sender, EventArgs e)
    {
        var current = _settingsService.LoadStartupMode();
        var next = current == StartupMode.FullWindow
            ? StartupMode.Collapsed
            : StartupMode.FullWindow;

        _settingsService.SaveStartupMode(next);

        if (_startModeItem is not null)
            _startModeItem.Text = FormatStartModeLabel(next);
    }

    private static string FormatStartModeLabel(StartupMode mode) =>
        mode == StartupMode.FullWindow
            ? "Start Mode: Full Window"
            : "Start Mode: Collapsed";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_notifyIcon is not null)
        {
            _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _emptyIcon.Dispose();
        _hasDataIcon.Dispose();
    }
}
