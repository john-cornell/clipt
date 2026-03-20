using System.Collections.ObjectModel;
using Clipt.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    [ObservableProperty]
    private int _rawMemoryByteStepIndex;

    [ObservableProperty]
    private string _rawMemoryCaption = string.Empty;

    [ObservableProperty]
    private bool _rawMemoryCanAdvanceStep;

    [ObservableProperty]
    private bool _rawMemoryCanRetreatStep;

    private ClipboardSnapshot? _currentSnapshot;

    partial void OnSelectedFormatChanged(FormatSelection? value)
    {
        int previousStep = RawMemoryByteStepIndex;
        RawMemoryByteStepIndex = 0;
        if (previousStep == 0)
            UpdateSelectedFormatDetails();
    }

    partial void OnRawMemoryByteStepIndexChanged(int value) =>
        UpdateSelectedFormatDetails();

    [RelayCommand]
    private void SetRawMemoryByteStep(object? parameter)
    {
        int step = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, out int x) => x,
            _ => 0,
        };

        int maxStep = PreviewStepSizes.ByteLimits.Length;
        RawMemoryByteStepIndex = Math.Clamp(step, 0, maxStep);
    }

    [RelayCommand(CanExecute = nameof(CanAdvanceRawMemoryByteStep))]
    private void AdvanceRawMemoryByteStep()
    {
        if (!CanAdvanceRawMemoryByteStep())
            return;
        RawMemoryByteStepIndex++;
    }

    private bool CanAdvanceRawMemoryByteStep()
    {
        if (_currentSnapshot is null || SelectedFormat is null)
            return false;
        var format = _currentSnapshot.Formats.FirstOrDefault(f => f.FormatId == SelectedFormat.FormatId);
        if (format is null)
            return false;
        return PreviewStepSizes.CanAdvanceByteStep(RawMemoryByteStepIndex, format.RawData.Length);
    }

    [RelayCommand(CanExecute = nameof(CanRetreatRawMemoryByteStep))]
    private void RetreatRawMemoryByteStep()
    {
        if (!CanRetreatRawMemoryByteStep())
            return;
        RawMemoryByteStepIndex--;
    }

    private bool CanRetreatRawMemoryByteStep() =>
        PreviewStepSizes.CanRetreatByteStep(RawMemoryByteStepIndex);

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
        AdvanceRawMemoryByteStepCommand.NotifyCanExecuteChanged();
        RetreatRawMemoryByteStepCommand.NotifyCanExecuteChanged();
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

        int captured = format.RawData.Length;
        int slice = PreviewStepSizes.GetByteSliceLength(RawMemoryByteStepIndex, captured);
        if (slice > 0)
        {
            byte[] window = captured == slice
                ? format.RawData
                : format.RawData.AsSpan(0, slice).ToArray();
            MemoryHexDump = Formatting.BuildHexDump(window, 16);
        }
        else
        {
            MemoryHexDump = "(no data)";
        }

        RawMemoryCaption = captured == 0
            ? "No captured bytes for this format."
            : slice >= captured
                ? $"All {captured:N0} captured bytes."
                : $"First {slice:N0} of {captured:N0} captured bytes — pick a size below to show more.";

        RawMemoryCanAdvanceStep = PreviewStepSizes.CanAdvanceByteStep(RawMemoryByteStepIndex, captured);
        RawMemoryCanRetreatStep = PreviewStepSizes.CanRetreatByteStep(RawMemoryByteStepIndex);

        AdvanceRawMemoryByteStepCommand.NotifyCanExecuteChanged();
        RetreatRawMemoryByteStepCommand.NotifyCanExecuteChanged();
    }

    private void ClearDetails()
    {
        HandleHex = string.Empty;
        LockPointerHex = string.Empty;
        GlobalSizeDisplay = string.Empty;
        MemoryHexDump = string.Empty;
        RawMemoryCaption = string.Empty;
        RawMemoryCanAdvanceStep = false;
        RawMemoryCanRetreatStep = false;
        AdvanceRawMemoryByteStepCommand.NotifyCanExecuteChanged();
        RetreatRawMemoryByteStepCommand.NotifyCanExecuteChanged();
    }
}
