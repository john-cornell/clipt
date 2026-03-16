using System.Collections.Frozen;

namespace ClipSpy.Native;

internal static class ClipboardConstants
{
    public const uint CF_TEXT = 1;
    public const uint CF_BITMAP = 2;
    public const uint CF_METAFILEPICT = 3;
    public const uint CF_SYLK = 4;
    public const uint CF_DIF = 5;
    public const uint CF_TIFF = 6;
    public const uint CF_OEMTEXT = 7;
    public const uint CF_DIB = 8;
    public const uint CF_PALETTE = 9;
    public const uint CF_PENDATA = 10;
    public const uint CF_RIFF = 11;
    public const uint CF_WAVE = 12;
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_ENHMETAFILE = 14;
    public const uint CF_HDROP = 15;
    public const uint CF_LOCALE = 16;
    public const uint CF_DIBV5 = 17;
    public const uint CF_OWNERDISPLAY = 0x0080;
    public const uint CF_DSPTEXT = 0x0081;
    public const uint CF_DSPBITMAP = 0x0082;
    public const uint CF_DSPMETAFILEPICT = 0x0083;
    public const uint CF_DSPENHMETAFILE = 0x008E;
    public const uint CF_PRIVATEFIRST = 0x0200;
    public const uint CF_PRIVATELAST = 0x02FF;
    public const uint CF_GDIOBJFIRST = 0x0300;
    public const uint CF_GDIOBJLAST = 0x03FF;

    public static readonly FrozenDictionary<uint, string> StandardFormatNames =
        new Dictionary<uint, string>
        {
            [CF_TEXT] = "CF_TEXT",
            [CF_BITMAP] = "CF_BITMAP",
            [CF_METAFILEPICT] = "CF_METAFILEPICT",
            [CF_SYLK] = "CF_SYLK",
            [CF_DIF] = "CF_DIF",
            [CF_TIFF] = "CF_TIFF",
            [CF_OEMTEXT] = "CF_OEMTEXT",
            [CF_DIB] = "CF_DIB",
            [CF_PALETTE] = "CF_PALETTE",
            [CF_PENDATA] = "CF_PENDATA",
            [CF_RIFF] = "CF_RIFF",
            [CF_WAVE] = "CF_WAVE",
            [CF_UNICODETEXT] = "CF_UNICODETEXT",
            [CF_ENHMETAFILE] = "CF_ENHMETAFILE",
            [CF_HDROP] = "CF_HDROP",
            [CF_LOCALE] = "CF_LOCALE",
            [CF_DIBV5] = "CF_DIBV5",
            [CF_OWNERDISPLAY] = "CF_OWNERDISPLAY",
            [CF_DSPTEXT] = "CF_DSPTEXT",
            [CF_DSPBITMAP] = "CF_DSPBITMAP",
            [CF_DSPMETAFILEPICT] = "CF_DSPMETAFILEPICT",
            [CF_DSPENHMETAFILE] = "CF_DSPENHMETAFILE",
        }.ToFrozenDictionary();

    public static string GetFormatName(uint formatId)
    {
        if (StandardFormatNames.TryGetValue(formatId, out var name))
            return name;

        if (formatId >= CF_PRIVATEFIRST && formatId <= CF_PRIVATELAST)
            return $"CF_PRIVATE(0x{formatId:X4})";

        if (formatId >= CF_GDIOBJFIRST && formatId <= CF_GDIOBJLAST)
            return $"CF_GDIOBJ(0x{formatId:X4})";

        var buffer = new char[256];
        int len = NativeMethods.GetClipboardFormatName(formatId, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : $"Unknown(0x{formatId:X4})";
    }

    public static bool IsStandardFormat(uint formatId) =>
        StandardFormatNames.ContainsKey(formatId);
}
