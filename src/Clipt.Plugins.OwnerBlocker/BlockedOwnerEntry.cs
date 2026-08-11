namespace Clipt.Plugins.OwnerBlocker;

/// <summary>A blocked process name or window class prefix that can be temporarily disabled without being removed.</summary>
public sealed class BlockedOwnerEntry
{
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
}
