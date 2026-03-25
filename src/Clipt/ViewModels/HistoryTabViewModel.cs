using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Clipt.Models;
using Clipt.Native;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class HistoryTabViewModel : ObservableObject
{
    private readonly IClipboardHistoryService _historyService;
    private readonly IClipboardService _clipboardService;
    private readonly ISettingsService _settingsService;
    private readonly ITrayIconService _trayIconService;
    private readonly IClipboardGroupService _groupService;
    private readonly Func<nint> _hwndProvider;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusText = "No history";

    /// <summary>
    /// When true, <see cref="ClearAllCommand"/> also clears the system clipboard after clearing history
    /// (tray icon preference / programmatic use).
    /// </summary>
    [ObservableProperty]
    private bool _alsoClearClipboardOnClearHistory;

    [ObservableProperty]
    private HistorySelectionFlow _selectionFlow;

    [ObservableProperty]
    private bool _selectAllHeaderChecked;

    [ObservableProperty]
    private bool _isNamingGroup;

    private bool _syncingSelectAllFromItems;
    private bool _applyingSelectionFromHeader;

    public bool IsSelectionMode => SelectionFlow != HistorySelectionFlow.None;

    public bool ShowSaveSelectedInChrome => SelectionFlow == HistorySelectionFlow.SaveGroup;

    public bool ShowDeleteSelectedInChrome => SelectionFlow == HistorySelectionFlow.DeleteEntries;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    /// <summary>
    /// When true, history rows show up/down reorder controls.
    /// </summary>
    [ObservableProperty]
    private bool _showMoveControls;

    /// <summary>
    /// When true, text entries can show More/Less controls for long summaries.
    /// </summary>
    [ObservableProperty]
    private bool _showTextExpandControls;

    public bool ShowNormalHistoryChrome => !IsSelectionMode && !IsNamingGroup;

    public bool ShowSelectionHistoryChrome => IsSelectionMode && !IsNamingGroup;

    public bool ShowNamingHistoryChrome => IsNamingGroup;

    public ObservableCollection<HistoryEntryDisplayItem> DisplayEntries { get; } = [];

    public event EventHandler? ClipboardRestored;
    public event Action<BitmapSource>? ImagePreviewRequested;

    public HistoryTabViewModel(
        IClipboardHistoryService historyService,
        IClipboardService clipboardService,
        Func<nint> hwndProvider,
        ISettingsService settingsService,
        ITrayIconService trayIconService,
        IClipboardGroupService groupService)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));

        _historyService.EntriesChanged += OnEntriesChanged;

        AlsoClearClipboardOnClearHistory = _settingsService.LoadClearClipboardWhenClearingHistory();
    }

    partial void OnSelectionFlowChanged(HistorySelectionFlow value)
    {
        EnterSaveSelectionModeCommand.NotifyCanExecuteChanged();
        EnterDeleteSelectionModeCommand.NotifyCanExecuteChanged();
        BeginSaveSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSelectionMode));
        OnPropertyChanged(nameof(ShowSaveSelectedInChrome));
        OnPropertyChanged(nameof(ShowDeleteSelectedInChrome));
        NotifyHistoryChromeVisibility();
    }

    partial void OnSelectAllHeaderCheckedChanged(bool value)
    {
        if (_syncingSelectAllFromItems)
            return;

        _applyingSelectionFromHeader = true;
        try
        {
            foreach (HistoryEntryDisplayItem e in DisplayEntries)
                e.IsSelected = value;
        }
        finally
        {
            _applyingSelectionFromHeader = false;
        }

        SyncSelectAllHeaderFromItems();
    }

    partial void OnIsNamingGroupChanged(bool value)
    {
        BeginSaveSelectedCommand.NotifyCanExecuteChanged();
        ConfirmSaveGroupCommand.NotifyCanExecuteChanged();
        NotifyHistoryChromeVisibility();
    }

    private void NotifyHistoryChromeVisibility()
    {
        OnPropertyChanged(nameof(ShowNormalHistoryChrome));
        OnPropertyChanged(nameof(ShowSelectionHistoryChrome));
        OnPropertyChanged(nameof(ShowNamingHistoryChrome));
    }

    private void SyncSelectAllHeaderFromItems()
    {
        if (_applyingSelectionFromHeader)
            return;

        bool all = DisplayEntries.Count > 0 && DisplayEntries.All(e => e.IsSelected);
        _syncingSelectAllFromItems = true;
        try
        {
            SelectAllHeaderChecked = all;
        }
        finally
        {
            _syncingSelectAllFromItems = false;
        }
    }

    partial void OnNewGroupNameChanged(string value) =>
        ConfirmSaveGroupCommand.NotifyCanExecuteChanged();

    partial void OnIsEmptyChanged(bool value)
    {
        EnterSaveSelectionModeCommand.NotifyCanExecuteChanged();
        EnterDeleteSelectionModeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEnterSaveOrDeleteSelectionMode))]
    private void EnterSaveSelectionMode()
    {
        SelectionFlow = HistorySelectionFlow.SaveGroup;
        IsNamingGroup = false;
        NewGroupName = string.Empty;
        foreach (HistoryEntryDisplayItem e in DisplayEntries)
            e.IsSelected = false;

        SyncSelectAllHeaderFromItems();
    }

    [RelayCommand(CanExecute = nameof(CanEnterSaveOrDeleteSelectionMode))]
    private void EnterDeleteSelectionMode()
    {
        SelectionFlow = HistorySelectionFlow.DeleteEntries;
        IsNamingGroup = false;
        NewGroupName = string.Empty;
        foreach (HistoryEntryDisplayItem e in DisplayEntries)
            e.IsSelected = false;

        SyncSelectAllHeaderFromItems();
    }

    private bool CanEnterSaveOrDeleteSelectionMode() =>
        !IsEmpty && SelectionFlow == HistorySelectionFlow.None;

    [RelayCommand]
    private void CancelSelectionMode()
    {
        SelectionFlow = HistorySelectionFlow.None;
        IsNamingGroup = false;
        NewGroupName = string.Empty;
        foreach (HistoryEntryDisplayItem e in DisplayEntries)
            e.IsSelected = false;

        SyncSelectAllHeaderFromItems();
    }

    [RelayCommand(CanExecute = nameof(CanBeginSaveSelected))]
    private void BeginSaveSelected()
    {
        IsNamingGroup = true;
        NewGroupName = "New group";
    }

    private bool CanBeginSaveSelected() =>
        SelectionFlow == HistorySelectionFlow.SaveGroup
        && !IsNamingGroup
        && DisplayEntries.Any(e => e.IsSelected);

    [RelayCommand]
    private void CancelNaming()
    {
        IsNamingGroup = false;
        NewGroupName = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmSaveGroup))]
    private async Task ConfirmSaveGroupAsync()
    {
        string name = NewGroupName.Trim();
        if (name.Length == 0)
            return;

        List<string> ids = DisplayEntries.Where(e => e.IsSelected).Select(e => e.Id).ToList();
        if (ids.Count == 0)
            return;

        await _groupService.SaveGroupAsync(name, ids).ConfigureAwait(false);

        void Finish()
        {
            SelectionFlow = HistorySelectionFlow.None;
            IsNamingGroup = false;
            NewGroupName = string.Empty;
            Refresh();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Finish();
        else
            dispatcher.Invoke(Finish);
    }

    private bool CanConfirmSaveGroup() =>
        IsNamingGroup && !string.IsNullOrWhiteSpace(NewGroupName);

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        List<string> ids = DisplayEntries.Where(e => e.IsSelected).Select(e => e.Id).ToList();
        if (ids.Count == 0)
            return;

        string? topId = _historyService.Entries.Count > 0 ? _historyService.Entries[0].Id : null;
        bool deletingCurrent = topId is not null && ids.Contains(topId);

        foreach (string id in ids)
            await _historyService.RemoveAsync(id).ConfigureAwait(false);

        if (deletingCurrent)
        {
            if (_historyService.Entries.Count > 0)
            {
                string nextId = _historyService.Entries[0].Id;
                await RestoreSnapshotToClipboard(nextId).ConfigureAwait(false);
            }
            else
            {
                await ClearSystemClipboardAsync().ConfigureAwait(false);
            }
        }

        void Finish()
        {
            SelectionFlow = HistorySelectionFlow.None;
            IsNamingGroup = false;
            NewGroupName = string.Empty;
            Refresh();
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Finish();
        else
            dispatcher.Invoke(Finish);
    }

    private bool CanDeleteSelected() =>
        SelectionFlow == HistorySelectionFlow.DeleteEntries
        && DisplayEntries.Any(e => e.IsSelected);

    partial void OnAlsoClearClipboardOnClearHistoryChanged(bool value)
    {
        _settingsService.SaveClearClipboardWhenClearingHistory(value);
        _trayIconService.SetClearClipboardWhenClearingHistoryChecked(value);
    }

    public void Refresh()
    {
        foreach (HistoryEntryDisplayItem old in DisplayEntries)
            old.PropertyChanged -= OnHistoryEntryDisplayPropertyChanged;

        DisplayEntries.Clear();
        SelectionFlow = HistorySelectionFlow.None;
        IsNamingGroup = false;
        NewGroupName = string.Empty;
        _syncingSelectAllFromItems = true;
        try
        {
            SelectAllHeaderChecked = false;
        }
        finally
        {
            _syncingSelectAllFromItems = false;
        }

        BeginSaveSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ConfirmSaveGroupCommand.NotifyCanExecuteChanged();
        NotifyHistoryChromeVisibility();

        var entries = _historyService.Entries;
        if (entries.Count == 0)
        {
            IsEmpty = true;
            StatusText = "No history";
            return;
        }

        IsEmpty = false;
        StatusText = entries.Count == 1 ? "1 entry" : $"{entries.Count} entries";

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string entryId = entry.Id;
            bool isFirst = i == 0;
            bool isLast = i == entries.Count - 1;
            var item = new HistoryEntryDisplayItem
            {
                Id = entry.Id,
                Name = entry.Name,
                Summary = entry.Summary,
                SummaryDisplay = entry.Summary,
                ContentTypeLabel = FormatContentType(entry.ContentType),
                ContentType = entry.ContentType,
                RelativeTime = FormatRelativeTime(entry.TimestampUtc),
                RestoreCommand = new AsyncRelayCommand(() => RestoreEntryAsync(entry.Id)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteEntryAsync(entry.Id)),
                RenameCommand = new AsyncRelayCommand<string>(newName => RenameEntryAsync(entry.Id, newName!)),
                MoveUpCommand = isFirst ? null : new AsyncRelayCommand(() => MoveEntryAsync(entry.Id, -1)),
                MoveDownCommand = isLast ? null : new AsyncRelayCommand(() => MoveEntryAsync(entry.Id, +1)),
                IsCurrent = isFirst,
                IsFirst = isFirst,
                IsLast = isLast,
                LoadUnicodeTextForSummaryExpandAsync = entry.ContentType == ContentType.Text
                    ? () => LoadEntryUnicodeTextAsync(entryId)
                    : null,
            };

            if (entry.ContentType == ContentType.Image)
                item.PreviewCommand = new AsyncRelayCommand(() => ShowPreviewAsync(entry.Id));

            item.PropertyChanged += OnHistoryEntryDisplayPropertyChanged;
            DisplayEntries.Add(item);
        }

        EnterSaveSelectionModeCommand.NotifyCanExecuteChanged();
        EnterDeleteSelectionModeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSelectionMode));
        OnPropertyChanged(nameof(ShowSaveSelectedInChrome));
        OnPropertyChanged(nameof(ShowDeleteSelectedInChrome));
        _ = LoadInlineThumbnailsSafeAsync();
    }

    private void OnHistoryEntryDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoryEntryDisplayItem.IsSelected))
            return;

        SyncSelectAllHeaderFromItems();
        BeginSaveSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadInlineThumbnailsSafeAsync()
    {
        try
        {
            await LoadInlineThumbnailsAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (AlsoClearClipboardOnClearHistory)
        {
            await _historyService.ClearAsync().ConfigureAwait(false);
            await ClearSystemClipboardAsync().ConfigureAwait(false);
        }
        else
        {
            await ClipboardHistoryService.ClearHistoryMatchingCurrentClipboardAsync(
                _historyService,
                _clipboardService,
                _hwndProvider()).ConfigureAwait(false);
        }

        EnsureRefreshOnUiThread();
    }

    /// <summary>
    /// Clears history and the system clipboard (tray popup menu).
    /// </summary>
    [RelayCommand]
    private async Task ClearAllIncludingClipboardAsync()
    {
        await _historyService.ClearAsync().ConfigureAwait(false);
        await ClearSystemClipboardAsync().ConfigureAwait(false);
        EnsureRefreshOnUiThread();
    }

    /// <summary>
    /// Clears history but keeps the current clipboard content in history when possible.
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryOnlyAsync()
    {
        await ClipboardHistoryService.ClearHistoryMatchingCurrentClipboardAsync(
            _historyService,
            _clipboardService,
            _hwndProvider()).ConfigureAwait(false);

        EnsureRefreshOnUiThread();
    }

    /// <summary>
    /// Forces the history list to sync immediately (may run off the UI thread after awaits).
    /// </summary>
    private void EnsureRefreshOnUiThread()
    {
        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Refresh();
        else
            dispatcher.Invoke(Refresh);
    }

    private async Task RestoreEntryAsync(string entryId)
    {
        await RestoreSnapshotToClipboard(entryId).ConfigureAwait(false);
        await _historyService.PromoteAsync(entryId).ConfigureAwait(false);
        ClipboardRestored?.Invoke(this, EventArgs.Empty);
    }

    private async Task MoveEntryAsync(string entryId, int direction)
    {
        string? previousTopId = _historyService.Entries.Count > 0
            ? _historyService.Entries[0].Id : null;

        await _historyService.MoveAsync(entryId, direction).ConfigureAwait(false);

        string? newTopId = _historyService.Entries.Count > 0
            ? _historyService.Entries[0].Id : null;

        if (newTopId is not null && newTopId != previousTopId)
            await RestoreSnapshotToClipboard(newTopId).ConfigureAwait(false);
    }

    private async Task DeleteEntryAsync(string entryId)
    {
        bool wasActive = _historyService.Entries.Count > 0
            && _historyService.Entries[0].Id == entryId;

        await _historyService.RemoveAsync(entryId).ConfigureAwait(false);

        if (!wasActive)
            return;

        if (_historyService.Entries.Count > 0)
        {
            string nextId = _historyService.Entries[0].Id;
            await RestoreSnapshotToClipboard(nextId).ConfigureAwait(false);
        }
        else
        {
            await ClearSystemClipboardAsync().ConfigureAwait(false);
        }
    }

    private async Task RenameEntryAsync(string entryId, string newName)
    {
        string trimmed = newName.Trim();
        if (trimmed.Length == 0)
            return;

        await _historyService.RenameAsync(entryId, trimmed).ConfigureAwait(false);
    }

    private async Task<string?> LoadEntryUnicodeTextAsync(string entryId)
    {
        var snapshot = await _historyService.RestoreAsync(entryId).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        var unicodeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);

        if (unicodeFormat is null || unicodeFormat.RawData.Length == 0)
            return null;

        return ClipboardHistoryService.DecodeUtf16Truncated(unicodeFormat.RawData, int.MaxValue);
    }

    private Task RestoreSnapshotToClipboard(string entryId) =>
        ClipboardSnapshotWriter.RestoreEntryToClipboardAsync(
            _historyService,
            _clipboardService,
            _hwndProvider(),
            entryId);

    /// <summary>
    /// Clears the system clipboard on the thread that owns the listener HWND.
    /// No suppression needed: an empty clipboard produces Formats.Length == 0,
    /// which OnClipboardChangedForTray already filters out (hasData check).
    /// </summary>
    private async Task ClearSystemClipboardAsync()
    {
        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            _clipboardService.ClearClipboard(_hwndProvider());
        else
            await dispatcher.InvokeAsync(() => _clipboardService.ClearClipboard(_hwndProvider()));
    }

    private void OnEntriesChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    internal async Task LoadThumbnailAsync(string entryId)
    {
        await LoadThumbnailCoreAsync(entryId, ThumbnailTarget.Tooltip).ConfigureAwait(false);
    }

    internal async Task LoadInlineThumbnailsAsync()
    {
        foreach (var item in DisplayEntries)
        {
            if (item.ContentType == ContentType.Image)
                await LoadThumbnailCoreAsync(item.Id, ThumbnailTarget.Inline).ConfigureAwait(false);
        }
    }

    internal enum ThumbnailTarget { Tooltip, Inline }

    private async Task LoadThumbnailCoreAsync(string entryId, ThumbnailTarget target)
    {
        var item = DisplayEntries.FirstOrDefault(e => e.Id == entryId);
        if (item is null || item.ContentType != ContentType.Image)
            return;

        bool alreadyLoaded = target == ThumbnailTarget.Tooltip
            ? item.PreviewThumbnail is not null
            : item.InlineThumbnail is not null;

        if (alreadyLoaded)
            return;

        var bmp = await LoadFullResImageAsync(entryId).ConfigureAwait(false);
        if (bmp is null)
            return;

        double maxDimension = target == ThumbnailTarget.Tooltip ? 200.0 : 32.0;
        if (bmp.PixelWidth > maxDimension || bmp.PixelHeight > maxDimension)
        {
            double scale = Math.Min(maxDimension / bmp.PixelWidth, maxDimension / bmp.PixelHeight);
            bmp = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
        }

        if (!bmp.IsFrozen)
            bmp.Freeze();

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (target == ThumbnailTarget.Tooltip)
                item.PreviewThumbnail = bmp;
            else
                item.InlineThumbnail = bmp;
        });
    }

    internal async Task ShowPreviewAsync(string entryId)
    {
        var bmp = await LoadFullResImageAsync(entryId).ConfigureAwait(false);
        if (bmp is null)
            return;

        if (!bmp.IsFrozen)
            bmp.Freeze();

        ImagePreviewRequested?.Invoke(bmp);
    }

    private async Task<BitmapSource?> LoadFullResImageAsync(string entryId)
    {
        var snapshot = await _historyService.RestoreAsync(entryId).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        var dibv5 = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIBV5);
        var dib = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIB);
        ClipboardFormatInfo? imageFormat = dibv5 ?? dib;

        if (imageFormat is null || imageFormat.RawData.Length < 40)
            return null;

        try
        {
            return ImageTabViewModel.DecodeDeviceIndependentBitmap(imageFormat.RawData);
        }
        catch (Exception ex) when (
            ex is NotSupportedException
              or InvalidOperationException
              or IOException
              or ArgumentException
              or System.Runtime.InteropServices.ExternalException
              or OverflowException)
        {
            return null;
        }
    }

    internal static string FormatContentType(ContentType contentType) => contentType switch
    {
        ContentType.Text => "Text",
        ContentType.Image => "Image",
        ContentType.Files => "Files",
        ContentType.Other => "Data",
        _ => "",
    };

    internal static string FormatRelativeTime(DateTime utcTimestamp)
    {
        var elapsed = DateTime.UtcNow - utcTimestamp;

        if (elapsed.TotalSeconds < 5) return "just now";
        if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        return utcTimestamp.ToLocalTime().ToString("MMM d");
    }
}

