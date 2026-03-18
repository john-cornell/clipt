using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Clipt.Services;

internal static partial class TrayIconHelper
{
    private const int IconSize = 16;

    private static readonly Color EmptyFill = Color.FromArgb(220, 53, 69);
    private static readonly Color EmptyBorder = Color.FromArgb(180, 40, 55);
    private static readonly Color HasDataFill = Color.FromArgb(40, 167, 69);
    private static readonly Color HasDataBorder = Color.FromArgb(30, 130, 52);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    public static Icon CreateEmptyClipboardIcon() => CreateCircleIcon(EmptyFill, EmptyBorder);

    public static Icon CreateHasDataIcon() => CreateCircleIcon(HasDataFill, HasDataBorder);

    private static Icon CreateCircleIcon(Color fill, Color border)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var circleRect = new Rectangle(1, 1, IconSize - 3, IconSize - 3);
            using var fillBrush = new SolidBrush(fill);
            g.FillEllipse(fillBrush, circleRect);

            using var borderPen = new Pen(border, 1.2f);
            g.DrawEllipse(borderPen, circleRect);
        }

        nint iconHandle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(iconHandle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }
}
