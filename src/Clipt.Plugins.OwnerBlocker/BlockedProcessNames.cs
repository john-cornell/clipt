namespace Clipt.Plugins.OwnerBlocker;

internal static class BlockedProcessNames
{
    public static bool IsBlockable(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        string trimmed = processName.Trim();
        return trimmed is not "(no owner)"
            and not "(none)"
            and not "(unknown)"
            && !trimmed.StartsWith("(PID ", StringComparison.Ordinal);
    }
}
