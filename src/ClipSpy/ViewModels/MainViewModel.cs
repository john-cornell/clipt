using ClipSpy.Models;
using ClipSpy.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipSpy.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboardService;
    private readonly ClipboardListenerService _listenerService;
    private bool _disposed;

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

    public TextTabViewModel TextTab { get; } = new();
    public HexTabViewModel HexTab { get; } = new();
    public ImageTabViewModel ImageTab { get; } = new();
    public RichContentTabViewModel RichContentTab { get; } = new();
    public FileDropTabViewModel FileDropTab { get; } = new();
    public FormatsTabViewModel FormatsTab { get; } = new();
    public NativeTabViewModel NativeTab { get; } = new();

    public MainViewModel(IClipboardService clipboardService, ClipboardListenerService listenerService)
    {
        _clipboardService = clipboardService;
        _listenerService = listenerService;
        _listenerService.ClipboardChanged += OnClipboardChanged;
    }

    public void Initialize()
    {
        _listenerService.Start();
        Refresh();
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

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
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
