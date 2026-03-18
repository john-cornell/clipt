using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardHistoryService : IDisposable
{
    IReadOnlyList<ClipboardHistoryEntry> Entries { get; }

    bool IsSuppressed { get; set; }

    Task LoadAsync();
    Task AddAsync(ClipboardSnapshot snapshot);
    Task RemoveAsync(string entryId);
    Task PromoteAsync(string entryId);
    Task ClearAsync();
    Task<ClipboardSnapshot?> RestoreAsync(string entryId);

    event EventHandler? EntriesChanged;
}
