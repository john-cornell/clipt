namespace Clipt.Plugins;

public interface ICliptOwnerBlockCoordinator
{
    Task BlockAsync(string? processName, string? windowClass);

    bool IsBlocked(CliptPluginClipboardSnapshot snapshot);

    IReadOnlySet<string> GetBlockedProcessNames();

    IReadOnlySet<string> GetBlockedWindowClassPrefixes();

    /// <summary>When false, history rows hide Block / blocked owner chrome.</summary>
    bool ShowHistoryBlockButton { get; }
}
