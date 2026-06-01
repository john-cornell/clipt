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

    public IReadOnlySet<string> BlockedProcesses { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> BlockedClassPrefixes { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            _settings.BlockedProcesses.Add(processName!.Trim());

        string? classPrefix = BlockedWindowClasses.NormalizeForBlock(windowClass);
        if (classPrefix is not null)
            _settings.BlockedClassPrefixes.Add(classPrefix);

        Persist();
    }

    public void UnblockProcess(string processName)
    {
        _settings.BlockedProcesses.RemoveAll(
            name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
        Persist();
    }

    public void UnblockWindowClass(string classPrefix)
    {
        _settings.BlockedClassPrefixes.RemoveAll(
            prefix => string.Equals(prefix, classPrefix, StringComparison.OrdinalIgnoreCase));
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
        _settings.BlockedProcesses = _settings.BlockedProcesses
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.BlockedClassPrefixes = _settings.BlockedClassPrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        BlockedProcesses = new HashSet<string>(_settings.BlockedProcesses, StringComparer.OrdinalIgnoreCase);
        BlockedClassPrefixes = new HashSet<string>(_settings.BlockedClassPrefixes, StringComparer.OrdinalIgnoreCase);
    }
}
