using System.Collections.ObjectModel;
using Clipt.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

public sealed partial class HexTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _hexDump = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FormatSelection> _availableFormats = [];

    [ObservableProperty]
    private FormatSelection? _selectedFormat;

    [ObservableProperty]
    private int _bytesPerRow = 16;

    [ObservableProperty]
    private int _totalBytes;

    private ClipboardSnapshot? _currentSnapshot;

    partial void OnSelectedFormatChanged(FormatSelection? value) =>
        RebuildHexDump();

    partial void OnBytesPerRowChanged(int value) =>
        RebuildHexDump();

    public void Update(ClipboardSnapshot snapshot)
    {
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
            TotalBytes = 0;
            return;
        }

        var format = _currentSnapshot.Formats
            .FirstOrDefault(f => f.FormatId == SelectedFormat.FormatId);

        if (format is null || format.RawData.Length == 0)
        {
            HexDump = "(no data)";
            TotalBytes = 0;
            return;
        }

        TotalBytes = format.RawData.Length;
        HexDump = Formatting.BuildHexDump(format.RawData, BytesPerRow);
    }

    internal static string BuildHexDump(byte[] data, int bytesPerRow) =>
        Formatting.BuildHexDump(data, bytesPerRow);
}
