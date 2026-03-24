using System.IO;
using System.Text.Json;
using Clipt.Models;

namespace Clipt.Services;

public sealed class ClipboardGroupService : IClipboardGroupService
{
    private readonly string _groupsPath;
    private readonly List<ClipboardGroup> _groups = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public ClipboardGroupService()
        : this(GetDefaultHistoryDirectory())
    {
    }

    internal ClipboardGroupService(string historyDirectory)
    {
        _groupsPath = Path.Combine(historyDirectory, "groups.json");
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
                return;

            byte[] json;
            try
            {
                json = await File.ReadAllBytesAsync(_groupsPath).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }

            try
            {
                var file = JsonSerializer.Deserialize<GroupsFileDto>(json, JsonOptions);
                if (file?.Groups is null)
                    return;

                foreach (var dto in file.Groups)
                {
                    if (string.IsNullOrWhiteSpace(dto.Id))
                        continue;

                    string name = string.IsNullOrWhiteSpace(dto.Name) ? "Untitled" : dto.Name.Trim();
                    var ids = dto.EntryIds?.Where(static id => !string.IsNullOrWhiteSpace(id)).ToList() ?? [];
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
            }
            catch (JsonException)
            {
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
            return;

        var group = new ClipboardGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            CreatedUtc = DateTime.UtcNow,
            EntryIds = ordered,
        };

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _groups.Insert(0, group);
            await WriteGroupsFileAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

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
                return;

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
                return;

            await WriteGroupsFileAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WriteGroupsFileAsync()
    {
        string? directory = Path.GetDirectoryName(_groupsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var file = new GroupsFileDto
        {
            Groups = _groups.Select(g => new GroupDto
            {
                Id = g.Id,
                Name = g.Name,
                CreatedUtc = g.CreatedUtc,
                EntryIds = g.EntryIds.ToList(),
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
    }
}
