namespace Clipt.Plugins;

public interface ICliptOwnerBlockCoordinator
{
    Task BlockAsync(string? processName, string? windowClass);

    bool IsBlocked(CliptPluginClipboardSnapshot snapshot);

    IReadOnlySet<string> GetBlockedProcessNames();

    IReadOnlySet<string> GetBlockedWindowClassPrefixes();

    /// <summary>When false, history rows hide Block / blocked owner chrome.</summary>
    bool ShowHistoryBlockButton { get; }

    /// <summary>Whether the owner process name can be added to the block list (excludes placeholders).</summary>
    bool IsBlockableOwnerProcess(string? processName);
}
