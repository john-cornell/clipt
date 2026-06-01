using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using Clipt.Models;
using Clipt.Native;
using Clipt.Plugins;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class TrayPopupViewModel : ObservableObject
{
    private readonly ISettingsService? _settingsService;
    private readonly ITrayIconService? _trayIconService;
    private bool _syncingTabVisibilityFromTray;

    public TrayPopupViewModel(ISettingsService? settingsService = null, ITrayIconService? trayIconService = null)
    {
        _settingsService = settingsService;
        _trayIconService = trayIconService;
        ApplyTabVisibilityFromSettings();
    }

    [ObservableProperty]
    private bool _showPluginsTab;

    [ObservableProperty]
    private string _clipboardSummary = "Clipboard is empty";

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _textPreviewCaption = string.Empty;

    [ObservableProperty]
    private int _textPreviewStepIndex;

    [ObservableProperty]
    private bool _textPreviewCanAdvanceStep;

    [ObservableProperty]
    private bool _textPreviewCanRetreatStep;

    /// <summary>
    /// True when the tray text is long enough that step controls (500 / 2K / …) change what is shown, or the user has moved off the default step.
    /// </summary>
    [ObservableProperty]
    private bool _textPreviewStepControlsVisible;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private ObservableCollection<string> _previewFiles = [];

    [ObservableProperty]
    private bool _hasText;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _hasFiles;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private HistoryTabViewModel? _historyTab;

    [ObservableProperty]
    private GroupsTabViewModel? _groupsTab;

    [ObservableProperty]
    private PluginsTabViewModel? _pluginsTab;

    public ObservableCollection<PluginTrayTabItem> PluginTrayTabs { get; } = [];

    public ObservableCollection<TrayTabShowMenuItem> TrayTabShowMenuItems { get; } = [];

    public event EventHandler? PluginTrayTabsChanged;

    public event EventHandler? OptionalTrayTabVisibilityChanged;

    public event EventHandler? TrayTabShowMenuItemsChanged;

    private string? _trayUnicodeFullText;
    private int _trayUnicodeFullCharCount;

    public event EventHandler? ExpandToFullRequested;

    public void ApplyTabVisibilityFromSettings()
    {
        if (_settingsService is null)
        {
            ShowPluginsTab = true;
            return;
        }

        ShowPluginsTab = _settingsService.LoadShowPluginsTrayTab();
    }

    public bool IsOptionalTrayTabVisible(string? pluginId)
    {
        if (pluginId is null)
            return ShowPluginsTab;

        return _settingsService?.IsPluginTrayTabVisible(pluginId) ?? true;
    }

    public IReadOnlyList<TrayTabShowMenuEntry> GetShowTabMenuEntries() =>
        TrayTabShowMenuItems
            .Select(item => new TrayTabShowMenuEntry
            {
                Header = item.Header,
                PluginId = item.PluginId,
                IsVisible = item.IsVisible,
            })
            .ToList();

    public void RebuildTrayTabShowMenuItems()
    {
        TrayTabShowMenuItems.Clear();

        foreach (PluginTrayTabItem tab in PluginTrayTabs)
        {
            TrayTabShowMenuItems.Add(new TrayTabShowMenuItem(
                this,
                tab.Header,
                tab.PluginId,
                IsOptionalTrayTabVisible(tab.PluginId)));
        }

        TrayTabShowMenuItems.Add(new TrayTabShowMenuItem(
            this,
            "Plugins",
            pluginId: null,
            ShowPluginsTab));

        SyncTrayShowTabsMenu();
        TrayTabShowMenuItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OnTrayTabShowMenuItemChanged(string? pluginId, bool isVisible)
    {
        if (_syncingTabVisibilityFromTray)
            return;

        ApplyOptionalTrayTabVisible(pluginId, isVisible, persist: true);
    }

    public void SetOptionalTrayTabVisibleFromTray(string? pluginId, bool isVisible)
    {
        _syncingTabVisibilityFromTray = true;
        try
        {
            ApplyOptionalTrayTabVisible(pluginId, isVisible, persist: true);
        }
        finally
        {
            _syncingTabVisibilityFromTray = false;
        }
    }

    private void ApplyOptionalTrayTabVisible(string? pluginId, bool isVisible, bool persist)
    {
        if (pluginId is null)
        {
            ShowPluginsTab = isVisible;
            if (persist)
                _settingsService?.SaveShowPluginsTrayTab(isVisible);
        }
        else if (persist)
        {
            _settingsService?.SetPluginTrayTabVisible(pluginId, isVisible);
        }

        SyncShowMenuItemState(pluginId, isVisible);
        SyncTrayShowTabsMenu();
        OptionalTrayTabVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncShowMenuItemState(string? pluginId, bool isVisible)
    {
        TrayTabShowMenuItem? item = TrayTabShowMenuItems
            .FirstOrDefault(entry => entry.PluginId == pluginId);

        item?.SetIsVisibleSilently(isVisible);
    }

    private void SyncTrayShowTabsMenu() =>
        _trayIconService?.RebuildShowTabsMenu(GetShowTabMenuEntries());

    partial void OnShowPluginsTabChanged(bool value)
    {
        if (_syncingTabVisibilityFromTray)
            return;

        _settingsService?.SaveShowPluginsTrayTab(value);
        SyncShowMenuItemState(pluginId: null, value);
        SyncTrayShowTabsMenu();
        OptionalTrayTabVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshPluginTrayTabs(IPluginRegistry registry, ICliptPluginHost pluginHost)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(pluginHost);

        PluginTrayTabs.Clear();

        foreach (ICliptTrayTabPlugin tabPlugin in registry.TrayTabPlugins)
        {
            if (tabPlugin is not ICliptTrayTabViewFactory factory)
                continue;

            ICliptHost scope = pluginHost.CreateHostScope(tabPlugin.Id);
            object viewModel = factory.CreateViewModel(scope);
            object view = factory.CreateView(viewModel);

            PluginTrayTabs.Add(new PluginTrayTabItem
            {
                PluginId = tabPlugin.Id,
                Header = tabPlugin.TabHeader,
                Content = view,
            });
        }

        PluginTrayTabsChanged?.Invoke(this, EventArgs.Empty);
        RebuildTrayTabShowMenuItems();
    }

    [RelayCommand]
    private void ExpandToFull()
    {
        ExpandToFullRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SetTrayTextPreviewStep(object? parameter)
    {
        int step = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, out int x) => x,
            _ => 0,
        };

        int maxStep = PreviewStepSizes.TrayTextCharLimits.Length;
        TextPreviewStepIndex = Math.Clamp(step, 0, maxStep);
        ApplyTrayTextPreviewSlice();
    }

    [RelayCommand(CanExecute = nameof(CanAdvanceTrayTextPreviewStep))]
    private void AdvanceTrayTextPreviewStep()
    {
        if (!CanAdvanceTrayTextPreviewStep())
            return;
        TextPreviewStepIndex++;
        ApplyTrayTextPreviewSlice();
    }

    private bool CanAdvanceTrayTextPreviewStep() => TextPreviewCanAdvanceStep;

    [RelayCommand(CanExecute = nameof(CanRetreatTrayTextPreviewStep))]
    private void RetreatTrayTextPreviewStep()
    {
        if (!CanRetreatTrayTextPreviewStep())
            return;
        TextPreviewStepIndex--;
        ApplyTrayTextPreviewSlice();
    }

    private bool CanRetreatTrayTextPreviewStep() => TextPreviewCanRetreatStep;

    public void Update(ClipboardSnapshot snapshot)
    {
        int formatCount = snapshot.Formats.Length;

        if (formatCount == 0)
        {
            ClearAll();
            return;
        }

        IsEmpty = false;
        ClipboardSummary = formatCount == 1
            ? "1 format on clipboard"
            : $"{formatCount} formats on clipboard";

        UpdateTextPreview(snapshot);
        UpdateImagePreview(snapshot);
        UpdateFilePreview(snapshot);
    }

    private void UpdateTextPreview(ClipboardSnapshot snapshot)
    {
        _trayUnicodeFullText = null;
        _trayUnicodeFullCharCount = 0;

        var unicodeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);

        if (unicodeFormat is null || unicodeFormat.RawData.Length == 0)
        {
            TextPreviewStepIndex = 0;
            PreviewText = string.Empty;
            TextPreviewCaption = string.Empty;
            HasText = false;
            TextPreviewCanAdvanceStep = false;
            TextPreviewCanRetreatStep = false;
            TextPreviewStepControlsVisible = false;
            PluginsTab?.SetClipboardText(null);
            AdvanceTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
            RetreatTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
            return;
        }

        _trayUnicodeFullText = ClipboardHistoryService.DecodeUtf16Truncated(unicodeFormat.RawData, int.MaxValue);
        _trayUnicodeFullCharCount = _trayUnicodeFullText.Length;
        HasText = _trayUnicodeFullCharCount > 0;
        PluginsTab?.SetClipboardText(_trayUnicodeFullText);
        TextPreviewStepIndex = 0;
        ApplyTrayTextPreviewSlice();
    }

    private void ApplyTrayTextPreviewSlice()
    {
        if (_trayUnicodeFullText is null || _trayUnicodeFullCharCount == 0)
        {
            PreviewText = string.Empty;
            TextPreviewCaption = string.Empty;
            TextPreviewCanAdvanceStep = false;
            TextPreviewCanRetreatStep = false;
            TextPreviewStepControlsVisible = false;
            AdvanceTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
            RetreatTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
            return;
        }

        int maxChars = PreviewStepSizes.GetTrayTextMaxChars(TextPreviewStepIndex, _trayUnicodeFullCharCount);
        PreviewText = maxChars >= _trayUnicodeFullCharCount
            ? _trayUnicodeFullText
            : _trayUnicodeFullText.Substring(0, maxChars);

        TextPreviewCaption = maxChars >= _trayUnicodeFullCharCount
            ? $"All {_trayUnicodeFullCharCount:N0} characters (captured)."
            : $"Showing {maxChars:N0} of {_trayUnicodeFullCharCount:N0} characters (captured).";

        TextPreviewCanAdvanceStep =
            PreviewStepSizes.CanAdvanceTrayTextStep(TextPreviewStepIndex, _trayUnicodeFullCharCount);
        TextPreviewCanRetreatStep =
            PreviewStepSizes.CanRetreatTrayTextStep(TextPreviewStepIndex);

        TextPreviewStepControlsVisible =
            PreviewStepSizes.CanAdvanceTrayTextStep(TextPreviewStepIndex, _trayUnicodeFullCharCount)
            || PreviewStepSizes.CanRetreatTrayTextStep(TextPreviewStepIndex);

        AdvanceTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
        RetreatTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
    }

    private void UpdateImagePreview(ClipboardSnapshot snapshot)
    {
        var dibv5 = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIBV5);
        var dib = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIB);
        ClipboardFormatInfo? imageFormat = dibv5 ?? dib;

        if (imageFormat is null || imageFormat.RawData.Length < 40)
        {
            TryWpfImageFallback();
            return;
        }

        try
        {
            var bmp = ImageTabViewModel.DecodeDeviceIndependentBitmap(imageFormat.RawData);
            if (bmp is not null)
            {
                if (!bmp.IsFrozen)
                    bmp.Freeze();
                PreviewImage = bmp;
                HasImage = true;
                return;
            }
        }
        catch (Exception ex) when (
            ex is NotSupportedException
              or InvalidOperationException
              or IOException
              or ArgumentException
              or ExternalException
              or OverflowException
              or FileFormatException)
        {
        }

        TryWpfImageFallback();
    }

    private void TryWpfImageFallback()
    {
        try
        {
            var bmp = System.Windows.Clipboard.GetImage();
            if (bmp is not null)
            {
                if (!bmp.IsFrozen)
                    bmp.Freeze();
                PreviewImage = bmp;
                HasImage = true;
                return;
            }
        }
        catch (Exception ex) when (
            ex is ExternalException
              or InvalidOperationException
              or ThreadStateException)
        {
        }

        PreviewImage = null;
        HasImage = false;
    }

    private void UpdateFilePreview(ClipboardSnapshot snapshot)
    {
        var hdropFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_HDROP);

        PreviewFiles.Clear();

        if (hdropFormat is null || hdropFormat.RawData.Length < 20)
        {
            HasFiles = false;
            return;
        }

        var paths = ParseFilePaths(hdropFormat.RawData);
        foreach (string path in paths)
            PreviewFiles.Add(path);

        HasFiles = PreviewFiles.Count > 0;
    }

    private void ClearAll()
    {
        ClipboardSummary = "Clipboard is empty";
        PreviewText = string.Empty;
        TextPreviewCaption = string.Empty;
        TextPreviewStepIndex = 0;
        _trayUnicodeFullText = null;
        _trayUnicodeFullCharCount = 0;
        TextPreviewCanAdvanceStep = false;
        TextPreviewCanRetreatStep = false;
        TextPreviewStepControlsVisible = false;
        PreviewImage = null;
        PreviewFiles.Clear();
        HasText = false;
        HasImage = false;
        HasFiles = false;
        IsEmpty = true;
        PluginsTab?.SetClipboardText(null);
        AdvanceTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
        RetreatTrayTextPreviewStepCommand.NotifyCanExecuteChanged();
    }

    private static List<string> ParseFilePaths(byte[] data)
    {
        var result = new List<string>();

        int fileOffset = BitConverter.ToInt32(data, 0);
        bool isWide = BitConverter.ToInt32(data, 16) != 0;

        if (fileOffset >= data.Length)
            return result;

        if (isWide)
        {
            int pos = fileOffset;
            while (pos + 1 < data.Length)
            {
                int start = pos;
                while (pos + 1 < data.Length && (data[pos] != 0 || data[pos + 1] != 0))
                    pos += 2;

                if (pos > start)
                    result.Add(Encoding.Unicode.GetString(data, start, pos - start));

                pos += 2;
                if (pos + 1 >= data.Length || (data[pos] == 0 && data[pos + 1] == 0))
                    break;
            }
        }
        else
        {
            int pos = fileOffset;
            while (pos < data.Length)
            {
                int start = pos;
                while (pos < data.Length && data[pos] != 0)
                    pos++;

                if (pos > start)
                    result.Add(Encoding.Default.GetString(data, start, pos - start));

                pos++;
                if (pos >= data.Length || data[pos] == 0)
                    break;
            }
        }

        return result;
    }
}
