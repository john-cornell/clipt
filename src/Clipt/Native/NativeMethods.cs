using System.Runtime.InteropServices;

namespace Clipt.Native;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint EnumClipboardFormats(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", EntryPoint = "GetClipboardFormatNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClipboardFormatName(uint format, [Out] char[] lpszFormatName, int cchMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AddClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetClipboardOwner();

    [LibraryImport("user32.dll")]
    public static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint GlobalSize(nint hMem);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint DragQueryFile(nint hDrop, uint iFile, [Out] char[]? lpszFile, uint cch);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalFree(nint hMem);

    public const uint GMEM_MOVEABLE = 0x0002;
}
