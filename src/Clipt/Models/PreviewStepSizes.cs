namespace Clipt.Models;

/// <summary>
/// Shared step sizes for incremental preview expansion (captured clipboard / blob data only).
/// </summary>
public static class PreviewStepSizes
{
    /// <summary>Raw-memory hex preview: each step shows up to this many bytes until Full (all captured).</summary>
    public static ReadOnlySpan<int> ByteLimits => [256, 1024, 4096, 16384];

    /// <summary>Tray text preview: initial and intermediate caps before Full.</summary>
    public static ReadOnlySpan<int> TrayTextCharLimits => [500, 2000, 8000];

    /// <summary>History text summary after first expansion (level 1+). Level 0 is the stored compact line.</summary>
    public static ReadOnlySpan<int> HistorySummaryExpandedCharLimits => [500, 2000, 8000];

    public static int GetByteSliceLength(int stepIndex, int capturedByteLength)
    {
        if (capturedByteLength <= 0)
            return 0;
        if (stepIndex < 0)
            stepIndex = 0;
        if (stepIndex >= ByteLimits.Length)
            return capturedByteLength;
        return Math.Min(ByteLimits[stepIndex], capturedByteLength);
    }

    public static bool CanAdvanceByteStep(int stepIndex, int capturedByteLength)
    {
        if (capturedByteLength <= 0 || stepIndex >= ByteLimits.Length)
            return false;
        int current = GetByteSliceLength(stepIndex, capturedByteLength);
        int next = GetByteSliceLength(stepIndex + 1, capturedByteLength);
        return next > current;
    }

    public static bool CanRetreatByteStep(int stepIndex) => stepIndex > 0;

    public static string FormatByteStepButtonLabel(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= ByteLimits.Length)
            return "Full";
        return ByteLimits[stepIndex] switch
        {
            < 1024 => $"{ByteLimits[stepIndex]}",
            < 1024 * 1024 => $"{ByteLimits[stepIndex] / 1024}K",
            _ => $"{ByteLimits[stepIndex] / (1024 * 1024)}M",
        };
    }

    public static int GetTrayTextMaxChars(int stepIndex, int fullCharCount)
    {
        if (fullCharCount <= 0)
            return 0;
        if (stepIndex < 0)
            stepIndex = 0;
        if (stepIndex >= TrayTextCharLimits.Length)
            return fullCharCount;
        return Math.Min(TrayTextCharLimits[stepIndex], fullCharCount);
    }

    public static bool CanAdvanceTrayTextStep(int stepIndex, int fullCharCount)
    {
        if (fullCharCount <= 0 || stepIndex >= TrayTextCharLimits.Length)
            return false;
        int current = GetTrayTextMaxChars(stepIndex, fullCharCount);
        int next = GetTrayTextMaxChars(stepIndex + 1, fullCharCount);
        return next > current;
    }

    public static bool CanRetreatTrayTextStep(int stepIndex) => stepIndex > 0;

    /// <summary>
    /// <paramref name="expandLevel"/>: 0 = compact stored summary only; 1..3 = capped by <see cref="HistorySummaryExpandedCharLimits"/>; 4 = full captured text.
    /// </summary>
    public static int GetHistorySummaryDisplayLimit(int expandLevel, int fullCharCount)
    {
        if (expandLevel <= 0 || fullCharCount <= 0)
            return 0;
        if (expandLevel >= 4)
            return fullCharCount;
        return Math.Min(HistorySummaryExpandedCharLimits[expandLevel - 1], fullCharCount);
    }

    /// <param name="compactMayBeTruncated">Stored summary ends with "..." (text was truncated for the list).</param>
    public static bool CanAdvanceHistorySummaryExpand(
        int expandLevel,
        int fullCharCount,
        bool fullTextLoaded,
        bool compactMayBeTruncated)
    {
        if (!fullTextLoaded)
            return compactMayBeTruncated && expandLevel == 0;
        if (fullCharCount <= 0)
            return false;
        if (expandLevel >= 4)
            return false;
        int shown = GetHistorySummaryDisplayLimit(expandLevel, fullCharCount);
        return shown < fullCharCount;
    }

    public static bool CanRetreatHistorySummaryExpand(int expandLevel, bool fullTextLoaded) =>
        fullTextLoaded && expandLevel > 0;

    public static int RetreatHistorySummaryExpandLevel(int expandLevel) =>
        expandLevel <= 1 ? 0 : expandLevel - 1;
}
