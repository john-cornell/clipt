namespace Clipt.Plugins.OwnerBlocker;

public sealed class OwnerBlockerSettings
{
    public List<BlockedOwnerEntry> BlockedProcesses { get; set; } = [];

    public List<BlockedOwnerEntry> BlockedClassPrefixes { get; set; } = [];

    /// <summary>When false, history rows hide Block / blocked owner chrome.</summary>
    public bool ShowHistoryBlockButton { get; set; } = true;
}
