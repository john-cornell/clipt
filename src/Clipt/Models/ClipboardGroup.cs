namespace Clipt.Models;

public sealed class ClipboardGroup
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedUtc { get; init; }
    public required IReadOnlyList<string> EntryIds { get; init; }

    /// <summary>Folder this group is filed under, or null for Ungrouped.</summary>
    public string? FolderId { get; set; }
}
