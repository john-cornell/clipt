using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public interface IPluginRegistry
{
    IReadOnlyList<PluginRegistrationInfo> Registrations { get; }

    IReadOnlyList<PluginLoadFailureInfo> LoadFailures { get; }

    void Initialize();

    void Rescan();
}
