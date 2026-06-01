namespace Clipt.Services;

public readonly record struct CliptPluginFilterResult(bool Allow, string? PluginId = null, string? Reason = null)
{
    public static CliptPluginFilterResult Allowed => new(true);
}
