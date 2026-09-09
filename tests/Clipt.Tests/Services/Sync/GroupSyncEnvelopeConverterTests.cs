using Clipt.Models;
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncEnvelopeConverterTests
{
    private static ClipboardGroup MakeGroup(string id = "g1", string? folderId = null) => new()
    {
        Id = id,
        Name = "My Group",
        CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        FolderId = folderId,
        EntryIds = ["e1"],
        Entries =
        [
            new ArchivedGroupEntryInfo(
                Id: "e1",
                SourceEntryId: "src1",
                Name: "Clip One",
                TimestampUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SequenceNumber: 1,
                OwnerProcess: "notepad",
                OwnerPid: 123,
                Summary: "Clip One",
                ContentType: ContentType.Text,
                DataSizeBytes: 5,
                ContentHash: "irrelevant-on-source-device"),
        ],
    };

    [Fact]
    public void ToEnvelope_BuildsExpectedEnvelope()
    {
        ClipboardGroup group = MakeGroup(folderId: "f1");
        var blobs = new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3, 4, 5] };

        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, blobs);

        Assert.Equal("My Group", envelope.Name);
        Assert.Equal("f1", envelope.FolderId);
        Assert.Single(envelope.Entries);
        Assert.Equal("e1", envelope.Entries[0].Id);
        Assert.Equal("Clip One", envelope.Entries[0].Name);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4, 5]), envelope.Entries[0].BlobBase64);
    }

    [Fact]
    public void ToEnvelope_EntryMissingFromBlobDictionary_IsSkipped()
    {
        ClipboardGroup group = MakeGroup();
        var blobs = new Dictionary<string, byte[]>(); // e1's blob missing

        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, blobs);

        Assert.Empty(envelope.Entries);
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTrips()
    {
        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(folderId: "f1"), new Dictionary<string, byte[]> { ["e1"] = [9, 9, 9] });

        byte[] bytes = GroupSyncEnvelopeConverter.SerializeToBytes(envelope);
        GroupSyncEnvelope roundTripped = GroupSyncEnvelopeConverter.DeserializeFromBytes(bytes);

        Assert.Equal(envelope.Name, roundTripped.Name);
        Assert.Equal(envelope.FolderId, roundTripped.FolderId);
        Assert.Equal(envelope.Entries[0].BlobBase64, roundTripped.Entries[0].BlobBase64);
    }

    [Fact]
    public void FromEnvelope_ReconstructsEntriesAndBlobs()
    {
        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [7, 7, 7] });

        (List<ArchivedGroupEntryInfo> entries, Dictionary<string, byte[]> blobs) = GroupSyncEnvelopeConverter.FromEnvelope(envelope);

        Assert.Single(entries);
        Assert.Equal("e1", entries[0].Id);
        Assert.Equal("Clip One", entries[0].Name);
        Assert.Equal([7, 7, 7], blobs["e1"]);
    }

    [Fact]
    public void ComputeContentHash_SameContent_ProducesSameHash()
    {
        GroupSyncEnvelope envelope1 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        GroupSyncEnvelope envelope2 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });

        Assert.Equal(
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope1),
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope2));
    }

    [Fact]
    public void ComputeContentHash_DifferentContent_ProducesDifferentHash()
    {
        GroupSyncEnvelope envelope1 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        GroupSyncEnvelope envelope2 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [9, 9, 9] });

        Assert.NotEqual(
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope1),
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope2));
    }
}
