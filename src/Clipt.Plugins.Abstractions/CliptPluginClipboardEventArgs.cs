namespace Clipt.Plugins;

public sealed class CliptPluginClipboardEventArgs : EventArgs
{
    public required CliptPluginClipboardSnapshot Snapshot { get; init; }

    public required CliptPluginHistoryAddOutcome AddOutcome { get; init; }
}
