using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipt.Models;
using Clipt.Native;

namespace Clipt.Services;

public sealed class ClipboardService : IClipboardService
{
    /// <summary>Hard ceiling for copying a single format from the clipboard (largest span <c>Marshal.Copy</c> supports).</summary>
    public const long NonImageFormatAbsoluteMaxBytes = int.MaxValue;

    private const long DefaultMaxFormatCaptureBytes = 64 * 1024;
    private const long ImageMaxCaptureBytes = int.MaxValue;

    private readonly ISettingsService _settingsService;
    private readonly IClipboardFormatOversizePrompt _oversizePrompt;

    public ClipboardService(ISettingsService settingsService, IClipboardFormatOversizePrompt oversizePrompt)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _oversizePrompt = oversizePrompt ?? throw new ArgumentNullException(nameof(oversizePrompt));
    }

    public ClipboardSnapshot CaptureSnapshot(nint hwnd)
    {
        (string ownerName, int ownerPid) = GetClipboardOwnerInfo();

        if (!NativeMethods.OpenClipboard(hwnd))
        {
            return new ClipboardSnapshot
            {
                Timestamp = DateTime.UtcNow,
                SequenceNumber = NativeMethods.GetClipboardSequenceNumber(),
                OwnerProcessName = ownerName,
                OwnerProcessId = ownerPid,
                Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
            };
        }

        try
        {
            uint sequenceNumber = NativeMethods.GetClipboardSequenceNumber();
            ImmutableArray<ClipboardFormatInfo> formats = EnumerateFormats();
            return new ClipboardSnapshot
            {
                Timestamp = DateTime.UtcNow,
                SequenceNumber = sequenceNumber,
                OwnerProcessName = ownerName,
                OwnerProcessId = ownerPid,
                Formats = formats,
            };
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private ImmutableArray<ClipboardFormatInfo> EnumerateFormats()
    {
        var builder = ImmutableArray.CreateBuilder<ClipboardFormatInfo>();
        uint formatId = 0;

        while ((formatId = NativeMethods.EnumClipboardFormats(formatId)) != 0)
        {
            ClipboardFormatInfo? info = CaptureFormatData(formatId);
            if (info is not null)
                builder.Add(info);
        }

        return builder.ToImmutable();
    }

    private ClipboardFormatInfo? CaptureFormatData(uint formatId)
    {
        string name = ClipboardConstants.GetFormatName(formatId);
        bool isStandard = ClipboardConstants.IsStandardFormat(formatId);

        nint handle = NativeMethods.GetClipboardData(formatId);
        if (handle == 0)
        {
            return new ClipboardFormatInfo
            {
                FormatId = formatId,
                FormatName = name,
                IsStandard = isStandard,
                DataSize = 0,
                Memory = new MemoryInfo("0x0", "0x0", 0, []),
                RawData = [],
            };
        }

        if (ClipboardConstants.IsGdiHandleFormat(formatId))
        {
            return new ClipboardFormatInfo
            {
                FormatId = formatId,
                FormatName = name,
                IsStandard = isStandard,
                DataSize = 0,
                Memory = new MemoryInfo(
                    $"0x{handle:X16}",
                    "(GDI handle — not lockable)",
                    0,
                    []),
                RawData = [],
            };
        }

        nuint rawSize = NativeMethods.GlobalSize(handle);
        long dataSize = (long)rawSize;

        nint lockPtr = NativeMethods.GlobalLock(handle);
        if (lockPtr == 0)
        {
            return new ClipboardFormatInfo
            {
                FormatId = formatId,
                FormatName = name,
                IsStandard = isStandard,
                DataSize = dataSize,
                Memory = new MemoryInfo(
                    $"0x{handle:X16}",
                    "0x0 (lock failed)",
                    dataSize,
                    []),
                RawData = [],
            };
        }

        try
        {
            long capBytes = GetEffectiveFormatCaptureCapBytes();
            long bytesToCopy = IsImageFormat(formatId)
                ? Math.Min(dataSize, ImageMaxCaptureBytes)
                : ComputeNonImageBytesToCopy(dataSize, capBytes, formatId, name);

            int captureSize = LongToIntCopyLength(bytesToCopy);
            byte[] rawData = new byte[captureSize];

            if (captureSize > 0)
                Marshal.Copy(lockPtr, rawData, 0, captureSize);

            return new ClipboardFormatInfo
            {
                FormatId = formatId,
                FormatName = name,
                IsStandard = isStandard,
                DataSize = dataSize,
                Memory = new MemoryInfo(
                    $"0x{handle:X16}",
                    $"0x{lockPtr:X16}",
                    dataSize,
                    []),
                RawData = rawData,
            };
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private long GetEffectiveFormatCaptureCapBytes()
    {
        long v = _settingsService.LoadMaxClipboardFormatCaptureBytes();
        if (v <= 0)
            v = DefaultMaxFormatCaptureBytes;

        return Math.Clamp(v, 1024, NonImageFormatAbsoluteMaxBytes);
    }

    private long ComputeNonImageBytesToCopy(long dataSize, long capBytes, uint formatId, string formatName)
    {
        ClipboardFormatOversizeMode mode = _settingsService.LoadClipboardFormatOversizeMode();
        ClipboardFormatOversizeReply ask = ClipboardFormatOversizeReply.TruncateToCap;
        if (dataSize > capBytes && mode == ClipboardFormatOversizeMode.AskEachFormat)
            ask = _oversizePrompt.Prompt(formatId, formatName, dataSize, capBytes);

        return ComputeNonImageBytesToCapture(dataSize, capBytes, mode, ask);
    }

    /// <summary>Deterministic capture length for non-image formats (used by the capture path and unit tests).</summary>
    internal static long ComputeNonImageBytesToCapture(
        long globalAllocSize,
        long capBytes,
        ClipboardFormatOversizeMode mode,
        ClipboardFormatOversizeReply askResultIfOversized)
    {
        if (globalAllocSize <= 0)
            return 0;

        capBytes = Math.Clamp(capBytes, 1024, NonImageFormatAbsoluteMaxBytes);

        if (globalAllocSize <= capBytes)
            return Math.Min(globalAllocSize, NonImageFormatAbsoluteMaxBytes);

        return mode switch
        {
            ClipboardFormatOversizeMode.AskEachFormat => askResultIfOversized == ClipboardFormatOversizeReply.CaptureFull
                ? Math.Min(globalAllocSize, NonImageFormatAbsoluteMaxBytes)
                : capBytes,
            _ => capBytes,
        };
    }

    private static int LongToIntCopyLength(long bytes)
    {
        if (bytes <= 0)
            return 0;
        if (bytes > int.MaxValue)
            return int.MaxValue;
        return (int)bytes;
    }

    private static (string Name, int Pid) GetClipboardOwnerInfo()
    {
        nint ownerHwnd = NativeMethods.GetClipboardOwner();
        if (ownerHwnd == 0)
            return ("(no owner)", 0);

        NativeMethods.GetWindowThreadProcessId(ownerHwnd, out uint pid);
        if (pid == 0)
            return ("(unknown)", 0);

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return (process.ProcessName, (int)pid);
        }
        catch (ArgumentException)
        {
            return ($"(PID {pid} exited)", (int)pid);
        }
    }

    private static bool IsImageFormat(uint formatId) =>
        formatId is ClipboardConstants.CF_DIB or ClipboardConstants.CF_DIBV5
            or ClipboardConstants.CF_TIFF;

    public void SetClipboardText(string text, nint hwnd)
    {
        ArgumentNullException.ThrowIfNull(text);
        string terminated = text.Contains('\0') ? text : text + "\0";
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes(terminated);
        SetClipboardData(ClipboardConstants.CF_UNICODETEXT, utf16, hwnd);
    }

    public void SetClipboardData(uint formatId, byte[] data, nint hwnd)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("Data must not be empty.", nameof(data));

        if (!NativeMethods.OpenClipboard(hwnd))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            if (!NativeMethods.EmptyClipboard())
                throw new InvalidOperationException("Failed to empty clipboard.");

            AllocAndSetFormat(formatId, data);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public void SetMultipleClipboardData(IReadOnlyList<(uint FormatId, byte[] Data)> formats, nint hwnd)
    {
        ArgumentNullException.ThrowIfNull(formats);
        if (formats.Count == 0)
            throw new ArgumentException("At least one format is required.", nameof(formats));

        if (!NativeMethods.OpenClipboard(hwnd))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            if (!NativeMethods.EmptyClipboard())
                throw new InvalidOperationException("Failed to empty clipboard.");

            foreach (var (formatId, data) in formats)
            {
                if (data.Length == 0)
                    continue;

                AllocAndSetFormat(formatId, data);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public void ClearClipboard(nint hwnd)
    {
        if (!NativeMethods.OpenClipboard(hwnd))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            if (!NativeMethods.EmptyClipboard())
                throw new InvalidOperationException("Failed to empty clipboard.");
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static void AllocAndSetFormat(uint formatId, byte[] data)
    {
        nint hGlobal = NativeMethods.GlobalAlloc(
            NativeMethods.GMEM_MOVEABLE, (nuint)data.Length);

        if (hGlobal == 0)
            throw new OutOfMemoryException("GlobalAlloc failed.");

        nint lockPtr = NativeMethods.GlobalLock(hGlobal);
        if (lockPtr == 0)
        {
            NativeMethods.GlobalFree(hGlobal);
            throw new InvalidOperationException("GlobalLock failed.");
        }

        try
        {
            Marshal.Copy(data, 0, lockPtr, data.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(hGlobal);
        }

        nint result = NativeMethods.SetClipboardData(formatId, hGlobal);
        if (result == 0)
        {
            NativeMethods.GlobalFree(hGlobal);
            throw new InvalidOperationException(
                $"SetClipboardData failed for format 0x{formatId:X4}.");
        }
    }
}
