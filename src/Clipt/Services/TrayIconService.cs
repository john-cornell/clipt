using System.Drawing;
using Clipt.Models;
using WinForms = System.Windows.Forms;

namespace Clipt.Services;

public sealed class TrayIconService : ITrayIconService
{
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _appLogger;
    private readonly Icon _emptyIcon;
    private readonly Icon _hasDataIcon;
    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ContextMenuStrip? _trayContextMenu;
    private WinForms.ToolStripMenuItem? _startModeItem;
    private WinForms.ToolStripMenuItem? _runOnStartupItem;
    private WinForms.ToolStripMenuItem? _purgeHistoryItem;
    private WinForms.ToolStripMenuItem? _clearClipboardWhenClearingHistoryItem;
    private WinForms.ToolStripMenuItem? _historyTypeSubmenuRoot;
    private WinForms.ToolStripMenuItem? _maxEntriesSubmenuRoot;
    private WinForms.ToolStripMenuItem? _maxSizeSubmenuRoot;
    private WinForms.ToolStripMenuItem? _overflowModeSubmenuRoot;
    private WinForms.ToolStripMenuItem? _formatCaptureCapSubmenuRoot;
    private WinForms.ToolStripMenuItem? _formatOversizeModeSubmenuRoot;
    private WinForms.ToolStripMenuItem? _logLevelSubmenuRoot;
    private WinForms.ToolStripMenuItem? _showTabsSubmenuRoot;
    private Action<string?, bool>? _onShowTabToggled;
    private Action<bool>? _syncClearClipboardPreference;
    private bool _disposed;

    private static readonly int[] MaxEntriesOptions = [5, 10, 25, 50];
    private static readonly (string Label, long Bytes)[] MaxSizeOptions =
    [
        ("50 MB", 50L * 1024 * 1024),
        ("100 MB", 100L * 1024 * 1024),
        ("250 MB", 250L * 1024 * 1024),
        ("500 MB", 500L * 1024 * 1024),
        ("Unlimited", 0),
    ];

    private static readonly (string Label, long Bytes)[] FormatCaptureCapOptions =
    [
        ("16 KB", 16 * 1024),
        ("64 KB", 64 * 1024),
        ("256 KB", 256 * 1024),
        ("1 MB", 1024 * 1024),
        ("4 MB", 4 * 1024 * 1024),
        ("16 MB", 16 * 1024 * 1024),
        ("64 MB", 64 * 1024 * 1024),
        ("256 MB", 256L * 1024 * 1024),
        ("500 MB", 500L * 1024 * 1024),
        ("1 GB", 1024L * 1024 * 1024),
        ("Unlimited (~2 GB)", int.MaxValue),
    ];

    private static readonly ContentType[] ToggleableContentTypes =
    [
        ContentType.Text,
        ContentType.Image,
        ContentType.Files,
        ContentType.Other,
    ];

    public event EventHandler? TrayIconClicked;
    public event EventHandler? OpenFullRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ClearHistoryRequested;

    public TrayIconService(ISettingsService settingsService, IAppLogger appLogger)
    {
        _settingsService = settingsService;
        _appLogger = appLogger;
        _emptyIcon = TrayIconHelper.CreateEmptyClipboardIcon();
        _hasDataIcon = TrayIconHelper.CreateHasDataIcon();
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_notifyIcon is not null)
            return;

