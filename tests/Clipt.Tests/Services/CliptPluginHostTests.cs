using System.Collections.Immutable;
using System.IO;
using System.Text;
using Clipt.Models;
using Clipt.Native;
using Clipt.Plugins;
using Clipt.Services;
using Moq;

namespace Clipt.Tests.Services;

public class CliptPluginHostTests
{
    [Fact]
    public void EvaluateFilters_WithNoFilterPlugins_AllowsSnapshot()
    {
        var registry = CreateRegistry();
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);
        var snapshot = CreateSnapshot("hello");

        CliptPluginFilterResult result = host.EvaluateFilters(snapshot);

        Assert.True(result.Allow);
        Assert.Null(result.PluginId);
    }

    [Fact]
    public void EvaluateFilters_FirstBlockWins()
    {
        var registry = new TestPluginRegistry();
        var blocking = new TestFilterPlugin("clipt.plugins.block", allow: false, reason: "blocked");
        registry.FilterPluginsList.Add(blocking);
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);

        CliptPluginFilterResult result = host.EvaluateFilters(CreateSnapshot("hello"));

        Assert.False(result.Allow);
        Assert.Equal("clipt.plugins.block", result.PluginId);
        Assert.Equal("blocked", result.Reason);
    }

    [Fact]
    public void PublishClipboardEvent_RaisesSubscribers()
    {
        var registry = CreateRegistry();
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);
        var snapshot = CreateSnapshot("hello");
        CliptPluginClipboardEventArgs? received = null;
        host.ClipboardProcessed += (_, args) => received = args;

        host.PublishClipboardEvent(snapshot, HistoryAddResult.Added);

        Assert.NotNull(received);
        Assert.Equal(CliptPluginHistoryAddOutcome.Added, received!.AddOutcome);
        Assert.Equal("test", received.Snapshot.OwnerProcessName);
    }

    [Fact]
    public async Task BlockOwnerAsync_WithoutCoordinator_IsNoOp()
    {
        var registry = CreateRegistry();
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);

        await host.BlockOwnerAsync("wispr", "WisprClipboard_1");

        history.Verify(h => h.RemoveByOwnerProcessAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BlockOwnerAsync_WithCoordinator_Delegates()
    {
        var registry = new TestPluginRegistry();
        var coordinator = new TestCoordinator();
        registry.Coordinator = coordinator;
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);

        await host.BlockOwnerAsync("wispr", null);

        Assert.Equal(1, coordinator.BlockCalls);
    }

    [Fact]
    public void CreateHostScope_LoadAndSaveSettings_RoundTripsJson()
    {
        var registry = CreateRegistry();
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);
        ICliptHost scope = host.CreateHostScope("clipt.plugins.test");
        string settingsDir = host.GetPluginSettingsDirectory("clipt.plugins.test");

        try
        {
            var settings = new TestPluginSettings { BlockedProcesses = ["wispr"] };
            scope.SaveSettings(settings);

            TestPluginSettings? loaded = scope.LoadSettings<TestPluginSettings>();

            Assert.NotNull(loaded);
            Assert.Contains("wispr", loaded!.BlockedProcesses);
        }
        finally
        {
            if (Directory.Exists(settingsDir))
                Directory.Delete(settingsDir, recursive: true);
        }
    }

    private static CliptPluginHost CreateHost(IPluginRegistry registry, Mock<IClipboardHistoryService> history)
    {
        var groups = new Mock<IClipboardGroupService>();
        return new CliptPluginHost(
            registry,
            new Lazy<IClipboardHistoryService>(() => history.Object),
            new Lazy<IClipboardGroupService>(() => groups.Object));
    }

    private static PluginRegistry CreateRegistry()
    {
        var logger = new Mock<IAppLogger>();
        logger.Setup(l => l.Level).Returns(AppLogLevel.Off);
        var registry = new PluginRegistry(logger.Object);
        var history = new Mock<IClipboardHistoryService>();
        var host = CreateHost(registry, history);
        registry.SetHost(host);
        return registry;
    }

    private static ClipboardSnapshot CreateSnapshot(string text)
    {
        byte[] data = Encoding.Unicode.GetBytes(text + "\0");
        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 42,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = data.Length,
                Memory = new MemoryInfo("0x0", "0x0", data.Length, []),
                RawData = data,
            }),
        };
    }

    private sealed class TestPluginSettings
    {
        public List<string> BlockedProcesses { get; set; } = [];
    }

    private sealed class TestFilterPlugin(string id, bool allow, string? reason) : ICliptClipboardFilterPlugin
    {
        public string Id => id;
        public string Name => id;
        public string Description => id;

        public CliptPluginFilterVerdict Evaluate(CliptPluginClipboardSnapshot snapshot) =>
            allow ? CliptPluginFilterVerdict.AllowSnapshot : CliptPluginFilterVerdict.BlockSnapshot(reason!);
    }

    private sealed class TestCoordinator : ICliptOwnerBlockCoordinator
    {
        public int BlockCalls { get; private set; }

        public Task BlockAsync(string? processName, string? windowClass)
        {
            BlockCalls++;
            return Task.CompletedTask;
        }

        public bool IsBlocked(CliptPluginClipboardSnapshot snapshot) => false;

        public IReadOnlySet<string> GetBlockedProcessNames() =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> GetBlockedWindowClassPrefixes() =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool ShowHistoryBlockButton => true;

        public bool IsBlockableOwnerProcess(string? processName) =>
            !string.IsNullOrWhiteSpace(processName) && processName != "(no owner)";
    }

    private sealed class TestPluginRegistry : IPluginRegistry
    {
        public List<ICliptClipboardFilterPlugin> FilterPluginsList { get; } = [];

        public ICliptOwnerBlockCoordinator? Coordinator { get; set; }

        public IReadOnlyList<PluginRegistrationInfo> Registrations => [];

        public IReadOnlyList<PluginLoadFailureInfo> LoadFailures => [];

        public IReadOnlyList<ICliptClipboardFilterPlugin> FilterPlugins => FilterPluginsList;

        public IReadOnlyList<ICliptTrayTabPlugin> TrayTabPlugins => [];

        public IReadOnlyList<ICliptHistoryActionPlugin> HistoryActionPlugins => [];

        public ICliptOwnerBlockCoordinator? OwnerBlockCoordinator => Coordinator;

        public bool IsPluginEnabled(string pluginId) => true;

        public void SetPluginEnabled(string pluginId, bool enabled) { }

        public event EventHandler? RescanCompleted;

        public void SetHost(ICliptPluginHost host) { }

        public void Initialize() { }

        public void Rescan() { }
    }
}
