using System.Windows;
using Clipt.Models;

namespace Clipt.Services;

public sealed class WpfClipboardFormatOversizePrompt : IClipboardFormatOversizePrompt
{
    public ClipboardFormatOversizeReply Prompt(uint formatId, string formatName, long dataSizeBytes, long capBytes)
    {
        Application? app = Application.Current;
        if (app?.Dispatcher is null)
            return ClipboardFormatOversizeReply.TruncateToCap;

        if (app.Dispatcher.CheckAccess())
            return Show();

        ClipboardFormatOversizeReply result = ClipboardFormatOversizeReply.TruncateToCap;
        app.Dispatcher.Invoke(() => { result = Show(); });
        return result;

        ClipboardFormatOversizeReply Show()
        {
            string msg =
                $"Format \"{formatName}\" (0x{formatId:X4}) is {Formatting.FormatDataSize(dataSizeBytes)} on the clipboard.\n" +
                $"Your capture limit is {Formatting.FormatDataSize(capBytes)}.\n\n" +
                "Yes — Capture only up to the limit\n" +
                $"No — Capture the full contents (up to {Formatting.FormatDataSize(ClipboardService.NonImageFormatAbsoluteMaxBytes)})";

            MessageBoxResult r = MessageBox.Show(
                msg,
                "Clipt — Large clipboard format",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);

            return r == MessageBoxResult.No
                ? ClipboardFormatOversizeReply.CaptureFull
                : ClipboardFormatOversizeReply.TruncateToCap;
        }
    }
}
