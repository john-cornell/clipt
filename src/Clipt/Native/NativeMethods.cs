using System.Runtime.InteropServices;

namespace Clipt.Native;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(nint hWnd, [Out] char[] lpString, int nMaxCount);

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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    private sealed class FindTitleContext
    {
        public required string Title;
        public nint Found;
    }

    private static bool EnumByTitleCallback(nint hWnd, nint lParam)
    {
        var ctx = (FindTitleContext)GCHandle.FromIntPtr(lParam).Target!;
        int len = GetWindowTextLength(hWnd);
        if (len <= 0)
            return true;

        var buf = new char[len + 1];
        int n = GetWindowText(hWnd, buf, buf.Length);
        if (n > 0 && new string(buf, 0, n) == ctx.Title)
        {
            ctx.Found = hWnd;
            return false;
        }

        return true;
    }

    internal static nint FindTopLevelWindowByTitle(string title)
    {
        var ctx = new FindTitleContext { Title = title, Found = 0 };
        var gch = GCHandle.Alloc(ctx);
        try
        {
            EnumWindows(EnumByTitleCallback, GCHandle.ToIntPtr(gch));
        }
        finally
        {
            gch.Free();
        }

        return ctx.Found;
    }
}
