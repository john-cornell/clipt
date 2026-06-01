namespace Clipt.Plugins.OwnerBlocker;

public interface IOwnerBlockerSettingsStore
{
    IReadOnlySet<string> BlockedProcesses { get; }

    IReadOnlySet<string> BlockedClassPrefixes { get; }

    void BlockSnapshotSource(string? processName, string? windowClass);

    void UnblockProcess(string processName);

    void UnblockWindowClass(string classPrefix);

    void ClearAll();
}
