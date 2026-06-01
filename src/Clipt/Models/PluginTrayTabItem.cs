namespace Clipt.Models;

public sealed class PluginTrayTabItem
{
    public required string PluginId { get; init; }

    public required string Header { get; init; }

    public required object Content { get; init; }
}
