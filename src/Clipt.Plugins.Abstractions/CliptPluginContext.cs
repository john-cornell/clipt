namespace Clipt.Plugins;

/// <summary>
/// Data and services available to tray action plugins at execution time.
/// </summary>
public sealed class CliptPluginContext
{
    public required string? ClipboardText { get; init; }

    public required IReadOnlyDictionary<string, bool> OptionValues { get; init; }
}