public sealed partial class HistoryEntryDisplayItem : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public required string Summary { get; init; }

    [ObservableProperty]
    private string _summaryDisplay = string.Empty;

    public required string ContentTypeLabel { get; init; }
    public required ContentType ContentType { get; init; }
    public required string RelativeTime { get; init; }
    public required IAsyncRelayCommand RestoreCommand { get; init; }
    public required IAsyncRelayCommand DeleteCommand { get; init; }
    public IAsyncRelayCommand? PreviewCommand { get; set; }
    public IAsyncRelayCommand? RenameCommand { get; set; }
    public IAsyncRelayCommand? MoveUpCommand { get; init; }
    public IAsyncRelayCommand? MoveDownCommand { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsFirst { get; init; }
    public bool IsLast { get; init; }

    public Func<Task<string?>>? LoadUnicodeTextForSummaryExpandAsync { get; init; }

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private ImageSource? _previewThumbnail;

    [ObservableProperty]
    private ImageSource? _inlineThumbnail;

    private string? _fullUnicodeSummaryText;
    private int _summaryExpandLevel;

    public bool SummaryExpandChromeVisible =>
        ContentType == ContentType.Text
        && (CompactSummaryMayBeTruncated || _summaryExpandLevel > 0);

    private bool CompactSummaryMayBeTruncated =>
        Summary.EndsWith("...", StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanExpandHistorySummary))]
    private async Task ExpandHistorySummary()
    {
        if (ContentType != ContentType.Text)
            return;

        bool loaded = _fullUnicodeSummaryText is not null;
        int fullLen = _fullUnicodeSummaryText?.Length ?? 0;
        if (!PreviewStepSizes.CanAdvanceHistorySummaryExpand(
                _summaryExpandLevel,
                fullLen,
                loaded,
                CompactSummaryMayBeTruncated))
        {
            return;
        }

        if (_summaryExpandLevel == 0)
        {
            if (LoadUnicodeTextForSummaryExpandAsync is not { } load)
                return;

            string? full = await load().ConfigureAwait(true);
            if (string.IsNullOrEmpty(full))
                return;

            _fullUnicodeSummaryText = full;
            _summaryExpandLevel = 1;
        }
        else
        {
            _summaryExpandLevel = Math.Min(4, _summaryExpandLevel + 1);
        }

        ApplySummaryDisplay();
        NotifySummaryExpandProperties();
    }

    private bool CanExpandHistorySummary() =>
        ContentType == ContentType.Text
        && PreviewStepSizes.CanAdvanceHistorySummaryExpand(
            _summaryExpandLevel,
            _fullUnicodeSummaryText?.Length ?? 0,
            _fullUnicodeSummaryText is not null,
            CompactSummaryMayBeTruncated);

    [RelayCommand(CanExecute = nameof(CanRetreatHistorySummary))]
    private void RetreatHistorySummary()
    {
        if (_summaryExpandLevel <= 0)
            return;

        _summaryExpandLevel = PreviewStepSizes.RetreatHistorySummaryExpandLevel(_summaryExpandLevel);
        if (_summaryExpandLevel == 0)
            _fullUnicodeSummaryText = null;

        ApplySummaryDisplay();
        NotifySummaryExpandProperties();
    }

    private bool CanRetreatHistorySummary() =>
        ContentType == ContentType.Text
        && PreviewStepSizes.CanRetreatHistorySummaryExpand(_summaryExpandLevel, _fullUnicodeSummaryText is not null);

    private void ApplySummaryDisplay()
    {
        if (_summaryExpandLevel == 0 || _fullUnicodeSummaryText is null)
        {
            SummaryDisplay = Summary;
            return;
        }

        int limit = PreviewStepSizes.GetHistorySummaryDisplayLimit(_summaryExpandLevel, _fullUnicodeSummaryText.Length);
        SummaryDisplay = limit >= _fullUnicodeSummaryText.Length
            ? _fullUnicodeSummaryText
            : _fullUnicodeSummaryText.Substring(0, limit);
    }

    private void NotifySummaryExpandProperties()
    {
        OnPropertyChanged(nameof(SummaryExpandChromeVisible));
        ExpandHistorySummaryCommand.NotifyCanExecuteChanged();
        RetreatHistorySummaryCommand.NotifyCanExecuteChanged();
    }
}
