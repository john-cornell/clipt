using System.Drawing;
using System.Reflection;

namespace Clipt.Services;

internal static class TrayIconHelper
{
    private const string RedIconResource = "Clipt.Icons.tray-red.ico";
    private const string GreenIconResource = "Clipt.Icons.tray-green.ico";

    public static Icon CreateEmptyClipboardIcon() => LoadEmbeddedIcon(RedIconResource);

    public static Icon CreateHasDataIcon() => LoadEmbeddedIcon(GreenIconResource);

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Available: [{string.Join(", ", assembly.GetManifestResourceNames())}]");
        return new Icon(stream, 16, 16);
    }
}
