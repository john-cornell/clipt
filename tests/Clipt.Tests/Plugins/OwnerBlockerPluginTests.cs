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
    public void BlockSnapshotSource_SavesProcessAndWisprClassPrefix()
    {
        var store = new InMemoryOwnerBlockerSettingsStore();

        store.BlockSnapshotSource("Wispr Flow Helper", "WisprClipboard_d6745597");

        Assert.Contains("Wispr Flow Helper", store.BlockedProcesses);
        Assert.Contains("WisprClipboard_", store.BlockedClassPrefixes);
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
        private readonly HashSet<string> _processes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _classes = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> BlockedProcesses => _processes;

        public IReadOnlySet<string> BlockedClassPrefixes => _classes;

        public void BlockSnapshotSource(string? processName, string? windowClass)
        {
            if (Clipt.Plugins.OwnerBlocker.BlockedProcessNames.IsBlockable(processName))
                _processes.Add(processName!.Trim());

            string? classPrefix = Clipt.Plugins.OwnerBlocker.BlockedWindowClasses.NormalizeForBlock(windowClass);
            if (classPrefix is not null)
                _classes.Add(classPrefix);
        }

        public void UnblockProcess(string processName) => _processes.Remove(processName);

        public void UnblockWindowClass(string classPrefix) => _classes.Remove(classPrefix);

        public void ClearAll()
        {
            _processes.Clear();
            _classes.Clear();
        }
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
        Assert.Equal("Blocked process", verdict.Reason);
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
    public void Registry_LoadsOwnerBlockerFromPluginsFolder()
    {
        var logger = new Mock<IAppLogger>();
        logger.Setup(l => l.Level).Returns(AppLogLevel.Off);
        var registry = new PluginRegistry(logger.Object);
        var history = new Mock<IClipboardHistoryService>();
        var host = new CliptPluginHost(registry, history.Object);
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
    }
}

public class OwnerBlockerTabViewModelTests
{
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
}
