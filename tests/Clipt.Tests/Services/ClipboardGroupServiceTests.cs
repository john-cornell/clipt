using System.IO;
using System.IO.Compression;
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

    [Fact]
    public async Task ExportGroupToPackageAsync_UnknownGroup_Fails()
    {
        var svc = CreateService();
        await svc.LoadAsync();

        GroupPackageOperationResult r = await svc.ExportGroupToPackageAsync(
            "ffffffffffffffffffffffffffffffff",
            Path.Combine(_tempDir, "out.cliptgroup"));

        Assert.False(r.Success);
        Assert.NotNull(r.ErrorMessage);
    }

    [Fact]
    public async Task ExportGroupToPackageAsync_MissingArchiveBlob_Fails()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("id1", blobText: "keep");
        await svc.SaveGroupAsync("G", new[] { "id1" });
        string gid = svc.Groups[0].Id;
        string bid = svc.Groups[0].EntryIds[0];
        File.Delete(Path.Combine(_tempDir, "groups", gid, "blobs", bid + ".bin"));

        GroupPackageOperationResult r = await svc.ExportGroupToPackageAsync(
            gid,
            Path.Combine(_tempDir, "out.cliptgroup"));

        Assert.False(r.Success);
    }

    [Fact]
    public async Task ExportGroupToPackageAsync_WritesZipWithManifestAndBlobs()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("id1", blobText: "payload-a");
        await SeedHistoryEntryAsync("id2", blobText: "payload-b");
        await svc.SaveGroupAsync("Pack", new[] { "id1", "id2" });
        string gid = svc.Groups[0].Id;

        string zipPath = Path.Combine(_tempDir, "export.cliptgroup");
        GroupPackageOperationResult r = await svc.ExportGroupToPackageAsync(gid, zipPath);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.True(File.Exists(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        foreach (string entryId in svc.Groups[0].EntryIds)
        {
            Assert.NotNull(zip.GetEntry("blobs/" + entryId + ".bin"));
        }
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsWithNewIdsAndSameBlobBytes()
    {
        string importDir = Path.Combine(Path.GetTempPath(), "CliptImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(importDir);
        try
        {
            var exportSvc = CreateService();
            await exportSvc.LoadAsync();
            await SeedHistoryEntryAsync("hist1", blobText: "round-trip-bytes");
            await exportSvc.SaveGroupAsync("Remote", new[] { "hist1" });
            string origGroupId = exportSvc.Groups[0].Id;
            string origEntryId = exportSvc.Groups[0].EntryIds[0];

            string zipPath = Path.Combine(_tempDir, "move.cliptgroup");
            Assert.True((await exportSvc.ExportGroupToPackageAsync(origGroupId, zipPath)).Success);

            var importSvc = new ClipboardGroupService(importDir, _loggerMock.Object);
            await importSvc.LoadAsync();
            GroupPackageOperationResult importResult = await importSvc.ImportGroupFromPackageAsync(zipPath);
            Assert.True(importResult.Success, importResult.ErrorMessage);

            Assert.Single(importSvc.Groups);
            Assert.Equal("Remote", importSvc.Groups[0].Name);
            Assert.NotEqual(origGroupId, importSvc.Groups[0].Id);
            Assert.Single(importSvc.Groups[0].EntryIds);
            Assert.NotEqual(origEntryId, importSvc.Groups[0].EntryIds[0]);

            string newBlob = Path.Combine(
                importDir,
                "groups",
                importSvc.Groups[0].Id,
                "blobs",
                importSvc.Groups[0].EntryIds[0] + ".bin");
            Assert.True(File.Exists(newBlob));
            Assert.Equal("round-trip-bytes", await File.ReadAllTextAsync(newBlob));
        }
        finally
        {
            try
            {
                if (Directory.Exists(importDir))
                    Directory.Delete(importDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ImportGroupFromPackageAsync_NotAZip_FailsWithoutAddingGroup()
    {
        string zipPath = Path.Combine(_tempDir, "garbage.cliptgroup");
        await File.WriteAllTextAsync(zipPath, "not a zip file");

        var svc = CreateService();
        await svc.LoadAsync();
        GroupPackageOperationResult r = await svc.ImportGroupFromPackageAsync(zipPath);

        Assert.False(r.Success);
        Assert.Empty(svc.Groups);
    }

    [Fact]
    public async Task CreateFolderAsync_AddsFolderAndPersists()
    {
        var svc = CreateService();
        await svc.LoadAsync();

        await svc.CreateFolderAsync("Work");

        Assert.Single(svc.Folders);
        Assert.Equal("Work", svc.Folders[0].Name);
        Assert.False(svc.Folders[0].IsCollapsed);

        var svc2 = CreateService();
        await svc2.LoadAsync();
        Assert.Single(svc2.Folders);
        Assert.Equal("Work", svc2.Folders[0].Name);
    }

    [Fact]
    public async Task CreateFolderAsync_BlankName_UsesDefault()
    {
        var svc = CreateService();
        await svc.LoadAsync();

        await svc.CreateFolderAsync("   ");

        Assert.Equal("Untitled folder", svc.Folders[0].Name);
    }

    [Fact]
    public async Task RenameFolderAsync_UpdatesName()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await svc.CreateFolderAsync("Old");
        string id = svc.Folders[0].Id;

        await svc.RenameFolderAsync(id, "New");

        Assert.Equal("New", svc.Folders[0].Name);
    }

    [Fact]
    public async Task RenameFolderAsync_UnknownId_DoesNotThrow()
    {
        var svc = CreateService();
        await svc.LoadAsync();

        await svc.RenameFolderAsync("nope", "New");

        Assert.Empty(svc.Folders);
    }

    [Fact]
    public async Task MoveGroupToFolderAsync_FilesGroupUnderFolder()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("x");
        await svc.SaveGroupAsync("G", new[] { "x" });
        await svc.CreateFolderAsync("Work");
        string groupId = svc.Groups[0].Id;
        string folderId = svc.Folders[0].Id;

        await svc.MoveGroupToFolderAsync(groupId, folderId);

        Assert.Equal(folderId, svc.Groups[0].FolderId);

        var svc2 = CreateService();
        await svc2.LoadAsync();
        Assert.Equal(folderId, svc2.Groups[0].FolderId);
    }

    [Fact]
    public async Task MoveGroupToFolderAsync_NullFolderId_MovesToUngrouped()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("x");
        await svc.SaveGroupAsync("G", new[] { "x" });
        await svc.CreateFolderAsync("Work");
        string groupId = svc.Groups[0].Id;
        await svc.MoveGroupToFolderAsync(groupId, svc.Folders[0].Id);

        await svc.MoveGroupToFolderAsync(groupId, null);

        Assert.Null(svc.Groups[0].FolderId);
    }

    [Fact]
    public async Task MoveGroupToFolderAsync_UnknownFolderId_DoesNotChangeGroup()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("x");
        await svc.SaveGroupAsync("G", new[] { "x" });
        string groupId = svc.Groups[0].Id;

        await svc.MoveGroupToFolderAsync(groupId, "does-not-exist");

        Assert.Null(svc.Groups[0].FolderId);
    }

    [Fact]
    public async Task DeleteFolderAsync_MovesGroupsToUngroupedAndKeepsData()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("x");
        await svc.SaveGroupAsync("G", new[] { "x" });
        await svc.CreateFolderAsync("Work");
        string groupId = svc.Groups[0].Id;
        string folderId = svc.Folders[0].Id;
        await svc.MoveGroupToFolderAsync(groupId, folderId);

        await svc.DeleteFolderAsync(folderId);

        Assert.Empty(svc.Folders);
        Assert.Single(svc.Groups);
        Assert.Null(svc.Groups[0].FolderId);
        Assert.Equal("G", svc.Groups[0].Name);
    }

    [Fact]
    public async Task SetFolderCollapsedAsync_PersistsCollapseState()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await svc.CreateFolderAsync("Work");
        string folderId = svc.Folders[0].Id;

        await svc.SetFolderCollapsedAsync(folderId, true);

        Assert.True(svc.Folders[0].IsCollapsed);

        var svc2 = CreateService();
        await svc2.LoadAsync();
        Assert.True(svc2.Folders[0].IsCollapsed);
    }

    [Fact]
    public async Task MoveFolderAsync_ReordersFolders()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await svc.CreateFolderAsync("First");
        await svc.CreateFolderAsync("Second");
        string firstId = svc.Folders[0].Id;

        await svc.MoveFolderAsync(firstId, +1);

        Assert.Equal("Second", svc.Folders[0].Name);
        Assert.Equal("First", svc.Folders[1].Name);
    }

    [Fact]
    public async Task MoveFolderAsync_AtBoundary_NoOp()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await svc.CreateFolderAsync("Only");
        string id = svc.Folders[0].Id;

        await svc.MoveFolderAsync(id, -1);

        Assert.Equal("Only", svc.Folders[0].Name);
    }

    [Fact]
    public async Task MoveGroupAsync_ReordersWithinSameFolderOnly()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        await SeedHistoryEntryAsync("a");
        await SeedHistoryEntryAsync("b");
        await SeedHistoryEntryAsync("c");
        await svc.CreateFolderAsync("Work");
        string folderId = svc.Folders[0].Id;

        // Saved newest-first: C, B, A. File B and C into Work; leave A ungrouped.
        await svc.SaveGroupAsync("A", new[] { "a" });
        await svc.SaveGroupAsync("B", new[] { "b" });
        await svc.SaveGroupAsync("C", new[] { "c" });
        string bId = svc.Groups.First(g => g.Name == "B").Id;
        string cId = svc.Groups.First(g => g.Name == "C").Id;
        await svc.MoveGroupToFolderAsync(bId, folderId);
        await svc.MoveGroupToFolderAsync(cId, folderId);

        // In-memory order is now [C, B, A] (all Work/Work/Ungrouped respectively after filing).
        // Moving C (index 0, a Work-folder member) with direction -1 must skip over "A" if it were
        // between them and only swap with another Work-folder member; here B is the only such sibling.
        await svc.MoveGroupAsync(cId, +1);

        Assert.Equal("B", svc.Groups[0].Name);
        Assert.Equal("C", svc.Groups[1].Name);
        Assert.Equal("A", svc.Groups[2].Name);
    }

    [Fact]
    public async Task MoveGroupAsync_UnknownId_DoesNotThrow()
    {
        var svc = CreateService();
        await svc.LoadAsync();

        await svc.MoveGroupAsync("nope", +1);
    }

    [Fact]
    public async Task LoadAsync_OldFormatFileWithoutFolders_LoadsGroupsAsUngrouped()
    {
        string groupsPath = Path.Combine(_tempDir, "groups.json");
        var oldFormat = new
        {
            groups = new[]
            {
                new
                {
                    id = "g1",
                    name = "Legacy",
                    createdUtc = DateTime.UtcNow,
                    entryIds = new[] { "x" },
                    archivedEntries = new[]
                    {
                        new
                        {
                            id = "x",
                            sourceEntryId = "src",
                            name = "Clip",
                            timestampUtc = DateTime.UtcNow,
                            sequenceNumber = 1u,
                            ownerProcess = "test",
                            ownerPid = 1,
                            summary = "s",
                            contentType = ContentType.Text,
                            dataSizeBytes = 4L,
                            contentHash = "abc",
                        },
                    },
                },
            },
        };
        await File.WriteAllTextAsync(groupsPath, JsonSerializer.Serialize(oldFormat, CliptJsonOptions.Shared));

        var svc = CreateService();
        await svc.LoadAsync();

        Assert.Empty(svc.Folders);
        Assert.Single(svc.Groups);
        Assert.Null(svc.Groups[0].FolderId);
        Assert.Equal("Legacy", svc.Groups[0].Name);
    }
}
