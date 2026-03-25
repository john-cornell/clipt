using System.Runtime.InteropServices;
using Clipt.Models;
using Clipt.Native;

namespace Clipt.Services;

public static class SingleInstanceActivation
{
    public const string WindowMessageName = "Clipt_SecondInstanceActivate";
    public const string MutexName = @"Global\Clipt_SingleInstance_7E2A9B3C4D5E6F708192A3B4C5D6E7F8";
    public const string HiddenWindowTitle = "CliptHidden";

    public static StartupMode StartupModeFromWParam(int wParam)
    {
        if (Enum.IsDefined(typeof(StartupMode), wParam))
            return (StartupMode)wParam;
        return StartupMode.FullWindow;
    }

    /// <summary>
    /// Creates or opens the named mutex. On success, <paramref name="ownsMutex"/> is true only for the first process.
    /// </summary>
    public static bool TryAcquireMutex(out Mutex? mutex, out bool ownsMutex)
    {
        mutex = null;
        ownsMutex = false;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            ownsMutex = createdNew;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public static bool TryNotifyRunningInstance(StartupMode mode)
    {
        uint msgId = NativeMethods.RegisterWindowMessage(WindowMessageName);
        if (msgId == 0)
            return false;

        nint wParam = (nint)(int)mode;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            nint hwnd = NativeMethods.FindWindow(null, HiddenWindowTitle);
            if (hwnd == 0)
                hwnd = NativeMethods.FindTopLevelWindowByTitle(HiddenWindowTitle);

            if (hwnd != 0)
                return NativeMethods.PostMessage(hwnd, msgId, wParam, 0);

            Thread.Sleep(50);
        }

        return false;
    }
}
