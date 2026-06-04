using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

public interface ICliptPluginHost
{
    bool HasOwnerBlockCoordinator { get; }

    bool ShowHistoryOwnerBlockButton { get; }

    IReadOnlyList<ICliptHistoryActionPlugin> HistoryActionPlugins { get; }

    event EventHandler? HistoryOwnerBlockUiChanged;

    CliptPluginFilterResult EvaluateFilters(ClipboardSnapshot snapshot);

    void PublishClipboardEvent(ClipboardSnapshot snapshot, HistoryAddResult addResult);

    Task BlockOwnerAsync(string? processName, string? windowClass);

    bool IsOwnerBlocked(string? processName, string? windowClass);

    ICliptHost CreateHostScope(string pluginId);
}
