using Clipt.Plugins;

namespace Clipt.Plugins.OwnerBlocker;

internal sealed class OwnerBlockerSettingsStore : IOwnerBlockerSettingsStore
{
    private readonly ICliptHost _host;
    private OwnerBlockerSettings _settings;

    public OwnerBlockerSettingsStore(ICliptHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _settings = _host.LoadSettings<OwnerBlockerSettings>() ?? new OwnerBlockerSettings();
        NormalizeSettings();
    }

    public IReadOnlyList<BlockedOwnerEntry> BlockedProcesses { get; private set; } = [];

    public IReadOnlyList<BlockedOwnerEntry> BlockedClassPrefixes { get; private set; } = [];

    public bool ShowHistoryBlockButton
    {
        get => _settings.ShowHistoryBlockButton;
        set
        {
            if (_settings.ShowHistoryBlockButton == value)
                return;

            _settings.ShowHistoryBlockButton = value;
            Persist();
        }
    }

    public void BlockSnapshotSource(string? processName, string? windowClass)
    {
        if (BlockedProcessNames.IsBlockable(processName))
            AddOrReEnable(_settings.BlockedProcesses, processName!.Trim());

        string? classPrefix = BlockedWindowClasses.NormalizeForBlock(windowClass);
        if (classPrefix is not null)
            AddOrReEnable(_settings.BlockedClassPrefixes, classPrefix);

        Persist();
    }

    /// <summary>Re-blocking a name that's on the list but temporarily disabled re-enables it, rather than duplicating it.</summary>
    private static void AddOrReEnable(List<BlockedOwnerEntry> entries, string name)
    {
        BlockedOwnerEntry? existing = entries.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            existing.IsEnabled = true;
        else
            entries.Add(new BlockedOwnerEntry { Name = name, IsEnabled = true });
    }

    public void UnblockProcess(string processName)
    {
        _settings.BlockedProcesses.RemoveAll(
            e => string.Equals(e.Name, processName, StringComparison.OrdinalIgnoreCase));
        Persist();
    }

    public void UnblockWindowClass(string classPrefix)
    {
        _settings.BlockedClassPrefixes.RemoveAll(
            e => string.Equals(e.Name, classPrefix, StringComparison.OrdinalIgnoreCase));
        Persist();
    }

    public void SetProcessEnabled(string processName, bool enabled) =>
        SetEnabled(_settings.BlockedProcesses, processName, enabled);

    public void SetWindowClassEnabled(string classPrefix, bool enabled) =>
        SetEnabled(_settings.BlockedClassPrefixes, classPrefix, enabled);

    private void SetEnabled(List<BlockedOwnerEntry> entries, string name, bool enabled)
    {
        BlockedOwnerEntry? entry = entries.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null || entry.IsEnabled == enabled)
            return;

        entry.IsEnabled = enabled;
        Persist();
    }

    public void ClearAll()
    {
        _settings.BlockedProcesses.Clear();
        _settings.BlockedClassPrefixes.Clear();
        Persist();
    }

    private void Persist()
    {
        NormalizeSettings();
        _host.SaveSettings(_settings);
    }

    private void NormalizeSettings()
    {
        _settings.BlockedProcesses = NormalizeEntries(_settings.BlockedProcesses);
        _settings.BlockedClassPrefixes = NormalizeEntries(_settings.BlockedClassPrefixes);

        BlockedProcesses = _settings.BlockedProcesses.AsReadOnly();
        BlockedClassPrefixes = _settings.BlockedClassPrefixes.AsReadOnly();
    }

    private static List<BlockedOwnerEntry> NormalizeEntries(List<BlockedOwnerEntry> entries) =>
        entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new BlockedOwnerEntry { Name = g.Key, IsEnabled = g.Any(e => e.IsEnabled) })
            .ToList();
}
