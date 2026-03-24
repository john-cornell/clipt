using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardHistoryService : IDisposable
{
    IReadOnlyList<ClipboardHistoryEntry> Entries { get; }

    bool IsSuppressed { get; set; }

    Task LoadAsync();
    Task AddAsync(ClipboardSnapshot snapshot);
    Task RemoveAsync(string entryId);
    Task RenameAsync(string entryId, string newName);
    Task PromoteAsync(string entryId);
    /// <summary>
    /// Moves the entry one position in the given direction (-1 = toward index 0, +1 = toward end).
    /// No-op when the entry is already at the boundary or the id is not found.
    /// </summary>
    Task MoveAsync(string entryId, int direction);
    Task ClearAsync();
    /// <summary>
    /// Removes entries whose <see cref="ClipboardHistoryEntry.ContentHash"/> does not match.
    /// When <paramref name="contentHashHex"/> is null or empty, removes all entries (same as <see cref="ClearAsync"/>).
    /// </summary>
    Task ClearExceptMatchingContentHashAsync(string? contentHashHex);
    Task<ClipboardSnapshot?> RestoreAsync(string entryId);

    /// <summary>
    /// Reorders history from a saved group. Missing entries or blobs are skipped.
    /// </summary>
    Task RestoreGroupAsync(IReadOnlyList<string> entryIds, GroupRestoreMode mode);

    event EventHandler? EntriesChanged;
}
