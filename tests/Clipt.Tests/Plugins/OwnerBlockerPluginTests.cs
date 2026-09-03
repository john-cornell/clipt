using Clipt.Models;
using Clipt.Plugins;
using Clipt.Plugins.OwnerBlocker;
using Clipt.Plugins.OwnerBlocker.ViewModels;
using Clipt.Services;
using Moq;

namespace Clipt.Tests.Plugins;

public class OwnerBlockerRulesTests
{
    [Fact]
    public void IsBlocked_MatchesProcessName_CaseInsensitive()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource("Wispr Flow Helper", null);

        var snapshot = CreateSnapshot(processName: "wispr flow helper");

        Assert.True(OwnerBlockRules.IsBlocked(store, snapshot));
    }

    [Fact]
    public void IsBlocked_MatchesWisprWindowClassPrefix()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource(null, "WisprClipboard_d6745597");

        var snapshot = CreateSnapshot(
            processName: "(no owner)",
            windowClass: "WisprClipboard_d6745597");

        Assert.True(OwnerBlockRules.IsBlocked(store, snapshot));
    }

    [Fact]
    public void TryGetBlockReason_DistinguishesProcessAndWindowClass()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource("Wispr", null);

        Assert.Equal(
            "Blocked process",
            OwnerBlockRules.TryGetBlockReason(store, CreateSnapshot(processName: "Wispr")));

        store.ClearAll();
        store.BlockSnapshotSource(null, "WisprClipboard_abc");

        Assert.Equal(
            "Blocked window class",
            OwnerBlockRules.TryGetBlockReason(
                store,
                CreateSnapshot(processName: "(no owner)", windowClass: "WisprClipboard_abc")));
    }

    [Fact]
    public void BlockSnapshotSource_SavesProcessAndWisprClassPrefix()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();

        store.BlockSnapshotSource("Wispr Flow Helper", "WisprClipboard_d6745597");

        Assert.Contains(store.BlockedProcesses, e => e.Name == "Wispr Flow Helper");
        Assert.Contains(store.BlockedClassPrefixes, e => e.Name == "WisprClipboard_");
    }

    [Fact]
    public void SetProcessEnabled_False_UnblocksWithoutRemovingEntry()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource("Wispr Flow Helper", null);

        store.SetProcessEnabled("Wispr Flow Helper", false);

        Assert.False(OwnerBlockRules.IsBlocked(store, CreateSnapshot(processName: "Wispr Flow Helper")));
        Assert.Contains(store.BlockedProcesses, e => e.Name == "Wispr Flow Helper" && !e.IsEnabled);
    }

    [Fact]
    public void SetProcessEnabled_ReEnable_BlocksAgain()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource("Wispr Flow Helper", null);
        store.SetProcessEnabled("Wispr Flow Helper", false);

        store.SetProcessEnabled("Wispr Flow Helper", true);

        Assert.True(OwnerBlockRules.IsBlocked(store, CreateSnapshot(processName: "Wispr Flow Helper")));
    }

    [Fact]
    public void SetWindowClassEnabled_False_UnblocksWithoutRemovingEntry()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource(null, "WisprClipboard_abc");

        store.SetWindowClassEnabled("WisprClipboard_", false);

        Assert.False(OwnerBlockRules.IsBlocked(
            store,
            CreateSnapshot(processName: "(no owner)", windowClass: "WisprClipboard_abc")));
        Assert.Contains(store.BlockedClassPrefixes, e => e.Name == "WisprClipboard_" && !e.IsEnabled);
    }

    [Fact]
    public void BlockSnapshotSource_ReBlockingDisabledEntry_ReEnablesInsteadOfDuplicating()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();
        store.BlockSnapshotSource("Wispr Flow Helper", null);
        store.SetProcessEnabled("Wispr Flow Helper", false);

        store.BlockSnapshotSource("Wispr Flow Helper", null);

        Assert.Single(store.BlockedProcesses);
        Assert.True(store.BlockedProcesses[0].IsEnabled);
    }

    private static CliptPluginClipboardSnapshot CreateSnapshot(
        string processName = "test",
        string windowClass = "")
    {
        return new CliptPluginClipboardSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = processName,
            OwnerProcessId = 1,
            OwnerWindowTitle = string.Empty,
            OwnerWindowClass = windowClass,
            Formats = [],
        };
    }

    internal sealed class InMemoryOwnerBlockerSettingsStore : IOwnerBlockerSettingsStore
    {
        private readonly List<BlockedOwnerEntry> _processes = [];
        private readonly List<BlockedOwnerEntry> _classes = [];

        public IReadOnlyList<BlockedOwnerEntry> BlockedProcesses => _processes;

        public IReadOnlyList<BlockedOwnerEntry> BlockedClassPrefixes => _classes;

        public void BlockSnapshotSource(string? processName, string? windowClass)
        {
            if (Clipt.Plugins.OwnerBlocker.BlockedProcessNames.IsBlockable(processName))
                AddOrReEnable(_processes, processName!.Trim());

            string? classPrefix = Clipt.Plugins.OwnerBlocker.BlockedWindowClasses.NormalizeForBlock(windowClass);
            if (classPrefix is not null)
                AddOrReEnable(_classes, classPrefix);
        }

        private static void AddOrReEnable(List<BlockedOwnerEntry> entries, string name)
        {
            BlockedOwnerEntry? existing = entries.FirstOrDefault(
                e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                existing.IsEnabled = true;
            else
                entries.Add(new BlockedOwnerEntry { Name = name, IsEnabled = true });
        }

        public void UnblockProcess(string processName) =>
            _processes.RemoveAll(e => string.Equals(e.Name, processName, StringComparison.OrdinalIgnoreCase));

        public void UnblockWindowClass(string classPrefix) =>
            _classes.RemoveAll(e => string.Equals(e.Name, classPrefix, StringComparison.OrdinalIgnoreCase));

        public void SetProcessEnabled(string processName, bool enabled) => SetEnabled(_processes, processName, enabled);

        public void SetWindowClassEnabled(string classPrefix, bool enabled) => SetEnabled(_classes, classPrefix, enabled);

        private static void SetEnabled(List<BlockedOwnerEntry> entries, string name, bool enabled)
        {
            BlockedOwnerEntry? entry = entries.FirstOrDefault(
                e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                entry.IsEnabled = enabled;
        }

        public void ClearAll()
        {
            _processes.Clear();
            _classes.Clear();
        }

        public bool ShowHistoryBlockButton { get; set; } = true;
    }
}

public class OwnerBlockerPluginTests
{
    [Fact]
    public async Task Evaluate_BlocksWhenClassPrefixSaved()
    {
        var host = new TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);

        await plugin.BlockAsync(null, "WisprClipboard_d6745597");

        CliptPluginFilterVerdict verdict = plugin.Evaluate(new CliptPluginClipboardSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "(no owner)",
            OwnerProcessId = 0,
            OwnerWindowTitle = string.Empty,
            OwnerWindowClass = "WisprClipboard_d6745597",
            Formats = [],
        });

        Assert.False(verdict.Allow);
        Assert.Equal("Blocked window class", verdict.Reason);
    }

    [Fact]
    public async Task BlockAsync_PurgesHistoryForProcess()
    {
        var host = new TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);

        await plugin.BlockAsync("Wispr Flow Helper", "WisprClipboard_d6745597");

        Assert.Contains("Wispr Flow Helper", host.RemovedProcesses);
        Assert.Contains("WisprClipboard_", plugin.GetBlockedWindowClassPrefixes());
    }

    [Fact]
    public async Task BlockAsync_RefreshesBlockedListWhenBlockerTabAlreadyOpen()
    {
        var host = new TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);

        await plugin.BlockAsync("mstsc", "CLIPBRDWNDCLASS");

        Assert.Single(vm.BlockedProcessItems);
        Assert.Equal("mstsc", vm.BlockedProcessItems[0].DisplayName);
        Assert.Single(vm.BlockedWindowClassItems);
        Assert.Equal("CLIPBRDWNDCLASS", vm.BlockedWindowClassItems[0].DisplayName);
    }

    [Fact]
    public void Registry_LoadsOwnerBlockerFromPluginsFolder()
    {
        var logger = new Mock<IAppLogger>();
        logger.Setup(l => l.Level).Returns(AppLogLevel.Off);
        var registry = new PluginRegistry(logger.Object);
        var history = new Mock<IClipboardHistoryService>();
        var groups = new Mock<IClipboardGroupService>();
        var host = new CliptPluginHost(
            registry,
            new Lazy<IClipboardHistoryService>(() => history.Object),
            new Lazy<IClipboardGroupService>(() => groups.Object));
        registry.SetHost(host);
        registry.Initialize();

        Assert.Contains(registry.Registrations, r => r.Plugin.Id == "clipt.plugins.owner-blocker");
        Assert.Contains(registry.FilterPlugins, p => p.Id == "clipt.plugins.owner-blocker");
        Assert.NotNull(registry.OwnerBlockCoordinator);
        Assert.Contains(registry.TrayTabPlugins, p => p.Id == "clipt.plugins.owner-blocker");
    }

    [Fact]
    public void CreateViewModel_ReturnOwnerBlockerTabViewModel()
    {
        var plugin = new OwnerBlockerPlugin();
        var host = new TestCliptHost();
        plugin.Initialize(host);

        object vm = plugin.CreateViewModel(host);

        Assert.IsType<OwnerBlockerTabViewModel>(vm);
        Assert.Equal("Blocker", plugin.TabHeader);
        Assert.IsAssignableFrom<ICliptTrayTabViewFactory>(plugin);
    }

    internal sealed class TestCliptHost : ICliptHost
    {
        private OwnerBlockerSettings _settings = new();
        private OwnerBlockerPlugin? _coordinator;

        public string PluginId => "clipt.plugins.owner-blocker";

        public List<string> RemovedProcesses { get; } = [];

        public event EventHandler<CliptPluginClipboardEventArgs>? ClipboardProcessed;

        public void AttachCoordinator(OwnerBlockerPlugin coordinator) => _coordinator = coordinator;

        public T? LoadSettings<T>() where T : class, new() =>
            _settings as T;

        public void SaveSettings<T>(T settings) where T : class
        {
            if (settings is OwnerBlockerSettings blockerSettings)
                _settings = blockerSettings;
        }

        public Task RemoveHistoryByOwnerProcessAsync(string processName)
        {
            RemovedProcesses.Add(processName);
            return Task.CompletedTask;
        }

        public Task BlockOwnerAsync(string? processName, string? windowClass) =>
            _coordinator?.BlockAsync(processName, windowClass) ?? Task.CompletedTask;

        public IReadOnlySet<string> GetBlockedProcessNames() =>
            _coordinator?.GetBlockedProcessNames()
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> GetBlockedWindowClassPrefixes() =>
            _coordinator?.GetBlockedWindowClassPrefixes()
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void NotifyHistoryOwnerBlockUiChanged() =>
            HistoryOwnerBlockUiChanged?.Invoke(this, EventArgs.Empty);

        public event EventHandler? HistoryOwnerBlockUiChanged;

        public IReadOnlyList<CliptPluginSavedGroup> GetSavedGroups() => [];

        public Task AddEntriesToGroupAsync(string groupId, IReadOnlyList<string> historyEntryIds) =>
            Task.CompletedTask;

        public string? GetTopHistoryEntryId() => null;

        public Task SaveGroupAsync(
            string name,
            IReadOnlyList<string> historyEntryIds,
            IReadOnlyDictionary<string, string>? entryNameOverrides = null) =>
            Task.CompletedTask;
    }
}

