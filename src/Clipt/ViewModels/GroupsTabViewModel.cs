using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Clipt.Models;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class GroupsTabViewModel : ObservableObject
{
    private readonly IClipboardGroupService _groupService;
    private readonly IClipboardHistoryService _historyService;
    private readonly IClipboardService _clipboardService;
    private readonly Func<nint> _hwndProvider;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusText = "No groups";

    public ObservableCollection<GroupDisplayItem> DisplayGroups { get; } = [];

    public GroupsTabViewModel(
        IClipboardGroupService groupService,
        IClipboardHistoryService historyService,
        IClipboardService clipboardService,
        Func<nint> hwndProvider)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));

        _groupService.GroupsChanged += OnGroupsChanged;
    }

    public void Refresh()
    {
        foreach (GroupDisplayItem old in DisplayGroups)
            old.PropertyChanged -= OnGroupDisplayPropertyChanged;

        DisplayGroups.Clear();

        IReadOnlyList<ClipboardGroup> groups = _groupService.Groups;
        if (groups.Count == 0)
        {
            IsEmpty = true;
            StatusText = "No groups";
            return;
        }

        IsEmpty = false;
        StatusText = groups.Count == 1 ? "1 group" : $"{groups.Count} groups";

        foreach (ClipboardGroup g in groups)
        {
            string gid = g.Id;
            var item = new GroupDisplayItem
            {
                Id = gid,
                Name = g.Name,
                ItemCountText = g.EntryIds.Count == 1 ? "1 item" : $"{g.EntryIds.Count} items",
                RelativeTime = HistoryTabViewModel.FormatRelativeTime(g.CreatedUtc),
                RenameCommand = new AsyncRelayCommand<string>(newName => RenameGroupAsync(gid, newName!)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteGroupAsync(gid)),
                RestoreCommand = new AsyncRelayCommand<GroupRestoreMode>(mode => RestoreGroupForIdAsync(gid, mode)),
            };
            item.PropertyChanged += OnGroupDisplayPropertyChanged;
            DisplayGroups.Add(item);
        }
    }

    private void OnGroupsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(Refresh);

    private void OnGroupDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupDisplayItem.IsEditing))
            NotifyPinStateMayHaveChanged();
    }

    /// <summary>
    /// Tray popup uses this to avoid closing while a group name is being edited.
    /// </summary>
    public bool AnyGroupEditing => DisplayGroups.Any(i => i.IsEditing);

    private void NotifyPinStateMayHaveChanged()
    {
        OnPropertyChanged(nameof(AnyGroupEditing));
    }

    private async Task RenameGroupAsync(string groupId, string newName)
    {
        await _groupService.RenameGroupAsync(groupId, newName).ConfigureAwait(false);
    }

    private async Task DeleteGroupAsync(string groupId)
    {
        await _groupService.DeleteGroupAsync(groupId).ConfigureAwait(false);
    }

    private async Task RestoreGroupForIdAsync(string groupId, GroupRestoreMode mode)
    {
        ClipboardGroup? g = _groupService.Groups.FirstOrDefault(x => x.Id == groupId);
        if (g is null)
            return;

        await _historyService.RestoreGroupAsync(g.EntryIds, mode).ConfigureAwait(false);

        if (_historyService.Entries.Count == 0)
            return;

        string topId = _historyService.Entries[0].Id;
        await ClipboardSnapshotWriter.RestoreEntryToClipboardAsync(
            _historyService,
            _clipboardService,
            _hwndProvider(),
            topId).ConfigureAwait(false);
    }
}

public sealed partial class GroupDisplayItem : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    public required string ItemCountText { get; init; }
    public required string RelativeTime { get; init; }

    [ObservableProperty]
    private bool _isEditing;

    public required IAsyncRelayCommand<string> RenameCommand { get; init; }
    public required IAsyncRelayCommand DeleteCommand { get; init; }
    public required IAsyncRelayCommand<GroupRestoreMode> RestoreCommand { get; init; }
}
