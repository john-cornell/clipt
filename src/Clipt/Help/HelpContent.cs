using System.Collections.Frozen;

namespace Clipt.Help;

public static class HelpContent
{
    private static readonly string[] TabNames =
    [
        "Text",
        "Hex",
        "Image",
        "RichContent",
        "FileDrop",
        "AllFormats",
        "Native",
    ];

    // ── Tooltip strings (short, 1-2 sentences) ──────────────────────────

    public const string CfUnicodeTextTooltip =
        "Windows clipboard format containing text encoded as UTF-16 (two bytes per character).";

    public const string CfTextTooltip =
        "Windows clipboard format containing text in the system's default ANSI code page.";

    public const string CfOemTextTooltip =
        "Windows clipboard format containing text in the OEM code page, historically used by DOS and the console.";

    public const string NullTerminatorTooltip =
        "Position (zero-based character index) of the first null character (U+0000) in the string, marking where C-style string reading stops.";

    public const string LocaleLcidTooltip =
        "Locale ID (LCID) from CF_LOCALE indicating the language/region of the text on the clipboard.";

    public const string EncodingStatsTooltip =
        "Counts of ASCII (0x00-0x7F), non-ASCII (0x80+), and control characters (0x00-0x1F excluding tab/CR/LF) in the text.";

    public const string HexDumpTooltip =
        "Raw binary content displayed as hexadecimal byte values with an ASCII sidebar for printable characters.";

    public const string BytesPerRowTooltip =
        "Number of bytes shown on each line of the hex dump. Common values: 8, 16, or 32.";

    public const string BppTooltip =
        "Bits per pixel — the number of bits used to represent each pixel's color. Higher values allow more colors.";

    public const string StrideTooltip =
        "Number of bytes per horizontal row in bitmap memory, including padding to align rows to a 4-byte boundary.";

    public const string PixelFormatTooltip =
        "Describes how color data is laid out per pixel (e.g. Bgra32 = Blue, Green, Red, Alpha, 8 bits each).";

    public const string PaletteTooltip =
        "Indexed-color images use a palette (color lookup table). Direct-color images encode color values directly in each pixel.";

    public const string HtmlFormatTooltip =
        "CF_HTML is a clipboard format containing HTML markup plus byte-offset headers (StartHTML, EndHTML, StartFragment, EndFragment).";

    public const string HtmlHeadersTooltip =
        "Byte-offset metadata prepended to CF_HTML data: Version, StartHTML, EndHTML, StartFragment, EndFragment, and SourceURL.";

    public const string RtfTooltip =
        "Rich Text Format — a document markup language by Microsoft. RTF source shows the raw control words and text.";

    public const string CfHdropTooltip =
        "CF_HDROP is the clipboard format for file drag-and-drop operations, containing a DROPFILES structure followed by null-terminated file paths.";

    public const string FormatIdTooltip =
        "Numeric identifier for a clipboard format. Standard formats (CF_TEXT=1, CF_UNICODETEXT=13, etc.) use IDs below 0xC000; registered formats use IDs 0xC000+.";

    public const string HglobalTooltip =
        "Handle to a Windows global memory block allocated on the process heap. Not a pointer — call GlobalLock() to access the memory.";

    public const string StandardFormatTooltip =
        "Standard formats are predefined by Windows (e.g. CF_TEXT, CF_BITMAP). Non-standard formats are registered by applications at runtime.";

    public const string ClipboardOwnerTooltip =
        "The process that last placed data on the clipboard, identified by name and process ID (PID).";

    public const string SequenceNumberTooltip =
        "Monotonically increasing counter incremented each time the clipboard contents change. Provided by GetClipboardSequenceNumber().";

    public const string GlobalLockTooltip =
        "Win32 function that converts an HGLOBAL handle into a usable memory pointer. Must be paired with GlobalUnlock().";

    public const string GlobalSizeTooltip =
        "Returns the allocated size in bytes of the global memory block identified by an HGLOBAL handle.";

    public const string RawMemoryTooltip =
        "Captured bytes from the clipboard format's global memory block, shown as a hex dump. " +
        "Use the size buttons (256 through Full) to show more of the captured data — up to the app's capture limit, not necessarily the entire GlobalSize allocation.";

    public const string SeqStatusTooltip =
        "Clipboard sequence number — incremented by Windows each time any application modifies the clipboard.";

    public const string FormatsCountTooltip =
        "Total number of distinct data formats currently stored on the clipboard.";

    public const string OwnerStatusTooltip =
        "The process that owns the clipboard (last called SetClipboardData), shown as process name and PID.";

