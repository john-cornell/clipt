using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public sealed class PluginRegistry : IPluginRegistry
{
    private readonly IAppLogger _logger;
    private readonly List<PluginRegistrationInfo> _registrations = [];
    private readonly List<PluginLoadFailureInfo> _loadFailures = [];
    private readonly HashSet<string> _registeredIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICliptPluginLifetime> _lifetimePlugins = [];
    private readonly List<ICliptClipboardFilterPlugin> _filterPlugins = [];
    private readonly List<ICliptTrayTabPlugin> _trayTabPlugins = [];
    private ICliptPluginHost? _pluginHost;
    private ICliptOwnerBlockCoordinator? _ownerBlockCoordinator;
    private bool _initialized;

    public PluginRegistry(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<PluginRegistrationInfo> Registrations => _registrations;

    public IReadOnlyList<PluginLoadFailureInfo> LoadFailures => _loadFailures;

    public IReadOnlyList<ICliptClipboardFilterPlugin> FilterPlugins => _filterPlugins;

    public IReadOnlyList<ICliptTrayTabPlugin> TrayTabPlugins => _trayTabPlugins;

    public ICliptOwnerBlockCoordinator? OwnerBlockCoordinator => _ownerBlockCoordinator;

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
    }

    private void ClearCapabilityIndexes()
    {
        _filterPlugins.Clear();
        _trayTabPlugins.Clear();
        _ownerBlockCoordinator = null;
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
            _lifetimePlugins.Add(lifetime);
            if (_pluginHost is not null)
            {
                try
                {
                    lifetime.Initialize(_pluginHost.CreateHostScope(plugin.Id));
                }
                catch (Exception ex)
                {
                    _loadFailures.Add(new PluginLoadFailureInfo
                    {
                        AssemblyPath = source,
                        ErrorMessage = $"Plugin '{plugin.Id}' Initialize failed: {ex.Message}",
                    });
                }
            }
        }

        _registrations.Add(new PluginRegistrationInfo
        {
            Plugin = plugin,
            Source = source,
            IsRegistered = true,
        });
    }

    private void IndexCapabilities(ICliptPlugin plugin)
    {
        if (plugin is ICliptClipboardFilterPlugin filter)
            _filterPlugins.Add(filter);

        if (plugin is ICliptTrayTabPlugin trayTab)
            _trayTabPlugins.Add(trayTab);

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
            }
        }
    }
}
