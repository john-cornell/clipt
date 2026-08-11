using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
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
    private HwndSource? _mainWindowSource;
    private bool _mainWindowHooksWired;
    private bool _taskbarShellRegistered;
    private bool _restoringPrimaryUi;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _startupLogger = new AppLogger(new SettingsService());
        LogInfo($"Clipt {Views.MainWindow.GetAppVersion()} starting (pid={Environment.ProcessId})");
        RegisterStartupExceptionHandlers();

        if (!SingleInstanceActivation.TryAcquireMutex(out _singleInstanceMutex, out _ownsSingleInstanceMutex))
        {
            // TryAcquireMutex returns false only on hard OS errors (UnauthorizedAccess,
            // WaitHandleCannotBeOpened). "Another instance holds it" returns true with
            // ownsMutex=false, handled in the block below.
            LogInfo("Could not open single-instance mutex; exiting.");
            Shutdown();
            return;
        }

        if (!_ownsSingleInstanceMutex)
        {
            LogInfo("Another Clipt instance is already running; notifying it and exiting.");
            if (!SingleInstanceActivation.TryNotifyRunningInstance(new SettingsService().LoadStartupMode()))
                LogInfo("Could not deliver activation message to the running instance.");
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
                PrepareTaskbarActivationShell();

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
            _trayIconService?.RebuildShowTabsMenu(_trayPopupViewModel.GetShowTabMenuEntries());
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

        _trayIconService.SetShowTabsMenuToggleHandler((pluginId, visible) =>
        {
            Dispatcher.InvokeAsync(() =>
                _trayPopupViewModel?.SetOptionalTrayTabVisibleFromTray(pluginId, visible));
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

                ClipboardSnapshot snapshot = await CaptureSnapshotWithRetryAsync().ConfigureAwait(true);
                if (_appLogger?.Level >= AppLogLevel.Debug)
                {
                    _appLogger.Debug($"Tray capture: {ClipboardHistoryService.DescribeSnapshotDebug(snapshot)}");
                }

                bool hasData = snapshot.Formats.Length > 0;
                _trayIconService?.UpdateIcon(hasData);
                _trayPopupViewModel?.Update(snapshot);

                HistoryAddResult addResult = hasData
                    ? await _historyService!.AddAsync(snapshot).ConfigureAwait(true)
                    : HistoryAddResult.SkippedEmptyFormats;
                PublishClipboardEventOnUiThread(snapshot, addResult);
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

        // WasRecentlyHidden debounce: clicking the tray icon while the popup is visible
        // deactivates it (OnDeactivated fires, hiding it) BEFORE this click handler runs.
        // Without the debounce, we'd immediately re-open the popup the user tried to close.
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
        BringTrayPopupToForeground();
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

        // BringTrayPopupToForeground() is intentionally NOT called here —
        // ShowTrayPopupWithClipboardSyncAsync is async and returns before the popup
        // is visible, so BringTrayPopupToForeground would no-op (IsVisible=false).
        // The method calls it internally after ShowNearTray.
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
        BringTrayPopupToForeground();
    }

    private void BringTrayPopupToForeground()
    {
        if (_trayPopupWindow is null || !_trayPopupWindow.IsVisible)
            return;

        _trayPopupWindow.Topmost = true;
        _trayPopupWindow.Activate();
        _trayPopupWindow.Topmost = true;
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
        EnsureMainWindowCreated();
        _trayPopupWindow?.Hide();

        _mainWindow!.Show();
        _mainWindow.Activate();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
    }

    private void EnsureMainWindowCreated()
    {
        if (_mainWindow is not null)
            return;

        _mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
        Current.MainWindow = _mainWindow;
        EnsureMainWindowHooksWired();
    }

    private void EnsureMainWindowHooksWired()
    {
        if (_mainWindow is null || _mainWindowHooksWired)
            return;

        _mainWindowHooksWired = true;
        _mainWindow.Activated += OnMainWindowActivated;
        _mainWindow.StateChanged += OnMainWindowStateChanged;
        _mainWindow.SourceInitialized += OnMainWindowSourceInitialized;
    }

    private void PrepareTaskbarActivationShell()
    {
        EnsureMainWindowCreated();
        if (_mainWindow is null || _taskbarShellRegistered)
            return;

        _taskbarShellRegistered = true;
        Current.MainWindow = _mainWindow;

        if (_mainWindow.IsVisible)
            return;

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.ShowActivated = false;
        _mainWindow.WindowState = WindowState.Minimized;
        _mainWindow.Show();
        _mainWindow.Hide();
    }

    private void OnMainWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (_mainWindow is null)
            return;

        _mainWindowSource?.RemoveHook(MainWindowWndProc);
        _mainWindowSource = HwndSource.FromHwnd(new WindowInteropHelper(_mainWindow).Handle);
        _mainWindowSource?.AddHook(MainWindowWndProc);
    }

    private IntPtr MainWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_mainWindow is null || _mainWindow.IsVisible)
            return IntPtr.Zero;

        const int WM_ACTIVATE = 0x0006;
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_RESTORE = 0xF120;

        if (msg == WM_ACTIVATE)
        {
            int active = (int)(wParam.ToInt64() & 0xFFFF);
            if (active != 0)
                BeginRestorePrimaryUiFromTaskbar();
        }
        else if (msg == WM_SYSCOMMAND && ((int)wParam.ToInt64() & 0xFFF0) == SC_RESTORE)
        {
            BeginRestorePrimaryUiFromTaskbar();
        }

        return IntPtr.Zero;
    }

    private void OnMainWindowActivated(object? sender, EventArgs e) =>
        RequestRestorePrimaryUiFromTaskbar();

    private void OnMainWindowStateChanged(object? sender, EventArgs e) =>
        RequestRestorePrimaryUiFromTaskbar();

    private void RequestRestorePrimaryUiFromTaskbar()
    {
        if (_mainWindow is null || _mainWindow.IsVisible)
            return;

        if (_mainWindow.WindowState == WindowState.Minimized)
            return;

        BeginRestorePrimaryUiFromTaskbar();
    }

    private void BeginRestorePrimaryUiFromTaskbar() =>
        Dispatcher.BeginInvoke(RestorePrimaryUiFromTaskbar, DispatcherPriority.Input);

    private void RestorePrimaryUiFromTaskbar()
    {
        if (_restoringPrimaryUi || _settingsService is null)
            return;

        try
        {
            _restoringPrimaryUi = true;

            if (_settingsService.LoadStartupMode() == StartupMode.FullWindow)
                ShowMainWindow();
            else
                ShowTrayPopupWithClipboardSyncAsync();
        }
        finally
        {
            _restoringPrimaryUi = false;
        }
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

    private void PublishClipboardEventOnUiThread(ClipboardSnapshot snapshot, HistoryAddResult addResult)
    {
        if (_pluginHost is null)
            return;

        void Publish() => _pluginHost.PublishClipboardEvent(snapshot, addResult);

        if (Dispatcher.CheckAccess())
            Publish();
        else
            Dispatcher.Invoke(Publish);
    }

    /// <summary>
    /// Retries capture when <see cref="NativeMethods.OpenClipboard"/> fails on the first
    /// WM_CLIPBOARDUPDATE — common with short-lived bridge processes (e.g. WSL clip.exe).
    /// </summary>
    private async Task<ClipboardSnapshot> CaptureSnapshotWithRetryAsync()
    {
        nint hwnd = _listenerService!.Hwnd;
        ClipboardSnapshot snapshot = _clipboardService!.CaptureSnapshot(hwnd);
        if (snapshot.Formats.Length > 0)
            return snapshot;

        int[] retryDelaysMs = [50, 100, 200];
        foreach (int delayMs in retryDelaysMs)
        {
            await Task.Delay(delayMs).ConfigureAwait(true);
            snapshot = _clipboardService.CaptureSnapshot(hwnd);
            if (snapshot.Formats.Length > 0)
                return snapshot;
        }

        return snapshot;
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
            HistoryAddResult addResult = await _historyService.AddAsync(snapshot).ConfigureAwait(true);
            PublishClipboardEventOnUiThread(snapshot, addResult);
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
            new Lazy<IClipboardHistoryService>(() => sp.GetRequiredService<IClipboardHistoryService>()),
            new Lazy<IClipboardGroupService>(() => sp.GetRequiredService<IClipboardGroupService>())));
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
            sp.GetRequiredService<ISettingsService>(),
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

        if (_mainWindow is not null)
        {
            _mainWindow.Activated -= OnMainWindowActivated;
            _mainWindow.StateChanged -= OnMainWindowStateChanged;
            _mainWindow.SourceInitialized -= OnMainWindowSourceInitialized;
        }

        if (_mainWindowSource is not null)
        {
            _mainWindowSource.RemoveHook(MainWindowWndProc);
            _mainWindowSource = null;
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
