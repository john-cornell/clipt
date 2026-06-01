using System.Text.Json;
using Clipt.Models;
using Clipt.Plugins;

namespace Clipt.Services;

internal static class CliptPluginHostAdapters
{
    public static CliptPluginClipboardSnapshot ToPluginSnapshot(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var formats = new CliptPluginFormatInfo[snapshot.Formats.Length];
        for (int i = 0; i < snapshot.Formats.Length; i++)
        {
            ClipboardFormatInfo format = snapshot.Formats[i];
            formats[i] = new CliptPluginFormatInfo
            {
                FormatId = format.FormatId,
                FormatName = format.FormatName,
                IsStandard = format.IsStandard,
                DataSize = format.DataSize,
            };
        }

        return new CliptPluginClipboardSnapshot
        {
            TimestampUtc = snapshot.Timestamp,
            SequenceNumber = snapshot.SequenceNumber,
            OwnerProcessName = snapshot.OwnerProcessName,
            OwnerProcessId = snapshot.OwnerProcessId,
            OwnerWindowHandle = snapshot.OwnerWindowHandle,
            OwnerWindowTitle = snapshot.OwnerWindowTitle,
            OwnerWindowClass = snapshot.OwnerWindowClass,
            Formats = formats,
        };
    }

    public static CliptPluginHistoryAddOutcome ToPluginOutcome(HistoryAddResult result) =>
        result switch
        {
            HistoryAddResult.Added => CliptPluginHistoryAddOutcome.Added,
            HistoryAddResult.SkippedEmptyFormats => CliptPluginHistoryAddOutcome.SkippedEmptyFormats,
            HistoryAddResult.SkippedSuppressed => CliptPluginHistoryAddOutcome.SkippedSuppressed,
            HistoryAddResult.SkippedDuplicate => CliptPluginHistoryAddOutcome.SkippedDuplicate,
            HistoryAddResult.SkippedDisabledContentType => CliptPluginHistoryAddOutcome.SkippedDisabledContentType,
            HistoryAddResult.SkippedBlockedProcess => CliptPluginHistoryAddOutcome.SkippedBlockedProcess,
            HistoryAddResult.SkippedByPluginFilter => CliptPluginHistoryAddOutcome.SkippedByPluginFilter,
            HistoryAddResult.SkippedUserOverflowPrompt => CliptPluginHistoryAddOutcome.SkippedUserOverflowPrompt,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown history add result."),
        };
}
