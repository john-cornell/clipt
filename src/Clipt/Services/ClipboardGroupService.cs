using System.IO;
using System.Text.Json;
using Clipt.Models;

namespace Clipt.Services;

public sealed class ClipboardGroupService : IClipboardGroupService
{
    private readonly string _groupsPath;
    private readonly string _historyIndexPath;
    private readonly string _historyBlobsDirectory;
    private readonly string _groupArchiveRootDirectory;
    private readonly IAppLogger? _logger;
    private readonly List<ClipboardGroup> _groups = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static JsonSerializerOptions JsonOptions => CliptJsonOptions.Shared;

    public ClipboardGroupService(IAppLogger logger)
        : this(GetDefaultHistoryDirectory(), logger)
    {
    }

    internal ClipboardGroupService(string historyDirectory, IAppLogger? logger = null)
    {
        _groupsPath = Path.Combine(historyDirectory, "groups.json");
        _historyIndexPath = Path.Combine(historyDirectory, "index.json");
        _historyBlobsDirectory = Path.Combine(historyDirectory, "blobs");
        _groupArchiveRootDirectory = Path.Combine(historyDirectory, "groups");
        _logger = logger;
    }

    public IReadOnlyList<ClipboardGroup> Groups => _groups.AsReadOnly();

    public event EventHandler? GroupsChanged;

    public async Task LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _groups.Clear();

            if (!File.Exists(_groupsPath))
            {
                LogDebug("LoadAsync: groups.json not found, nothing to load");
                return;
            }

            byte[] json;
            try
            {
                json = await File.ReadAllBytesAsync(_groupsPath).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                LogWarn($"LoadAsync: failed to read groups.json — {ex.Message}");
                return;
            }

