using System.IO;

namespace Clipt.Services.Sync;

public interface IGroupEntryBlobReader
{
    Task<byte[]> ReadEntryBlobAsync(string groupId, string entryId);
}

public sealed class GroupEntryBlobReader : IGroupEntryBlobReader
{
    private readonly string _groupArchiveRootDirectory;

    public GroupEntryBlobReader()
        : this(GetDefaultGroupArchiveRootDirectory())
    {
    }

    internal GroupEntryBlobReader(string groupArchiveRootDirectory)
    {
        _groupArchiveRootDirectory = groupArchiveRootDirectory;
    }

    public Task<byte[]> ReadEntryBlobAsync(string groupId, string entryId)
    {
        string path = Path.Combine(_groupArchiveRootDirectory, groupId, "blobs", entryId + ".bin");
        return File.ReadAllBytesAsync(path);
    }

    private static string GetDefaultGroupArchiveRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Clipt", "History", "groups");
    }
}
