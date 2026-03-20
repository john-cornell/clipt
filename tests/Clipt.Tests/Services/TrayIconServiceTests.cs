using Clipt.Models;
using Clipt.Services;
using Moq;
using WinForms = System.Windows.Forms;

namespace Clipt.Tests.Services;

public class TrayIconServiceTests
{
    private static Mock<ISettingsService> CreateFullSettingsMock()
    {
        var mock = new Mock<ISettingsService>();
        mock.Setup(s => s.LoadStartupMode()).Returns(StartupMode.FullWindow);
        mock.Setup(s => s.LoadMaxHistoryEntries()).Returns(10);
        mock.Setup(s => s.LoadMaxHistorySizeBytes()).Returns(100L * 1024 * 1024);
        mock.Setup(s => s.LoadRunOnStartup()).Returns(false);
        mock.Setup(s => s.LoadPurgeHistoryOnStartup()).Returns(false);
        mock.Setup(s => s.LoadDisabledHistoryTypes()).Returns(new HashSet<ContentType>());
        mock.Setup(s => s.LoadLogLevel()).Returns(AppLogLevel.Off);
        return mock;
    }

    private static Mock<IAppLogger> CreateLoggerMock()
    {
        var mock = new Mock<IAppLogger>();
        mock.Setup(l => l.Level).Returns(AppLogLevel.Off);
        return mock;
    }

    [Fact]
    public void Initialize_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();

        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();

        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        service.Initialize();
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void UpdateIcon_BeforeInitialize_DoesNotThrow()
    {
        var settingsMock = new Mock<ISettingsService>();
        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        var ex = Record.Exception(() => service.UpdateIcon(true));
        Assert.Null(ex);
    }

    [Fact]
    public void UpdateIcon_AfterInitialize_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();

        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        service.Initialize();

        var ex = Record.Exception(() => service.UpdateIcon(true));
        Assert.Null(ex);

        ex = Record.Exception(() => service.UpdateIcon(false));
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();

        var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        service.Initialize();

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();

        var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        service.Initialize();
        service.Dispose();

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_AfterDispose_ThrowsObjectDisposedException()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadLogLevel()).Returns(AppLogLevel.Off);
        var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.Initialize());
    }

    [Fact]
    public void ITrayIconService_ImplementedByTrayIconService()
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadLogLevel()).Returns(AppLogLevel.Off);
        ITrayIconService service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        Assert.NotNull(service);
        service.Dispose();
    }

    [Fact]
    public void Events_AreSubscribable()
    {
        var settingsMock = new Mock<ISettingsService>();
        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);

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

    [Fact]
    public void Initialize_WithDisabledHistoryTypes_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();
        settingsMock.Setup(s => s.LoadDisabledHistoryTypes())
            .Returns(new HashSet<ContentType> { ContentType.Text, ContentType.Image });

        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_WithRunOnStartupEnabled_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();
        settingsMock.Setup(s => s.LoadRunOnStartup()).Returns(true);

        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_WithPurgeHistoryEnabled_DoesNotThrow()
    {
        var settingsMock = CreateFullSettingsMock();
        settingsMock.Setup(s => s.LoadPurgeHistoryOnStartup()).Returns(true);

        using var service = new TrayIconService(settingsMock.Object, CreateLoggerMock().Object);
        var ex = Record.Exception(() => service.Initialize());
        Assert.Null(ex);
    }

    [Fact]
    public void LogLevelMenuClick_SavesSettingAndUpdatesLogger()
    {
        var settingsMock = CreateFullSettingsMock();
        var loggerMock = CreateLoggerMock();
        using var service = new TrayIconService(settingsMock.Object, loggerMock.Object);

        var buildMenu = typeof(TrayIconService).GetMethod(
            "BuildLoggingSubmenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(buildMenu);

        var submenu = Assert.IsType<WinForms.ToolStripMenuItem>(buildMenu!.Invoke(service, null));
        var debugItem = submenu.DropDownItems
            .OfType<WinForms.ToolStripMenuItem>()
            .Single(item => item.Tag is AppLogLevel.Debug);

        debugItem.PerformClick();

        settingsMock.Verify(s => s.SaveLogLevel(AppLogLevel.Debug), Times.Once);
        loggerMock.Verify(l => l.SetLevel(AppLogLevel.Debug), Times.Once);
        Assert.True(debugItem.Checked);
    }
}
