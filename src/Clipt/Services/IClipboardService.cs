using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardService
{
    ClipboardSnapshot CaptureSnapshot(nint hwnd);
}
