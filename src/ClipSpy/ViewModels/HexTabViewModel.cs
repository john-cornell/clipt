using System.Collections.ObjectModel;
using System.Text;
using ClipSpy.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipSpy.ViewModels;

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
        HexDump = BuildHexDump(format.RawData, BytesPerRow);
    }

    internal static string BuildHexDump(byte[] data, int bytesPerRow)
    {
        if (data.Length == 0)
            return string.Empty;

        int bpr = Math.Clamp(bytesPerRow, 1, 64);
        int offsetWidth = data.Length > 0xFFFF ? 8 : 4;
        var sb = new StringBuilder((data.Length / bpr + 1) * (offsetWidth + 3 + bpr * 3 + 2 + bpr + 2));

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            int count = Math.Min(bpr, data.Length - offset);

            sb.Append(offset.ToString($"X{offsetWidth}"));
            sb.Append("  ");

            for (int i = 0; i < bpr; i++)
            {
                if (i < count)
                {
                    sb.Append(data[offset + i].ToString("X2"));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }

                if (i == bpr / 2 - 1)
                    sb.Append(' ');
            }

            sb.Append(' ');

            for (int i = 0; i < count; i++)
            {
                byte b = data[offset + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public sealed record FormatSelection(uint FormatId, string Name, long Size)
{
    public override string ToString() => $"{Name} ({Size} bytes)";
}
