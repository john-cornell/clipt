using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardGroupService
{
    IReadOnlyList<ClipboardGroup> Groups { get; }

    /// <summary>Folders, in display order. Order is changed only via <see cref="MoveFolderAsync"/>.</summary>
    IReadOnlyList<ClipboardGroupFolder> Folders { get; }

    Task LoadAsync();

    Task SaveGroupAsync(string name, IReadOnlyList<string> entryIds);

    Task RenameGroupAsync(string groupId, string newName);

    Task DeleteGroupAsync(string groupId);

    /// <summary>Writes a portable .cliptgroup zip (manifest + blobs) for the given saved group.</summary>
    Task<GroupPackageOperationResult> ExportGroupToPackageAsync(
        string groupId,
        string packageFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>Imports a group from a .cliptgroup file; assigns new group and entry IDs on this machine.</summary>
    Task<GroupPackageOperationResult> ImportGroupFromPackageAsync(
        string packageFilePath,
        CancellationToken cancellationToken = default);

    Task AddEntriesToGroupAsync(string groupId, IReadOnlyList<string> entryIds);

    Task CreateFolderAsync(string name);
    Task RenameFolderAsync(string folderId, string newName);

    /// <summary>Moves every group in the folder to Ungrouped (FolderId = null), then removes the folder. Never deletes group data.</summary>
    Task DeleteFolderAsync(string folderId);

    Task SetFolderCollapsedAsync(string folderId, bool collapsed);

    /// <summary>Reorders a folder among all folders. direction: -1 toward index 0, +1 toward the end. No-op at a boundary or unknown id.</summary>
    Task MoveFolderAsync(string folderId, int direction);

    /// <summary>Files a group under a folder, or moves it to Ungrouped when <paramref name="folderId"/> is null.</summary>
    Task MoveGroupToFolderAsync(string groupId, string? folderId);

    /// <summary>Reorders a group among its current folder/Ungrouped siblings only. direction: -1/+1. No-op at a boundary or unknown id.</summary>
    Task MoveGroupAsync(string groupId, int direction);

    /// <summary>Renames one archived clip within a group (metadata only — does not touch its blob).</summary>
    Task RenameGroupEntryAsync(string groupId, string entryId, string newName);

    /// <summary>
    /// Removes one archived clip from a group and deletes its blob file. If this empties the group,
    /// deletes the whole group (and its archive folder) instead of leaving a 0-item group behind.
    /// </summary>
    Task DeleteGroupEntryAsync(string groupId, string entryId);

    /// <summary>
    /// Reorders a clip within its group. direction: -1 toward index 0, +1 toward the end. No-op at a
    /// boundary or unknown id. Changes restore order/content, since restore uses <see cref="ClipboardGroup.EntryIds"/> order.
    /// </summary>
    Task MoveGroupEntryAsync(string groupId, string entryId, int direction);

    event EventHandler? GroupsChanged;
}