    public const string AutoRefreshTooltip =
        "When enabled, the view automatically updates whenever the clipboard contents change.";

    // ── Detailed help strings (3-5 sentences) ───────────────────────────

    private static readonly FrozenDictionary<string, string> DetailedHelpMap =
        new Dictionary<string, string>
        {
            ["CF_UNICODETEXT"] =
                "CF_UNICODETEXT is standard clipboard format 13. " +
                "Windows stores copied text as a null-terminated UTF-16LE string. " +
                "Most modern applications place text in this format. " +
                "The 'CF_' prefix stands for Clipboard Format, a naming convention from the Win32 API.",

            ["UTF-16"] =
                "UTF-16 is a variable-length Unicode encoding that uses 2 bytes for characters in the Basic Multilingual Plane " +
                "and 4 bytes (surrogate pairs) for supplementary characters. " +
                "Windows uses UTF-16LE (little-endian) as its native string representation throughout the kernel and Win32 API.",

            ["CF_TEXT"] =
                "CF_TEXT is standard clipboard format 1, containing text in the system's default ANSI code page. " +
                "Characters outside the current code page are lost or replaced. " +
                "Windows automatically synthesizes CF_TEXT from CF_UNICODETEXT and vice versa when only one is provided.",

            ["ANSI"] =
                "ANSI encoding refers to the system's default Windows code page, such as Windows-1252 for Western European locales. " +
                "It is a single-byte encoding that supports only 256 characters. " +
                "Characters not representable in the ANSI code page are replaced with a fallback character.",

            ["CF_OEMTEXT"] =
                "CF_OEMTEXT is standard clipboard format 7, containing text in the OEM code page. " +
                "The OEM code page (typically CP437 on US systems) was used by MS-DOS and is still used by the Windows console. " +
                "It includes box-drawing characters not present in the ANSI code page.",

            ["OEM/CP437"] =
                "Code Page 437 is the original IBM PC character set. " +
                "It includes ASCII (0x00–0x7F) plus graphical characters: box-drawing, block elements, " +
                "accented letters, Greek letters, and mathematical symbols (0x80–0xFF). " +
                "Modern Windows uses it mainly for console output and legacy DOS applications.",

            ["NullTerminator"] =
                "In C-style strings, a null character (U+0000 or 0x00) marks the end of the string. " +
                "Clipboard text formats use null-terminated strings. " +
                "The 'Null at' value shows the zero-based index of the first null character. " +
                "Text after the null terminator is ignored by most applications.",

            ["Locale/LCID"] =
                "CF_LOCALE is a clipboard format containing a Locale Identifier (LCID) — a 32-bit value that specifies " +
                "the language, country, and sorting order of the clipboard text. " +
                "Windows uses this to correctly convert between CF_UNICODETEXT and CF_TEXT/CF_OEMTEXT.",

            ["EncodingStats"] =
                "Encoding statistics break down the clipboard text into: " +
                "ASCII characters (U+0000 to U+007F), non-ASCII characters (U+0080 and above), " +
                "and control characters (U+0000 to U+001F, excluding tab, carriage return, and line feed). " +
                "High non-ASCII counts may indicate CJK text or rich Unicode content.",

            ["HexDump"] =
                "A hex dump shows binary data as hexadecimal byte values arranged in rows. " +
                "Each row shows an offset, hex bytes, and an ASCII sidebar where printable characters (0x20–0x7E) " +
                "are shown directly and non-printable bytes are replaced with a dot. " +
                "This is a standard way to inspect raw binary data.",

            ["BytesPerRow"] =
                "Controls how many bytes are displayed on each line of the hex dump. " +
                "16 bytes per row is the most common default, matching the traditional format used by tools like xxd and HxD. " +
                "8 bytes/row provides more space for narrow windows; 32 fits more data on screen.",

            ["bpp"] =
                "Bits per pixel (bpp) indicates how many bits are used to represent a single pixel's color. " +
                "Common values: 1 (monochrome), 8 (256 colors, indexed), 24 (16.7M colors, RGB), " +
                "32 (16.7M colors + alpha transparency, ARGB/BGRA). " +
                "Higher bpp means more colors but larger image data.",

            ["DPI"] =
                "Dots Per Inch (DPI) describes the image's intended physical resolution. " +
                "96 DPI is the standard Windows display density (100% scaling). " +
                "DPI affects how large the image appears when printed or rendered at 'actual size' but does not change the pixel count. " +
                "High-DPI images (e.g. 192 DPI) are used for Retina/HiDPI displays.",

            ["Stride"] =
                "Stride (also called pitch or scanline width) is the number of bytes per row in bitmap memory. " +
                "Bitmap rows are padded to align to a 4-byte (DWORD) boundary. " +
                "A 24bpp image 10 pixels wide needs 30 bytes of pixel data per row, but stride is 32 (next multiple of 4). " +
                "Stride is essential when navigating raw pixel data by byte offset.",

            ["PixelFormat"] =
                "Pixel format describes the per-pixel color layout. " +
                "Common formats include Bgr24 (8 bits each for Blue, Green, Red), " +
                "Bgra32 (adds an 8-bit alpha channel), and Indexed8 (8-bit index into a color palette). " +
                "The byte order (BGR vs RGB) reflects how Windows stores bitmap data internally.",

            ["Palette"] =
                "An image palette (color lookup table) maps index values to actual colors. " +
                "Indexed images (1, 4, or 8 bpp) store palette indices per pixel instead of full color values. " +
                "Direct-color images (16, 24, 32 bpp) encode color values directly in each pixel and have no palette. " +
                "'No palette (direct color)' means each pixel's bytes directly represent its color.",

            ["HtmlFormat"] =
                "CF_HTML is a registered clipboard format (not a standard CF_* constant) for transferring HTML content. " +
                "The data consists of a text header with byte offsets followed by the actual HTML markup. " +
                "Applications use StartFragment/EndFragment offsets to extract just the selected HTML content.",

            ["HtmlHeaders"] =
                "CF_HTML data begins with a header block containing byte offsets into the payload: " +
                "Version (format version, typically 0.9 or 1.0), " +
                "StartHTML/EndHTML (byte offsets to the full HTML document), " +
                "StartFragment/EndFragment (byte offsets to the selected fragment), " +
                "and SourceURL (the page the content was copied from).",

            ["RTF"] =
                "Rich Text Format (RTF) is a document file format developed by Microsoft. " +
                "RTF uses control words like \\b (bold) and \\i (italic) mixed with plain text. " +
                "The RTF source view shows the raw markup before it is rendered by a word processor.",

            ["CF_HDROP"] =
                "CF_HDROP (format ID 15) is used for file drag-and-drop clipboard operations. " +
                "The data begins with a DROPFILES structure (header) followed by a null-terminated list of file paths. " +
                "Windows Shell uses this format when files are copied or cut in Explorer.",

            ["FormatID"] =
                "Each clipboard format has a numeric ID. " +
                "Standard Windows formats (CF_TEXT=1, CF_BITMAP=2, CF_UNICODETEXT=13, etc.) use IDs from 1 to 0xBFFF. " +
                "Application-registered formats use IDs from 0xC000 to 0xFFFF, assigned at runtime by RegisterClipboardFormat(). " +
                "The ID column shows both decimal and hexadecimal (e.g. '13 (0x000D)').",

            ["HGLOBAL"] =
                "HGLOBAL is a Win32 handle type returned by GlobalAlloc(). " +
                "Clipboard data is stored in global memory blocks that can be shared between processes. " +
                "The handle itself is not a pointer — you must call GlobalLock() to obtain a usable memory address, " +
                "and GlobalUnlock() when done reading.",

            ["StandardFormat"] =
                "Standard formats are predefined by Windows with constant IDs (e.g. CF_TEXT=1, CF_BITMAP=2, CF_UNICODETEXT=13). " +
                "Registered formats are created by applications at runtime via RegisterClipboardFormat() and receive IDs in the 0xC000-0xFFFF range. " +
                "The 'Standard' column indicates whether a format is a Windows built-in.",

            ["ClipboardOwner"] =
                "The clipboard owner is the process that last called SetClipboardData() or EmptyClipboard()/SetClipboardData(). " +
                "Windows tracks this via the window handle passed to OpenClipboard(). " +
                "The owner process name and PID are obtained via GetClipboardOwner() and GetWindowThreadProcessId().",

            ["SequenceNumber"] =
                "The clipboard sequence number is a 32-bit unsigned integer maintained by Windows. " +
                "It increments by one each time any process modifies the clipboard. " +
                "Applications use GetClipboardSequenceNumber() to detect changes without polling the actual content.",

            ["GlobalLock"] =
                "GlobalLock() pins a global memory block in place and returns a pointer to the first byte. " +
                "Each call to GlobalLock() must be paired with a call to GlobalUnlock(). " +
                "Clipt uses GlobalLock() to read the raw bytes of clipboard data for hex dumps and format inspection.",

            ["GlobalSize"] =
                "GlobalSize() returns the size in bytes of a global memory block identified by its HGLOBAL handle. " +
                "The returned size may be larger than the data written to the block due to allocation granularity. " +
                "This value is shown in the Native/Memory tab to indicate how much memory the format occupies.",

            ["RawMemory"] =
                "The raw memory view shows a hex dump of bytes captured from the format's global memory block. " +
                "You can expand the preview in steps (256 bytes up through Full) to inspect more of the captured payload — " +
                "useful for headers, magic bytes, and binary layout. Full means all bytes Clipt captured for that format (see capture limits in documentation), not always every byte reported by GlobalSize.",

            ["FormatInfo"] =
                "The Format Info panel shows a description of the currently selected clipboard format. " +
                "In the Hex tab, select a format from the dropdown to see its description below the toolbar. " +
                "In the All Formats tab, click a row in the grid to see a detailed description of that format. " +
                "Hover any format in the dropdown or grid row to see a short tooltip summary.",
        }.ToFrozenDictionary();

