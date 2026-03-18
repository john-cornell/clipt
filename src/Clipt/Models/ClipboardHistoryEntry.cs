namespace Clipt.Models;

public sealed class ClipboardHistoryEntry
{
    public required string Id { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required uint SequenceNumber { get; init; }
    public required string OwnerProcess { get; init; }
    public required int OwnerPid { get; init; }
    public required string Summary { get; init; }
    public required ContentType ContentType { get; init; }
    public required long DataSizeBytes { get; init; }
    public string ContentHash { get; init; } = string.Empty;
}
