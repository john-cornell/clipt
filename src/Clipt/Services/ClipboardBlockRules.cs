using Clipt.Models;

namespace Clipt.Services;

internal static class ClipboardBlockRules
{
    public static bool IsSnapshotBlocked(ISettingsService settings, ClipboardSnapshot snapshot) =>
        IsProcessBlocked(settings, snapshot.OwnerProcessName)
        || IsWindowClassBlocked(settings, snapshot.OwnerWindowClass);

    public static bool IsProcessBlocked(ISettingsService settings, string? processName)
    {
        if (!BlockedProcessNames.IsBlockable(processName))
            return false;

        return settings.LoadBlockedHistoryProcessNames().Contains(processName!.Trim());
    }

    public static bool IsWindowClassBlocked(ISettingsService settings, string? windowClass)
    {
        string? prefix = BlockedWindowClasses.NormalizeForBlock(windowClass);
        if (prefix is null)
            return false;

        foreach (string blocked in settings.LoadBlockedHistoryWindowClassPrefixes())
        {
            if (prefix.StartsWith(blocked, StringComparison.OrdinalIgnoreCase)
                || blocked.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void BlockSnapshotSource(ISettingsService settings, string? processName, string? windowClass)
    {
        if (BlockedProcessNames.IsBlockable(processName))
        {
            var blocked = new HashSet<string>(settings.LoadBlockedHistoryProcessNames(), StringComparer.OrdinalIgnoreCase)
            {
                processName!.Trim(),
            };
            settings.SaveBlockedHistoryProcessNames(blocked);
        }

        string? classPrefix = BlockedWindowClasses.NormalizeForBlock(windowClass);
        if (classPrefix is not null)
        {
            var blockedClasses = new HashSet<string>(
                settings.LoadBlockedHistoryWindowClassPrefixes(),
                StringComparer.OrdinalIgnoreCase)
            {
                classPrefix,
            };
            settings.SaveBlockedHistoryWindowClassPrefixes(blockedClasses);
        }
    }

    public static string FormatBlockedSummary(ISettingsService settings)
    {
        var processes = settings.LoadBlockedHistoryProcessNames();
        var classes = settings.LoadBlockedHistoryWindowClassPrefixes();
        if (processes.Count == 0 && classes.Count == 0)
            return "(none)";

        var parts = new List<string>();
        if (processes.Count > 0)
            parts.Add(string.Join(", ", processes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
        if (classes.Count > 0)
            parts.Add("class:" + string.Join(", ", classes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)));

        return string.Join(" · ", parts);
    }
}

internal static class BlockedWindowClasses
{
    public static string? NormalizeForBlock(string? windowClass)
    {
        if (string.IsNullOrWhiteSpace(windowClass) || windowClass == "—")
            return null;

        string cls = windowClass.Trim();
        if (cls.StartsWith("WisprClipboard_", StringComparison.OrdinalIgnoreCase))
            return "WisprClipboard_";

        return cls;
    }

    public static bool IsBlockable(string? windowClass) =>
        NormalizeForBlock(windowClass) is not null;
}
