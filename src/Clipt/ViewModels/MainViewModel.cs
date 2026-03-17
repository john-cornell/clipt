using System.Collections.ObjectModel;
using Clipt.Help;
using Clipt.Models;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboardService;
    private readonly ClipboardListenerService _listenerService;
    private readonly IThemeService _themeService;
    private bool _disposed;
    private bool _suppressThemeCallback;

    [ObservableProperty]
    private ClipboardSnapshot _currentSnapshot = ClipboardSnapshot.Empty;

    [ObservableProperty]
    private string _statusTimestamp = string.Empty;

    [ObservableProperty]
    private string _statusSequence = string.Empty;

    [ObservableProperty]
    private string _statusFormats = string.Empty;

    [ObservableProperty]
    private string _statusOwner = string.Empty;

    [ObservableProperty]
    private bool _autoRefresh = true;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private bool _isHelpVisible;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private ObservableCollection<HelpEntry> _currentHelpEntries = [];

    public TextTabViewModel TextTab { get; }
    public HexTabViewModel HexTab { get; }
    public ImageTabViewModel ImageTab { get; } = new();
    public RichContentTabViewModel RichContentTab { get; } = new();
    public FileDropTabViewModel FileDropTab { get; } = new();
    public FormatsTabViewModel FormatsTab { get; } = new();
    public NativeTabViewModel NativeTab { get; } = new();

    public MainViewModel(
        IClipboardService clipboardService,
        ClipboardListenerService listenerService,
        IThemeService themeService)
    {
        _clipboardService = clipboardService;
        _listenerService = listenerService;
        _themeService = themeService;
        _listenerService.ClipboardChanged += OnClipboardChanged;

        TextTab = new TextTabViewModel(clipboardService, () => _listenerService.Hwnd);
        HexTab = new HexTabViewModel(clipboardService, () => _listenerService.Hwnd);
    }

    public void Initialize()
    {
        _suppressThemeCallback = true;
        IsDarkMode = _themeService.LoadSavedTheme() == AppTheme.Dark;
        _suppressThemeCallback = false;

        UpdateHelpEntries(SelectedTabIndex);
        _listenerService.Start();
        Refresh();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (_suppressThemeCallback)
            return;

        _themeService.ApplyTheme(value ? AppTheme.Dark : AppTheme.Normal);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        UpdateHelpEntries(value);
    }

    private void UpdateHelpEntries(int tabIndex)
    {
        var tabName = HelpContent.GetTabNameByIndex(tabIndex);
        var entries = new ObservableCollection<HelpEntry>();

        if (!string.IsNullOrEmpty(tabName)
            && HelpContent.TabTerms.TryGetValue(tabName, out var termKeys))
        {
            foreach (var key in termKeys)
            {
                if (HelpContent.DetailedHelp.TryGetValue(key, out var description))
                {
                    var displayName = HelpContent.DisplayNames.TryGetValue(key, out var name)
                        ? name
                        : key;
                    entries.Add(new HelpEntry(displayName, description));
                }
            }
        }

        CurrentHelpEntries = entries;
    }

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var snapshot = _clipboardService.CaptureSnapshot(_listenerService.Hwnd);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            StatusTimestamp = DateTime.UtcNow.ToLocalTime().ToString("HH:mm:ss.fff");
            StatusFormats = $"Error: {ex.Message}";
        }
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (!AutoRefresh)
            return;

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            dispatcher.BeginInvoke(Refresh);
    }

    private void ApplySnapshot(ClipboardSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;

        StatusTimestamp = snapshot.Timestamp != DateTime.MinValue
            ? snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff")
            : "—";
        StatusSequence = $"Seq: {snapshot.SequenceNumber}";
        StatusFormats = $"Formats: {snapshot.Formats.Length}";
        StatusOwner = snapshot.OwnerProcessId > 0
            ? $"Owner: {snapshot.OwnerProcessName} (PID {snapshot.OwnerProcessId})"
            : $"Owner: {snapshot.OwnerProcessName}";

        TextTab.Update(snapshot);
        HexTab.Update(snapshot);
        ImageTab.Update(snapshot);
        RichContentTab.Update(snapshot);
        FileDropTab.Update(snapshot);
        FormatsTab.Update(snapshot);
        NativeTab.Update(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _listenerService.ClipboardChanged -= OnClipboardChanged;
        _listenerService.Dispose();
    }
}
