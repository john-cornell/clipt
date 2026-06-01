namespace Clipt.Plugins;

public enum CliptPluginHistoryAddOutcome
{
    Added,
    SkippedEmptyFormats,
    SkippedSuppressed,
    SkippedDuplicate,
    SkippedDisabledContentType,
    SkippedBlockedProcess,
    SkippedByPluginFilter,
    SkippedUserOverflowPrompt,
}
