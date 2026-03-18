using System.Windows.Interop;
using System.Windows.Threading;
using Clipt.Native;

namespace Clipt.Services;

public sealed class ClipboardListenerService : IDisposable
{
    private HwndSource? _hwndSource;
    private DispatcherTimer? _debounceTimer;
    private bool _disposed;

    internal static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    public event EventHandler? ClipboardChanged;

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

        if (!NativeMethods.AddClipboardFormatListener(_hwndSource.Handle))
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"AddClipboardFormatListener failed with error code {error}.");
        }

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = DebounceInterval,
        };
        _debounceTimer.Tick += OnDebounceElapsed;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == ClipboardConstants.WM_CLIPBOARDUPDATE)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
            handled = true;
        }

        return 0;
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        ClipboardChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_debounceTimer is not null)
        {
            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceElapsed;
            _debounceTimer = null;
        }

        if (_hwndSource is not null)
        {
            NativeMethods.RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
