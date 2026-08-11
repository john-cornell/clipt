namespace Clipt.Plugins.OwnerBlocker;

public interface IOwnerBlockerSettingsStore
{
    /// <summary>All blocked process entries, including temporarily-disabled ones.</summary>
    IReadOnlyList<BlockedOwnerEntry> BlockedProcesses { get; }

    /// <summary>All blocked window-class-prefix entries, including temporarily-disabled ones.</summary>
    IReadOnlyList<BlockedOwnerEntry> BlockedClassPrefixes { get; }

    void BlockSnapshotSource(string? processName, string? windowClass);

    void UnblockProcess(string processName);

    void UnblockWindowClass(string classPrefix);

    /// <summary>Enables/disables an existing process entry without removing it. No-op if the entry doesn't exist.</summary>
    void SetProcessEnabled(string processName, bool enabled);

    /// <summary>Enables/disables an existing window-class entry without removing it. No-op if the entry doesn't exist.</summary>
    void SetWindowClassEnabled(string classPrefix, bool enabled);

    void ClearAll();

    bool ShowHistoryBlockButton { get; set; }
}