public class OwnerBlockerTabViewModelTests
{
    [Fact]
    public void ShowHistoryBlockButton_DefaultsTrue()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);

        Assert.True(plugin.ShowHistoryBlockButton);
    }

    [Fact]
    public void ShowHistoryBlockButton_ToggleNotifiesHost()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);

        int notifyCount = 0;
        host.HistoryOwnerBlockUiChanged += (_, _) => notifyCount++;

        vm.ShowHistoryBlockButton = false;

        Assert.False(plugin.ShowHistoryBlockButton);
        Assert.Equal(1, notifyCount);
    }

    [Fact]
    public void RecordEvent_AddsToRecentEvents_WithOwnerMetadata()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);

        vm.RecordEvent(new CliptPluginClipboardEventArgs
        {
            AddOutcome = CliptPluginHistoryAddOutcome.Added,
            Snapshot = new CliptPluginClipboardSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                SequenceNumber = 42,
                OwnerProcessName = "Wispr",
                OwnerProcessId = 999,
                OwnerWindowHandle = 0x1234,
                OwnerWindowTitle = "Wispr Flow",
                OwnerWindowClass = "Chrome_WidgetWin_1",
                Formats =
                [
                    new CliptPluginFormatInfo
                    {
                        FormatId = 1,
                        FormatName = "CF_UNICODETEXT",
                        IsStandard = true,
                        DataSize = 0,
                    },
                ],
            },
        });

        Assert.Single(vm.RecentEvents);
        Assert.NotNull(vm.RecentEvents[0].BlockOwnerCommand);
        Assert.Equal("Wispr", vm.RecentEvents[0].BlockableProcessName);
        Assert.True(vm.RecentEvents[0].CanBlockOwner);
    }

    [Fact]
    public async Task BlockOwnerCommand_FromEventRow_BlocksAndPurgesHistory()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);

        vm.RecordEvent(new CliptPluginClipboardEventArgs
        {
            AddOutcome = CliptPluginHistoryAddOutcome.SkippedEmptyFormats,
            Snapshot = new CliptPluginClipboardSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                OwnerProcessName = "Wispr Flow Helper",
                OwnerProcessId = 24444,
                OwnerWindowTitle = string.Empty,
                OwnerWindowClass = "WisprClipboard_d6745597",
                Formats = [],
            },
        });

        await vm.RecentEvents[0].BlockOwnerCommand!.ExecuteAsync(null);

        Assert.Contains("Wispr Flow Helper", host.RemovedProcesses);
        Assert.False(vm.RecentEvents[0].CanBlockOwner);
        Assert.Single(vm.BlockedProcessItems);
        Assert.Equal("Wispr Flow Helper", vm.BlockedProcessItems[0].DisplayName);
        Assert.Single(vm.BlockedWindowClassItems);
        Assert.Equal("WisprClipboard_", vm.BlockedWindowClassItems[0].DisplayName);
    }

    [Fact]
    public void ClearRecentEvents_RemovesAllEvents()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);

        vm.RecordEvent(new CliptPluginClipboardEventArgs
        {
            AddOutcome = CliptPluginHistoryAddOutcome.SkippedEmptyFormats,
            Snapshot = new CliptPluginClipboardSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                OwnerProcessName = "test",
                OwnerProcessId = 1,
                OwnerWindowTitle = string.Empty,
                OwnerWindowClass = string.Empty,
                Formats = [],
            },
        });

        vm.ClearRecentEventsCommand.Execute(null);

        Assert.Empty(vm.RecentEvents);
    }

    [Fact]
    public async Task BlockedProcessItem_UncheckingIsEnabled_UnblocksWithoutRemovingEntry()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);
        await plugin.BlockAsync("mstsc", null);

        Assert.Single(vm.BlockedProcessItems);
        vm.BlockedProcessItems[0].IsEnabled = false;

        Assert.Single(vm.BlockedProcessItems);
        Assert.False(plugin.GetBlockedProcessNames().Contains("mstsc"));
    }

    [Fact]
    public async Task BlockedProcessItem_ReCheckingIsEnabled_BlocksAgain()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);
        await plugin.BlockAsync("mstsc", null);
        vm.BlockedProcessItems[0].IsEnabled = false;

        vm.BlockedProcessItems[0].IsEnabled = true;

        Assert.True(plugin.GetBlockedProcessNames().Contains("mstsc"));
    }

    [Fact]
    public async Task BlockedWindowClassItem_UncheckingIsEnabled_UnblocksWithoutRemovingEntry()
    {
        var host = new OwnerBlockerPluginTests.TestCliptHost();
        var plugin = new OwnerBlockerPlugin();
        plugin.Initialize(host);
        host.AttachCoordinator(plugin);
        var vm = (OwnerBlockerTabViewModel)plugin.CreateViewModel(host);
        await plugin.BlockAsync(null, "CLIPBRDWNDCLASS");

        vm.BlockedWindowClassItems[0].IsEnabled = false;

        Assert.Single(vm.BlockedWindowClassItems);
        Assert.False(plugin.GetBlockedWindowClassPrefixes().Contains("CLIPBRDWNDCLASS"));
    }
}
