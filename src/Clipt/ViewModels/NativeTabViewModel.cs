using System.Collections.ObjectModel;
using Clipt.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

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
        MemoryHexDump = format.Memory.FirstBytes.Length > 0
            ? Formatting.BuildHexDump(format.Memory.FirstBytes, 16)
            : "(no data)";
    }

    private void ClearDetails()
    {
        HandleHex = string.Empty;
        LockPointerHex = string.Empty;
        GlobalSizeDisplay = string.Empty;
        MemoryHexDump = string.Empty;
    }
}
