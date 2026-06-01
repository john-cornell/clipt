using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public interface IPluginRegistry
{
    IReadOnlyList<PluginRegistrationInfo> Registrations { get; }

    IReadOnlyList<PluginLoadFailureInfo> LoadFailures { get; }

    IReadOnlyList<ICliptClipboardFilterPlugin> FilterPlugins { get; }

    IReadOnlyList<ICliptTrayTabPlugin> TrayTabPlugins { get; }

    ICliptOwnerBlockCoordinator? OwnerBlockCoordinator { get; }

    event EventHandler? RescanCompleted;

    void SetHost(ICliptPluginHost host);

    void Initialize();

    void Rescan();
}
