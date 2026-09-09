using Clipt.Models;
using Clipt.Services;
using Clipt.Services.Sync;
using Moq;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncServiceTests
{
    private readonly Mock<IClipboardGroupService> _groupServiceMock = new();
    private readonly Mock<IGroupSyncApiClient> _apiClientMock = new();
    private readonly Mock<IGroupSyncCrypto> _cryptoMock = new();
    private readonly Mock<IGroupSyncStateStore> _stateStoreMock = new();
    private readonly Mock<IGroupEntryBlobReader> _blobReaderMock = new();
    private readonly Mock<ISettingsService> _settingsMock = new();

    private static readonly byte[] FakeKey = new byte[32];

    public GroupSyncServiceTests()
    {
        _settingsMock.Setup(s => s.LoadGroupSyncServerUrl()).Returns((string?)null);
        _settingsMock.Setup(s => s.LoadGroupSyncToken()).Returns((string?)null);
        _settingsMock.Setup(s => s.LoadGroupSyncEnabled()).Returns(false);
        _groupServiceMock.Setup(g => g.Groups).Returns([]);
        _apiClientMock
            .Setup(a => a.GetKdfAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncKdfParams([1, 2, 3, 4], 3, 65536, 4));
        _cryptoMock
            .Setup(c => c.DeriveKey(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(FakeKey);
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(new GroupSyncState());
        _apiClientMock
            .Setup(a => a.PullGroupsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncPullResult([], 0));
    }

    private GroupSyncService CreateService() => new(
        _groupServiceMock.Object,
        _apiClientMock.Object,
        _cryptoMock.Object,
        _stateStoreMock.Object,
        _blobReaderMock.Object,
        _settingsMock.Object);

    private static ClipboardGroup MakeGroup(string id, params string[] entryIds) => new()
    {
        Id = id,
        Name = "Group",
        CreatedUtc = DateTime.UtcNow,
        EntryIds = entryIds.ToList(),
        Entries = entryIds.Select(eid => new ArchivedGroupEntryInfo(
            Id: eid, SourceEntryId: "", Name: "Clip", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "test", OwnerPid: 0, Summary: "Clip",
            ContentType: ContentType.Text, DataSizeBytes: 1, ContentHash: "x")).ToList(),
    };

    [Fact]
    public async Task EnableAsync_ConfiguresApiClientDerivesKeyAndSavesSettings()
    {
        var service = CreateService();

        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _apiClientMock.Verify(a => a.Configure(new Uri("https://sync.example.com/"), "token"), Times.Once);
        _settingsMock.Verify(s => s.SaveGroupSyncServerUrl("https://sync.example.com/"), Times.Once);
        _settingsMock.Verify(s => s.SaveGroupSyncToken("token"), Times.Once);
        _settingsMock.Verify(s => s.SaveGroupSyncEnabled(true), Times.Once);
        Assert.True(service.IsConfigured);
        Assert.True(service.IsUnlocked);
    }

    [Fact]
    public async Task EnableAsync_PushesExistingLocalGroupWithBasedOnVersionZero()
    {
        _groupServiceMock.Setup(g => g.Groups).Returns([MakeGroup("g1", "e1")]);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);
        _cryptoMock.Setup(c => c.Encrypt(FakeKey, It.IsAny<byte[]>())).Returns([9, 9, 9]);
        _apiClientMock
            .Setup(a => a.PushGroupAsync("g1", It.IsAny<byte[]>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _apiClientMock.Verify(a => a.PushGroupAsync("g1", It.IsAny<byte[]>(), 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_NotUnlocked_DoesNothing()
    {
        var service = CreateService(); // EnableAsync never called

        await service.SyncNowAsync();

        _apiClientMock.Verify(a => a.PullGroupsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncNowAsync_UnchangedGroup_SkipsPush()
    {
        var group = MakeGroup("g1", "e1");
        _groupServiceMock.Setup(g => g.Groups).Returns([group]);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);

        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        string hash = GroupSyncEnvelopeConverter.ComputeContentHash(envelope);
        var state = new GroupSyncState();
        state.Groups["g1"] = new GroupSyncStateEntry { SyncedVersion = 1, ContentHash = hash };
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");
        _apiClientMock.Invocations.Clear();

        await service.SyncNowAsync();

        _apiClientMock.Verify(a => a.PushGroupAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncNowAsync_LocallyDeletedGroup_CallsDeleteGroupAsync()
    {
        var state = new GroupSyncState();
        state.Groups["gone"] = new GroupSyncStateEntry { SyncedVersion = 4, ContentHash = "irrelevant" };
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);
        _groupServiceMock.Setup(g => g.Groups).Returns([]); // "gone" no longer exists locally
        _apiClientMock
            .Setup(a => a.DeleteGroupAsync("gone", 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _apiClientMock.Verify(a => a.DeleteGroupAsync("gone", 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_PullAppliesRemoteGroup_WhenNotLocallyDirty()
    {
        GroupSyncEnvelope envelope = new()
        {
            Name = "Remote Group",
            FolderId = null,
            CreatedUtc = DateTime.UtcNow,
            Entries = [new GroupSyncEnvelopeEntry { Id = "e1", Name = "Clip", TimestampUtc = DateTime.UtcNow, ContentType = ContentType.Text, BlobBase64 = Convert.ToBase64String([1, 2, 3]) }],
        };
        byte[] plaintext = GroupSyncEnvelopeConverter.SerializeToBytes(envelope);
        _apiClientMock
            .Setup(a => a.PullGroupsAsync(0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncPullResult([new GroupSyncPulledGroup("remote1", [9, 9, 9], 3, false)], 3));
        _cryptoMock.Setup(c => c.Decrypt(FakeKey, new byte[] { 9, 9, 9 })).Returns(plaintext);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _groupServiceMock.Verify(g => g.ApplyRemoteGroupAsync(
            "remote1", "Remote Group", null, envelope.CreatedUtc,
            It.Is<IReadOnlyList<ArchivedGroupEntryInfo>>(e => e.Count == 1 && e[0].Id == "e1"),
            It.Is<IReadOnlyDictionary<string, byte[]>>(b => b["e1"].SequenceEqual(new byte[] { 1, 2, 3 }))),
            Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_PullTombstone_DeletesLocalGroupIfPresent()
    {
        _groupServiceMock.Setup(g => g.Groups).Returns([MakeGroup("g1", "e1")]);
        var state = new GroupSyncState();
        state.Groups["g1"] = new GroupSyncStateEntry { SyncedVersion = 2, ContentHash = "will-not-match-anyway" };
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);
        // Make the local content hash match tracked state, so the incoming tombstone isn't skipped as "locally dirty".
        GroupSyncEnvelope localEnvelope = GroupSyncEnvelopeConverter.ToEnvelope(MakeGroup("g1", "e1"), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        state.Groups["g1"].ContentHash = GroupSyncEnvelopeConverter.ComputeContentHash(localEnvelope);
        _apiClientMock
            .Setup(a => a.PullGroupsAsync(0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncPullResult([new GroupSyncPulledGroup("g1", null, 3, true)], 3));

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _groupServiceMock.Verify(g => g.DeleteGroupAsync("g1"), Times.Once);
    }

    [Fact]
    public async Task PushConflict_PullsThenRetriesOnce_ThenSucceeds()
    {
        var group = MakeGroup("g1", "e1");
        _groupServiceMock.Setup(g => g.Groups).Returns([group]);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);
        var callCount = 0;
        _apiClientMock
            .Setup(a => a.PushGroupAsync("g1", It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1 ? throw new GroupSyncConflictException() : Task.FromResult(9);
            });

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        Assert.Equal(2, callCount);
        _apiClientMock.Verify(a => a.PullGroupsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
