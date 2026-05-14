namespace Clipt.Services;

/// <summary>User choice from <see cref="IHistorySizeOverflowPrompt"/> when storage would exceed the limit.</summary>
public enum HistorySizeOverflowAnswer
{
    /// <summary>Evict oldest clips until under the limit, then keep the new clip.</summary>
    TrimOldest = 0,

    /// <summary>Do not add the incoming clip to history.</summary>
    SkipIncoming = 1,

    /// <summary>Add the clip and skip size-based eviction for this add only.</summary>
    AllowOverLimitOnce = 2,
}
