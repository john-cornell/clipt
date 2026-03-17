using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Clipt.Models;
using Clipt.Native;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

public sealed partial class ImageTabViewModel : ObservableObject
{
    [ObservableProperty]
    private BitmapSource? _imageSource;

    [ObservableProperty]
    private int _pixelWidth;

    [ObservableProperty]
    private int _pixelHeight;

    [ObservableProperty]
    private double _dpiX;

    [ObservableProperty]
    private double _dpiY;

    [ObservableProperty]
    private string _pixelFormat = string.Empty;

    [ObservableProperty]
    private int _stride;

    [ObservableProperty]
    private int _bitsPerPixel;

    [ObservableProperty]
    private string _paletteInfo = string.Empty;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private string _imageSummary = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void Update(ClipboardSnapshot snapshot)
    {
        var dibv5 = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIBV5);
        var dib = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIB);

        ClipboardFormatInfo? imageFormat = dibv5 ?? dib;

        if (imageFormat is null || imageFormat.RawData.Length < 40)
        {
            if (TryWpfClipboardFallback())
                return;

            ClearImage();
            return;
        }

        try
        {
            var bmp = DecodeDeviceIndependentBitmap(imageFormat.RawData);
            if (bmp is null)
            {
                if (TryWpfClipboardFallback())
                    return;

                ClearImage();
                return;
            }

            ApplyBitmapSource(bmp);
        }
        catch (Exception ex) when (
            ex is NotSupportedException
              or InvalidOperationException
              or IOException
              or ArgumentException
              or ExternalException
              or OverflowException
              or FileFormatException)
        {
            if (TryWpfClipboardFallback())
                return;

            ClearImage();
            ErrorMessage = $"Decode failed: {ex.Message}";
            HasError = true;
        }
    }

    private bool TryWpfClipboardFallback()
    {
        try
        {
            var bmp = System.Windows.Clipboard.GetImage();
            if (bmp is null)
                return false;

            ApplyBitmapSource(bmp);
            return true;
        }
        catch (Exception ex) when (
            ex is ExternalException
              or InvalidOperationException
              or ThreadStateException)
        {
            return false;
        }
    }

    private void ApplyBitmapSource(BitmapSource bmp)
    {
        if (!bmp.IsFrozen)
            bmp.Freeze();

        ImageSource = bmp;
        PixelWidth = bmp.PixelWidth;
        PixelHeight = bmp.PixelHeight;
        DpiX = bmp.DpiX;
        DpiY = bmp.DpiY;
        PixelFormat = bmp.Format.ToString();
        Stride = ((bmp.PixelWidth * bmp.Format.BitsPerPixel + 31) / 32) * 4;
        BitsPerPixel = bmp.Format.BitsPerPixel;
        PaletteInfo = bmp.Palette is not null
            ? $"{bmp.Palette.Colors.Count} colors"
            : "No palette (direct color)";
        HasImage = true;
        HasError = false;
        ErrorMessage = string.Empty;
        ImageSummary = $"{PixelWidth}×{PixelHeight} @ {BitsPerPixel}bpp, {DpiX:F0}×{DpiY:F0} DPI";
    }

    private void ClearImage()
    {
        ImageSource = null;
        PixelWidth = 0;
        PixelHeight = 0;
        DpiX = 0;
        DpiY = 0;
        PixelFormat = string.Empty;
        Stride = 0;
        BitsPerPixel = 0;
        PaletteInfo = string.Empty;
        HasImage = false;
        HasError = false;
        ErrorMessage = string.Empty;
        ImageSummary = string.Empty;
    }

    internal static BitmapSource? DecodeDeviceIndependentBitmap(byte[] dibData)
    {
        const int BitmapFileHeaderSize = 14;
        if (dibData.Length < 40)
            return null;

        int headerSize = BitConverter.ToInt32(dibData, 0);
        if (headerSize < 40)
            return null;

        int fileSize = BitmapFileHeaderSize + dibData.Length;
        byte[] bmpData = new byte[fileSize];

        bmpData[0] = (byte)'B';
        bmpData[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmpData.AsSpan(2), fileSize);

        int bitsOffset = CalculatePixelDataOffset(dibData, headerSize);
        BitConverter.TryWriteBytes(bmpData.AsSpan(10), BitmapFileHeaderSize + bitsOffset);

        Buffer.BlockCopy(dibData, 0, bmpData, BitmapFileHeaderSize, dibData.Length);

        using var ms = new MemoryStream(bmpData);
        var decoder = new BmpBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
    }

    private static int CalculatePixelDataOffset(byte[] dib, int headerSize)
    {
        int bitsPerPixel = BitConverter.ToInt16(dib, 14);
        int colorsUsed = headerSize >= 36 ? BitConverter.ToInt32(dib, 32) : 0;

        int paletteSize;
        if (bitsPerPixel <= 8)
        {
            int paletteEntries = colorsUsed > 0 ? colorsUsed : (1 << bitsPerPixel);
            paletteSize = paletteEntries * 4;
        }
        else
        {
            paletteSize = colorsUsed * 4;
        }

        int compression = BitConverter.ToInt32(dib, 16);
        int maskSize = 0;
        if (headerSize == 40)
        {
            if (compression == 3)
                maskSize = 12;
            else if (compression == 6)
                maskSize = 16;
        }

        return headerSize + paletteSize + maskSize;
    }
}
