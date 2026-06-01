namespace Clipt.Models;

public enum HistoryAddResult
{
    Added,
    SkippedEmptyFormats,
    SkippedSuppressed,
    SkippedDuplicate,
    SkippedDisabledContentType,
    SkippedBlockedProcess,
    SkippedUserOverflowPrompt,
}