            try
            {
                var file = JsonSerializer.Deserialize<GroupsFileDto>(json, JsonOptions);
                if (file?.Groups is null)
                {
                    LogWarn("LoadAsync: deserialized groups.json but Groups property was null");
                    return;
                }

                foreach (var dto in file.Groups)
                {
                    if (string.IsNullOrWhiteSpace(dto.Id))
                        continue;

                    string name = string.IsNullOrWhiteSpace(dto.Name) ? "Untitled" : dto.Name.Trim();
                    List<string> ids = dto.ArchivedEntries is { Count: > 0 }
                        ? dto.ArchivedEntries
                            .Where(static a => !string.IsNullOrWhiteSpace(a.Id))
                            .Select(static a => a.Id)
                            .ToList()
                        : dto.EntryIds?.Where(static id => !string.IsNullOrWhiteSpace(id)).ToList() ?? [];
                    if (ids.Count == 0)
                        continue;

                    _groups.Add(new ClipboardGroup
                    {
                        Id = dto.Id,
                        Name = name,
                        CreatedUtc = dto.CreatedUtc == default ? DateTime.UtcNow : dto.CreatedUtc,
                        EntryIds = ids,
                    });
                }

                LogDebug($"LoadAsync: loaded {_groups.Count} group(s) from groups.json");
            }
            catch (JsonException ex)
            {
                LogWarn($"LoadAsync: failed to deserialize groups.json — {ex.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveGroupAsync(string name, IReadOnlyList<string> entryIds)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        string trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
            trimmedName = "Untitled";

        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in entryIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;
            ordered.Add(id);
        }

        if (ordered.Count == 0)
        {
            LogWarn("SaveGroupAsync: no valid entry IDs provided, nothing to save");
            return;
        }

        LogDebug($"SaveGroupAsync: building archive for '{trimmedName}' with {ordered.Count} source entry ID(s)");

        var archivedEntries = await BuildArchivedEntriesAsync(ordered).ConfigureAwait(false);
        if (archivedEntries.Count == 0)
        {
            LogWarn($"SaveGroupAsync: could not resolve any entries from index.json for group '{trimmedName}' " +
                    $"(requested IDs: {string.Join(", ", ordered)})");
            return;
        }

        string groupId = Guid.NewGuid().ToString("N");
        var group = new ClipboardGroup
        {
            Id = groupId,
            Name = trimmedName,
            CreatedUtc = DateTime.UtcNow,
            EntryIds = archivedEntries.Select(static x => x.Id).ToList(),
        };

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteArchivedBlobsAsync(groupId, archivedEntries).ConfigureAwait(false);
            _groups.Insert(0, group);
            await WriteGroupsFileAsync(archivedByGroupId: new Dictionary<string, List<ArchivedGroupEntryDto>>
            {
                [groupId] = archivedEntries,
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        LogDebug($"SaveGroupAsync: saved group '{trimmedName}' (id={groupId}) with {archivedEntries.Count} archived entry(ies)");
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RenameGroupAsync(string groupId, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);
        ArgumentNullException.ThrowIfNull(newName);

        string trimmed = newName.Trim();
        if (trimmed.Length == 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ClipboardGroup? group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group is null)
            {
                LogWarn($"RenameGroupAsync: group '{groupId}' not found");
                return;
            }

            group.Name = trimmed;
            await WriteGroupsFileAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteGroupAsync(string groupId)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            int removed = _groups.RemoveAll(g => g.Id == groupId);
            if (removed == 0)
            {
                LogWarn($"DeleteGroupAsync: group '{groupId}' not found, nothing to delete");
                return;
            }

            DeleteGroupArchiveQuietly(groupId);
            await WriteGroupsFileAsync().ConfigureAwait(false);
            LogDebug($"DeleteGroupAsync: deleted group '{groupId}' and its archive");
        }
        finally
        {
            _gate.Release();
        }

        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WriteGroupsFileAsync(Dictionary<string, List<ArchivedGroupEntryDto>>? archivedByGroupId = null)
    {
        string? directory = Path.GetDirectoryName(_groupsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        GroupsFileDto? existing = null;
        if (File.Exists(_groupsPath))
        {
            try
            {
                byte[] existingJson = await File.ReadAllBytesAsync(_groupsPath).ConfigureAwait(false);
                existing = JsonSerializer.Deserialize<GroupsFileDto>(existingJson, JsonOptions);
            }
            catch (IOException ex)
            {
                LogWarn($"WriteGroupsFileAsync: failed to read existing groups.json — {ex.Message}");
            }
            catch (JsonException ex)
            {
                LogWarn($"WriteGroupsFileAsync: failed to deserialize existing groups.json — {ex.Message}");
            }
        }

        Dictionary<string, List<ArchivedGroupEntryDto>> archivedLookup = new(StringComparer.Ordinal);
        if (existing?.Groups is not null)
        {
            foreach (GroupDto dto in existing.Groups)
            {
                if (string.IsNullOrWhiteSpace(dto.Id))
                    continue;
                if (dto.ArchivedEntries is null || dto.ArchivedEntries.Count == 0)
                    continue;

                archivedLookup[dto.Id] = dto.ArchivedEntries;
            }
        }

        if (archivedByGroupId is not null)
        {
            foreach ((string groupId, List<ArchivedGroupEntryDto> archivedEntries) in archivedByGroupId)
                archivedLookup[groupId] = archivedEntries;
        }

        var file = new GroupsFileDto
        {
            Groups = _groups.Select(g => new GroupDto
            {
                Id = g.Id,
                Name = g.Name,
                CreatedUtc = g.CreatedUtc,
                EntryIds = g.EntryIds.ToList(),
                ArchivedEntries = archivedLookup.TryGetValue(g.Id, out List<ArchivedGroupEntryDto>? archived)
                    ? archived
                    : null,
            }).ToList(),
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(file, JsonOptions);
        string tmpPath = _groupsPath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, json).ConfigureAwait(false);
        File.Move(tmpPath, _groupsPath, overwrite: true);
    }

    private static string GetDefaultHistoryDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Clipt", "History");
    }

    private async Task<List<ArchivedGroupEntryDto>> BuildArchivedEntriesAsync(IReadOnlyList<string> sourceEntryIds)
    {
        List<HistoryIndexEntryDto>? historyEntries = await ReadHistoryEntriesByIdAsync().ConfigureAwait(false);
        if (historyEntries is null || historyEntries.Count == 0)
        {
            LogWarn("BuildArchivedEntriesAsync: history index is empty or could not be read");
            return [];
        }

        var indexLookup = historyEntries
            .Where(static x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(static x => x.Id, StringComparer.Ordinal);

        LogDebug($"BuildArchivedEntriesAsync: index has {indexLookup.Count} entry(ies), resolving {sourceEntryIds.Count} source ID(s)");

        var result = new List<ArchivedGroupEntryDto>();
        foreach (string sourceId in sourceEntryIds)
        {
            if (!indexLookup.TryGetValue(sourceId, out HistoryIndexEntryDto? source))
            {
                LogWarn($"BuildArchivedEntriesAsync: source entry '{sourceId}' not found in index.json");
                continue;
            }

            string blobPath = Path.Combine(_historyBlobsDirectory, sourceId + ".bin");
            if (!File.Exists(blobPath))
            {
                LogWarn($"BuildArchivedEntriesAsync: blob file missing for entry '{sourceId}' at {blobPath}");
                continue;
            }

            result.Add(new ArchivedGroupEntryDto
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceEntryId = sourceId,
                Name = source.Name ?? "Clip",
                TimestampUtc = source.TimestampUtc,
                SequenceNumber = source.SequenceNumber,
                OwnerProcess = source.OwnerProcess ?? "(unknown)",
                OwnerPid = source.OwnerPid,
                Summary = source.Summary ?? string.Empty,
                ContentType = source.ContentType,
                DataSizeBytes = source.DataSizeBytes,
                ContentHash = source.ContentHash ?? string.Empty,
            });
        }

        LogDebug($"BuildArchivedEntriesAsync: resolved {result.Count}/{sourceEntryIds.Count} entry(ies)");
        return result;
    }

    private async Task<List<HistoryIndexEntryDto>?> ReadHistoryEntriesByIdAsync()
    {
        if (!File.Exists(_historyIndexPath))
        {
            LogWarn($"ReadHistoryEntriesByIdAsync: index.json not found at {_historyIndexPath}");
            return null;
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(_historyIndexPath).ConfigureAwait(false);
            HistoryIndexFileDto? file = JsonSerializer.Deserialize<HistoryIndexFileDto>(bytes, JsonOptions);
            int count = file?.Entries?.Count ?? 0;
            LogDebug($"ReadHistoryEntriesByIdAsync: deserialized index.json with {count} entry(ies)");
            return file?.Entries;
        }
        catch (IOException ex)
        {
            LogWarn($"ReadHistoryEntriesByIdAsync: failed to read index.json — {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            LogWarn($"ReadHistoryEntriesByIdAsync: failed to deserialize index.json — {ex.Message}");
            return null;
        }
    }

    private async Task WriteArchivedBlobsAsync(string groupId, IReadOnlyList<ArchivedGroupEntryDto> archivedEntries)
    {
        string blobRoot = Path.Combine(_groupArchiveRootDirectory, groupId, "blobs");
        Directory.CreateDirectory(blobRoot);

        int copied = 0;
        foreach (ArchivedGroupEntryDto archived in archivedEntries)
        {
            string sourceBlobPath = Path.Combine(_historyBlobsDirectory, archived.SourceEntryId + ".bin");
            if (!File.Exists(sourceBlobPath))
            {
                LogWarn($"WriteArchivedBlobsAsync: source blob missing for '{archived.SourceEntryId}', skipping");
                continue;
            }

            string targetBlobPath = Path.Combine(blobRoot, archived.Id + ".bin");
            byte[] blobBytes = await File.ReadAllBytesAsync(sourceBlobPath).ConfigureAwait(false);
            await File.WriteAllBytesAsync(targetBlobPath, blobBytes).ConfigureAwait(false);
            copied++;
        }

        LogDebug($"WriteArchivedBlobsAsync: copied {copied}/{archivedEntries.Count} blob(s) to group archive '{groupId}'");
    }

    private void DeleteGroupArchiveQuietly(string groupId)
    {
        try
        {
            string groupPath = Path.Combine(_groupArchiveRootDirectory, groupId);
            if (Directory.Exists(groupPath))
                Directory.Delete(groupPath, recursive: true);
        }
        catch (IOException ex)
        {
            LogWarn($"DeleteGroupArchiveQuietly: failed to delete archive for '{groupId}' — {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            LogWarn($"DeleteGroupArchiveQuietly: access denied deleting archive for '{groupId}' — {ex.Message}");
        }
    }

    private void LogDebug(string message)
    {
        if (_logger is null || _logger.Level < AppLogLevel.Debug)
            return;
        _logger.Debug($"[GroupService] {message}");
    }

    private void LogWarn(string message)
    {
        if (_logger is null || _logger.Level < AppLogLevel.Warn)
            return;
        _logger.Warn($"[GroupService] {message}");
    }

    private sealed class GroupsFileDto
    {
        public List<GroupDto> Groups { get; set; } = [];
    }

    private sealed class GroupDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public List<string> EntryIds { get; set; } = [];
        public List<ArchivedGroupEntryDto>? ArchivedEntries { get; set; }
    }

    private sealed class ArchivedGroupEntryDto
    {
        public string Id { get; set; } = string.Empty;
        public string SourceEntryId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public uint SequenceNumber { get; set; }
        public string OwnerProcess { get; set; } = string.Empty;
        public int OwnerPid { get; set; }
        public string Summary { get; set; } = string.Empty;
        public ContentType ContentType { get; set; }
        public long DataSizeBytes { get; set; }
        public string ContentHash { get; set; } = string.Empty;
    }

    private sealed class HistoryIndexFileDto
    {
        public List<HistoryIndexEntryDto> Entries { get; set; } = [];
    }

    private sealed class HistoryIndexEntryDto
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
}
