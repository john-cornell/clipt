using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Clipt.Models;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Clipt.ViewModels;

public sealed partial class GroupsTabViewModel : ObservableObject
{
    private readonly IClipboardGroupService _groupService;
    private readonly IClipboardHistoryService _historyService;
    private readonly IClipboardService _clipboardService;
    private readonly ISettingsService _settingsService;
    private readonly Func<nint> _hwndProvider;

    /// <summary>Set right before a folder-creating operation's Refresh() so the new folder opens in rename mode.</summary>
    private string? _pendingEditFolderId;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusText = "No groups";

    [ObservableProperty]
    private GroupSortMode _sortMode;

    /// <summary>Move up/down arrows (groups and folders) are only meaningful, and only shown, in Custom sort mode.</summary>
    public bool ShowMoveControls => SortMode == GroupSortMode.Custom;

    public ObservableCollection<GroupSectionDisplayItem> Sections { get; } = [];

    /// <summary>Folders only, for "move to folder" pickers. Recomputed whenever <see cref="Sections"/> rebuilds.</summary>
    public IEnumerable<GroupSectionDisplayItem> FolderSections => Sections.Where(s => s.IsFolder);

    public IAsyncRelayCommand ImportGroupCommand { get; }
    public IAsyncRelayCommand CreateFolderCommand { get; }

