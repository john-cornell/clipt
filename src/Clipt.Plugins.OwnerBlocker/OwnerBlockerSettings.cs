namespace Clipt.Plugins.OwnerBlocker;

public sealed class OwnerBlockerSettings
{
    public List<string> BlockedProcesses { get; set; } = [];

    public List<string> BlockedClassPrefixes { get; set; } = [];

    /// <summary>When false, history rows hide Block / blocked owner chrome.</summary>
    public bool ShowHistoryBlockButton { get; set; } = true;
}
