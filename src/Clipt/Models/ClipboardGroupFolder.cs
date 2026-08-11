namespace Clipt.Models;

public sealed class ClipboardGroupFolder
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedUtc { get; init; }
    public bool IsCollapsed { get; set; }
}
