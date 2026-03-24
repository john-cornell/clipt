using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardGroupService
{
    IReadOnlyList<ClipboardGroup> Groups { get; }

    Task LoadAsync();

    Task SaveGroupAsync(string name, IReadOnlyList<string> entryIds);

    Task RenameGroupAsync(string groupId, string newName);

    Task DeleteGroupAsync(string groupId);

    event EventHandler? GroupsChanged;
}
