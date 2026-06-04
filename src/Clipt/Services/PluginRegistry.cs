using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public sealed class PluginRegistry : IPluginRegistry
{
    private readonly IAppLogger _logger;
    private readonly ISettingsService? _settingsService;
    private readonly List<PluginRegistrationInfo> _registrations = [];
    private readonly List<PluginLoadFailureInfo> _loadFailures = [];
    private readonly HashSet<string> _registeredIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICliptPluginLifetime> _lifetimePlugins = [];
    private readonly List<ICliptClipboardFilterPlugin> _filterPlugins = [];
    private readonly List<ICliptTrayTabPlugin> _trayTabPlugins = [];
    private readonly List<ICliptHistoryActionPlugin> _historyActionPlugins = [];
    private ICliptPluginHost? _pluginHost;
    private ICliptOwnerBlockCoordinator? _ownerBlockCoordinator;
    private string? _ownerBlockCoordinatorId;
    private bool _initialized;

    public PluginRegistry(IAppLogger logger, ISettingsService? settingsService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService;
    }

    public IReadOnlyList<PluginRegistrationInfo> Registrations => _registrations;

    public IReadOnlyList<PluginLoadFailureInfo> LoadFailures => _loadFailures;

    public IReadOnlyList<ICliptClipboardFilterPlugin> FilterPlugins =>
        _disabledPluginIds.Count == 0 ? _filterPlugins
        : _filterPlugins.Where(p => !_disabledPluginIds.Contains(p.Id)).ToList();

    public IReadOnlyList<ICliptTrayTabPlugin> TrayTabPlugins =>
        _disabledPluginIds.Count == 0 ? _trayTabPlugins
        : _trayTabPlugins.Where(p => !_disabledPluginIds.Contains(p.Id)).ToList();

    public IReadOnlyList<ICliptHistoryActionPlugin> HistoryActionPlugins =>
        _disabledPluginIds.Count == 0 ? _historyActionPlugins
        : _historyActionPlugins.Where(p => !_disabledPluginIds.Contains(p.Id)).ToList();

    public ICliptOwnerBlockCoordinator? OwnerBlockCoordinator =>
        _ownerBlockCoordinator is not null
        && (_ownerBlockCoordinatorId is null || !_disabledPluginIds.Contains(_ownerBlockCoordinatorId))
            ? _ownerBlockCoordinator
            : null;

    public bool IsPluginEnabled(string pluginId) => !_disabledPluginIds.Contains(pluginId);

    public void SetPluginEnabled(string pluginId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return;
        _settingsService?.SavePluginEnabled(pluginId, enabled);
        if (enabled)
            _disabledPluginIds.Remove(pluginId);
        else
            _disabledPluginIds.Add(pluginId);
        RescanCompleted?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RescanCompleted;

    public void SetHost(ICliptPluginHost host)
    {
        _pluginHost = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        Rescan();
    }

    public void Rescan()
    {
        ShutdownLifetimePlugins();
        ClearCapabilityIndexes();

        _registrations.Clear();
        _loadFailures.Clear();
        _registeredIds.Clear();

        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (Directory.Exists(pluginsDir))
        {
            foreach (string dllPath in Directory.EnumerateFiles(pluginsDir, "*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                RegisterFromAssemblyPath(dllPath);
        }

        _trayTabPlugins.Sort(static (a, b) => a.TabOrder.CompareTo(b.TabOrder));

        _disabledPluginIds.Clear();
        if (_settingsService is not null)
        {
            foreach (PluginRegistrationInfo r in _registrations)
            {
                if (!_settingsService.LoadPluginEnabled(r.Plugin.Id))
                    _disabledPluginIds.Add(r.Plugin.Id);
            }
        }

        RescanCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ClearCapabilityIndexes()
    {
        _filterPlugins.Clear();
        _trayTabPlugins.Clear();
        _historyActionPlugins.Clear();
        _ownerBlockCoordinator = null;
        _ownerBlockCoordinatorId = null;
    }

    private void ShutdownLifetimePlugins()
    {
        foreach (ICliptPluginLifetime lifetime in _lifetimePlugins)
        {
            try
            {
                lifetime.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Warn($"Plugin '{lifetime.Id}' Shutdown failed: {ex.Message}");
            }
        }

        _lifetimePlugins.Clear();
    }

    private void RegisterFromAssemblyPath(string assemblyPath)
    {
        try
        {
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            RegisterFromAssembly(assembly, assemblyPath);
        }
        catch (Exception ex)
        {
            _loadFailures.Add(new PluginLoadFailureInfo
            {
                AssemblyPath = assemblyPath,
                ErrorMessage = ex.Message,
            });
        }
    }

    private void RegisterFromAssembly(Assembly assembly, string source)
    {
        IEnumerable<Type> pluginTypes;
        try
        {
            pluginTypes = assembly.GetExportedTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false }
                            && typeof(ICliptPlugin).IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException ex)
        {
            _loadFailures.Add(new PluginLoadFailureInfo
            {
                AssemblyPath = source,
                ErrorMessage = string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message).Where(m => m is not null)),
            });
            return;
        }

        foreach (Type type in pluginTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is not ICliptPlugin plugin)
                    continue;

                RegisterPlugin(plugin, source);
            }
            catch (Exception ex)
            {
                _loadFailures.Add(new PluginLoadFailureInfo
                {
                    AssemblyPath = $"{source} ({type.FullName})",
                    ErrorMessage = ex.Message,
                });
            }
        }
    }

    private void RegisterPlugin(ICliptPlugin plugin, string source)
    {
        if (!_registeredIds.Add(plugin.Id))
        {
            _loadFailures.Add(new PluginLoadFailureInfo
            {
                AssemblyPath = source,
                ErrorMessage = $"Duplicate plugin id '{plugin.Id}' was skipped.",
            });
            return;
        }

        IndexCapabilities(plugin);

        if (plugin is ICliptPluginLifetime lifetime)
        {
            if (_pluginHost is not null)
            {
                try
                {
                    lifetime.Initialize(_pluginHost.CreateHostScope(plugin.Id));
                    _lifetimePlugins.Add(lifetime);
                }
                catch (Exception ex)
                {
                    RemoveCapabilities(plugin);
                    _registeredIds.Remove(plugin.Id);
                    _loadFailures.Add(new PluginLoadFailureInfo
                    {
                        AssemblyPath = source,
                        ErrorMessage = $"Plugin '{plugin.Id}' Initialize failed: {ex.Message}",
                    });
                    return;
                }
            }
            else
            {
                _lifetimePlugins.Add(lifetime);
            }
        }

        _registrations.Add(new PluginRegistrationInfo
        {
            Plugin = plugin,
            Source = source,
            IsRegistered = true,
        });
    }

    private void RemoveCapabilities(ICliptPlugin plugin)
    {
        if (plugin is ICliptClipboardFilterPlugin filter)
            _filterPlugins.Remove(filter);

        if (plugin is ICliptTrayTabPlugin trayTab)
            _trayTabPlugins.Remove(trayTab);

        if (plugin is ICliptHistoryActionPlugin historyAction)
            _historyActionPlugins.Remove(historyAction);

        if (plugin is ICliptOwnerBlockCoordinator coordinator && ReferenceEquals(_ownerBlockCoordinator, coordinator))
        {
            _ownerBlockCoordinator = null;
            _ownerBlockCoordinatorId = null;
        }
    }

    private void IndexCapabilities(ICliptPlugin plugin)
    {
        if (plugin is ICliptClipboardFilterPlugin filter)
            _filterPlugins.Add(filter);

        if (plugin is ICliptTrayTabPlugin trayTab)
            _trayTabPlugins.Add(trayTab);

        if (plugin is ICliptHistoryActionPlugin historyAction)
            _historyActionPlugins.Add(historyAction);

        if (plugin is ICliptOwnerBlockCoordinator coordinator)
        {
            if (_ownerBlockCoordinator is not null)
            {
                _logger.Warn(
                    $"Multiple owner block coordinators found; keeping '{_ownerBlockCoordinator.GetType().FullName}', ignoring '{coordinator.GetType().FullName}'.");
            }
            else
            {
                _ownerBlockCoordinator = coordinator;
                _ownerBlockCoordinatorId = plugin.Id;
            }
        }
    }
}
