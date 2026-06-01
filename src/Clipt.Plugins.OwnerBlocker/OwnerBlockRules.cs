namespace Clipt.Plugins.OwnerBlocker;

internal static class OwnerBlockRules
{
    public static bool IsBlocked(IOwnerBlockerSettingsStore settings, CliptPluginClipboardSnapshot snapshot) =>
        IsProcessBlocked(settings, snapshot.OwnerProcessName)
        || IsWindowClassBlocked(settings, snapshot.OwnerWindowClass);

    public static bool IsProcessBlocked(IOwnerBlockerSettingsStore settings, string? processName)
    {
        if (!BlockedProcessNames.IsBlockable(processName))
            return false;

        return settings.BlockedProcesses.Contains(processName!.Trim());
    }

    public static bool IsWindowClassBlocked(IOwnerBlockerSettingsStore settings, string? windowClass)
    {
        string? prefix = BlockedWindowClasses.NormalizeForBlock(windowClass);
        if (prefix is null)
            return false;

        foreach (string blocked in settings.BlockedClassPrefixes)
        {
            if (prefix.StartsWith(blocked, StringComparison.OrdinalIgnoreCase)
                || blocked.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
