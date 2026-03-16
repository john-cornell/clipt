using System.Collections.ObjectModel;
using System.Text;
using ClipSpy.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipSpy.ViewModels;

public sealed partial class NativeTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _clipboardOwner = string.Empty;

    [ObservableProperty]
    private uint _sequenceNumber;

    [ObservableProperty]
    private ObservableCollection<FormatSelection> _availableFormats = [];

    [ObservableProperty]
    private FormatSelection? _selectedFormat;

    [ObservableProperty]
    private string _handleHex = string.Empty;

    [ObservableProperty]
    private string _lockPointerHex = string.Empty;

    [ObservableProperty]
    private string _globalSizeDisplay = string.Empty;

    [ObservableProperty]
    private string _memoryHexDump = string.Empty;

    private ClipboardSnapshot? _currentSnapshot;

    partial void OnSelectedFormatChanged(FormatSelection? value) =>
        UpdateSelectedFormatDetails();

    public void Update(ClipboardSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        ClipboardOwner = snapshot.OwnerProcessId > 0
            ? $"{snapshot.OwnerProcessName} (PID {snapshot.OwnerProcessId})"
            : snapshot.OwnerProcessName;
        SequenceNumber = snapshot.SequenceNumber;

        AvailableFormats.Clear();
        foreach (var f in snapshot.Formats)
        {
            AvailableFormats.Add(new FormatSelection(f.FormatId, f.FormatName, f.DataSize));
        }

        SelectedFormat = AvailableFormats.FirstOrDefault();
    }

    private void UpdateSelectedFormatDetails()
    {
        if (_currentSnapshot is null || SelectedFormat is null)
        {
            ClearDetails();
            return;
        }

        var format = _currentSnapshot.Formats
            .FirstOrDefault(f => f.FormatId == SelectedFormat.FormatId);

        if (format is null)
        {
            ClearDetails();
            return;
        }

        HandleHex = format.Memory.HandleHex;
        LockPointerHex = format.Memory.LockPointerHex;
        GlobalSizeDisplay = $"{format.Memory.AllocationSize} bytes ({format.DataSizeFormatted})";
        MemoryHexDump = BuildCompactHexDump(format.Memory.FirstBytes);
    }

    private void ClearDetails()
    {
        HandleHex = string.Empty;
        LockPointerHex = string.Empty;
        GlobalSizeDisplay = string.Empty;
        MemoryHexDump = string.Empty;
    }

    private static string BuildCompactHexDump(byte[] data)
    {
        if (data.Length == 0)
            return "(no data)";

        const int bpr = 16;
        var sb = new StringBuilder();

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            int count = Math.Min(bpr, data.Length - offset);
            sb.Append(offset.ToString("X4"));
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

                if (i == 7)
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
