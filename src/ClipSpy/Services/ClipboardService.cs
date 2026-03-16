using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ClipSpy.Models;
using ClipSpy.Native;

namespace ClipSpy.Services;

public sealed class ClipboardService : IClipboardService
{
    private const int MaxCaptureBytes = 64 * 1024;
    private const int MaxPreviewBytes = 256;

    public ClipboardSnapshot CaptureSnapshot(nint hwnd)
    {
        uint sequenceNumber = NativeMethods.GetClipboardSequenceNumber();
        (string ownerName, int ownerPid) = GetClipboardOwnerInfo();

        if (!NativeMethods.OpenClipboard(hwnd))
        {
            return new ClipboardSnapshot
            {
                Timestamp = DateTime.UtcNow,
                SequenceNumber = sequenceNumber,
                OwnerProcessName = ownerName,
                OwnerProcessId = ownerPid,
                Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
            };
        }

        try
        {
            var formats = EnumerateFormats();
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

    private static ImmutableArray<ClipboardFormatInfo> EnumerateFormats()
    {
        var builder = ImmutableArray.CreateBuilder<ClipboardFormatInfo>();
        uint formatId = 0;

        while ((formatId = NativeMethods.EnumClipboardFormats(formatId)) != 0)
        {
            var info = CaptureFormatData(formatId);
            if (info is not null)
                builder.Add(info);
        }

        return builder.ToImmutable();
    }

    private static ClipboardFormatInfo? CaptureFormatData(uint formatId)
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
            int captureSize = (int)Math.Min(dataSize, MaxCaptureBytes);
            int previewSize = (int)Math.Min(dataSize, MaxPreviewBytes);

            byte[] rawData = new byte[captureSize];
            byte[] previewData = new byte[previewSize];

            if (captureSize > 0)
                Marshal.Copy(lockPtr, rawData, 0, captureSize);

            if (previewSize > 0)
                Buffer.BlockCopy(rawData, 0, previewData, 0, previewSize);

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
                    previewData),
                RawData = rawData,
            };
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
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
}
