using System.IO;
using System.Text.Json;
using Clipt.Models;
using Clipt.Services;
using Moq;
using Xunit;

namespace Clipt.Tests.Services;

public class ClipboardGroupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IAppLogger> _loggerMock;
    private readonly List<HistorySeedIndexEntry> _seedEntries = [];

    public ClipboardGroupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliptGroupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _loggerMock = new Mock<IAppLogger>();
        _loggerMock.Setup(l => l.Level).Returns(AppLogLevel.Debug);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ClipboardGroupService CreateService() => new(_tempDir, _loggerMock.Object);

    private async Task SeedHistoryEntryAsync(
        string id,
        string name = "Clip",
        string summary = "Summary",
        string blobText = "blob")
    {
        string blobsDir = Path.Combine(_tempDir, "blobs");
        Directory.CreateDirectory(blobsDir);
        await File.WriteAllTextAsync(Path.Combine(blobsDir, id + ".bin"), blobText);

        string indexPath = Path.Combine(_tempDir, "index.json");
        _seedEntries.Add(new HistorySeedIndexEntry
        {
            Id = id,
            Name = name,
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 1u,
            OwnerProcess = "test",
            OwnerPid = 1,
            Summary = summary,
            ContentType = ContentType.Text,
            DataSizeBytes = blobText.Length,
            ContentHash = "ABC",
        });

        string json = JsonSerializer.Serialize(
            new { Entries = _seedEntries.ToList() },
            CliptJsonOptions.Shared);
        await File.WriteAllTextAsync(indexPath, json);
    }

    private sealed class HistorySeedIndexEntry
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public DateTime TimestampUtc { get; set; }
        public uint SequenceNumber { get; set; }
        public string? OwnerProcess { get; set; }
        public int OwnerPid { get; set; }
        public string? Summary { get; set; }
        public ContentType ContentType { get; set; }
        public long DataSizeBytes { get; set; }
        public string? ContentHash { get; set; }
    }

    [Fact]
    public async Task SaveGroupAsync_PersistsToGroupsJson()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("id1");
        await SeedHistoryEntryAsync("id2");

        await svc.SaveGroupAsync("Alpha", new[] { "id1", "id2" });

        Assert.Single(svc.Groups);
        Assert.Equal("Alpha", svc.Groups[0].Name);
        Assert.Equal(2, svc.Groups[0].EntryIds.Count);

        string path = Path.Combine(_tempDir, "groups.json");
        Assert.True(File.Exists(path));
        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("groups", out JsonElement arr));
        Assert.Equal(1, arr.GetArrayLength());
        Assert.True(arr[0].TryGetProperty("archivedEntries", out JsonElement archived));
        Assert.Equal(2, archived.GetArrayLength());
    }

    [Fact]
    public async Task LoadAsync_RoundTripsSavedGroups()
    {
        var svc = CreateService();
        await SeedHistoryEntryAsync("a");
        await SeedHistoryEntryAsync("b");
        await SeedHistoryEntryAsync("c");
        await svc.SaveGroupAsync("One", new[] { "a" });
        await svc.SaveGroupAsync("Two", new[] { "b", "c" });

        var svc2 = CreateService();
        await svc2.LoadAsync();

        Assert.Equal(2, svc2.Groups.Count);
        Assert.Equal("Two", svc2.Groups[0].Name);
        Assert.Equal("One", svc2.Groups[1].Name);
    }

    [Fact]
    public async Task RenameGroupAsync_UpdatesName()
    {
        var svc = CreateService();
        await SeedHistoryEntryAsync("x");
        await svc.SaveGroupAsync("Old", new[] { "x" });
        string id = svc.Groups[0].Id;

        await svc.RenameGroupAsync(id, "New");

        Assert.Equal("New", svc.Groups[0].Name);
    }

    [Fact]
    public async Task DeleteGroupAsync_RemovesGroup()
    {
        var svc = CreateService();
        await SeedHistoryEntryAsync("x");
        await svc.SaveGroupAsync("G", new[] { "x" });
        string id = svc.Groups[0].Id;

        await svc.DeleteGroupAsync(id);

        Assert.Empty(svc.Groups);
    }

    [Fact]
    public async Task SaveGroupAsync_DeduplicatesIds_PreservesOrder()
    {
        var svc = CreateService();
        await SeedHistoryEntryAsync("a");
        await SeedHistoryEntryAsync("b");
        await svc.SaveGroupAsync("Dup", new[] { "a", "a", "b" });

        Assert.Equal(2, svc.Groups[0].EntryIds.Count);
    }

    [Fact]
    public async Task SaveGroupAsync_CopiesBlobsToGroupArchive()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("id1", blobText: "hello-blob");

        await svc.SaveGroupAsync("Archive", new[] { "id1" });

        string groupId = svc.Groups[0].Id;
        string archivedId = svc.Groups[0].EntryIds[0];
        string archivedBlobPath = Path.Combine(_tempDir, "groups", groupId, "blobs", archivedId + ".bin");
        Assert.True(File.Exists(archivedBlobPath));
        Assert.Equal("hello-blob", await File.ReadAllTextAsync(archivedBlobPath));
    }

    [Fact]
    public async Task SaveGroupAsync_LogsWarningWhenEntryNotInIndex()
    {
        var svc = CreateService();
        await SeedHistoryEntryAsync("exists");

        await svc.SaveGroupAsync("Partial", new[] { "exists", "ghost" });

        Assert.Single(svc.Groups);
        Assert.Single(svc.Groups[0].EntryIds);
        _loggerMock.Verify(
            l => l.Warn(It.Is<string>(m => m.Contains("ghost") && m.Contains("not found in index"))),
            Times.Once);
    }

    [Fact]
    public async Task SaveGroupAsync_LogsWarningWhenAllEntriesUnresolvable()
    {
        var svc = CreateService();
        await SeedHistoryEntryAsync("real");

        await svc.SaveGroupAsync("Empty", new[] { "fake1", "fake2" });

        Assert.Empty(svc.Groups);
        _loggerMock.Verify(
            l => l.Warn(It.Is<string>(m => m.Contains("could not resolve any entries"))),
            Times.Once);
    }

    [Fact]
    public async Task SaveGroupAsync_IndexWithStringContentType_WritesGroup()
    {
        string entryId = "prodstyle1";
        Directory.CreateDirectory(Path.Combine(_tempDir, "blobs"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "blobs", entryId + ".bin"), "payload");

        string indexJson = JsonSerializer.Serialize(new
        {
            Entries = new[]
            {
                new
                {
                    Id = entryId,
                    Name = "Line",
                    TimestampUtc = DateTime.UtcNow,
                    SequenceNumber = 42u,
                    OwnerProcess = "test",
                    OwnerPid = 1,
                    Summary = "hello",
                    ContentType = ContentType.Text,
                    DataSizeBytes = 7L,
                    ContentHash = "abc",
                },
            },
        }, CliptJsonOptions.Shared);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "index.json"), indexJson);

        var svc = CreateService();
        await svc.LoadAsync();
        await svc.SaveGroupAsync("FromStringEnumIndex", new[] { entryId });

        Assert.Single(svc.Groups);
        Assert.Equal("FromStringEnumIndex", svc.Groups[0].Name);
        Assert.Single(svc.Groups[0].EntryIds);

        string rawJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "index.json"));
        Assert.Contains("\"text\"", rawJson);
        Assert.DoesNotContain("\"1\"", rawJson);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "groups.json");
        await File.WriteAllTextAsync(path, "NOT VALID JSON {{{");

        var svc = CreateService();
        await svc.LoadAsync();

        Assert.Empty(svc.Groups);
        _loggerMock.Verify(
            l => l.Warn(It.Is<string>(m => m.Contains("failed to deserialize groups.json"))),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_EmptyGroupsJson_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "groups.json");
        await File.WriteAllTextAsync(path, "{}");

        var svc = CreateService();
        await svc.LoadAsync();

        Assert.Empty(svc.Groups);
    }

    [Fact]
    public async Task SaveGroupAsync_NoIndexFile_LogsWarning()
    {
        var svc = CreateService();
        Directory.CreateDirectory(Path.Combine(_tempDir, "blobs"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "blobs", "x.bin"), "data");

        await svc.SaveGroupAsync("NoIndex", new[] { "x" });

        Assert.Empty(svc.Groups);
        _loggerMock.Verify(
            l => l.Warn(It.Is<string>(m => m.Contains("index.json not found"))),
            Times.Once);
    }
}
