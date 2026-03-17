using System.Collections.ObjectModel;
using System.Globalization;
using Clipt.Help;
using Clipt.Models;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class HexTabViewModel : ObservableObject
{
    private readonly IClipboardService? _clipboardService;
    private readonly Func<nint>? _hwndProvider;

    [ObservableProperty]
    private string _hexDump = string.Empty;

    [ObservableProperty]
    private string _offsetColumn = string.Empty;

    [ObservableProperty]
    private string _hexColumn = string.Empty;

    [ObservableProperty]
    private string _asciiColumn = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FormatSelection> _availableFormats = [];

    [ObservableProperty]
    private FormatSelection? _selectedFormat;

    [ObservableProperty]
    private int _bytesPerRow = 16;

    [ObservableProperty]
    private int _totalBytes;

    [ObservableProperty]
    private string _selectedFormatShortDescription = string.Empty;

    [ObservableProperty]
    private string _selectedFormatDetailedDescription = string.Empty;

    [ObservableProperty]
    private int _selectedByteOffset = -1;

    [ObservableProperty]
    private int _selectedByteCount;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editStatusMessage = string.Empty;

    private ClipboardSnapshot? _currentSnapshot;

    public byte[] CurrentRawData { get; private set; } = [];

    public HexTabViewModel()
    {
    }

    public HexTabViewModel(IClipboardService clipboardService, Func<nint> hwndProvider)
    {
        _clipboardService = clipboardService;
        _hwndProvider = hwndProvider;
    }

    partial void OnSelectedFormatChanged(FormatSelection? value)
    {
        UpdateFormatDescription(value);
        RebuildHexDump();
    }

    partial void OnBytesPerRowChanged(int value) =>
        RebuildHexDump();

    [RelayCommand]
    private void ToggleEditing()
    {
        IsEditing = !IsEditing;
        EditStatusMessage = IsEditing ? "Editing — auto-refresh paused." : string.Empty;
    }

    [RelayCommand]
    private void ApplyHexToClipboard()
    {
        if (_clipboardService is null || _hwndProvider is null || SelectedFormat is null)
        {
            EditStatusMessage = "Clipboard service unavailable.";
            return;
        }

        byte[]? parsed = ParseHexColumn(HexColumn);
        if (parsed is null)
        {
            EditStatusMessage = "Invalid hex data — fix and retry.";
            return;
        }

        try
        {
            _clipboardService.SetClipboardData(SelectedFormat.FormatId, parsed, _hwndProvider());
            EditStatusMessage = "Applied to clipboard.";
            IsEditing = false;
        }
        catch (InvalidOperationException ex)
        {
            EditStatusMessage = $"Failed: {ex.Message}";
        }
    }

    internal static byte[]? ParseHexColumn(string hexColumn)
    {
        if (string.IsNullOrWhiteSpace(hexColumn))
            return [];

        var bytes = new List<byte>();
        foreach (string line in hexColumn.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.TrimEnd('\r', ' ');
            if (trimmed.Length == 0)
                continue;

            string[] tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (token.Length != 2)
                    return null;

                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    return null;

                bytes.Add(b);
            }
        }

        return bytes.ToArray();
    }

    private void UpdateFormatDescription(FormatSelection? format)
    {
        if (format is null)
        {
            SelectedFormatShortDescription = string.Empty;
            SelectedFormatDetailedDescription = string.Empty;
            return;
        }

        var desc = FormatDescriptions.GetDescription(format.FormatId, format.Name);
        SelectedFormatShortDescription = desc?.Short ?? FormatDescriptions.UnknownFormatFallback.Short;
        SelectedFormatDetailedDescription = desc?.Detailed ?? FormatDescriptions.UnknownFormatFallback.Detailed;
    }

    public void Update(ClipboardSnapshot snapshot)
    {
        if (IsEditing)
            return;

        _currentSnapshot = snapshot;
        AvailableFormats.Clear();

        foreach (var format in snapshot.Formats)
        {
            AvailableFormats.Add(new FormatSelection(format.FormatId, format.FormatName, format.DataSize));
        }

        SelectedFormat = AvailableFormats.FirstOrDefault();
    }

    private void RebuildHexDump()
    {
        if (_currentSnapshot is null || SelectedFormat is null)
        {
            HexDump = string.Empty;
            OffsetColumn = string.Empty;
            HexColumn = string.Empty;
            AsciiColumn = string.Empty;
            TotalBytes = 0;
            CurrentRawData = [];
            SelectedByteOffset = -1;
            SelectedByteCount = 0;
            return;
        }

        var format = _currentSnapshot.Formats
            .FirstOrDefault(f => f.FormatId == SelectedFormat.FormatId);

        if (format is null || format.RawData.Length == 0)
        {
            HexDump = "(no data)";
            OffsetColumn = string.Empty;
            HexColumn = string.Empty;
            AsciiColumn = string.Empty;
            TotalBytes = 0;
            CurrentRawData = [];
            SelectedByteOffset = -1;
            SelectedByteCount = 0;
            return;
        }

        CurrentRawData = format.RawData;
        TotalBytes = format.RawData.Length;
        HexDump = Formatting.BuildHexDump(format.RawData, BytesPerRow);
        OffsetColumn = Formatting.BuildOffsetColumn(format.RawData, BytesPerRow);
        HexColumn = Formatting.BuildHexColumn(format.RawData, BytesPerRow);
        AsciiColumn = Formatting.BuildAsciiColumn(format.RawData, BytesPerRow);
        SelectedByteOffset = -1;
        SelectedByteCount = 0;
    }
}
