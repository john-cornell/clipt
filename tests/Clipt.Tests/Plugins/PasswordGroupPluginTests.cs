using Clipt.Plugins;
using Clipt.Plugins.PasswordGroup;

namespace Clipt.Tests.Plugins;

public class PasswordGroupPluginTests
{
    [Fact]
    public void GetSubActions_ReturnsSaveTop_WhenTopEntryExists()
    {
        var plugin = new PasswordGroupPlugin();
        var host = new TestPasswordHost { TopHistoryEntryId = "entry-1" };

        IReadOnlyList<CliptPluginHistorySubAction> subActions = plugin.GetSubActions(host);

        Assert.Single(subActions);
        Assert.Equal(PasswordGroupPlugin.SaveTopSubActionId, subActions[0].Id);
        Assert.Equal("Save top item", subActions[0].Label);
    }

    [Fact]
    public void GetSubActions_ReturnsEmpty_WhenHistoryEmpty()
    {
        var plugin = new PasswordGroupPlugin();
        var host = new TestPasswordHost();

        IReadOnlyList<CliptPluginHistorySubAction> subActions = plugin.GetSubActions(host);

        Assert.Empty(subActions);
    }

    [Fact]
    public async Task ExecuteAsync_SavesTopEntryToPasswordDatedGroup()
    {
        var plugin = new PasswordGroupPlugin();
        var host = new TestPasswordHost { TopHistoryEntryId = "top-entry" };
        string expectedName = $"password {DateTime.Now:yyyy-MM-dd}";

        await plugin.ExecuteAsync(
            PasswordGroupPlugin.SaveTopSubActionId,
            host,
            [],
            CancellationToken.None);

        Assert.Equal(expectedName, host.LastSavedGroupName);
        Assert.Equal(["top-entry"], host.LastSavedEntryIds);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresUnknownSubAction()
    {
        var plugin = new PasswordGroupPlugin();
        var host = new TestPasswordHost { TopHistoryEntryId = "top-entry" };

        await plugin.ExecuteAsync("other", host, [], CancellationToken.None);

        Assert.Null(host.LastSavedGroupName);
    }

    private sealed class TestPasswordHost : ICliptHost
    {
        public string PluginId => "test";

        public string? TopHistoryEntryId { get; init; }

        public string? LastSavedGroupName { get; private set; }

        public IReadOnlyList<string>? LastSavedEntryIds { get; private set; }

        public event EventHandler<CliptPluginClipboardEventArgs>? ClipboardProcessed;

        public T? LoadSettings<T>() where T : class, new() => null;

        public void SaveSettings<T>(T settings) where T : class { }

        public Task RemoveHistoryByOwnerProcessAsync(string processName) => Task.CompletedTask;

        public Task BlockOwnerAsync(string? processName, string? windowClass) => Task.CompletedTask;

        public IReadOnlySet<string> GetBlockedProcessNames() =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> GetBlockedWindowClassPrefixes() =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void NotifyHistoryOwnerBlockUiChanged() { }

        public IReadOnlyList<CliptPluginSavedGroup> GetSavedGroups() => [];

        public Task AddEntriesToGroupAsync(string groupId, IReadOnlyList<string> historyEntryIds) =>
            Task.CompletedTask;

        public string? GetTopHistoryEntryId() => TopHistoryEntryId;

        public Task SaveGroupAsync(string name, IReadOnlyList<string> historyEntryIds)
        {
            LastSavedGroupName = name;
            LastSavedEntryIds = historyEntryIds.ToArray();
            return Task.CompletedTask;
        }
    }
}
