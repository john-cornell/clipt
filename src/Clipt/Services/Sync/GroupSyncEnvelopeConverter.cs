using System.Security.Cryptography;
using System.Text.Json;
using Clipt.Models;

namespace Clipt.Services.Sync;

public static class GroupSyncEnvelopeConverter
{
    public static GroupSyncEnvelope ToEnvelope(ClipboardGroup group, IReadOnlyDictionary<string, byte[]> entryBlobs)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(entryBlobs);

        var entries = new List<GroupSyncEnvelopeEntry>(group.Entries.Count);
        foreach (ArchivedGroupEntryInfo entry in group.Entries)
        {
            if (!entryBlobs.TryGetValue(entry.Id, out byte[]? blob))
                continue;

            entries.Add(new GroupSyncEnvelopeEntry
            {
                Id = entry.Id,
                Name = entry.Name,
                TimestampUtc = entry.TimestampUtc,
                ContentType = entry.ContentType,
                BlobBase64 = Convert.ToBase64String(blob),
            });
        }

        return new GroupSyncEnvelope
        {
            Name = group.Name,
            FolderId = group.FolderId,
            CreatedUtc = group.CreatedUtc,
            Entries = entries,
        };
    }

    public static byte[] SerializeToBytes(GroupSyncEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope);

    public static GroupSyncEnvelope DeserializeFromBytes(byte[] json) =>
        JsonSerializer.Deserialize<GroupSyncEnvelope>(json)
            ?? throw new InvalidOperationException("Decrypted envelope deserialized to null.");

    /// <summary>Reconstructs the entry list and raw blob bytes for <c>IClipboardGroupService.ApplyRemoteGroupAsync</c>.</summary>
    public static (List<ArchivedGroupEntryInfo> Entries, Dictionary<string, byte[]> Blobs) FromEnvelope(GroupSyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var entries = new List<ArchivedGroupEntryInfo>(envelope.Entries.Count);
        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (GroupSyncEnvelopeEntry e in envelope.Entries)
        {
            byte[] blob = Convert.FromBase64String(e.BlobBase64);
            blobs[e.Id] = blob;
            entries.Add(new ArchivedGroupEntryInfo(
                Id: e.Id,
                SourceEntryId: string.Empty,
                Name: e.Name,
                TimestampUtc: e.TimestampUtc,
                SequenceNumber: 0,
                OwnerProcess: "(synced)",
                OwnerPid: 0,
                Summary: e.Name,
                ContentType: e.ContentType,
                DataSizeBytes: blob.LongLength,
                ContentHash: Convert.ToHexString(SHA256.HashData(blob))));
        }

        return (entries, blobs);
    }

    /// <summary>SHA-256 over the canonical envelope bytes — used to detect local changes since the last successful sync.</summary>
    public static string ComputeContentHash(GroupSyncEnvelope envelope) =>
        Convert.ToHexString(SHA256.HashData(SerializeToBytes(envelope)));
}
