using Clipt.Models;
using Clipt.Services;
using Moq;

namespace Clipt.Tests.Services;

public class TrayIconServiceTests
{
    [Fact]
    public void Initialize_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadStartupMode()).Returns(StartupMode.FullWindow);

        using var service = new TrayIconService(settingsMock.Object);
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadStartupMode()).Returns(StartupMode.FullWindow);

        using var service = new TrayIconService(settingsMock.Object);
        service.Initialize();
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void UpdateIcon_BeforeInitialize_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        using var service = new TrayIconService(settingsMock.Object);
        var ex = Record.Exception(() => service.UpdateIcon(true));
        Assert.Null(ex);
    }

    [Fact]
    public void UpdateIcon_AfterInitialize_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadStartupMode()).Returns(StartupMode.FullWindow);

        using var service = new TrayIconService(settingsMock.Object);
        service.Initialize();

        var ex = Record.Exception(() => service.UpdateIcon(true));
        Assert.Null(ex);

        ex = Record.Exception(() => service.UpdateIcon(false));
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadStartupMode()).Returns(StartupMode.FullWindow);

        var service = new TrayIconService(settingsMock.Object);
        service.Initialize();

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadStartupMode()).Returns(StartupMode.FullWindow);

        var service = new TrayIconService(settingsMock.Object);
        service.Initialize();
        service.Dispose();

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_AfterDispose_ThrowsObjectDisposedException()
    {
        var settingsMock = new Mock<ISettingsService>();
        var service = new TrayIconService(settingsMock.Object);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.Initialize());
    }

    [Fact]
    public void ITrayIconService_ImplementedByTrayIconService()
    {
        var settingsMock = new Mock<ISettingsService>();
        ITrayIconService service = new TrayIconService(settingsMock.Object);
        Assert.NotNull(service);
        service.Dispose();
    }

    [Fact]
    public void Events_AreSubscribable()
    {
        var settingsMock = new Mock<ISettingsService>();
        using var service = new TrayIconService(settingsMock.Object);

        bool trayClicked = false;
        bool openFull = false;
        bool exit = false;

        service.TrayIconClicked += (_, _) => trayClicked = true;
        service.OpenFullRequested += (_, _) => openFull = true;
        service.ExitRequested += (_, _) => exit = true;

        Assert.False(trayClicked);
        Assert.False(openFull);
        Assert.False(exit);
    }
}
