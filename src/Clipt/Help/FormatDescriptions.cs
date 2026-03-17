using System.Collections.Frozen;

namespace Clipt.Help;

public static class FormatDescriptions
{
    public sealed record FormatDescription(string Short, string Detailed);

    private static readonly FrozenDictionary<uint, FormatDescription> StandardFormats =
        new Dictionary<uint, FormatDescription>
        {
            [1] = new(
                "ANSI text in the system default code page.",
                "CF_TEXT contains plain text encoded in the system's default ANSI code page (e.g. Windows-1252). " +
                "Windows auto-synthesizes this from CF_UNICODETEXT if only Unicode text is set. " +
                "Characters outside the code page are replaced with a fallback character. " +
                "The data is a null-terminated byte string allocated in a global memory block."),
            [2] = new(
                "Device-dependent bitmap (DDB) handle.",
                "CF_BITMAP provides an HBITMAP handle to a device-dependent bitmap. " +
                "This format is resolution- and color-depth-dependent on the source device context. " +
                "Most applications prefer CF_DIB or CF_DIBV5 for interchange because DDBs are device-specific. " +
                "The clipboard stores the GDI object handle, not raw pixel data."),
            [3] = new(
                "Windows Metafile picture in a METAFILEPICT structure.",
                "CF_METAFILEPICT contains a handle to a METAFILEPICT structure that describes a Windows Metafile. " +
                "The structure includes mapping mode, viewport extents, and an HMETAFILE handle. " +
                "This is a legacy vector graphics format largely replaced by Enhanced Metafiles (CF_ENHMETAFILE). " +
                "Applications receiving this format must use the mapping mode to render the metafile correctly."),
            [4] = new(
                "Symbolic Link (SYLK) spreadsheet interchange format.",
                "CF_SYLK contains data in the Symbolic Link format, a text-based spreadsheet interchange format " +
                "originally developed by Microsoft for Multiplan. " +
                "Each record is a text line starting with a record-type identifier. " +
                "SYLK is rarely used by modern applications but may appear when copying from legacy spreadsheet software."),
            [5] = new(
                "Data Interchange Format (DIF) for spreadsheet data.",
                "CF_DIF contains data in the Data Interchange Format, a text-based format for exchanging " +
                "spreadsheet data between applications. " +
                "DIF was created by Software Arts (VisiCalc) and stores single-sheet tabular data. " +
                "Like SYLK, it is largely obsolete but may appear with legacy spreadsheet applications."),
            [6] = new(
                "Tag Image File Format (TIFF) image data.",
                "CF_TIFF contains image data in the TIFF format. " +
                "TIFF supports multiple compression schemes, color spaces, and bit depths. " +
                "This clipboard format is rarely used directly; most applications use CF_DIB or CF_DIBV5 instead. " +
                "When present, the data is the complete TIFF file content in the global memory block."),
            [7] = new(
                "Text in the OEM/DOS code page (typically CP437).",
                "CF_OEMTEXT contains text encoded in the OEM code page, historically used by MS-DOS and the Windows console. " +
                "On US English systems this is Code Page 437, which includes box-drawing characters. " +
                "Windows auto-synthesizes CF_OEMTEXT from CF_UNICODETEXT when CF_LOCALE is present. " +
                "Characters outside the OEM code page are replaced with a fallback character."),
            [8] = new(
                "Device-independent bitmap with BITMAPINFOHEADER.",
                "CF_DIB contains a complete device-independent bitmap: a BITMAPINFOHEADER followed by an optional " +
                "color table and the pixel data. " +
                "This is the most widely used bitmap clipboard format and is resolution-independent. " +
                "The header specifies width, height, bit depth, and compression. " +
                "Negative height values indicate a top-down bitmap (origin at top-left)."),
            [9] = new(
                "Handle to a GDI color palette (HPALETTE).",
                "CF_PALETTE contains an HPALETTE handle to a logical color palette. " +
                "Applications place this alongside CF_DIB when the bitmap uses indexed colors. " +
                "The palette maps index values in the bitmap to actual RGB colors. " +
                "This format is rarely relevant on modern true-color displays."),
            [10] = new(
                "Pen extension data for pen-based computing.",
                "CF_PENDATA was reserved for Microsoft Pen Computing extensions. " +
                "It was intended for ink and handwriting data from pen input devices. " +
                "This format is obsolete and not used by modern Windows applications. " +
                "The format ID is reserved and should not be repurposed."),
            [11] = new(
                "Resource Interchange File Format (RIFF) audio data.",
                "CF_RIFF contains audio data in the RIFF container format. " +
                "RIFF is the container for WAV audio, AVI video, and other multimedia formats. " +
                "This clipboard format is rarely used; CF_WAVE is more common for audio. " +
                "The data includes the RIFF header followed by the format-specific chunks."),
            [12] = new(
                "Waveform audio data (WAV).",
                "CF_WAVE contains audio data in standard WAV format with a RIFF header. " +
                "The data typically includes PCM audio samples with a WAVEFORMATEX header. " +
                "This format is used when copying audio samples between audio editors. " +
                "The complete WAV file content is stored in the global memory block."),
            [13] = new(
                "Null-terminated UTF-16LE text.",
                "CF_UNICODETEXT is the primary text format on modern Windows, storing text as null-terminated UTF-16LE. " +
                "Each code unit is 2 bytes; supplementary characters use surrogate pairs (4 bytes). " +
                "Windows auto-synthesizes CF_TEXT and CF_OEMTEXT from this format when they are not explicitly set. " +
                "This is the format most applications use when placing text on the clipboard."),
            [14] = new(
                "Enhanced Metafile (EMF) vector graphics handle.",
                "CF_ENHMETAFILE provides an HENHMETAFILE handle to an Enhanced Metafile. " +
                "EMF is a resolution-independent vector graphics format that records GDI drawing commands. " +
                "It supersedes the older CF_METAFILEPICT format and supports more complex drawing operations. " +
                "Applications use this to transfer scalable vector graphics via the clipboard."),
            [15] = new(
                "File drop list (DROPFILES + null-terminated paths).",
                "CF_HDROP contains a DROPFILES structure followed by a double-null-terminated list of file paths. " +
                "Windows Shell places this format when files are copied or cut in Explorer. " +
                "The DROPFILES header specifies the offset to the file list and whether paths are Unicode. " +
                "Each path is null-terminated, and the list ends with an additional null character."),
            [16] = new(
                "4-byte Locale Identifier (LCID) for the clipboard text.",
                "CF_LOCALE contains a 32-bit LCID that identifies the language, country, and sorting order " +
                "of the text on the clipboard. " +
                "Windows uses this to select the correct code page when converting between CF_UNICODETEXT " +
                "and CF_TEXT or CF_OEMTEXT. " +
                "The value is typically 4 bytes representing the LCID as a little-endian uint32."),
            [17] = new(
                "Device-independent bitmap with BITMAPV5HEADER (supports ICC profiles and alpha).",
                "CF_DIBV5 extends CF_DIB with a BITMAPV5HEADER that supports ICC color profiles, " +
                "alpha transparency, and CIE colorimetric color spaces. " +
                "This is the richest standard bitmap format available on the clipboard. " +
                "Applications that need color management or per-pixel alpha should prefer this over CF_DIB."),
            [0x0080] = new(
                "Owner-display format managed by the clipboard owner.",
                "CF_OWNERDISPLAY indicates that the clipboard owner is responsible for painting the clipboard viewer window. " +
                "The clipboard owner receives WM_PAINTCLIPBOARD, WM_SIZECLIPBOARD, and related messages. " +
                "This format is largely obsolete; modern clipboard viewers read data formats directly. " +
                "No actual data is stored — the owner renders content on demand."),
            [0x0081] = new(
                "Private display text for the clipboard owner.",
                "CF_DSPTEXT is a private display format for text data. " +
                "It uses the same representation as CF_TEXT but is intended for private use by the clipboard owner. " +
                "Applications use DSP formats when they want to provide a display representation separate from the actual data. " +
                "Only the clipboard owner can meaningfully interpret this format."),
            [0x0082] = new(
                "Private display bitmap for the clipboard owner.",
                "CF_DSPBITMAP is a private display format for bitmap data. " +
                "It uses the same representation as CF_BITMAP but is intended for private use by the clipboard owner. " +
                "This allows an application to provide a preview bitmap distinct from the actual clipboard data. " +
                "Only the clipboard owner can meaningfully interpret this format."),
            [0x0083] = new(
                "Private display metafile for the clipboard owner.",
                "CF_DSPMETAFILEPICT is a private display format using the METAFILEPICT representation. " +
                "It allows the clipboard owner to provide a metafile preview distinct from the actual data. " +
                "This is a legacy format; CF_DSPENHMETAFILE is the modern equivalent. " +
                "Only the clipboard owner can meaningfully interpret this format."),
            [0x008E] = new(
                "Private display Enhanced Metafile for the clipboard owner.",
                "CF_DSPENHMETAFILE is a private display format using the Enhanced Metafile representation. " +
                "It allows the clipboard owner to provide a scalable vector preview of clipboard content. " +
                "This is used when an application wants separate display and data representations. " +
                "Only the clipboard owner can meaningfully interpret this format."),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, FormatDescription> RegisteredFormats =
        new Dictionary<string, FormatDescription>(StringComparer.OrdinalIgnoreCase)
        {
            ["HTML Format"] = new(
                "CF_HTML: HTML markup with byte-offset headers.",
                "This registered format contains an HTML fragment with a header specifying byte offsets " +
                "(StartHTML, EndHTML, StartFragment, EndFragment) and a SourceURL. " +
                "Browsers and editors use this to transfer rich HTML content via the clipboard. " +
                "The header is plain ASCII text; offsets are byte positions (not character positions) into the payload."),
            ["Rich Text Format"] = new(
                "RTF markup for formatted text interchange.",
                "Rich Text Format is a document markup language developed by Microsoft. " +
                "RTF uses backslash-prefixed control words (e.g. \\b for bold, \\par for paragraph break) mixed with plain text. " +
                "Word processors, text editors, and many applications use this format for styled text interchange. " +
                "The clipboard data is the raw RTF source as a byte string."),
            ["Chromium internal source RFH token"] = new(
                "Internal Chromium identifier for the RenderFrameHost that placed data on the clipboard.",
                "Chromium-based browsers place this 24-byte token on the clipboard to identify which RenderFrameHost " +
                "originated the copy operation. " +
                "It is used internally by Chromium for security checks (e.g. preventing paste from untrusted sources). " +
                "This token is not meaningful to other applications and is safe to ignore."),
            ["Chromium internal source URL"] = new(
                "The URL of the page from which Chromium copied content.",
                "Chromium-based browsers store the source page URL as a clipboard format so that paste targets " +
                "can determine the origin of copied content. " +
                "The data is a UTF-8 or UTF-16 encoded URL string. " +
                "This is used for security attribution and source tracking within the browser."),
            ["Chromium Web Custom MIME Data Format"] = new(
                "Custom MIME data written by web applications via the Async Clipboard API.",
                "Web applications using the Clipboard API can write custom MIME types to the clipboard. " +
                "Chromium wraps these in a registered format with a structured payload. " +
                "The data format is specific to Chromium's implementation of the web platform clipboard API. " +
                "Other browsers may use different internal representations for the same web standard."),
            ["DataObject"] = new(
                "COM IDataObject-related clipboard marker.",
                "The DataObject format is used internally by COM-based clipboard and drag-and-drop operations. " +
                "It may contain metadata about the OLE data object that placed the clipboard content. " +
                "This format is primarily relevant to OLE/COM interoperability. " +
                "Most applications do not need to read this format directly."),
            ["Preferred DropEffect"] = new(
                "Indicates the preferred drag-and-drop operation (copy, move, link).",
                "This registered format contains a 4-byte DWORD specifying the preferred drop effect: " +
                "DROPEFFECT_COPY (1), DROPEFFECT_MOVE (2), or DROPEFFECT_LINK (4). " +
                "Windows Shell uses this to determine whether a paste operation should copy or move files. " +
                "When cutting files in Explorer, this is set to DROPEFFECT_MOVE."),
            ["Shell IDList Array"] = new(
                "CIDA structure containing Shell item identifier lists (PIDLs).",
                "This format contains a CIDA (ITEMIDLIST array) structure with pointers to PIDLs " +
                "(Pointer to Item IDentifier Lists) representing Shell objects. " +
                "The first PIDL is the parent folder; subsequent PIDLs are the individual items. " +
                "Windows Shell uses this for clipboard operations on virtual and physical filesystem objects."),
            ["FileName"] = new(
                "ANSI file path string for drag-and-drop compatibility.",
                "The FileName format contains a single null-terminated ANSI file path. " +
                "It is used by legacy applications for file-based clipboard and drag-and-drop operations. " +
                "Modern applications should prefer FileNameW (Unicode) or CF_HDROP for multiple files. " +
                "The path refers to a temporary or source file containing the clipboard content."),
            ["FileNameW"] = new(
                "Unicode (UTF-16) file path string for drag-and-drop.",
                "FileNameW contains a single null-terminated UTF-16LE file path. " +
                "It is the Unicode equivalent of the FileName format and is preferred on modern Windows. " +
                "Applications use this when providing a single file as clipboard or drag-and-drop content. " +
                "For multiple files, CF_HDROP is the standard format."),
            ["UniformResourceLocator"] = new(
                "ANSI URL string for hyperlink clipboard transfer.",
                "This format contains a null-terminated ANSI string representing a URL. " +
                "Browsers and applications use this to transfer hyperlinks via the clipboard or drag-and-drop. " +
                "The URL is typically encoded in the system's default ANSI code page. " +
                "Modern applications should prefer UniformResourceLocatorW for Unicode URL support."),
            ["UniformResourceLocatorW"] = new(
                "Unicode (UTF-16) URL string for hyperlink clipboard transfer.",
                "This format contains a null-terminated UTF-16LE string representing a URL. " +
                "It is the Unicode equivalent of UniformResourceLocator and supports internationalized URLs. " +
                "Browsers place this format when a link is copied to the clipboard. " +
                "Paste targets should prefer this over the ANSI variant when both are available."),
            ["text/html"] = new(
                "HTML content from web applications (Firefox/Gecko format).",
                "Firefox and Gecko-based browsers use 'text/html' as the registered format name for HTML clipboard data. " +
                "Unlike Chromium's 'HTML Format', this typically contains raw HTML without the byte-offset header. " +
                "The encoding is usually UTF-8 or UTF-16 depending on the browser version. " +
                "Paste targets may need to handle both 'HTML Format' and 'text/html' for cross-browser compatibility."),
            ["text/x-moz-url"] = new(
                "Firefox/Mozilla URL format containing URL and title on separate lines.",
                "This Gecko-specific format contains a URL followed by a line break and the page title, encoded as UTF-16. " +
                "Firefox uses this for bookmark and link drag-and-drop operations. " +
                "The format allows paste targets to receive both the URL and a human-readable title. " +
                "Applications outside the Mozilla ecosystem generally do not produce this format."),
            ["Ole Private Data"] = new(
                "Internal OLE clipboard bookkeeping data.",
                "Ole Private Data is an internal format used by OLE (Object Linking and Embedding) to manage " +
                "clipboard state and data object lifecycle. " +
                "The data is opaque and meaningful only to the OLE subsystem. " +
                "Applications should not attempt to parse or use this format directly."),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static FormatDescription? GetDescription(uint formatId, string formatName)
    {
        if (StandardFormats.TryGetValue(formatId, out var standardDesc))
            return standardDesc;

        if (RegisteredFormats.TryGetValue(formatName, out var registeredDesc))
            return registeredDesc;

        return null;
    }

    internal static IReadOnlyDictionary<uint, FormatDescription> AllStandardFormats => StandardFormats;
    internal static IReadOnlyDictionary<string, FormatDescription> AllRegisteredFormats => RegisteredFormats;

    public static readonly FormatDescription UnknownFormatFallback = new(
        "Application-registered clipboard format.",
        "This format was registered at runtime by an application. " +
        "Format IDs 0xC000–0xFFFF are assigned dynamically by RegisterClipboardFormat(). " +
        "The format's meaning depends on the application that created it. " +
        "Consult the application's documentation for details on this format's data structure.");
}
