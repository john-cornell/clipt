using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipt.Views;

namespace Clipt.Tests.Views;

public class ImagePreviewWindowTests
{
    [Fact]
    public void CreateEncoder_FilterIndex1_ReturnsPngEncoder()
    {
        var encoder = ImagePreviewWindow.CreateEncoder(1);
        Assert.IsType<PngBitmapEncoder>(encoder);
    }

    [Fact]
    public void CreateEncoder_FilterIndex2_ReturnsBmpEncoder()
    {
        var encoder = ImagePreviewWindow.CreateEncoder(2);
        Assert.IsType<BmpBitmapEncoder>(encoder);
    }

    [Fact]
    public void CreateEncoder_FilterIndex3_ReturnsJpegEncoder()
    {
        var encoder = ImagePreviewWindow.CreateEncoder(3);
        var jpeg = Assert.IsType<JpegBitmapEncoder>(encoder);
        Assert.Equal(95, jpeg.QualityLevel);
    }

    [Fact]
    public void CreateEncoder_FilterIndex4_ReturnsGifEncoder()
    {
        var encoder = ImagePreviewWindow.CreateEncoder(4);
        Assert.IsType<GifBitmapEncoder>(encoder);
    }

    [Fact]
    public void CreateEncoder_FilterIndex5_ReturnsTiffEncoder()
    {
        var encoder = ImagePreviewWindow.CreateEncoder(5);
        Assert.IsType<TiffBitmapEncoder>(encoder);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void CreateEncoder_InvalidIndex_DefaultsToPng(int filterIndex)
    {
        var encoder = ImagePreviewWindow.CreateEncoder(filterIndex);
        Assert.IsType<PngBitmapEncoder>(encoder);
    }

    [Fact]
    public void BuildDefaultFileName_ContainsTimestamp()
    {
        var fixedTime = new DateTime(2025, 3, 20, 14, 30, 45, DateTimeKind.Local);
        var name = ImagePreviewWindow.BuildDefaultFileName(fixedTime);
        Assert.Equal("clipboard_20250320_143045", name);
    }

    [Fact]
    public void DeepCopyAndFreeze_ReturnsFrozenBitmap()
    {
        var wb = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);

        var result = ImagePreviewWindow.DeepCopyAndFreeze(wb);

        Assert.True(result.IsFrozen);
    }

    [Fact]
    public void DeepCopyAndFreeze_PreservesDimensions()
    {
        var wb = new WriteableBitmap(16, 24, 96, 96, PixelFormats.Bgra32, null);

        var result = ImagePreviewWindow.DeepCopyAndFreeze(wb);

        Assert.Equal(16, result.PixelWidth);
        Assert.Equal(24, result.PixelHeight);
    }

    [Fact]
    public void DeepCopyAndFreeze_PreservesFormat()
    {
        var wb = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgr24, null);

        var result = ImagePreviewWindow.DeepCopyAndFreeze(wb);

        Assert.Equal(PixelFormats.Bgr24, result.Format);
    }

    [Fact]
    public void DeepCopyAndFreeze_PreservesPixelData()
    {
        var wb = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        byte[] srcPixels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 128, 128, 128, 255];
        wb.WritePixels(new System.Windows.Int32Rect(0, 0, 2, 2), srcPixels, 8, 0);

        var result = ImagePreviewWindow.DeepCopyAndFreeze(wb);

        byte[] dstPixels = new byte[16];
        result.CopyPixels(dstPixels, 8, 0);
        Assert.Equal(srcPixels, dstPixels);
    }

    [Fact]
    public void DeepCopyAndFreeze_AlreadyFrozenSource_StillReturnsCleanFrozenCopy()
    {
        var wb = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
        wb.Freeze();

        var result = ImagePreviewWindow.DeepCopyAndFreeze(wb);

        Assert.True(result.IsFrozen);
        Assert.Equal(4, result.PixelWidth);
    }

    [Fact]
    public void CreateBitmapFrame_GifEncoder_ProducesIndexed8Frame()
    {
        var source = ImagePreviewWindow.DeepCopyAndFreeze(
            new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null));

        var frame = ImagePreviewWindow.CreateBitmapFrame(source, new GifBitmapEncoder());

        Assert.Equal(PixelFormats.Indexed8, frame.Format);
    }

    [Fact]
    public void CreateBitmapFrame_PngEncoder_PreservesSourceFormat()
    {
        var source = ImagePreviewWindow.DeepCopyAndFreeze(
            new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null));

        var frame = ImagePreviewWindow.CreateBitmapFrame(source, new PngBitmapEncoder());

        Assert.Equal(PixelFormats.Bgra32, frame.Format);
    }

    [Fact]
    public void CreateBitmapFrame_WithDeepCopiedSource_DoesNotThrow()
    {
        var source = ImagePreviewWindow.DeepCopyAndFreeze(
            new WriteableBitmap(10, 10, 96, 96, PixelFormats.Bgra32, null));

        var encoder = new PngBitmapEncoder();
        var frame = ImagePreviewWindow.CreateBitmapFrame(source, encoder);
        encoder.Frames.Add(frame);

        using var ms = new MemoryStream();
        encoder.Save(ms);

        Assert.True(ms.Length > 0);
    }
}