        var menu = BuildContextMenu();
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _emptyIcon,
            Text = "Clipt — Clipboard Inspector",
            Visible = true,
        };

        // Do not assign ContextMenuStrip — it blocks left-click open on Windows 10/11.
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
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

    public void SetClearClipboardPreferenceSync(Action<bool>? sync)
    {
        _syncClearClipboardPreference = sync;
    }

    public void SetShowTabsMenuToggleHandler(Action<string?, bool>? handler)
    {
        _onShowTabToggled = handler;
    }

    public void RebuildShowTabsMenu(IReadOnlyList<TrayTabShowMenuEntry> entries)
    {
        if (_showTabsSubmenuRoot is null)
            return;

        _showTabsSubmenuRoot.DropDownItems.Clear();

        foreach (TrayTabShowMenuEntry entry in entries)
        {
            var item = new WinForms.ToolStripMenuItem(entry.Header)
            {
                Checked = entry.IsVisible,
                Tag = entry.PluginId ?? string.Empty,
            };
            item.Click += OnShowTabMenuItemClick;
            _showTabsSubmenuRoot.DropDownItems.Add(item);
        }
    }

    public void SetClearClipboardWhenClearingHistoryChecked(bool value)
    {
        if (_clearClipboardWhenClearingHistoryItem is not null)
            _clearClipboardWhenClearingHistoryItem.Checked = value;
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

        _runOnStartupItem = new WinForms.ToolStripMenuItem("Run on Startup")
        {
            Checked = _settingsService.LoadRunOnStartup(),
        };
        _runOnStartupItem.Click += OnRunOnStartupToggle;
        menu.Items.Add(_runOnStartupItem);

        _purgeHistoryItem = new WinForms.ToolStripMenuItem("Purge History on Startup")
        {
            Checked = _settingsService.LoadPurgeHistoryOnStartup(),
        };
        _purgeHistoryItem.Click += OnPurgeHistoryToggle;
        menu.Items.Add(_purgeHistoryItem);

        _clearClipboardWhenClearingHistoryItem = new WinForms.ToolStripMenuItem("Clear Clipboard When Clearing History")
        {
            Checked = _settingsService.LoadClearClipboardWhenClearingHistory(),
        };
        _clearClipboardWhenClearingHistoryItem.Click += OnClearClipboardWhenClearingHistoryToggle;
        menu.Items.Add(_clearClipboardWhenClearingHistoryItem);

        menu.Items.Add(BuildShowInNotificationSubmenu());

        menu.Items.Add(BuildHistoryTypeSubmenu());

        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(BuildMaxEntriesSubmenu());
        menu.Items.Add(BuildMaxSizeSubmenu());
        menu.Items.Add(BuildOverflowModeSubmenu());
        menu.Items.Add(BuildFormatCaptureCapSubmenu());
        menu.Items.Add(BuildFormatOversizeModeSubmenu());

        menu.Items.Add(BuildLoggingSubmenu());

        var clearHistoryItem = new WinForms.ToolStripMenuItem("Clear History");
        clearHistoryItem.Click += (_, _) => ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(clearHistoryItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        menu.ItemClicked += OnTrayContextMenuItemClicked;
        _trayContextMenu = menu;

        return menu;
    }

    /// <summary>
    /// Keeps the tray context menu open after choosing settings (toggles and submenu picks) so several options can be changed in one visit.
    /// Single-action items (open window, clear history, exit) still dismiss the menu normally.
    /// </summary>
    private void OnTrayContextMenuItemClicked(object? sender, WinForms.ToolStripItemClickedEventArgs e)
    {
        if (sender is not WinForms.ContextMenuStrip menu || !ShouldKeepTrayMenuOpen(e.ClickedItem))
            return;

        menu.AutoClose = false;
        menu.BeginInvoke(() =>
        {
            try
            {
                if (!menu.IsDisposed)
                    menu.AutoClose = true;
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }

    private bool ShouldKeepTrayMenuOpen(WinForms.ToolStripItem? clicked)
    {
        if (clicked is null or WinForms.ToolStripSeparator)
            return false;

        if (ReferenceEquals(clicked, _startModeItem)
            || ReferenceEquals(clicked, _runOnStartupItem)
            || ReferenceEquals(clicked, _purgeHistoryItem)
            || ReferenceEquals(clicked, _clearClipboardWhenClearingHistoryItem))
            return true;

        if (ReferenceEquals(clicked, _showTabsSubmenuRoot))
            return true;

        if (clicked is WinForms.ToolStripMenuItem leaf && leaf.OwnerItem == _showTabsSubmenuRoot)
            return true;

        if (ReferenceEquals(clicked, _historyTypeSubmenuRoot)
            || ReferenceEquals(clicked, _maxEntriesSubmenuRoot)
            || ReferenceEquals(clicked, _maxSizeSubmenuRoot)
            || ReferenceEquals(clicked, _overflowModeSubmenuRoot)
            || ReferenceEquals(clicked, _formatCaptureCapSubmenuRoot)
            || ReferenceEquals(clicked, _formatOversizeModeSubmenuRoot)
            || ReferenceEquals(clicked, _logLevelSubmenuRoot))
            return true;

        if (clicked is WinForms.ToolStripMenuItem nested && nested.OwnerItem is WinForms.ToolStripMenuItem owner)
        {
            return ReferenceEquals(owner, _historyTypeSubmenuRoot)
                || ReferenceEquals(owner, _maxEntriesSubmenuRoot)
                || ReferenceEquals(owner, _maxSizeSubmenuRoot)
                || ReferenceEquals(owner, _overflowModeSubmenuRoot)
                || ReferenceEquals(owner, _formatCaptureCapSubmenuRoot)
                || ReferenceEquals(owner, _formatOversizeModeSubmenuRoot)
                || ReferenceEquals(owner, _logLevelSubmenuRoot)
                || ReferenceEquals(owner, _showTabsSubmenuRoot);
        }

        return false;
    }

    private WinForms.ToolStripMenuItem BuildShowInNotificationSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("Show tabs");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _showTabsSubmenuRoot = parent;
        return parent;
    }

    private void OnShowTabMenuItemClick(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem item)
            return;

        bool next = !item.Checked;
        item.Checked = next;
        string tag = item.Tag as string ?? string.Empty;
        string? pluginId = tag.Length == 0 ? null : tag;
        _onShowTabToggled?.Invoke(pluginId, next);
    }

    private WinForms.ToolStripMenuItem BuildHistoryTypeSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("Enable History for Type");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _historyTypeSubmenuRoot = parent;
        var disabled = _settingsService.LoadDisabledHistoryTypes();

        foreach (ContentType ct in ToggleableContentTypes)
        {
            var item = new WinForms.ToolStripMenuItem(ct.ToString())
            {
                Checked = !disabled.Contains(ct),
                Tag = ct,
            };
            item.Click += OnHistoryTypeToggle;
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private WinForms.ToolStripMenuItem BuildMaxEntriesSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("Max History Entries");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _maxEntriesSubmenuRoot = parent;
        int current = _settingsService.LoadMaxHistoryEntries();

        foreach (int value in MaxEntriesOptions)
        {
            var item = new WinForms.ToolStripMenuItem(value.ToString())
            {
                Checked = value == current,
                Tag = value,
            };
            item.Click += OnMaxEntriesOptionClicked;
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private WinForms.ToolStripMenuItem BuildMaxSizeSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("Max History Size");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _maxSizeSubmenuRoot = parent;
        long current = _settingsService.LoadMaxHistorySizeBytes();

        foreach (var (label, bytes) in MaxSizeOptions)
        {
            var item = new WinForms.ToolStripMenuItem(label)
            {
                Checked = bytes == current,
                Tag = bytes,
            };
            item.Click += OnMaxSizeOptionClicked;
            parent.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(new WinForms.ToolStripSeparator());
        var customLimitItem = new WinForms.ToolStripMenuItem("Custom limit (MB)…");
        customLimitItem.Click += OnCustomMaxHistorySizeClicked;
        parent.DropDownItems.Add(customLimitItem);

        return parent;
    }

    private WinForms.ToolStripMenuItem BuildOverflowModeSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("When total history exceeds cap");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _overflowModeSubmenuRoot = parent;
        HistorySizeOverflowMode current = _settingsService.LoadHistorySizeOverflowMode();

        foreach (var (label, mode) in new (string Label, HistorySizeOverflowMode Mode)[]
        {
            ("Make room (remove oldest clips)", HistorySizeOverflowMode.TrimOldest),
            ("Allow going over the limit", HistorySizeOverflowMode.AllowOverLimit),
            ("Ask me each time", HistorySizeOverflowMode.AskEachTime),
        })
        {
            var item = new WinForms.ToolStripMenuItem(label)
            {
                Checked = mode == current,
                Tag = mode,
            };
            item.Click += OnOverflowModeOptionClicked;
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private WinForms.ToolStripMenuItem BuildFormatCaptureCapSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("Max capture per format (not raw bitmaps)");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _formatCaptureCapSubmenuRoot = parent;
        long current = _settingsService.LoadMaxClipboardFormatCaptureBytes();

        foreach (var (label, bytes) in FormatCaptureCapOptions)
        {
            var item = new WinForms.ToolStripMenuItem(label)
            {
                Checked = bytes == current,
                Tag = bytes,
            };
            item.Click += OnFormatCaptureCapClicked;
            parent.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(new WinForms.ToolStripSeparator());
        var customItem = new WinForms.ToolStripMenuItem("Custom capture cap (MB)…");
        customItem.Click += OnCustomFormatCaptureCapClicked;
        parent.DropDownItems.Add(customItem);

        return parent;
    }

    private WinForms.ToolStripMenuItem BuildFormatOversizeModeSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("When a format exceeds capture cap");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _formatOversizeModeSubmenuRoot = parent;
        ClipboardFormatOversizeMode current = _settingsService.LoadClipboardFormatOversizeMode();

        foreach (var (label, mode) in new (string Label, ClipboardFormatOversizeMode Mode)[]
        {
            ("Always use capture cap (no prompt)", ClipboardFormatOversizeMode.TruncateToCap),
            ("Ask when a format exceeds the cap", ClipboardFormatOversizeMode.AskEachFormat),
        })
        {
            var item = new WinForms.ToolStripMenuItem(label)
            {
                Checked = mode == current,
                Tag = mode,
            };
            item.Click += OnFormatOversizeModeClicked;
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private WinForms.ToolStripMenuItem BuildLoggingSubmenu()
    {
        var parent = new WinForms.ToolStripMenuItem("Log level");
        parent.DropDownDirection = WinForms.ToolStripDropDownDirection.Left;
        _logLevelSubmenuRoot = parent;
        AppLogLevel current = _settingsService.LoadLogLevel();

        foreach (var (label, level) in new (string Label, AppLogLevel Level)[]
        {
            ("Off", AppLogLevel.Off),
            ("Warn", AppLogLevel.Warn),
            ("Debug", AppLogLevel.Debug),
        })
        {
            var item = new WinForms.ToolStripMenuItem(label)
            {
                Checked = level == current,
                Tag = level,
            };
            item.Click += OnLogLevelClicked;
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private void OnLogLevelClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not AppLogLevel level)
            return;

        _settingsService.SaveLogLevel(level);
        _appLogger.SetLevel(level);

        if (clicked.OwnerItem is WinForms.ToolStripMenuItem parent)
        {
            foreach (WinForms.ToolStripMenuItem sibling in parent.DropDownItems)
                sibling.Checked = ReferenceEquals(sibling, clicked);
        }
    }

    private void OnRunOnStartupToggle(object? sender, EventArgs e)
    {
        if (_runOnStartupItem is null)
            return;

        bool next = !_runOnStartupItem.Checked;
        bool ok = _settingsService.SaveRunOnStartup(next);
        _runOnStartupItem.Checked = ok ? next : _settingsService.LoadRunOnStartup();
    }

    private void OnPurgeHistoryToggle(object? sender, EventArgs e)
    {
        if (_purgeHistoryItem is null)
            return;

        bool next = !_purgeHistoryItem.Checked;
        _settingsService.SavePurgeHistoryOnStartup(next);
        _purgeHistoryItem.Checked = next;
    }

    private void OnClearClipboardWhenClearingHistoryToggle(object? sender, EventArgs e)
    {
        if (_clearClipboardWhenClearingHistoryItem is null)
            return;

        bool next = !_clearClipboardWhenClearingHistoryItem.Checked;
        _settingsService.SaveClearClipboardWhenClearingHistory(next);
        _clearClipboardWhenClearingHistoryItem.Checked = next;
        _syncClearClipboardPreference?.Invoke(next);
    }

    private void OnHistoryTypeToggle(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not ContentType ct)
            return;

        var disabled = new HashSet<ContentType>(_settingsService.LoadDisabledHistoryTypes());

        if (disabled.Contains(ct))
            disabled.Remove(ct);
        else
            disabled.Add(ct);

        _settingsService.SaveDisabledHistoryTypes(disabled);
        clicked.Checked = !disabled.Contains(ct);
    }

    private void OnMaxEntriesOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not int value)
            return;

        _settingsService.SaveMaxHistoryEntries(value);

        if (clicked.OwnerItem is WinForms.ToolStripMenuItem parent)
        {
            foreach (WinForms.ToolStripMenuItem sibling in parent.DropDownItems)
                sibling.Checked = ReferenceEquals(sibling, clicked);
        }
    }

    private void OnMaxSizeOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not long value)
            return;

        _settingsService.SaveMaxHistorySizeBytes(value);
        SyncMaxSizeMenuChecks();
    }

    private void SyncMaxSizeMenuChecks()
    {
        if (_maxSizeSubmenuRoot is null)
            return;

        long current = _settingsService.LoadMaxHistorySizeBytes();
        foreach (WinForms.ToolStripItem item in _maxSizeSubmenuRoot.DropDownItems)
        {
            if (item is WinForms.ToolStripMenuItem mi && mi.Tag is long presetBytes)
                mi.Checked = presetBytes == current;
        }
    }

    private void OnCustomMaxHistorySizeClicked(object? sender, EventArgs e)
    {
        long currentBytes = _settingsService.LoadMaxHistorySizeBytes();
        decimal initialMb = currentBytes <= 0
            ? 100
            : Math.Clamp((decimal)(currentBytes / (1024d * 1024d)), 1, 999_999);

        using var form = new WinForms.Form
        {
            Text = "Custom history limit",
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = WinForms.FormStartPosition.CenterScreen,
            ClientSize = new Size(300, 110),
        };

        var label = new WinForms.Label
        {
            Text = "Limit in megabytes (0 = unlimited):",
            Location = new Point(12, 14),
            AutoSize = true,
        };

        var nud = new WinForms.NumericUpDown
        {
            Location = new Point(12, 40),
            Width = 160,
            Minimum = 0,
            Maximum = 999_999,
            DecimalPlaces = 0,
            Increment = 1,
            Value = initialMb,
        };

        var ok = new WinForms.Button
        {
            Text = "OK",
            DialogResult = WinForms.DialogResult.OK,
            Location = new Point(190, 68),
            Width = 90,
        };

        var cancel = new WinForms.Button
        {
            Text = "Cancel",
            DialogResult = WinForms.DialogResult.Cancel,
            Location = new Point(96, 68),
            Width = 90,
        };

        form.Controls.Add(label);
        form.Controls.Add(nud);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() != WinForms.DialogResult.OK)
            return;

        long newBytes = nud.Value == 0 ? 0 : (long)nud.Value * 1024 * 1024;
        _settingsService.SaveMaxHistorySizeBytes(newBytes);
        SyncMaxSizeMenuChecks();
    }

    private void OnOverflowModeOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not HistorySizeOverflowMode mode)
            return;

        _settingsService.SaveHistorySizeOverflowMode(mode);

        if (clicked.OwnerItem is WinForms.ToolStripMenuItem parent)
        {
            foreach (WinForms.ToolStripMenuItem sibling in parent.DropDownItems)
                sibling.Checked = ReferenceEquals(sibling, clicked);
        }
    }

    private void OnFormatCaptureCapClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not long value)
            return;

        _settingsService.SaveMaxClipboardFormatCaptureBytes(value);
        SyncFormatCaptureCapMenuChecks();
    }

    private void SyncFormatCaptureCapMenuChecks()
    {
        if (_formatCaptureCapSubmenuRoot is null)
            return;

        long current = _settingsService.LoadMaxClipboardFormatCaptureBytes();
        foreach (WinForms.ToolStripItem item in _formatCaptureCapSubmenuRoot.DropDownItems)
        {
            if (item is WinForms.ToolStripMenuItem mi && mi.Tag is long presetBytes)
                mi.Checked = presetBytes == current;
        }
    }

    private void OnCustomFormatCaptureCapClicked(object? sender, EventArgs e)
    {
        long currentBytes = _settingsService.LoadMaxClipboardFormatCaptureBytes();
        int initialMb = (int)Math.Clamp(
            currentBytes < 1024 ? 1 : currentBytes / (1024 * 1024), 1, 2047);

        using var form = new WinForms.Form
        {
            Text = "Custom capture cap",
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = WinForms.FormStartPosition.CenterScreen,
            ClientSize = new Size(320, 110),
        };

        var label = new WinForms.Label
        {
            Text = "Megabytes per non-image format (1–2047; ~2 GB max per format):",
            Location = new Point(12, 14),
            AutoSize = true,
        };

        var nud = new WinForms.NumericUpDown
        {
            Location = new Point(12, 40),
            Width = 120,
            Minimum = 1,
            Maximum = 2047,
            DecimalPlaces = 0,
            Increment = 1,
            Value = initialMb,
        };

        var ok = new WinForms.Button
        {
            Text = "OK",
            DialogResult = WinForms.DialogResult.OK,
            Location = new Point(200, 68),
            Width = 90,
        };

        var cancel = new WinForms.Button
        {
            Text = "Cancel",
            DialogResult = WinForms.DialogResult.Cancel,
            Location = new Point(106, 68),
            Width = 90,
        };

        form.Controls.Add(label);
        form.Controls.Add(nud);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() != WinForms.DialogResult.OK)
            return;

        long newBytes = (long)nud.Value * 1024 * 1024;
        _settingsService.SaveMaxClipboardFormatCaptureBytes(newBytes);
        SyncFormatCaptureCapMenuChecks();
    }

    private void OnFormatOversizeModeClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem clicked || clicked.Tag is not ClipboardFormatOversizeMode mode)
            return;

        _settingsService.SaveClipboardFormatOversizeMode(mode);

        if (clicked.OwnerItem is WinForms.ToolStripMenuItem parent)
        {
            foreach (WinForms.ToolStripMenuItem sibling in parent.DropDownItems)
                sibling.Checked = ReferenceEquals(sibling, clicked);
        }
    }

    private void OnNotifyIconMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            TrayIconClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.Button == WinForms.MouseButtons.Right && _trayContextMenu is not null)
            _trayContextMenu.Show(WinForms.Cursor.Position);
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

        if (_trayContextMenu is not null)
        {
            _trayContextMenu.ItemClicked -= OnTrayContextMenuItemClicked;
            _trayContextMenu = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.MouseUp -= OnNotifyIconMouseUp;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _emptyIcon.Dispose();
        _hasDataIcon.Dispose();
    }
}
