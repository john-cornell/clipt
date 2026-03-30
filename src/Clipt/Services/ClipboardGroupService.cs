using System.IO;
using System.IO.Compression;
using System.Reflection;
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

    public async Task<GroupPackageOperationResult> ExportGroupToPackageAsync(
        string groupId,
        string packageFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);
        ArgumentException.ThrowIfNullOrEmpty(packageFilePath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_groups.All(g => g.Id != groupId))
                return GroupPackageOperationResult.Fail("That group was not found.");

            GroupsFileDto? snapshot = await ReadGroupsFileSnapshotAsync(cancellationToken).ConfigureAwait(false);
            GroupDto? dto = snapshot?.Groups?.FirstOrDefault(g => g.Id == groupId);
            if (dto?.ArchivedEntries is null || dto.ArchivedEntries.Count == 0)
                return GroupPackageOperationResult.Fail("This group has no archived data to export.");

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArchivedGroupEntryDto a in dto.ArchivedEntries)
            {
                if (string.IsNullOrWhiteSpace(a.Id))
                    return GroupPackageOperationResult.Fail("Group data is invalid (empty entry id).");
                if (!seenIds.Add(a.Id))
                    return GroupPackageOperationResult.Fail("Group data is invalid (duplicate entry id).");
            }

            string blobRoot = Path.Combine(_groupArchiveRootDirectory, groupId, "blobs");
            foreach (ArchivedGroupEntryDto a in dto.ArchivedEntries)
            {
                string path = Path.Combine(blobRoot, a.Id + ".bin");
                if (!File.Exists(path))
                {
                    return GroupPackageOperationResult.Fail(
                        "A clip file is missing on disk for this group. Export was cancelled; nothing was changed.");
                }
            }

            var manifest = new ClipboardGroupPackageManifest
            {
                FormatVersion = ClipboardGroupPackage.FormatVersion,
                ExportedUtc = DateTime.UtcNow,
                ExporterAppVersion = GetAssemblyVersionString(),
                Group = new ClipboardGroupPackageGroupPayload
                {
                    Name = dto.Name,
                    CreatedUtc = dto.CreatedUtc,
                    ArchivedEntries = dto.ArchivedEntries.Select(ToPackageArchived).ToList(),
                },
            };

            string tempZip = Path.Combine(Path.GetTempPath(), "clipt-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var fs = new FileStream(
                                 tempZip,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    ZipArchiveEntry manifestEntry = zip.CreateEntry(ClipboardGroupPackage.ManifestEntryName, CompressionLevel.Optimal);
                    await using (Stream mw = manifestEntry.Open())
                        await JsonSerializer.SerializeAsync(mw, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);

                    foreach (ArchivedGroupEntryDto a in dto.ArchivedEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string diskPath = Path.Combine(blobRoot, a.Id + ".bin");
                        ZipArchiveEntry blobZip = zip.CreateEntry(ClipboardGroupPackage.BlobsPrefix + a.Id + ".bin", CompressionLevel.Optimal);
                        await using Stream bw = blobZip.Open();
                        await using FileStream br = new(
                            diskPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await br.CopyToAsync(bw, cancellationToken).ConfigureAwait(false);
                    }
                }

                string? parent = Path.GetDirectoryName(Path.GetFullPath(packageFilePath));
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                string partial = packageFilePath + ".clipt-partial";
                try
                {
                    File.Copy(tempZip, partial, overwrite: true);
                    if (File.Exists(packageFilePath))
                        File.Delete(packageFilePath);
                    File.Move(partial, packageFilePath);
                }
                catch (IOException ex)
                {
                    LogWarn($"ExportGroupToPackageAsync: could not write destination — {ex.Message}");
                    TryDeleteQuietly(partial);
                    return GroupPackageOperationResult.Fail(
                        "Could not save the package file. Check the folder path and disk space, then try again.");
                }
                catch (UnauthorizedAccessException ex)
                {
                    LogWarn($"ExportGroupToPackageAsync: access denied — {ex.Message}");
                    TryDeleteQuietly(partial);
                    return GroupPackageOperationResult.Fail(
                        "Access denied when saving the package file. Try another folder.");
                }

                LogDebug($"ExportGroupToPackageAsync: exported group '{groupId}' to {packageFilePath}");
                return GroupPackageOperationResult.Ok();
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip))
                        File.Delete(tempZip);
                }
                catch (IOException)
                {
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GroupPackageOperationResult> ImportGroupFromPackageAsync(
        string packageFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFilePath);

        if (!File.Exists(packageFilePath))
            return GroupPackageOperationResult.Fail("That file does not exist.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClipboardGroupPackageManifest? manifest;
            List<ClipboardGroupPackageArchivedEntry> sourceEntries;

            try
            {
                await using (FileStream zipFs = new(
                                 packageFilePath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 bufferSize: 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (ZipArchive zip = new(zipFs, ZipArchiveMode.Read, leaveOpen: false))
                {
                    ZipArchiveEntry? manifestZ = FindZipEntryOrdinalIgnoreCase(zip, ClipboardGroupPackage.ManifestEntryName);
                    if (manifestZ is null)
                        return GroupPackageOperationResult.Fail("This file is not a valid Clipt group package (missing manifest).");

                    await using (Stream ms = manifestZ.Open())
                    {
                        manifest = await JsonSerializer.DeserializeAsync<ClipboardGroupPackageManifest>(ms, JsonOptions, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (manifest?.Group?.ArchivedEntries is null)
                        return GroupPackageOperationResult.Fail("The package manifest is invalid.");

                    if (manifest.FormatVersion != ClipboardGroupPackage.FormatVersion)
                    {
                        return GroupPackageOperationResult.Fail(
                            $"This package format (version {manifest.FormatVersion}) is not supported by this app version.");
                    }

                    sourceEntries = manifest.Group.ArchivedEntries;
                    if (sourceEntries.Count == 0)
                        return GroupPackageOperationResult.Fail("The package contains no clips.");

                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ClipboardGroupPackageArchivedEntry a in sourceEntries)
                    {
                        if (string.IsNullOrWhiteSpace(a.Id))
                            return GroupPackageOperationResult.Fail("The package lists a clip with no id.");
                        if (!seen.Add(a.Id))
                            return GroupPackageOperationResult.Fail("The package lists duplicate clip ids.");
                        if (FindZipEntryOrdinalIgnoreCase(zip, ClipboardGroupPackage.BlobsPrefix + a.Id + ".bin") is null)
                        {
                            return GroupPackageOperationResult.Fail(
                                $"The package is missing blob data for a clip (id {a.Id}).");
                        }
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                LogWarn($"ImportGroupFromPackageAsync: invalid zip — {ex.Message}");
                return GroupPackageOperationResult.Fail("This file is not a valid Clipt group package.");
            }
            catch (IOException ex)
            {
                LogWarn($"ImportGroupFromPackageAsync: read error — {ex.Message}");
                return GroupPackageOperationResult.Fail("Could not read the group package file.");
            }
            catch (JsonException ex)
            {
                LogWarn($"ImportGroupFromPackageAsync: bad manifest JSON — {ex.Message}");
                return GroupPackageOperationResult.Fail("The package manifest is damaged or not valid JSON.");
            }

            // Re-open zip for blob extraction (manifest validated; avoids holding one archive across long I/O with unclear state).
            await using (FileStream zipFs2 = new(
                             packageFilePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (ZipArchive zip2 = new(zipFs2, ZipArchiveMode.Read, leaveOpen: false))
            {
                string newGroupId = Guid.NewGuid().ToString("N");
                string blobRoot = Path.Combine(_groupArchiveRootDirectory, newGroupId, "blobs");

                try
                {
                    Directory.CreateDirectory(blobRoot);
                    var newArchived = new List<ArchivedGroupEntryDto>(sourceEntries.Count);

                    foreach (ClipboardGroupPackageArchivedEntry src in sourceEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ZipArchiveEntry? blobZ = FindZipEntryOrdinalIgnoreCase(zip2, ClipboardGroupPackage.BlobsPrefix + src.Id + ".bin");
                        if (blobZ is null)
                            throw new InvalidOperationException($"Missing blob for entry id {src.Id}.");

                        byte[] bytes;
                        await using (Stream rs = blobZ.Open())
                        {
                            using var acc = new MemoryStream();
                            await rs.CopyToAsync(acc, cancellationToken).ConfigureAwait(false);
                            bytes = acc.ToArray();
                        }

                        if (src.DataSizeBytes > 0 && bytes.LongLength != src.DataSizeBytes)
                        {
                            throw new InvalidOperationException(
                                $"Clip \"{src.Name}\" size does not match the package metadata (expected {src.DataSizeBytes} bytes, found {bytes.LongLength}).");
                        }

                        string newEntryId = Guid.NewGuid().ToString("N");
                        await File.WriteAllBytesAsync(Path.Combine(blobRoot, newEntryId + ".bin"), bytes, cancellationToken)
                            .ConfigureAwait(false);

                        newArchived.Add(new ArchivedGroupEntryDto
                        {
                            Id = newEntryId,
                            SourceEntryId = string.Empty,
                            Name = string.IsNullOrWhiteSpace(src.Name) ? "Clip" : src.Name.Trim(),
                            TimestampUtc = src.TimestampUtc == default ? DateTime.UtcNow : src.TimestampUtc,
                            SequenceNumber = src.SequenceNumber,
                            OwnerProcess = string.IsNullOrWhiteSpace(src.OwnerProcess) ? "(unknown)" : src.OwnerProcess,
                            OwnerPid = src.OwnerPid,
                            Summary = src.Summary ?? string.Empty,
                            ContentType = src.ContentType,
                            DataSizeBytes = bytes.LongLength,
                            ContentHash = src.ContentHash ?? string.Empty,
                        });
                    }

                    string name = string.IsNullOrWhiteSpace(manifest!.Group!.Name) ? "Untitled" : manifest.Group.Name.Trim();
                    DateTime created = manifest.Group.CreatedUtc == default ? DateTime.UtcNow : manifest.Group.CreatedUtc;
                    var group = new ClipboardGroup
                    {
                        Id = newGroupId,
                        Name = name,
                        CreatedUtc = created,
                        EntryIds = newArchived.Select(x => x.Id).ToList(),
                    };

                    _groups.Insert(0, group);
                    try
                    {
                        await WriteGroupsFileAsync(new Dictionary<string, List<ArchivedGroupEntryDto>>(StringComparer.Ordinal)
                        {
                            [newGroupId] = newArchived,
                        }).ConfigureAwait(false);
                    }
                    catch
                    {
                        _groups.RemoveAll(g => g.Id == newGroupId);
                        throw;
                    }

                    LogDebug($"ImportGroupFromPackageAsync: imported group '{name}' as id={newGroupId} ({newArchived.Count} clip(s))");
                    GroupsChanged?.Invoke(this, EventArgs.Empty);
                    return GroupPackageOperationResult.Ok();
                }
                catch (Exception ex) when (ex is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException
                    or InvalidOperationException)
                {
                    _groups.RemoveAll(g => g.Id == newGroupId);
                    DeleteGroupArchiveQuietly(newGroupId);
                    LogWarn($"ImportGroupFromPackageAsync: failed — {ex.Message}");
                    return GroupPackageOperationResult.Fail($"Import failed: {ex.Message}");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GroupsFileDto?> ReadGroupsFileSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_groupsPath))
            return null;

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(_groupsPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GroupsFileDto>(bytes, JsonOptions);
        }
        catch (IOException ex)
        {
            LogWarn($"ReadGroupsFileSnapshotAsync: failed to read — {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            LogWarn($"ReadGroupsFileSnapshotAsync: failed to deserialize — {ex.Message}");
            return null;
        }
    }

    private static ZipArchiveEntry? FindZipEntryOrdinalIgnoreCase(ZipArchive zip, string fullName)
    {
        string normalized = fullName.Replace('\\', '/').TrimEnd('/');
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            string n = e.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase))
                return e;
        }

        return null;
    }

    private static ClipboardGroupPackageArchivedEntry ToPackageArchived(ArchivedGroupEntryDto a) => new()
    {
        Id = a.Id,
        SourceEntryId = a.SourceEntryId,
        Name = a.Name,
        TimestampUtc = a.TimestampUtc,
        SequenceNumber = a.SequenceNumber,
        OwnerProcess = a.OwnerProcess,
        OwnerPid = a.OwnerPid,
        Summary = a.Summary,
        ContentType = a.ContentType,
        DataSizeBytes = a.DataSizeBytes,
        ContentHash = a.ContentHash,
    };

    private static string GetAssemblyVersionString()
    {
        Version? v = typeof(ClipboardGroupService).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
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

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
