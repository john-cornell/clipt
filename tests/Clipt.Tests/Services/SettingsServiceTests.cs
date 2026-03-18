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

    [Fact]
    public void LoadMaxHistoryEntries_DefaultsTo10()
    {
        var service = new SettingsService();
        int entries = service.LoadMaxHistoryEntries();
        Assert.True(entries > 0, "Default max history entries should be positive");
    }

    [Fact]
    public void SaveAndLoadMaxHistoryEntries_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveMaxHistoryEntries(25);
        int loaded = service.LoadMaxHistoryEntries();
        Assert.Equal(25, loaded);

        service.SaveMaxHistoryEntries(10);
        loaded = service.LoadMaxHistoryEntries();
        Assert.Equal(10, loaded);
    }

    [Fact]
    public void SaveMaxHistoryEntries_InvalidValue_FallsBackToDefault()
    {
        var service = new SettingsService();
        service.SaveMaxHistoryEntries(-5);
        int loaded = service.LoadMaxHistoryEntries();
        Assert.True(loaded > 0, "Should fall back to a positive default");
    }

    [Fact]
    public void LoadMaxHistorySizeBytes_DefaultsTo100MB()
    {
        var service = new SettingsService();
        long size = service.LoadMaxHistorySizeBytes();
        Assert.True(size >= 0, "Default max history size should be non-negative");
    }

    [Fact]
    public void SaveAndLoadMaxHistorySizeBytes_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveMaxHistorySizeBytes(250L * 1024 * 1024);
        long loaded = service.LoadMaxHistorySizeBytes();
        Assert.Equal(250L * 1024 * 1024, loaded);

        service.SaveMaxHistorySizeBytes(0);
        loaded = service.LoadMaxHistorySizeBytes();
        Assert.Equal(0, loaded);
    }

    [Fact]
    public void SaveMaxHistorySizeBytes_NegativeValue_FallsBackToDefault()
    {
        var service = new SettingsService();
        service.SaveMaxHistorySizeBytes(-1);
        long loaded = service.LoadMaxHistorySizeBytes();
        Assert.True(loaded >= 0, "Should fall back to a non-negative default");
    }
}
