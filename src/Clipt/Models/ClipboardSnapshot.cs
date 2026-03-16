using System.Collections.Immutable;

namespace Clipt.Models;

public sealed class ClipboardSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required uint SequenceNumber { get; init; }
    public required string OwnerProcessName { get; init; }
    public required int OwnerProcessId { get; init; }
    public required ImmutableArray<ClipboardFormatInfo> Formats { get; init; }

    public static ClipboardSnapshot Empty { get; } = new()
    {
        Timestamp = DateTime.MinValue,
        SequenceNumber = 0,
        OwnerProcessName = "(none)",
        OwnerProcessId = 0,
        Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
    };
}
