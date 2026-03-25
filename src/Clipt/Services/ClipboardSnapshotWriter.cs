using System.Text;
using Clipt.Models;
using Clipt.Native;

namespace Clipt.Services;

/// <summary>
/// Writes history snapshots to the system clipboard with history capture suppressed during the write.
/// </summary>
public static class ClipboardSnapshotWriter
{
    public static void WriteSnapshotToClipboard(IClipboardService clipboardService, ClipboardSnapshot snapshot, nint hwnd)
    {
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(snapshot);

        var textFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);

        if (textFormat is not null && textFormat.RawData.Length > 0)
        {
            clipboardService.SetClipboardText(
                Encoding.Unicode.GetString(textFormat.RawData).TrimEnd('\0'),
                hwnd);
            return;
        }

        var restorableFormats = snapshot.Formats
            .Where(f => f.RawData.Length > 0 && !ClipboardConstants.IsGdiHandleFormat(f.FormatId))
            .Select(f => (f.FormatId, f.RawData))
            .ToList();

        if (restorableFormats.Count > 0)
            clipboardService.SetMultipleClipboardData(restorableFormats, hwnd);
    }

    public static async Task RestoreEntryToClipboardAsync(
        IClipboardHistoryService historyService,
        IClipboardService clipboardService,
        nint hwnd,
        string entryId)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentException.ThrowIfNullOrEmpty(entryId);

        ClipboardSnapshot? snapshot = await historyService.RestoreAsync(entryId).ConfigureAwait(false);
        if (snapshot is null)
            return;

        historyService.IsSuppressed = true;
        try
        {
            WriteSnapshotToClipboard(clipboardService, snapshot, hwnd);
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
            historyService.IsSuppressed = false;
        }
    }
}
