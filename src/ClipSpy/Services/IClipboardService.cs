using ClipSpy.Models;

namespace ClipSpy.Services;

public interface IClipboardService
{
    ClipboardSnapshot CaptureSnapshot(nint hwnd);
}
