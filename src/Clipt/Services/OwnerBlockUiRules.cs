namespace Clipt.Services;

/// <summary>
/// Host-side mirror of <c>Clipt.Plugins.OwnerBlocker.BlockedProcessNames</c> for History UI when the plugin is absent.
/// </summary>
internal static class OwnerBlockUiRules
{
    public static bool IsBlockableProcessName(string? processName)
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