    public GroupsTabViewModel(
        IClipboardGroupService groupService,
        IClipboardHistoryService historyService,
        IClipboardService clipboardService,
        ISettingsService settingsService,
        Func<nint> hwndProvider)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));

        _groupService.GroupsChanged += OnGroupsChanged;
        ImportGroupCommand = new AsyncRelayCommand(ImportGroupWithDialogAsync);
        CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync);

        SortMode = _settingsService.LoadGroupSortMode();
    }

    partial void OnSortModeChanged(GroupSortMode value)
    {
        _settingsService.SaveGroupSortMode(value);
        OnPropertyChanged(nameof(ShowMoveControls));
        Refresh();
    }

    public void Refresh()
    {
        foreach (GroupSectionDisplayItem oldSection in Sections)
        {
            oldSection.PropertyChanged -= OnSectionPropertyChanged;
            foreach (GroupDisplayItem oldGroup in oldSection.Groups)
                oldGroup.PropertyChanged -= OnGroupDisplayPropertyChanged;
        }

        Sections.Clear();

        IReadOnlyList<ClipboardGroup> groups = _groupService.Groups;
        IReadOnlyList<ClipboardGroupFolder> folders = _groupService.Folders;

        if (groups.Count == 0)
        {
            IsEmpty = true;
            StatusText = "No groups";
            OnPropertyChanged(nameof(FolderSections));
            return;
        }

        IsEmpty = false;
        StatusText = groups.Count == 1 ? "1 group" : $"{groups.Count} groups";

        Sections.Add(BuildSection(
            folderId: null,
            name: "Ungrouped",
            isCollapsed: _settingsService.LoadGroupsUngroupedCollapsed(),
            groupsInSection: groups.Where(static g => g.FolderId is null),
            folderIndex: -1,
            folderCount: 0));

        for (int i = 0; i < folders.Count; i++)
        {
            ClipboardGroupFolder folder = folders[i];
            GroupSectionDisplayItem section = BuildSection(
                folderId: folder.Id,
                name: folder.Name,
                isCollapsed: folder.IsCollapsed,
                groupsInSection: groups.Where(g => g.FolderId == folder.Id),
                folderIndex: i,
                folderCount: folders.Count);

            if (folder.Id == _pendingEditFolderId)
            {
                section.IsEditing = true;
                _pendingEditFolderId = null;
            }

            Sections.Add(section);
        }

        OnPropertyChanged(nameof(FolderSections));
    }

    private GroupSectionDisplayItem BuildSection(
        string? folderId,
        string name,
        bool isCollapsed,
        IEnumerable<ClipboardGroup> groupsInSection,
        int folderIndex,
        int folderCount)
    {
        List<ClipboardGroup> ordered = SortGroups(groupsInSection.ToList());

        var section = new GroupSectionDisplayItem
        {
            FolderId = folderId,
            Name = name,
            IsCollapsed = isCollapsed,
            ItemCountText = ordered.Count == 1 ? "1 group" : $"{ordered.Count} groups",
            ToggleCollapsedCommand = new RelayCommand(() => ToggleSectionCollapsed(folderId)),
            RenameCommand = folderId is null
                ? null
                : new AsyncRelayCommand<string>(newName => RenameFolderAsync(folderId, newName!)),
            DeleteCommand = folderId is null
                ? null
                : new AsyncRelayCommand(() => DeleteFolderAsync(folderId)),
            MoveUpCommand = folderId is not null && folderIndex > 0
                ? new AsyncRelayCommand(() => _groupService.MoveFolderAsync(folderId, -1))
                : null,
            MoveDownCommand = folderId is not null && folderIndex < folderCount - 1
                ? new AsyncRelayCommand(() => _groupService.MoveFolderAsync(folderId, +1))
                : null,
        };
        section.PropertyChanged += OnSectionPropertyChanged;

        for (int i = 0; i < ordered.Count; i++)
        {
            ClipboardGroup g = ordered[i];
            string gid = g.Id;
            bool isCustomOrder = SortMode == GroupSortMode.Custom;
            var item = new GroupDisplayItem
            {
                Id = gid,
                FolderId = g.FolderId,
                Name = g.Name,
                ItemCountText = g.EntryIds.Count == 1 ? "1 item" : $"{g.EntryIds.Count} items",
                RelativeTime = HistoryTabViewModel.FormatRelativeTime(g.CreatedUtc),
                RenameCommand = new AsyncRelayCommand<string>(newName => RenameGroupAsync(gid, newName!)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteGroupAsync(gid)),
                RestoreCommand = new AsyncRelayCommand<GroupRestoreMode>(mode => RestoreGroupForIdAsync(gid, mode)),
                ExportCommand = new AsyncRelayCommand(() => ExportGroupWithDialogAsync(gid, g.Name)),
                MoveToFolderCommand = new AsyncRelayCommand<string?>(targetFolderId => _groupService.MoveGroupToFolderAsync(gid, targetFolderId)),
                MoveToNewFolderCommand = new AsyncRelayCommand(() => CreateFolderAndMoveGroupAsync(gid)),
                MoveUpCommand = isCustomOrder && i > 0
                    ? new AsyncRelayCommand(() => _groupService.MoveGroupAsync(gid, -1))
                    : null,
                MoveDownCommand = isCustomOrder && i < ordered.Count - 1
                    ? new AsyncRelayCommand(() => _groupService.MoveGroupAsync(gid, +1))
                    : null,
            };
            item.PropertyChanged += OnGroupDisplayPropertyChanged;
            section.Groups.Add(item);
        }

        return section;
    }

    private List<ClipboardGroup> SortGroups(List<ClipboardGroup> groups) => SortMode switch
    {
        GroupSortMode.Alphabetical => groups.OrderBy(static g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        GroupSortMode.ItemCount => groups.OrderByDescending(static g => g.EntryIds.Count).ToList(),
        GroupSortMode.DateCreated => groups.OrderByDescending(static g => g.CreatedUtc).ToList(),
        // Custom: `groups` was filtered (not reordered) from the service's list, whose order IS the
        // custom order — MoveGroupAsync/MoveFolderAsync mutate that list directly.
        GroupSortMode.Custom => groups,
        _ => groups,
    };

    private void OnGroupsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(Refresh);

    private void OnGroupDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupDisplayItem.IsEditing))
            NotifyPinStateMayHaveChanged();
    }

    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupSectionDisplayItem.IsEditing))
            NotifyPinStateMayHaveChanged();
    }

    /// <summary>
    /// Tray popup uses this to avoid closing while a group or folder name is being edited.
    /// </summary>
    public bool AnyGroupEditing =>
        Sections.Any(static s => s.IsEditing) || Sections.SelectMany(static s => s.Groups).Any(static i => i.IsEditing);

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

    private async Task ExportGroupWithDialogAsync(string groupId, string groupName)
    {
        string safe = SanitizeFileNameForExport(groupName);
        var dialog = new SaveFileDialog
        {
            Title = "Export Clipt group",
            Filter = $"Clipt group package (*{ClipboardGroupPackage.FileExtension})|*{ClipboardGroupPackage.FileExtension}",
            DefaultExt = ClipboardGroupPackage.FileExtension.TrimStart('.'),
            FileName = safe + ClipboardGroupPackage.FileExtension,
        };

        if (dialog.ShowDialog() != true)
            return;

        GroupPackageOperationResult result =
            await _groupService.ExportGroupToPackageAsync(groupId, dialog.FileName).ConfigureAwait(true);
        if (!result.Success)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                result.ErrorMessage ?? "Export failed.",
                "Export group",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ImportGroupWithDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Clipt group",
            Filter = $"Clipt group package (*{ClipboardGroupPackage.FileExtension})|*{ClipboardGroupPackage.FileExtension}",
        };

        if (dialog.ShowDialog() != true)
            return;

        GroupPackageOperationResult result =
            await _groupService.ImportGroupFromPackageAsync(dialog.FileName).ConfigureAwait(true);
        MessageBox.Show(
            Application.Current?.MainWindow,
            result.Success
                ? "The group was imported successfully."
                : (result.ErrorMessage ?? "Import failed."),
            "Import group",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static string SanitizeFileNameForExport(string name)
    {
        string n = string.IsNullOrWhiteSpace(name) ? "CliptGroup" : name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return string.IsNullOrWhiteSpace(n) ? "CliptGroup" : n;
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

    private async Task CreateFolderAsync()
    {
        await _groupService.CreateFolderAsync("Untitled folder").ConfigureAwait(true);
        ClipboardGroupFolder? created = _groupService.Folders.Count > 0 ? _groupService.Folders[^1] : null;
        if (created is null)
            return;

        _pendingEditFolderId = created.Id;
        Refresh();
    }

    private async Task CreateFolderAndMoveGroupAsync(string groupId)
    {
        await _groupService.CreateFolderAsync("Untitled folder").ConfigureAwait(true);
        ClipboardGroupFolder? created = _groupService.Folders.Count > 0 ? _groupService.Folders[^1] : null;
        if (created is null)
            return;

        await _groupService.MoveGroupToFolderAsync(groupId, created.Id).ConfigureAwait(true);
        _pendingEditFolderId = created.Id;
        Refresh();
    }

    private async Task RenameFolderAsync(string folderId, string newName)
    {
        await _groupService.RenameFolderAsync(folderId, newName).ConfigureAwait(false);
    }

    private async Task DeleteFolderAsync(string folderId)
    {
        await _groupService.DeleteFolderAsync(folderId).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads current collapse state from the source of truth (not the possibly-stale display item) before
    /// flipping it, so two quick clicks can't race each other into the wrong end state.
    /// </summary>
    private void ToggleSectionCollapsed(string? folderId)
    {
        if (folderId is null)
        {
            bool newValue = !_settingsService.LoadGroupsUngroupedCollapsed();
            _settingsService.SaveGroupsUngroupedCollapsed(newValue);
            Refresh();
            return;
        }

        ClipboardGroupFolder? folder = _groupService.Folders.FirstOrDefault(f => f.Id == folderId);
        bool newCollapsed = folder is null || !folder.IsCollapsed;
        _ = _groupService.SetFolderCollapsedAsync(folderId, newCollapsed);
    }
}

public sealed partial class GroupSectionDisplayItem : ObservableObject
{
    /// <summary>Null for the pinned Ungrouped section; non-null for a real folder.</summary>
    public string? FolderId { get; init; }

    public bool IsFolder => FolderId is not null;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isCollapsed;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _itemCountText = string.Empty;

    public ObservableCollection<GroupDisplayItem> Groups { get; } = [];

    public required IRelayCommand ToggleCollapsedCommand { get; init; }

    /// <summary>Null for the Ungrouped section — it can't be renamed.</summary>
    public IAsyncRelayCommand<string>? RenameCommand { get; init; }

    /// <summary>Null for the Ungrouped section — it can't be deleted.</summary>
    public IAsyncRelayCommand? DeleteCommand { get; init; }

    /// <summary>Null for the Ungrouped section, and for the first/last folder.</summary>
    public IAsyncRelayCommand? MoveUpCommand { get; init; }
    public IAsyncRelayCommand? MoveDownCommand { get; init; }
}

public sealed partial class GroupDisplayItem : ObservableObject
{
    public required string Id { get; init; }

    /// <summary>Null when Ungrouped.</summary>
    public string? FolderId { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    public required string ItemCountText { get; init; }
    public required string RelativeTime { get; init; }

    [ObservableProperty]
    private bool _isEditing;

    public required IAsyncRelayCommand<string> RenameCommand { get; init; }
    public required IAsyncRelayCommand DeleteCommand { get; init; }
    public required IAsyncRelayCommand<GroupRestoreMode> RestoreCommand { get; init; }
    public required IAsyncRelayCommand ExportCommand { get; init; }

    /// <summary>Parameter is the target folder id, or null to move to Ungrouped.</summary>
    public required IAsyncRelayCommand<string?> MoveToFolderCommand { get; init; }

    /// <summary>Creates a new folder and files this group into it in one step.</summary>
    public required IAsyncRelayCommand MoveToNewFolderCommand { get; init; }

    /// <summary>Non-null only in Custom sort mode, and only when not already at the boundary of its section.</summary>
    public IAsyncRelayCommand? MoveUpCommand { get; init; }
    public IAsyncRelayCommand? MoveDownCommand { get; init; }
}
