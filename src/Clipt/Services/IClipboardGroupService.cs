using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardGroupService
{
    IReadOnlyList<ClipboardGroup> Groups { get; }

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

    event EventHandler? GroupsChanged;
}
