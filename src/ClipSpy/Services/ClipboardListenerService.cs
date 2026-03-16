using System.Windows.Interop;
using ClipSpy.Native;

namespace ClipSpy.Services;

public sealed class ClipboardListenerService : IDisposable
{
    private HwndSource? _hwndSource;
    private bool _disposed;

    public event EventHandler? ClipboardChanged;

    public nint Hwnd => _hwndSource?.Handle ?? 0;

    public void Start()
    {
        if (_hwndSource is not null)
            return;

        var parameters = new HwndSourceParameters("ClipSpyHidden")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        if (!NativeMethods.AddClipboardFormatListener(_hwndSource.Handle))
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"AddClipboardFormatListener failed with error code {error}.");
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_hwndSource is not null)
        {
            NativeMethods.RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
