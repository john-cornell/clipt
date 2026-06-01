namespace Clipt.Plugins;

public sealed class CliptPluginFilterVerdict
{
    public bool Allow { get; init; }

    public string? Reason { get; init; }

    public static CliptPluginFilterVerdict AllowSnapshot => new() { Allow = true };

    public static CliptPluginFilterVerdict BlockSnapshot(string reason) =>
        new() { Allow = false, Reason = reason };
}
