using Clipt.Models;

namespace Clipt.Services.Sync;

/// <summary>
/// The plaintext JSON shape encrypted into one group's sync ciphertext. Deliberately narrower than
/// <see cref="ClipboardGroup"/>/<see cref="ArchivedGroupEntryInfo"/> — fields like SourceEntryId,
/// OwnerProcess, and SequenceNumber describe the originating device's local history and aren't
/// meaningful once synced elsewhere (mirrors what <c>ClipboardGroupService.ImportGroupFromPackageAsync</c>
/// already does for cross-device group transfer via .cliptgroup files).
/// </summary>
public sealed class GroupSyncEnvelope
{
    public required string Name { get; init; }
    public string? FolderId { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required List<GroupSyncEnvelopeEntry> Entries { get; init; }
}

public sealed class GroupSyncEnvelopeEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required ContentType ContentType { get; init; }
    public required string BlobBase64 { get; init; }
}
