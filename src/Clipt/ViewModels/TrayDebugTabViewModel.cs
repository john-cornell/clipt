using System.Collections.ObjectModel;
using System.Text;
using Clipt.Models;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class TrayDebugTabViewModel : ObservableObject
{
    private const int MaxRecentEvents = 25;

    private readonly ISettingsService _settingsService;
    private readonly IClipboardHistoryService _historyService;
    private string _lastOwnerProcessName = string.Empty;
    private string _lastOwnerWindowClass = string.Empty;

    [ObservableProperty]
    private bool _hasBlockedProcessItems;

    [ObservableProperty]
    private bool _hasBlockedWindowClassItems;

    public ObservableCollection<BlockedOwnerItem> BlockedProcessItems { get; } = [];
    public ObservableCollection<BlockedOwnerItem> BlockedWindowClassItems { get; } = [];
    public ObservableCollection<ClipboardDebugEventItem> RecentEvents { get; } = [];

    public bool HasAnyBlockedOwners => HasBlockedProcessItems || HasBlockedWindowClassItems;

    public TrayDebugTabViewModel(ISettingsService settingsService, IClipboardHistoryService historyService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        RefreshBlockedList();
    }

    public void RecordEvent(ClipboardSnapshot snapshot, HistoryAddResult historyResult)
    {
        if (BlockedProcessNames.IsBlockable(snapshot.OwnerProcessName))
            _lastOwnerProcessName = snapshot.OwnerProcessName;
        if (BlockedWindowClasses.IsBlockable(snapshot.OwnerWindowClass))
            _lastOwnerWindowClass = snapshot.OwnerWindowClass;

        var item = ClipboardDebugEventItem.FromSnapshot(snapshot, historyResult);
        item.ApplyBlockedState(_settingsService);
        item.BlockOwnerCommand = CreateBlockCommand(item);
        RecentEvents.Insert(0, item);
        while (RecentEvents.Count > MaxRecentEvents)
            RecentEvents.RemoveAt(RecentEvents.Count - 1);

        BlockLastOwnerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanBlockLastOwner))]
    private async Task BlockLastOwnerAsync()
    {
        if (!CanBlockLastOwner())
            return;

        await ApplyBlockAsync(_lastOwnerProcessName, _lastOwnerWindowClass).ConfigureAwait(true);
    }

    private bool CanBlockLastOwner()
    {
        if (BlockedProcessNames.IsBlockable(_lastOwnerProcessName)
            && !ClipboardBlockRules.IsProcessBlocked(_settingsService, _lastOwnerProcessName))
        {
            return true;
        }

        return BlockedWindowClasses.IsBlockable(_lastOwnerWindowClass)
            && !ClipboardBlockRules.IsWindowClassBlocked(_settingsService, _lastOwnerWindowClass);
    }

    [RelayCommand]
    private void UnblockProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return;

        var blocked = new HashSet<string>(_settingsService.LoadBlockedHistoryProcessNames(), StringComparer.OrdinalIgnoreCase);
        blocked.Remove(processName.Trim());
        _settingsService.SaveBlockedHistoryProcessNames(blocked);
        RefreshBlockedList();
        UpdateEventBlockedStates();
        BlockLastOwnerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void UnblockWindowClass(string? classPrefix)
    {
        if (string.IsNullOrWhiteSpace(classPrefix))
            return;

        var blocked = new HashSet<string>(
            _settingsService.LoadBlockedHistoryWindowClassPrefixes(),
            StringComparer.OrdinalIgnoreCase);
        blocked.Remove(classPrefix.Trim());
        _settingsService.SaveBlockedHistoryWindowClassPrefixes(blocked);
        RefreshBlockedList();
        UpdateEventBlockedStates();
        BlockLastOwnerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearBlockedProcesses()
    {
        _settingsService.SaveBlockedHistoryProcessNames(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _settingsService.SaveBlockedHistoryWindowClassPrefixes(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        RefreshBlockedList();
        UpdateEventBlockedStates();
        BlockLastOwnerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearRecentEvents()
    {
        RecentEvents.Clear();
    }

    private AsyncRelayCommand CreateBlockCommand(ClipboardDebugEventItem item) =>
        new AsyncRelayCommand(
            async () =>
            {
                await ApplyBlockAsync(item.BlockableProcessName, item.BlockableWindowClass).ConfigureAwait(true);
                item.ApplyBlockedState(_settingsService);
                item.BlockOwnerCommand?.NotifyCanExecuteChanged();
            },
            () => item.CanBlockOwner);

    private async Task ApplyBlockAsync(string processName, string? windowClass)
    {
        ClipboardBlockRules.BlockSnapshotSource(_settingsService, processName, windowClass);
        if (BlockedProcessNames.IsBlockable(processName))
            await _historyService.RemoveByOwnerProcessAsync(processName).ConfigureAwait(true);

        RefreshBlockedList();
        UpdateEventBlockedStates();
        BlockLastOwnerCommand.NotifyCanExecuteChanged();
    }

    private void RefreshBlockedList()
    {
        BlockedProcessItems.Clear();
        foreach (string name in _settingsService.LoadBlockedHistoryProcessNames()
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            string captured = name;
            BlockedProcessItems.Add(new BlockedOwnerItem
            {
                DisplayName = captured,
                UnblockCommand = new RelayCommand(() => UnblockProcess(captured)),
            });
        }

        BlockedWindowClassItems.Clear();
        foreach (string classPrefix in _settingsService.LoadBlockedHistoryWindowClassPrefixes()
                     .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            string captured = classPrefix;
            BlockedWindowClassItems.Add(new BlockedOwnerItem
            {
                DisplayName = captured,
                UnblockCommand = new RelayCommand(() => UnblockWindowClass(captured)),
            });
        }

        HasBlockedProcessItems = BlockedProcessItems.Count > 0;
        HasBlockedWindowClassItems = BlockedWindowClassItems.Count > 0;
        OnPropertyChanged(nameof(HasAnyBlockedOwners));
    }

    private void UpdateEventBlockedStates()
    {
        foreach (ClipboardDebugEventItem item in RecentEvents)
        {
            item.ApplyBlockedState(_settingsService);
            item.BlockOwnerCommand?.NotifyCanExecuteChanged();
        }
    }
}

public sealed class BlockedOwnerItem
{
    public required string DisplayName { get; init; }
    public required IRelayCommand UnblockCommand { get; init; }
}

public sealed partial class ClipboardDebugEventItem : ObservableObject
{
    public required string RelativeTime { get; init; }
    public required string HistoryOutcome { get; init; }
    public required string OwnerProcess { get; init; }
    public required string OwnerPid { get; init; }
    public required string OwnerHwnd { get; init; }
    public required string WindowTitle { get; init; }
    public required string WindowClass { get; init; }
    public required string SequenceNumber { get; init; }
    public required string FormatSummary { get; init; }
    public required string DetailLine { get; init; }
    public required string BlockableProcessName { get; init; }
    public required string BlockableWindowClass { get; init; }

    public IAsyncRelayCommand? BlockOwnerCommand { get; set; }

    public bool CanBlockOwner =>
        (BlockedProcessNames.IsBlockable(BlockableProcessName)
         || BlockedWindowClasses.IsBlockable(BlockableWindowClass))
        && !IsOwnerBlocked;

    [ObservableProperty]
    private bool _isOwnerBlocked;

    partial void OnIsOwnerBlockedChanged(bool value) =>
        OnPropertyChanged(nameof(CanBlockOwner));

    public void ApplyBlockedState(ISettingsService settings)
    {
        IsOwnerBlocked = ClipboardBlockRules.IsProcessBlocked(settings, BlockableProcessName)
            || ClipboardBlockRules.IsWindowClassBlocked(settings, BlockableWindowClass);
        OnPropertyChanged(nameof(CanBlockOwner));
    }

    public static ClipboardDebugEventItem FromSnapshot(ClipboardSnapshot snapshot, HistoryAddResult historyResult)
    {
        string formats = snapshot.Formats.Length == 0
            ? "(empty clipboard)"
            : string.Join(", ", snapshot.Formats.Select(f => f.FormatName));

        var detail = new StringBuilder();
        detail.Append($"Outcome: {FormatHistoryOutcome(historyResult)}");
        detail.Append($" · Seq {snapshot.SequenceNumber}");
        if (snapshot.OwnerWindowHandle != 0)
            detail.Append($" · HWND {FormatHwnd(snapshot.OwnerWindowHandle)}");
        if (!string.IsNullOrWhiteSpace(snapshot.OwnerWindowTitle))
            detail.Append($" · \"{snapshot.OwnerWindowTitle}\"");
        if (!string.IsNullOrWhiteSpace(snapshot.OwnerWindowClass))
            detail.Append($" · class {snapshot.OwnerWindowClass}");

        return new ClipboardDebugEventItem
        {
            RelativeTime = FormatRelativeTime(snapshot.Timestamp),
            HistoryOutcome = FormatHistoryOutcome(historyResult),
            OwnerProcess = snapshot.OwnerProcessName,
            OwnerPid = snapshot.OwnerProcessId > 0 ? snapshot.OwnerProcessId.ToString() : "—",
            OwnerHwnd = snapshot.OwnerWindowHandle != 0
                ? FormatHwnd(snapshot.OwnerWindowHandle)
                : "—",
            WindowTitle = string.IsNullOrWhiteSpace(snapshot.OwnerWindowTitle)
                ? "—"
                : snapshot.OwnerWindowTitle,
            WindowClass = string.IsNullOrWhiteSpace(snapshot.OwnerWindowClass)
                ? "—"
                : snapshot.OwnerWindowClass,
            SequenceNumber = snapshot.SequenceNumber.ToString(),
            FormatSummary = formats,
            DetailLine = detail.ToString(),
            BlockableProcessName = BlockedProcessNames.IsBlockable(snapshot.OwnerProcessName)
                ? snapshot.OwnerProcessName
                : string.Empty,
            BlockableWindowClass = snapshot.OwnerWindowClass ?? string.Empty,
        };
    }

    private static string FormatHwnd(nint hwnd) =>
        Environment.Is64BitProcess
            ? $"0x{hwnd.ToInt64():X16}"
            : $"0x{hwnd.ToInt32():X8}";

    private static string FormatHistoryOutcome(HistoryAddResult result) => result switch
    {
        HistoryAddResult.Added => "Added to history",
        HistoryAddResult.SkippedEmptyFormats => "Empty clipboard",
        HistoryAddResult.SkippedSuppressed => "Suppressed (Clipt restore)",
        HistoryAddResult.SkippedDuplicate => "Skipped (duplicate)",
        HistoryAddResult.SkippedDisabledContentType => "Skipped (type disabled)",
        HistoryAddResult.SkippedBlockedProcess => "Blocked process",
        HistoryAddResult.SkippedUserOverflowPrompt => "Skipped (size limit)",
        _ => result.ToString(),
    };

    private static string FormatRelativeTime(DateTime utcTimestamp)
    {
        if (utcTimestamp == DateTime.MinValue)
            return "—";

        var elapsed = DateTime.UtcNow - utcTimestamp;
        if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        return utcTimestamp.ToLocalTime().ToString("HH:mm:ss");
    }
}
