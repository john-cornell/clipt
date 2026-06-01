using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public sealed class PluginRegistry : IPluginRegistry
{
    private readonly List<PluginRegistrationInfo> _registrations = [];
    private readonly List<PluginLoadFailureInfo> _loadFailures = [];
    private readonly HashSet<string> _registeredIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public IReadOnlyList<PluginRegistrationInfo> Registrations => _registrations;

    public IReadOnlyList<PluginLoadFailureInfo> LoadFailures => _loadFailures;

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        Rescan();
    }

    public void Rescan()
    {
        _registrations.Clear();
        _loadFailures.Clear();
        _registeredIds.Clear();

        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (Directory.Exists(pluginsDir))
        {
            foreach (string dllPath in Directory.EnumerateFiles(pluginsDir, "*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                RegisterFromAssemblyPath(dllPath);
        }
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

        _registrations.Add(new PluginRegistrationInfo
        {
            Plugin = plugin,
            Source = source,
            IsRegistered = true,
        });
    }
}
