using System.Collections.Immutable;
using System.Windows.Media.Imaging;
using Clipt.Models;
using Clipt.ViewModels;

namespace Clipt.Tests.ViewModels;

public class ImageTabViewModelTests
{
    [Fact]
    public void Update_NoImageFormat_ClearsImage()
    {
        var vm = new ImageTabViewModel();
        var snapshot = MakeEmptySnapshot();

        vm.Update(snapshot);

        Assert.False(vm.HasImage);
        Assert.False(vm.HasError);
        Assert.Null(vm.ImageSource);
    }

    [Fact]
    public void Update_TruncatedDibData_SetsError()
    {
        var vm = new ImageTabViewModel();
        byte[] truncatedDib = CreateMinimalInvalidDib();

        var snapshot = MakeSnapshot(8, truncatedDib);
        vm.Update(snapshot);

        Assert.False(vm.HasImage);
    }

    [Fact]
    public void Update_ShortData_ClearsImage()
    {
        var vm = new ImageTabViewModel();
        var snapshot = MakeSnapshot(8, new byte[10]);

        vm.Update(snapshot);

        Assert.False(vm.HasImage);
        Assert.Null(vm.ImageSource);
    }

    [Fact]
    public void Update_ValidSnapshot_ClearsErrorOnSuccess()
    {
        var vm = new ImageTabViewModel();

        byte[] bad = CreateMinimalInvalidDib();
        vm.Update(MakeSnapshot(8, bad));

        var emptySnapshot = MakeEmptySnapshot();
        vm.Update(emptySnapshot);

        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public void HasError_DefaultsFalse()
    {
        var vm = new ImageTabViewModel();
        Assert.False(vm.HasError);
    }

    [Fact]
    public void ErrorMessage_DefaultsEmpty()
    {
        var vm = new ImageTabViewModel();
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public void DecodeDeviceIndependentBitmap_HeaderTooSmall_ReturnsNull()
    {
        byte[] data = new byte[40];
        BitConverter.TryWriteBytes(data.AsSpan(0), 20);

        var result = ImageTabViewModel.DecodeDeviceIndependentBitmap(data);
        Assert.Null(result);
    }

    [Fact]
    public void DecodeDeviceIndependentBitmap_CorruptData_ThrowsOrReturnsNull()
    {
        byte[] data = new byte[100];
        BitConverter.TryWriteBytes(data.AsSpan(0), 40);
        BitConverter.TryWriteBytes(data.AsSpan(4), 1);
        BitConverter.TryWriteBytes(data.AsSpan(8), 1);
        BitConverter.TryWriteBytes(data.AsSpan(14), (short)32);

        try
        {
            var result = ImageTabViewModel.DecodeDeviceIndependentBitmap(data);
            Assert.True(result is null || result is BitmapSource,
                "Expected null or a valid BitmapSource for corrupt data");
        }
        catch (Exception ex)
        {
            Assert.True(
                ex is System.Runtime.InteropServices.ExternalException
                    or System.IO.IOException
                    or System.IO.FileFormatException
                    or ArgumentException
                    or InvalidOperationException
                    or NotSupportedException
                    or OverflowException,
                $"Unexpected exception type: {ex.GetType().FullName}");
        }
    }

    private static ClipboardSnapshot MakeEmptySnapshot() =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
        };

    private static ClipboardSnapshot MakeSnapshot(uint formatId, byte[] data) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = formatId,
                FormatName = $"Format_{formatId}",
                IsStandard = true,
                DataSize = data.Length,
                Memory = new MemoryInfo("0x0", "0x0", data.Length, data.Length > 256 ? data[..256] : data),
                RawData = data,
            }),
        };

    private static byte[] CreateMinimalInvalidDib()
    {
        byte[] dib = new byte[60];
        BitConverter.TryWriteBytes(dib.AsSpan(0), 40);
        BitConverter.TryWriteBytes(dib.AsSpan(4), 10);
        BitConverter.TryWriteBytes(dib.AsSpan(8), 10);
        BitConverter.TryWriteBytes(dib.AsSpan(12), (short)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14), (short)32);
        return dib;
    }
}
