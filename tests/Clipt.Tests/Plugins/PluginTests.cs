using Clipt.Plugins;
using Clipt.Plugins.WhereIn;
using Clipt.Services;
using Clipt.ViewModels;
using Moq;

namespace Clipt.Tests.Plugins;

public class WhereInPluginTests
{
    [Fact]
    public async Task ExecuteAsync_ProducesWhereInClause()
    {
        var plugin = new WhereInPlugin();
        const string guid = "550e8400-e29b-41d4-a716-446655440000";
        var context = new CliptPluginContext
        {
            ClipboardText = $"Id\n{guid}",
            OptionValues = new Dictionary<string, bool>
            {
                [WhereInSqlBuilder.UseFirstLineAsColumnHeaderOptionKey] = true,
            },
        };

        CliptPluginResult result = await plugin.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Id IN (", result.OutputClipboardText, StringComparison.Ordinal);
        Assert.Contains($"'{guid}'", result.OutputClipboardText, StringComparison.Ordinal);
    }

    [Fact]
    public void CanExecute_RequiresMultipleLines()
    {
        var plugin = new WhereInPlugin();
        var single = new CliptPluginContext
        {
            ClipboardText = "550e8400-e29b-41d4-a716-446655440000",
            OptionValues = new Dictionary<string, bool>(),
        };
        var multi = new CliptPluginContext
        {
            ClipboardText = "Id\n550e8400-e29b-41d4-a716-446655440000",
            OptionValues = new Dictionary<string, bool>(),
        };

        Assert.False(plugin.CanExecute(single));
        Assert.True(plugin.CanExecute(multi));
    }
}

public class PluginsTabViewModelTests
{
    [Fact]
    public void Refresh_LoadsWhereInPluginFromPluginsFolder()
    {
        var registry = new PluginRegistry();
        var clipboard = new Mock<IClipboardService>();
        var vm = new PluginsTabViewModel(registry, clipboard.Object, () => nint.Zero);

        vm.Refresh();

        Assert.Contains(vm.DisplayPlugins, p => p.Plugin.Id == "clipt.plugins.where-in");
        Assert.True(vm.DisplayPlugins.First(p => p.Plugin.Id == "clipt.plugins.where-in").IsActionPlugin);
    }

    [Fact]
    public async Task RunPlugin_WritesOutputToClipboard()
    {
        var registry = new PluginRegistry();
        registry.Initialize();
        var clipboard = new Mock<IClipboardService>();
        nint hwnd = new(42);
        var vm = new PluginsTabViewModel(registry, clipboard.Object, () => hwnd);

        const string guid = "550e8400-e29b-41d4-a716-446655440000";
        vm.SetClipboardText($"Id\n{guid}");
        vm.Refresh();
        vm.SetClipboardText($"Id\n{guid}");

        PluginDisplayItem item = vm.DisplayPlugins.First(p => p.Plugin.Id == "clipt.plugins.where-in");
        Assert.True(item.CanRun);

        await item.RunCommand.ExecuteAsync(null);

        clipboard.Verify(c => c.SetClipboardText(It.Is<string>(s => s.Contains("Id IN (")), hwnd), Times.Once);
        Assert.False(item.LastMessageIsError);
    }
}
