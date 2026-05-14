namespace Clipt.Services;

/// <summary>Shows a modal choice when a clipboard capture would exceed the configured total history size.</summary>
public interface IHistorySizeOverflowPrompt
{
    Task<HistorySizeOverflowAnswer> PromptAsync(
        long maxBytes,
        long currentTotalBytes,
        long incomingBlobBytes,
        CancellationToken cancellationToken = default);
}
