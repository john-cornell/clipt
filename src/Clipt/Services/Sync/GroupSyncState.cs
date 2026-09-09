using System.IO;
using System.Text.Json;

namespace Clipt.Services.Sync;

public sealed class GroupSyncState
{
    public long GlobalLastPulledVersion { get; set; }
    public Dictionary<string, GroupSyncStateEntry> Groups { get; init; } = new(StringComparer.Ordinal);
}

public sealed class GroupSyncStateEntry
{
    public required int SyncedVersion { get; set; }
    public required string ContentHash { get; set; }
}

public interface IGroupSyncStateStore
{
    Task<GroupSyncState> LoadAsync();
    Task SaveAsync(GroupSyncState state);
}

public sealed class GroupSyncStateStore : IGroupSyncStateStore
{
    private readonly string _path;

    public GroupSyncStateStore()
        : this(GetDefaultPath())
    {
    }

    internal GroupSyncStateStore(string path)
    {
        _path = path;
    }

    public async Task<GroupSyncState> LoadAsync()
    {
        if (!File.Exists(_path))
            return new GroupSyncState();

        try
        {
            byte[] json = await File.ReadAllBytesAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GroupSyncState>(json) ?? new GroupSyncState();
        }
        catch (IOException)
        {
            return new GroupSyncState();
        }
        catch (JsonException)
        {
            return new GroupSyncState();
        }
    }

    public async Task SaveAsync(GroupSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(state);
        string tmpPath = _path + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, json).ConfigureAwait(false);
        File.Move(tmpPath, _path, overwrite: true);
    }

    private static string GetDefaultPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Clipt", "History", "sync-state.json");
    }
}
