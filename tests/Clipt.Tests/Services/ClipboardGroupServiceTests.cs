using System.IO;
using System.Text.Json;
using Clipt.Services;
using Xunit;

namespace Clipt.Tests.Services;

public class ClipboardGroupServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ClipboardGroupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliptGroupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    private ClipboardGroupService CreateService() => new(_tempDir);

    [Fact]
    public async Task SaveGroupAsync_PersistsToGroupsJson()
    {
        var svc = CreateService();
        await svc.LoadAsync();

        await svc.SaveGroupAsync("Alpha", new[] { "id1", "id2" });

        Assert.Single(svc.Groups);
        Assert.Equal("Alpha", svc.Groups[0].Name);
        Assert.Equal(new[] { "id1", "id2" }, svc.Groups[0].EntryIds);

        string path = Path.Combine(_tempDir, "groups.json");
        Assert.True(File.Exists(path));
        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("groups", out JsonElement arr));
        Assert.Equal(1, arr.GetArrayLength());
    }

    [Fact]
    public async Task LoadAsync_RoundTripsSavedGroups()
    {
        var svc = CreateService();
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
        await svc.SaveGroupAsync("Old", new[] { "x" });
        string id = svc.Groups[0].Id;

        await svc.RenameGroupAsync(id, "New");

        Assert.Equal("New", svc.Groups[0].Name);
    }

    [Fact]
    public async Task DeleteGroupAsync_RemovesGroup()
    {
        var svc = CreateService();
        await svc.SaveGroupAsync("G", new[] { "x" });
        string id = svc.Groups[0].Id;

        await svc.DeleteGroupAsync(id);

        Assert.Empty(svc.Groups);
    }

    [Fact]
    public async Task SaveGroupAsync_DeduplicatesIds_PreservesOrder()
    {
        var svc = CreateService();
        await svc.SaveGroupAsync("Dup", new[] { "a", "a", "b" });

        Assert.Equal(new[] { "a", "b" }, svc.Groups[0].EntryIds);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "groups.json");
        await File.WriteAllTextAsync(path, "NOT VALID JSON {{{");

        var svc = CreateService();
        await svc.LoadAsync();

        Assert.Empty(svc.Groups);
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
}
