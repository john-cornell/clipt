namespace Clipt.Plugins;

public sealed class CliptPluginClipboardSnapshot
{
    public required DateTime TimestampUtc { get; init; }

    public required uint SequenceNumber { get; init; }

    public required string OwnerProcessName { get; init; }

    public required int OwnerProcessId { get; init; }

    public nint OwnerWindowHandle { get; init; }

    public required string OwnerWindowTitle { get; init; }

    public required string OwnerWindowClass { get; init; }

    public required IReadOnlyList<CliptPluginFormatInfo> Formats { get; init; }
}
