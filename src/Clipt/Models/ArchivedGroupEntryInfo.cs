namespace Clipt.Models;

/// <summary>
/// Read model for one archived clip inside a saved group — full field parity with the private
/// serialization DTO in <c>ClipboardGroupService</c> so renaming/deleting/reordering one entry never
/// drops metadata (OwnerProcess, SequenceNumber, etc.) for the group's other entries on write-back.
/// </summary>
public sealed record ArchivedGroupEntryInfo(
    string Id,
    string SourceEntryId,
    string Name,
    DateTime TimestampUtc,
    uint SequenceNumber,
    string OwnerProcess,
    int OwnerPid,
    string Summary,
    ContentType ContentType,
    long DataSizeBytes,
    string ContentHash);