    // ── Per-tab term lists ───────────────────────────────────────────────

    private static readonly FrozenDictionary<string, string[]> TabTermMap =
        new Dictionary<string, string[]>
        {
            ["Text"] =
            [
                "CF_UNICODETEXT", "UTF-16", "CF_TEXT", "ANSI",
                "CF_OEMTEXT", "OEM/CP437", "NullTerminator", "Locale/LCID", "EncodingStats",
            ],
            ["Hex"] =
            [
                "HexDump", "BytesPerRow", "FormatInfo",
            ],
            ["Image"] = ["bpp", "DPI", "Stride", "PixelFormat", "Palette"],
            ["RichContent"] = ["HtmlFormat", "HtmlHeaders", "RTF"],
            ["FileDrop"] = ["CF_HDROP"],
            ["AllFormats"] = ["FormatID", "HGLOBAL", "StandardFormat", "FormatInfo"],
            ["Native"] =
            [
                "ClipboardOwner", "SequenceNumber", "HGLOBAL",
                "GlobalLock", "GlobalSize", "RawMemory",
            ],
        }.ToFrozenDictionary();

    public static IReadOnlyDictionary<string, string> DetailedHelp => DetailedHelpMap;

    public static IReadOnlyDictionary<string, string[]> TabTerms => TabTermMap;

