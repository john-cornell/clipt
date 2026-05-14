namespace Clipt.Models;

/// <summary>
/// When a single clipboard format (other than raw DIB/DIBV5/TIFF) is larger than the configured per-format capture cap,
/// whether to prompt. Larger raw images use a separate capture path with its own ceiling.
/// Use the tray "Max capture per format" setting to raise the cap (including unlimited up to the hard max).
/// </summary>
public enum ClipboardFormatOversizeMode
{
    /// <summary>Read at most the configured cap; no prompt.</summary>
    TruncateToCap = 0,

    /// <summary>Prompt when a format exceeds the cap.</summary>
    AskEachFormat = 1,
}
