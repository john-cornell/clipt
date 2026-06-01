namespace Clipt.Models;

public sealed class TrayTabShowMenuEntry
{
    public required string Header { get; init; }

    /// <summary>Null for the built-in Plugins tab; otherwise the plugin id for a tray tab plugin.</summary>
    public string? PluginId { get; init; }

    public required bool IsVisible { get; init; }
}
