using System.Reflection;
using Clipt.Models;
using Clipt.Plugins;
using Clipt.Services;
using Moq;

namespace Clipt.Tests.Services;

public class PluginRegistryTests
{
    [Fact]
    public void Rescan_RaisesRescanCompleted()
    {
        var logger = new Mock<IAppLogger>();
        logger.Setup(l => l.Level).Returns(AppLogLevel.Off);
        var registry = new PluginRegistry(logger.Object);
        int eventCount = 0;
        registry.RescanCompleted += (_, _) => eventCount++;

        registry.Rescan();

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void RegisterPlugin_WhenInitializeFails_SkipsRegistration()
    {
        var logger = new Mock<IAppLogger>();
        logger.Setup(l => l.Level).Returns(AppLogLevel.Off);
        var history = new Mock<IClipboardHistoryService>();
        var host = new CliptPluginHost(new PluginRegistry(logger.Object), history.Object);
        var registry = new PluginRegistry(logger.Object);
        registry.SetHost(host);

        var plugin = new FailingLifetimePlugin();

        InvokeRegisterPlugin(registry, plugin, "test.dll");

        Assert.Empty(registry.Registrations);
        Assert.Contains(registry.LoadFailures, f => f.ErrorMessage.Contains("Initialize failed"));
        Assert.Empty(registry.FilterPlugins);
    }

    private static void InvokeRegisterPlugin(PluginRegistry registry, ICliptPlugin plugin, string source)
    {
        MethodInfo? method = typeof(PluginRegistry).GetMethod(
            "RegisterPlugin",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(registry, [plugin, source]);
    }

    private sealed class FailingLifetimePlugin : ICliptPlugin, ICliptPluginLifetime, ICliptClipboardFilterPlugin
    {
        public string Id => "test.failing";
        public string Name => "Failing";
        public string Description => "Throws on init";

        public void Initialize(ICliptHost host) =>
            throw new InvalidOperationException("init failed");

        public void Shutdown() { }

        public CliptPluginFilterVerdict Evaluate(CliptPluginClipboardSnapshot snapshot) =>
            CliptPluginFilterVerdict.AllowSnapshot;
    }
}
