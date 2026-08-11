using Clipt.Plugins;
using Clipt.Plugins.OwnerBlocker.ViewModels;
using Clipt.Plugins.OwnerBlocker.Views;

namespace Clipt.Plugins.OwnerBlocker;

public sealed class OwnerBlockerPlugin :
    ICliptClipboardFilterPlugin,
    ICliptOwnerBlockCoordinator,
    ICliptPluginLifetime,
    ICliptTrayTabViewFactory
{
    private ICliptHost? _host;
    private OwnerBlockerSettingsStore? _settingsStore;
    private OwnerBlockerTabViewModel? _tabViewModel;

    public string Id => "clipt.plugins.owner-blocker";

    public string Name => "Owner Blocker";

    public string Description =>
        "Block clipboard history from specific owner processes and window classes. Includes debug event log.";

    public string TabHeader => "Blocker";

    public int TabOrder => 50;

    public void Initialize(ICliptHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        OwnerBlockerSettingsMigrator.MigrateLegacyRegistrySettings(host);
        _settingsStore = new OwnerBlockerSettingsStore(host);
        host.ClipboardProcessed += OnClipboardProcessed;
    }

    public void Shutdown()
    {
        if (_host is not null)
            _host.ClipboardProcessed -= OnClipboardProcessed;

        _tabViewModel = null;
        _settingsStore = null;
        _host = null;
    }

    public CliptPluginFilterVerdict Evaluate(CliptPluginClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_settingsStore is null)
            return CliptPluginFilterVerdict.AllowSnapshot;

        string? reason = OwnerBlockRules.TryGetBlockReason(_settingsStore, snapshot);
        if (reason is not null)
            return CliptPluginFilterVerdict.BlockSnapshot(reason);

        return CliptPluginFilterVerdict.AllowSnapshot;
    }

    public async Task BlockAsync(string? processName, string? windowClass)
    {
        if (_settingsStore is null || _host is null)
            return;

        OwnerBlockRules.BlockSnapshotSource(_settingsStore, processName, windowClass);
        _tabViewModel?.RefreshBlockedOwners();
        if (BlockedProcessNames.IsBlockable(processName))
            await _host.RemoveHistoryByOwnerProcessAsync(processName!).ConfigureAwait(false);
    }

    public bool IsBlocked(CliptPluginClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _settingsStore is not null && OwnerBlockRules.IsBlocked(_settingsStore, snapshot);
    }

    public IReadOnlySet<string> GetBlockedProcessNames() =>
        ToEnabledNameSet(_settingsStore?.BlockedProcesses);

    public IReadOnlySet<string> GetBlockedWindowClassPrefixes() =>
        ToEnabledNameSet(_settingsStore?.BlockedClassPrefixes);

    /// <summary>
    /// Temporarily-disabled entries are excluded — everywhere outside the Blocker tab's own list, a
    /// disabled entry must behave as if it isn't blocked at all.
    /// </summary>
    private static IReadOnlySet<string> ToEnabledNameSet(IReadOnlyList<BlockedOwnerEntry>? entries) =>
        entries is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(entries.Where(e => e.IsEnabled).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

    public bool ShowHistoryBlockButton =>
        _settingsStore?.ShowHistoryBlockButton ?? true;

    public bool IsBlockableOwnerProcess(string? processName) =>
        BlockedProcessNames.IsBlockable(processName);

    public object CreateViewModel(ICliptHost host)
    {
        if (_settingsStore is null)
            throw new InvalidOperationException("OwnerBlockerPlugin has not been initialized.");

        _tabViewModel ??= new OwnerBlockerTabViewModel(host, _settingsStore);
        return _tabViewModel;
    }

    public object CreateView(object viewModel)
    {
        if (viewModel is not OwnerBlockerTabViewModel vm)
            throw new ArgumentException("Expected OwnerBlockerTabViewModel.", nameof(viewModel));

        return new OwnerBlockerTabView { DataContext = vm };
    }

    private void OnClipboardProcessed(object? sender, CliptPluginClipboardEventArgs e) =>
        _tabViewModel?.RecordEvent(e);
}
