using System.IO;
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public GroupSyncStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliptSyncStateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "sync-state.json");
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

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsEmptyState()
    {
        var store = new GroupSyncStateStore(_path);

        GroupSyncState state = await store.LoadAsync();

        Assert.Equal(0, state.GlobalLastPulledVersion);
        Assert.Empty(state.Groups);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var store = new GroupSyncStateStore(_path);
        var state = new GroupSyncState { GlobalLastPulledVersion = 42 };
        state.Groups["g1"] = new GroupSyncStateEntry { SyncedVersion = 3, ContentHash = "abc123" };

        await store.SaveAsync(state);
        GroupSyncState loaded = await store.LoadAsync();

        Assert.Equal(42, loaded.GlobalLastPulledVersion);
        Assert.Equal(3, loaded.Groups["g1"].SyncedVersion);
        Assert.Equal("abc123", loaded.Groups["g1"].ContentHash);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyStateRatherThanThrowing()
    {
        await File.WriteAllTextAsync(_path, "{ not valid json");
        var store = new GroupSyncStateStore(_path);

        GroupSyncState state = await store.LoadAsync();

        Assert.Equal(0, state.GlobalLastPulledVersion);
    }
}
