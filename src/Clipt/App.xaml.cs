using System.Windows;
using Clipt.Models;
using Clipt.Services;
using Clipt.ViewModels;
using Clipt.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Clipt;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ITrayIconService? _trayIconService;
    private TrayPopupWindow? _trayPopupWindow;
    private TrayPopupViewModel? _trayPopupViewModel;
    private MainWindow? _mainWindow;
    private ClipboardListenerService? _listenerService;
    private IClipboardService? _clipboardService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var themeService = _serviceProvider.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(themeService.LoadSavedTheme());

        _clipboardService = _serviceProvider.GetRequiredService<IClipboardService>();
        _listenerService = _serviceProvider.GetRequiredService<ClipboardListenerService>();
        _trayPopupViewModel = _serviceProvider.GetRequiredService<TrayPopupViewModel>();

        _listenerService.Start();

        InitializeTray();
        InitializeTrayPopup();

        _listenerService.ClipboardChanged += OnClipboardChangedForTray;

        PerformInitialTrayRefresh();

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var startupMode = settingsService.LoadStartupMode();

        if (startupMode == StartupMode.FullWindow)
            ShowMainWindow();
    }

    private void InitializeTray()
    {
        _trayIconService = _serviceProvider!.GetRequiredService<ITrayIconService>();
        _trayIconService.Initialize();

        _trayIconService.TrayIconClicked += OnTrayIconClicked;
        _trayIconService.OpenFullRequested += OnOpenFullRequested;
        _trayIconService.ExitRequested += OnExitRequested;
    }

    private void InitializeTrayPopup()
    {
        _trayPopupWindow = _serviceProvider!.GetRequiredService<TrayPopupWindow>();
        _trayPopupViewModel!.ExpandToFullRequested += OnExpandToFullRequested;
    }

    private void OnClipboardChangedForTray(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var snapshot = _clipboardService!.CaptureSnapshot(_listenerService!.Hwnd);
                bool hasData = snapshot.Formats.Length > 0;
                _trayIconService?.UpdateIcon(hasData);
                _trayPopupViewModel?.Update(snapshot);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        if (_trayPopupWindow is null)
            return;

        if (_trayPopupWindow.IsVisible)
        {
            _trayPopupWindow.Hide();
            return;
        }

        if (_trayPopupWindow.WasRecentlyHidden)
            return;

        RefreshTrayPopup();
        _trayPopupWindow.ShowNearTray();
    }

    private void OnOpenFullRequested(object? sender, EventArgs e) => ShowMainWindow();

    private void OnExpandToFullRequested(object? sender, EventArgs e)
    {
        _trayPopupWindow?.Hide();
        ShowMainWindow();
    }

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    private void ShowMainWindow()
    {
        _mainWindow ??= _serviceProvider!.GetRequiredService<MainWindow>();

        _mainWindow.Show();
        _mainWindow.Activate();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
    }

    private void RefreshTrayPopup()
    {
        try
        {
            var snapshot = _clipboardService!.CaptureSnapshot(_listenerService!.Hwnd);
            _trayPopupViewModel?.Update(snapshot);
            _trayIconService?.UpdateIcon(snapshot.Formats.Length > 0);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void PerformInitialTrayRefresh()
    {
        try
        {
            var snapshot = _clipboardService!.CaptureSnapshot(_listenerService!.Hwnd);
            bool hasData = snapshot.Formats.Length > 0;
            _trayIconService?.UpdateIcon(hasData);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ClipboardListenerService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayPopupViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<TrayPopupWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIconService is not null)
        {
            _trayIconService.TrayIconClicked -= OnTrayIconClicked;
            _trayIconService.OpenFullRequested -= OnOpenFullRequested;
            _trayIconService.ExitRequested -= OnExitRequested;
        }

        if (_trayPopupViewModel is not null)
            _trayPopupViewModel.ExpandToFullRequested -= OnExpandToFullRequested;

        if (_listenerService is not null)
            _listenerService.ClipboardChanged -= OnClipboardChangedForTray;

        _mainWindow?.ForceClose();
        _trayPopupWindow?.Close();

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        base.OnExit(e);
    }
}
