namespace Clipt.Plugins.OwnerBlocker;

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
