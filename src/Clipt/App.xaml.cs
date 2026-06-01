using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
    private GroupsTabViewModel? _groupsTabViewModel;
    private PluginsTabViewModel? _pluginsTabViewModel;
    private MainWindow? _mainWindow;
    private ClipboardListenerService? _listenerService;
    private IClipboardService? _clipboardService;
    private IClipboardHistoryService? _historyService;
    private ISettingsService? _settingsService;
    private ICliptPluginHost? _pluginHost;
    private IAppLogger? _appLogger;
    private IAppLogger? _startupLogger;
    private int _clipboardTrayDispatchOrdinal;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _startupLogger = new AppLogger(new SettingsService());
        LogInfo($"Clipt {Views.MainWindow.GetAppVersion()} starting (pid={Environment.ProcessId})");
        RegisterStartupExceptionHandlers();

        if (!SingleInstanceActivation.TryAcquireMutex(out _singleInstanceMutex, out _ownsSingleInstanceMutex))
        {
            LogInfo("Could not open single-instance mutex; exiting.");
            Shutdown();
            return;
        }

        if (!_ownsSingleInstanceMutex)
        {
            LogInfo("Another Clipt instance is already running; notifying it and exiting.");
            SingleInstanceActivation.TryNotifyRunningInstance(new SettingsService().LoadStartupMode());
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        LogInfo("Single-instance mutex acquired.");

        try
        {
            CompleteStartupCore();

            try
            {
                await _historyService!.LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                LogError("History load failed.", ex);
            }

            try
            {
                var groupService = _serviceProvider!.GetRequiredService<IClipboardGroupService>();
                await groupService.LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                LogError("Groups load failed.", ex);
            }

            if (_settingsService!.LoadPurgeHistoryOnStartup())
            {
                try
                {
                    await _historyService!.ClearAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { }
            }

            var startupMode = _settingsService.LoadStartupMode();
            LogInfo($"Showing UI for startup mode: {startupMode}.");

            await Dispatcher.InvokeAsync(() =>
            {
                _groupsTabViewModel?.Refresh();
                PerformInitialTrayRefresh();

                if (startupMode == StartupMode.FullWindow)
                    ShowMainWindow();
                else
                    ShowTrayPopupWithClipboardSyncAsync();
            });

            LogInfo("Startup complete.");
        }
        catch (Exception ex)
        {
            LogError("Startup failed.", ex);
            throw;
        }
    }

    /// <summary>UI-thread startup: DI, tray, listener, plugins. Must not await.</summary>
    private void CompleteStartupCore()
    {
        LogInfo("Building services.");
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        LogInfo("Service provider built.");

        var themeService = _serviceProvider.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(themeService.LoadSavedTheme());

        _clipboardService = _serviceProvider.GetRequiredService<IClipboardService>();
        _listenerService = _serviceProvider.GetRequiredService<ClipboardListenerService>();
        _trayPopupViewModel = _serviceProvider.GetRequiredService<TrayPopupViewModel>();
        _pluginHost = _serviceProvider.GetRequiredService<ICliptPluginHost>();
        _historyService = _serviceProvider.GetRequiredService<IClipboardHistoryService>();
        _historyTabViewModel = _serviceProvider.GetRequiredService<HistoryTabViewModel>();
        _groupsTabViewModel = _serviceProvider.GetRequiredService<GroupsTabViewModel>();
        _pluginsTabViewModel = _serviceProvider.GetRequiredService<PluginsTabViewModel>();
        _appLogger = _serviceProvider.GetRequiredService<IAppLogger>();
        _settingsService = _serviceProvider.GetRequiredService<ISettingsService>();

        LogInfo("Services configured.");

        var pluginRegistry = _serviceProvider.GetRequiredService<PluginRegistry>();
        pluginRegistry.SetHost(_pluginHost);
        pluginRegistry.RescanCompleted += OnPluginRegistryRescanned;
        if (_pluginHost is CliptPluginHost concreteHost)
            OwnerBlockerSettingsMigrator.MigrateLegacyRegistrySettings(concreteHost);
        pluginRegistry.Initialize();
        LogInfo($"Plugins loaded: {pluginRegistry.Registrations.Count} registered, {pluginRegistry.LoadFailures.Count} failed.");

        _trayPopupViewModel.HistoryTab = _historyTabViewModel;
        _trayPopupViewModel.GroupsTab = _groupsTabViewModel;
        _trayPopupViewModel.PluginsTab = _pluginsTabViewModel;

        _listenerService.SecondInstanceActivateRequested += OnSecondInstanceActivateRequested;
        _listenerService.Start();
        LogInfo("Clipboard listener started.");

        LogInfo("Initializing tray icon.");
        InitializeTray();
        LogInfo("Initializing tray popup.");
        InitializeTrayPopup();

        try
        {
            _trayPopupViewModel.RefreshPluginTrayTabs(pluginRegistry, _pluginHost);
            LogInfo($"Plugin tray tabs: {_trayPopupViewModel.PluginTrayTabs.Count}.");
        }
        catch (Exception ex)
        {
            LogError("Failed to load plugin tray tabs; continuing without plugin tabs.", ex);
        }

        _pluginsTabViewModel.PluginOutputWritten += OnPluginOutputWritten;
        _pluginsTabViewModel.Refresh();

        _listenerService.ClipboardChanged += OnClipboardChangedForTray;
    }

    private void RegisterStartupExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogError("Unhandled UI exception.", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogError("Unhandled domain exception.", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    private void LogInfo(string message) => (_appLogger ?? _startupLogger)?.Info(message);

    private void LogError(string message, Exception? exception = null) =>
        (_appLogger ?? _startupLogger)?.Error(message, exception);

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

        _trayIconService.SetTrayTabVisibilitySync((showPlugins, showDebug) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _trayPopupViewModel?.SetTabVisibility(showPlugins, showDebug);
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

                HistoryAddResult addResult = hasData
                    ? await _historyService!.AddAsync(snapshot).ConfigureAwait(false)
                    : HistoryAddResult.SkippedEmptyFormats;
                _pluginHost?.PublishClipboardEvent(snapshot, addResult);
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
        _groupsTabViewModel?.Refresh();
        _pluginsTabViewModel?.Refresh();
        RefreshTrayPluginTabs();
        _trayPopupWindow.ShowNearTray();
    }

    private void OnSecondInstanceActivateRequested(object? sender, SecondInstanceActivateEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        var mode = SingleInstanceActivation.StartupModeFromWParam(e.ModeWParam);
        Dispatcher.BeginInvoke(() => ActivatePrimaryUiFromSecondLaunch(mode), DispatcherPriority.Normal);
    }

    private void ActivatePrimaryUiFromSecondLaunch(StartupMode mode)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        if (mode == StartupMode.FullWindow)
        {
            _trayPopupWindow?.Hide();
            ShowMainWindow();
            return;
        }

        ShowTrayPopupWithClipboardSyncAsync();
    }

    private async void ShowTrayPopupWithClipboardSyncAsync()
    {
        if (_trayPopupWindow is null)
            return;

        var snapshot = RefreshTrayPopup();
        if (snapshot is not null)
            await SyncClipboardToHistoryAsync(snapshot);

        _historyTabViewModel?.Refresh();
        _groupsTabViewModel?.Refresh();
        _pluginsTabViewModel?.Refresh();
        RefreshTrayPluginTabs();
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

    private void RefreshTrayPluginTabs()
    {
        if (_trayPopupViewModel is null || _pluginHost is null || _serviceProvider is null)
            return;

        try
        {
            _trayPopupViewModel.RefreshPluginTrayTabs(
                _serviceProvider.GetRequiredService<IPluginRegistry>(),
                _pluginHost);
        }
        catch (Exception ex)
        {
            LogError("Failed to refresh plugin tray tabs.", ex);
        }
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
            HistoryAddResult addResult = await _historyService.AddAsync(snapshot).ConfigureAwait(false);
            _pluginHost?.PublishClipboardEvent(snapshot, addResult);
        }
        catch (Exception ex) when (
            ex is ObjectDisposedException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private void OnPluginRegistryRescanned(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            RefreshTrayPluginTabs();
            _historyTabViewModel?.Refresh();
        });
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

    private void OnPluginOutputWritten(object? sender, EventArgs e)
    {
        if (_clipboardService is null || _listenerService is null)
            return;

        try
        {
            var snapshot = _clipboardService.CaptureSnapshot(_listenerService.Hwnd);
            _trayPopupViewModel?.Update(snapshot);
            _trayIconService?.UpdateIcon(snapshot.Formats.Length > 0);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IClipboardFormatOversizePrompt, WpfClipboardFormatOversizePrompt>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<ClipboardListenerService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IHistorySizeOverflowPrompt, WpfHistorySizeOverflowPrompt>();
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IPluginRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<CliptPluginHost>(sp => new CliptPluginHost(
            sp.GetRequiredService<PluginRegistry>(),
            new Lazy<IClipboardHistoryService>(() => sp.GetRequiredService<IClipboardHistoryService>())));
        services.AddSingleton<ICliptPluginHost>(sp => sp.GetRequiredService<CliptPluginHost>());
        services.AddSingleton<IClipboardHistoryService, ClipboardHistoryService>();
        services.AddSingleton<IClipboardGroupService, ClipboardGroupService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayPopupViewModel>(sp => new TrayPopupViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ITrayIconService>()));
        services.AddSingleton<GroupsTabViewModel>(sp => new GroupsTabViewModel(
            sp.GetRequiredService<IClipboardGroupService>(),
            sp.GetRequiredService<IClipboardHistoryService>(),
            sp.GetRequiredService<IClipboardService>(),
            () => sp.GetRequiredService<ClipboardListenerService>().Hwnd));
        services.AddSingleton<HistoryTabViewModel>(sp => new HistoryTabViewModel(
            sp.GetRequiredService<IClipboardHistoryService>(),
            sp.GetRequiredService<IClipboardService>(),
            () => sp.GetRequiredService<ClipboardListenerService>().Hwnd,
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ITrayIconService>(),
            sp.GetRequiredService<IClipboardGroupService>(),
            sp.GetRequiredService<ICliptPluginHost>()));
        services.AddSingleton<PluginsTabViewModel>(sp => new PluginsTabViewModel(
            sp.GetRequiredService<IPluginRegistry>(),
            sp.GetRequiredService<IClipboardService>(),
            () => sp.GetRequiredService<ClipboardListenerService>().Hwnd));
        services.AddSingleton<MainWindow>();
        services.AddSingleton<TrayPopupWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogInfo("Clipt exiting.");
        if (_listenerService is not null)
            _listenerService.SecondInstanceActivateRequested -= OnSecondInstanceActivateRequested;

        if (_singleInstanceMutex is not null)
        {
            if (_ownsSingleInstanceMutex)
                _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        if (_trayIconService is not null)
        {
            _trayIconService.TrayIconClicked -= OnTrayIconClicked;
            _trayIconService.OpenFullRequested -= OnOpenFullRequested;
            _trayIconService.ExitRequested -= OnExitRequested;
            _trayIconService.ClearHistoryRequested -= OnClearHistoryRequested;
        }

        if (_trayPopupViewModel is not null)
            _trayPopupViewModel.ExpandToFullRequested -= OnExpandToFullRequested;

        if (_pluginsTabViewModel is not null)
            _pluginsTabViewModel.PluginOutputWritten -= OnPluginOutputWritten;

        if (_serviceProvider?.GetService<PluginRegistry>() is PluginRegistry registry)
            registry.RescanCompleted -= OnPluginRegistryRescanned;

        if (_listenerService is not null)
            _listenerService.ClipboardChanged -= OnClipboardChangedForTray;

        _mainWindow?.ForceClose();
        _trayPopupWindow?.Close();

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        base.OnExit(e);
    }
}
