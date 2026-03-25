using System.Windows.Interop;
using Clipt.Models;
using Clipt.Native;

namespace Clipt.Services;

public sealed class ClipboardListenerService : IDisposable
{
    private readonly IAppLogger _logger;
    private HwndSource? _hwndSource;
    private uint _secondInstanceActivateMsg;
    private bool _disposed;

    public ClipboardListenerService(IAppLogger logger)
    {
        _logger = logger;
    }

    public event EventHandler? ClipboardChanged;
    public event EventHandler<SecondInstanceActivateEventArgs>? SecondInstanceActivateRequested;

    public nint Hwnd => _hwndSource?.Handle ?? 0;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hwndSource is not null)
            return;

        var parameters = new HwndSourceParameters("CliptHidden")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        _secondInstanceActivateMsg = NativeMethods.RegisterWindowMessage(SingleInstanceActivation.WindowMessageName);

        if (!NativeMethods.AddClipboardFormatListener(_hwndSource.Handle))
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"AddClipboardFormatListener failed with error code {error}.");
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_secondInstanceActivateMsg != 0 && (uint)msg == _secondInstanceActivateMsg)
        {
            SecondInstanceActivateRequested?.Invoke(this, new SecondInstanceActivateEventArgs((int)wParam));
            handled = true;
            return 0;
        }

        if (msg == ClipboardConstants.WM_CLIPBOARDUPDATE)
        {
            if (_logger.Level >= AppLogLevel.Debug)
            {
                uint seq = NativeMethods.GetClipboardSequenceNumber();
                _logger.Debug(
                    $"WM_CLIPBOARDUPDATE nativeSeq={seq} managedThread={Environment.CurrentManagedThreadId}");
            }

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
