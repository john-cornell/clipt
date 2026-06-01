using System.IO;
using System.Text.Json;
using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public sealed class CliptPluginHost : ICliptPluginHost
{
    private static JsonSerializerOptions JsonOptions => CliptJsonOptions.Shared;

    private readonly IPluginRegistry _registry;
    private readonly IClipboardHistoryService _historyService;

    public CliptPluginHost(IPluginRegistry registry, IClipboardHistoryService historyService)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
    }

    public event EventHandler<CliptPluginClipboardEventArgs>? ClipboardProcessed;

    public bool HasOwnerBlockCoordinator => _registry.OwnerBlockCoordinator is not null;

    public CliptPluginFilterResult EvaluateFilters(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CliptPluginClipboardSnapshot pluginSnapshot = CliptPluginHostAdapters.ToPluginSnapshot(snapshot);

        foreach (ICliptClipboardFilterPlugin filter in _registry.FilterPlugins)
        {
            CliptPluginFilterVerdict verdict = filter.Evaluate(pluginSnapshot);
            if (!verdict.Allow)
                return new CliptPluginFilterResult(false, filter.Id, verdict.Reason);
        }

        return CliptPluginFilterResult.Allowed;
    }

    public void PublishClipboardEvent(ClipboardSnapshot snapshot, HistoryAddResult addResult)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var args = new CliptPluginClipboardEventArgs
        {
            Snapshot = CliptPluginHostAdapters.ToPluginSnapshot(snapshot),
            AddOutcome = CliptPluginHostAdapters.ToPluginOutcome(addResult),
        };

        ClipboardProcessed?.Invoke(this, args);
    }

    public async Task BlockOwnerAsync(string? processName, string? windowClass)
    {
        ICliptOwnerBlockCoordinator? coordinator = _registry.OwnerBlockCoordinator;
        if (coordinator is null)
            return;

        await coordinator.BlockAsync(processName, windowClass).ConfigureAwait(false);
    }

    public bool IsOwnerBlocked(string? processName, string? windowClass)
    {
        ICliptOwnerBlockCoordinator? coordinator = _registry.OwnerBlockCoordinator;
        if (coordinator is null)
            return false;

        return coordinator.IsBlocked(new CliptPluginClipboardSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 0,
            OwnerProcessName = processName ?? string.Empty,
            OwnerProcessId = 0,
            OwnerWindowTitle = string.Empty,
            OwnerWindowClass = windowClass ?? string.Empty,
            Formats = Array.Empty<CliptPluginFormatInfo>(),
        });
    }

    public ICliptHost CreateHostScope(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("Plugin id is required.", nameof(pluginId));

        return new CliptPluginHostScope(this, pluginId);
    }

    internal string GetPluginSettingsDirectory(string pluginId)
    {
        string safeId = SanitizePluginId(pluginId);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipt",
            "Plugins",
            safeId);
    }

    internal T? LoadSettings<T>(string pluginId) where T : class, new()
    {
        string path = Path.Combine(GetPluginSettingsDirectory(pluginId), "settings.json");
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal void SaveSettings<T>(string pluginId, T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);

        string dir = GetPluginSettingsDirectory(pluginId);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "settings.json");
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static string SanitizePluginId(string pluginId)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            pluginId = pluginId.Replace(invalid, '_');

        return pluginId;
    }

    private sealed class CliptPluginHostScope : ICliptHost
    {
        private readonly CliptPluginHost _parent;

        public CliptPluginHostScope(CliptPluginHost parent, string pluginId)
        {
            _parent = parent;
            PluginId = pluginId;
        }

        public string PluginId { get; }

        public event EventHandler<CliptPluginClipboardEventArgs>? ClipboardProcessed
        {
            add => _parent.ClipboardProcessed += value;
            remove => _parent.ClipboardProcessed -= value;
        }

        public T? LoadSettings<T>() where T : class, new() =>
            _parent.LoadSettings<T>(PluginId);

        public void SaveSettings<T>(T settings) where T : class =>
            _parent.SaveSettings(PluginId, settings);

        public Task RemoveHistoryByOwnerProcessAsync(string processName) =>
            _parent._historyService.RemoveByOwnerProcessAsync(processName);

        public Task BlockOwnerAsync(string? processName, string? windowClass) =>
            _parent.BlockOwnerAsync(processName, windowClass);

        public IReadOnlySet<string> GetBlockedProcessNames() =>
            _parent._registry.OwnerBlockCoordinator?.GetBlockedProcessNames()
            ?? EmptyBlockedSets.ProcessNames;

        public IReadOnlySet<string> GetBlockedWindowClassPrefixes() =>
            _parent._registry.OwnerBlockCoordinator?.GetBlockedWindowClassPrefixes()
            ?? EmptyBlockedSets.ClassPrefixes;
    }

    private static class EmptyBlockedSets
    {
        public static IReadOnlySet<string> ProcessNames { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlySet<string> ClassPrefixes { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
