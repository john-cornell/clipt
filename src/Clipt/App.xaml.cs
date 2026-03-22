using System.IO;
using System.Threading;
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
    private HistoryTabViewModel? _historyTabViewModel;
    private MainWindow? _mainWindow;
    private ClipboardListenerService? _listenerService;
    private IClipboardService? _clipboardService;
    private IClipboardHistoryService? _historyService;
    private ISettingsService? _settingsService;
    private IAppLogger? _appLogger;
    private int _clipboardTrayDispatchOrdinal;

    protected override async void OnStartup(StartupEventArgs e)
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
        _historyService = _serviceProvider.GetRequiredService<IClipboardHistoryService>();
        _historyTabViewModel = _serviceProvider.GetRequiredService<HistoryTabViewModel>();
        _appLogger = _serviceProvider.GetRequiredService<IAppLogger>();

        _trayPopupViewModel.HistoryTab = _historyTabViewModel;

        _listenerService.Start();

        InitializeTray();
        InitializeTrayPopup();

        _listenerService.ClipboardChanged += OnClipboardChangedForTray;

        try
        {
            await _historyService.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        _settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var settings = _settingsService;
        if (settings.LoadPurgeHistoryOnStartup())
        {
            try
            {
                await _historyService.ClearAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { }
        }

        Dispatcher.Invoke(() =>
        {
            PerformInitialTrayRefresh();

            var startupMode = settings.LoadStartupMode();

            if (startupMode == StartupMode.FullWindow)
                ShowMainWindow();
        });
    }

    private void InitializeTray()
    {
        _trayIconService = _serviceProvider!.GetRequiredService<ITrayIconService>();
        _trayIconService.Initialize();

        _trayIconService.SetClearClipboardPreferenceSync(next =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_historyTabViewModel is not null)
                    _historyTabViewModel.AlsoClearClipboardOnClearHistory = next;
            });
        });

        _trayIconService.TrayIconClicked += OnTrayIconClicked;
        _trayIconService.OpenFullRequested += OnOpenFullRequested;
        _trayIconService.ExitRequested += OnExitRequested;
        _trayIconService.ClearHistoryRequested += OnClearHistoryRequested;
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

        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                int dispatch = Interlocked.Increment(ref _clipboardTrayDispatchOrdinal);
                if (_appLogger?.Level >= AppLogLevel.Debug)
                {
                    _appLogger.Debug(
                        $"Tray clipboard handler dispatch#{dispatch} thread={Environment.CurrentManagedThreadId}");
                }

                var snapshot = _clipboardService!.CaptureSnapshot(_listenerService!.Hwnd);
                if (_appLogger?.Level >= AppLogLevel.Debug)
                {
                    _appLogger.Debug($"Tray capture: {ClipboardHistoryService.DescribeSnapshotDebug(snapshot)}");
                }

                bool hasData = snapshot.Formats.Length > 0;
                _trayIconService?.UpdateIcon(hasData);
                _trayPopupViewModel?.Update(snapshot);

                if (hasData)
                    await _historyService!.AddAsync(snapshot).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private async void OnTrayIconClicked(object? sender, EventArgs e)
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

        var snapshot = RefreshTrayPopup();
        if (snapshot is not null)
            await SyncClipboardToHistoryAsync(snapshot);

        _historyTabViewModel?.Refresh();
        _trayPopupWindow.ShowNearTray();
    }

    private void OnOpenFullRequested(object? sender, EventArgs e) => ShowMainWindow();

    private void OnExpandToFullRequested(object? sender, EventArgs e)
    {
        _trayPopupWindow?.Hide();
        ShowMainWindow();
    }

    private void OnExitRequested(object? sender, EventArgs e) => Shutdown();

    private async void OnClearHistoryRequested(object? sender, EventArgs e)
    {
        if (_historyService is null || _clipboardService is null || _listenerService is null || _settingsService is null)
            return;

        try
        {
            await Task.Delay(75).ConfigureAwait(false);

            if (_settingsService.LoadClearClipboardWhenClearingHistory())
            {
                await _historyService.ClearAsync().ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                    _clipboardService.ClearClipboard(_listenerService.Hwnd));
            }
            else
            {
                await Dispatcher.InvokeAsync(() =>
                    ClipboardHistoryService.ClearHistoryMatchingCurrentClipboardAsync(
                        _historyService, _clipboardService, _listenerService.Hwnd)).Task.Unwrap();
            }

            Dispatcher.Invoke(() => _historyTabViewModel?.Refresh());
        }
        catch (ObjectDisposedException) { }
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= _serviceProvider!.GetRequiredService<MainWindow>();

        _mainWindow.Show();
        _mainWindow.Activate();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
    }

    private ClipboardSnapshot? RefreshTrayPopup()
    {
        try
        {
            var snapshot = _clipboardService!.CaptureSnapshot(_listenerService!.Hwnd);
            _trayPopupViewModel?.Update(snapshot);
            _trayIconService?.UpdateIcon(snapshot.Formats.Length > 0);
            return snapshot;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task SyncClipboardToHistoryAsync(ClipboardSnapshot snapshot)
    {
        if (_historyService is null || snapshot.Formats.Length == 0)
            return;

        try
        {
            await _historyService.AddAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is ObjectDisposedException or IOException or UnauthorizedAccessException)
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
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<ClipboardListenerService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IClipboardHistoryService, ClipboardHistoryService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayPopupViewModel>();
        services.AddSingleton<HistoryTabViewModel>(sp => new HistoryTabViewModel(
            sp.GetRequiredService<IClipboardHistoryService>(),
            sp.GetRequiredService<IClipboardService>(),
            () => sp.GetRequiredService<ClipboardListenerService>().Hwnd,
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ITrayIconService>()));
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
            _trayIconService.ClearHistoryRequested -= OnClearHistoryRequested;
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