    private static readonly FrozenDictionary<string, string> DisplayNameMap =
        new Dictionary<string, string>
        {
            ["CF_UNICODETEXT"] = "CF_UNICODETEXT",
            ["UTF-16"] = "UTF-16",
            ["CF_TEXT"] = "CF_TEXT",
            ["ANSI"] = "ANSI Encoding",
            ["CF_OEMTEXT"] = "CF_OEMTEXT",
            ["OEM/CP437"] = "OEM / Code Page 437",
            ["NullTerminator"] = "Null Terminator Position",
            ["Locale/LCID"] = "Locale / LCID",
            ["EncodingStats"] = "Encoding Statistics",
            ["HexDump"] = "Hex Dump",
            ["BytesPerRow"] = "Bytes Per Row",
            ["bpp"] = "Bits Per Pixel (bpp)",
            ["DPI"] = "Dots Per Inch (DPI)",
            ["Stride"] = "Stride",
            ["PixelFormat"] = "Pixel Format",
            ["Palette"] = "Palette / Direct Color",
            ["HtmlFormat"] = "HTML Format (CF_HTML)",
            ["HtmlHeaders"] = "HTML Headers",
            ["RTF"] = "Rich Text Format (RTF)",
            ["CF_HDROP"] = "CF_HDROP (File Drop)",
            ["FormatID"] = "Format ID",
            ["HGLOBAL"] = "HGLOBAL Handle",
            ["StandardFormat"] = "Standard vs Registered Format",
            ["ClipboardOwner"] = "Clipboard Owner",
            ["SequenceNumber"] = "Sequence Number",
            ["GlobalLock"] = "GlobalLock Pointer",
            ["GlobalSize"] = "GlobalSize",
            ["RawMemory"] = "Raw Memory",
            ["FormatInfo"] = "Format Info Panel",
        }.ToFrozenDictionary();

    public static IReadOnlyDictionary<string, string> DisplayNames => DisplayNameMap;

    public static string GetTabNameByIndex(int index)
    {
        if (index < 0 || index >= TabNames.Length)
            return string.Empty;

        return TabNames[index];
    }

    public static int TabCount => TabNames.Length;
}
