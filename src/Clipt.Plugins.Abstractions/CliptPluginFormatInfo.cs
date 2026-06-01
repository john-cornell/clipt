namespace Clipt.Plugins;

public sealed class CliptPluginFormatInfo
{
    public required uint FormatId { get; init; }

    public required string FormatName { get; init; }

    public required bool IsStandard { get; init; }

    public required long DataSize { get; init; }
}
