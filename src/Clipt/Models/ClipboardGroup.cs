namespace Clipt.Models;

public sealed class ClipboardGroup
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedUtc { get; init; }
    public required IReadOnlyList<string> EntryIds { get; init; }

    /// <summary>Folder this group is filed under, or null for Ungrouped.</summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// Rich per-clip metadata, same order as <see cref="EntryIds"/>. Empty when the group predates
    /// archived-entry metadata (loaded from a legacy groups.json with EntryIds but no ArchivedEntries) —
    /// non-required, defaulting to empty, so every existing construction site keeps compiling.
    /// </summary>
    public IReadOnlyList<ArchivedGroupEntryInfo> Entries { get; init; } = [];
}
