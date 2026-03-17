using Clipt.Models;

namespace Clipt.Services;

public interface IClipboardService
{
    ClipboardSnapshot CaptureSnapshot(nint hwnd);
    void SetClipboardText(string text, nint hwnd);
    void SetClipboardData(uint formatId, byte[] data, nint hwnd);
}
