namespace Clipt.Plugins;

public interface ICliptOwnerBlockCoordinator
{
    Task BlockAsync(string? processName, string? windowClass);

    bool IsBlocked(CliptPluginClipboardSnapshot snapshot);

    IReadOnlySet<string> GetBlockedProcessNames();

    IReadOnlySet<string> GetBlockedWindowClassPrefixes();
}
