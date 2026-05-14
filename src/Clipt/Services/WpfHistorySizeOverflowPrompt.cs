using System.Windows;
using Clipt.Models;

namespace Clipt.Services;

public sealed class WpfHistorySizeOverflowPrompt : IHistorySizeOverflowPrompt
{
    public Task<HistorySizeOverflowAnswer> PromptAsync(
        long maxBytes,
        long currentTotalBytes,
        long incomingBlobBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Application? app = Application.Current;
        if (app?.Dispatcher is null)
            return Task.FromResult(HistorySizeOverflowAnswer.TrimOldest);

        return app.Dispatcher.InvokeAsync(Show).Task;

        HistorySizeOverflowAnswer Show()
        {
            string limitLabel = maxBytes <= 0 ? "unlimited" : Formatting.FormatDataSize(maxBytes);
            string msg =
                $"Saving this clipboard would take stored history past your limit ({limitLabel}).\n\n" +
                $"Current stored total: {Formatting.FormatDataSize(currentTotalBytes)}\n" +
                $"This clip: {Formatting.FormatDataSize(incomingBlobBytes)}\n\n" +
                "Yes — Remove oldest clips until there is room\n" +
                "No — Do not add this clip to history\n" +
                "Cancel — Add it anyway (total may exceed your limit)";

            MessageBoxResult result = MessageBox.Show(
                msg,
                "Clipt — History storage limit",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);

            return result switch
            {
                MessageBoxResult.Yes => HistorySizeOverflowAnswer.TrimOldest,
                MessageBoxResult.No => HistorySizeOverflowAnswer.SkipIncoming,
                _ => HistorySizeOverflowAnswer.AllowOverLimitOnce,
            };
        }
    }
}
