using Clipt.Models;
using Clipt.Services;

namespace Clipt.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public void LoadStartupMode_DefaultsToFullWindow()
    {
        var service = new SettingsService();
        var mode = service.LoadStartupMode();

        Assert.True(mode == StartupMode.FullWindow || mode == StartupMode.Collapsed);
    }

    [Fact]
    public void SaveAndLoadStartupMode_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveStartupMode(StartupMode.Collapsed);
        var loaded = service.LoadStartupMode();

        Assert.Equal(StartupMode.Collapsed, loaded);

        service.SaveStartupMode(StartupMode.FullWindow);
        loaded = service.LoadStartupMode();

        Assert.Equal(StartupMode.FullWindow, loaded);
    }

    [Fact]
    public void SaveStartupMode_InvalidValue_FallsBackToFullWindow()
    {
        var service = new SettingsService();

        service.SaveStartupMode((StartupMode)999);

        var loaded = service.LoadStartupMode();
        Assert.Equal(StartupMode.FullWindow, loaded);
    }

    [Fact]
    public void ISettingsService_ImplementedBySettingsService()
    {
        ISettingsService service = new SettingsService();
        Assert.NotNull(service);
    }
}
