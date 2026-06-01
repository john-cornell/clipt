namespace Clipt.Plugins;

public sealed class CliptPluginOption
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public CliptPluginOptionKind Kind { get; init; } = CliptPluginOptionKind.Checkbox;

    public bool DefaultValue { get; init; }
}
