namespace Clipt.Services;

public enum ClipboardFormatOversizeReply
{
    /// <summary>Read only up to the configured cap (first N bytes).</summary>
    TruncateToCap = 0,

    /// <summary>Read up to the hard per-format maximum.</summary>
    CaptureFull = 1,
}
