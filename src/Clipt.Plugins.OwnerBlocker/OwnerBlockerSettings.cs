namespace Clipt.Plugins.OwnerBlocker;

public sealed class OwnerBlockerSettings
{
    public List<string> BlockedProcesses { get; set; } = [];

    public List<string> BlockedClassPrefixes { get; set; } = [];
}
