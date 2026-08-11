namespace Clipt.Plugins.OwnerBlocker;

internal static class OwnerBlockRules
{
    public static bool IsBlocked(IOwnerBlockerSettingsStore settings, CliptPluginClipboardSnapshot snapshot) =>
        TryGetBlockReason(settings, snapshot) is not null;

    public static string? TryGetBlockReason(
        IOwnerBlockerSettingsStore settings,
        CliptPluginClipboardSnapshot snapshot)
    {
        if (IsProcessBlocked(settings, snapshot.OwnerProcessName))
            return "Blocked process";

        if (IsWindowClassBlocked(settings, snapshot.OwnerWindowClass))
            return "Blocked window class";

        return null;
    }

    public static bool IsProcessBlocked(IOwnerBlockerSettingsStore settings, string? processName)
    {
        if (!BlockedProcessNames.IsBlockable(processName))
            return false;

        string trimmed = processName!.Trim();
        return settings.BlockedProcesses.Any(e =>
            e.IsEnabled && string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsWindowClassBlocked(IOwnerBlockerSettingsStore settings, string? windowClass)
    {
        string? prefix = BlockedWindowClasses.NormalizeForBlock(windowClass);
        if (prefix is null)
            return false;

        foreach (BlockedOwnerEntry entry in settings.BlockedClassPrefixes)
        {
            if (!entry.IsEnabled)
                continue;

            if (prefix.StartsWith(entry.Name, StringComparison.OrdinalIgnoreCase)
                || entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void BlockSnapshotSource(
        IOwnerBlockerSettingsStore settings,
        string? processName,
        string? windowClass)
    {
        settings.BlockSnapshotSource(processName, windowClass);
    }
}
