namespace Clipt.Models;

/// <summary>
/// What to do when adding a new history clip would push total stored history past the configured max storage size.
/// Entry-count limits still always apply.
/// </summary>
public enum HistorySizeOverflowMode
{
    /// <summary>Remove oldest clips until total size is at or under the limit (default).</summary>
    TrimOldest = 0,

    /// <summary>Never remove clips for size; total stored size may exceed the configured limit.</summary>
    AllowOverLimit = 1,

    /// <summary>Prompt when a new clipboard capture would exceed the limit (clipboard adds only).</summary>
    AskEachTime = 2,
}
