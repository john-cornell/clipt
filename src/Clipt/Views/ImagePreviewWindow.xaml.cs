using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clipt.Views;

public partial class ImagePreviewWindow : Window
{
    private const double MaxScreenFraction = 0.80;
    private const double ChromePadding = 12.0;
    private const double ToolbarReserveHeight = 36.0;

    private readonly BitmapSource _image;
    private bool _isClosing;

    public ImagePreviewWindow(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = DeepCopyAndFreeze(image);
        InitializeComponent();
        Title = $"Image preview {MainWindow.GetAppVersion()}";
        PreviewImage.Source = _image;
        SizeToImage(_image);
    }

    /// <summary>
    /// Deep pixel-level copy that strips all internal decoder, stream, and dispatcher ties.
    /// Decoder-produced BitmapFrames carry residual thread affinity in metadata even after
    /// Freeze(); a raw pixel copy to BitmapSource.Create yields a clean CachedBitmap with
    /// no such baggage.
    /// </summary>
    internal static BitmapSource DeepCopyAndFreeze(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        var format = source.Format;
        int bpp = format.BitsPerPixel;

        if (bpp == 0 || width == 0 || height == 0)
            throw new ArgumentException("Source bitmap has no pixel data.");

        int stride = (width * bpp + 7) / 8;
        byte[] pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        var clean = BitmapSource.Create(
            width, height,
            source.DpiX, source.DpiY,
            format, source.Palette,
            pixels, stride);

        clean.Freeze();
        return clean;
    }

    internal static string BuildDefaultFileName(DateTime? timestamp = null)
    {
        var t = timestamp ?? DateTime.Now;
        return $"clipboard_{t:yyyyMMdd_HHmmss}";
    }

    internal static BitmapEncoder CreateEncoder(int filterIndex) =>
        filterIndex switch
        {
            1 => new PngBitmapEncoder(),
            2 => new BmpBitmapEncoder(),
            3 => new JpegBitmapEncoder { QualityLevel = 95 },
            4 => new GifBitmapEncoder(),
            5 => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };

    internal static BitmapFrame CreateBitmapFrame(BitmapSource source, BitmapEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(encoder);

        if (encoder is GifBitmapEncoder)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Indexed8;
            converted.DestinationPalette = BitmapPalettes.WebPalette;
            converted.EndInit();
            if (converted.CanFreeze)
                converted.Freeze();
            return BitmapFrame.Create(converted);
        }

        return BitmapFrame.Create(source);
    }

    private void SizeToImage(BitmapSource image)
    {
        var workArea = SystemParameters.WorkArea;
        double maxW = workArea.Width * MaxScreenFraction;
        double maxH = workArea.Height * MaxScreenFraction;

        double dpiScaleX = image.DpiX > 0 ? image.DpiX / 96.0 : 1.0;
        double dpiScaleY = image.DpiY > 0 ? image.DpiY / 96.0 : 1.0;
        double imgW = image.PixelWidth / dpiScaleX;
        double imgH = image.PixelHeight / dpiScaleY;

        if (imgW > maxW - ChromePadding || imgH > maxH - ChromePadding - ToolbarReserveHeight)
        {
            double scale = Math.Min(
                (maxW - ChromePadding) / imgW,
                (maxH - ChromePadding - ToolbarReserveHeight) / imgH);
            imgW *= scale;
            imgH *= scale;
        }

        Width = imgW + ChromePadding;
        Height = imgH + ChromePadding + ToolbarReserveHeight;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            RequestClose();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        RequestClose();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, RequestClose);
    }

    private void RequestClose()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // Window already closing or disposed via another path.
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Deactivated -= OnDeactivated;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Image As",
                Filter =
                    "PNG Image (*.png)|*.png|BMP Image (*.bmp)|*.bmp|JPEG Image (*.jpg;*.jpeg)|*.jpg|GIF Image (*.gif)|*.gif|TIFF Image (*.tiff;*.tif)|*.tiff",
                DefaultExt = ".png",
                FileName = BuildDefaultFileName(),
            };

            if (dialog.ShowDialog(this) != true)
                return;

            BitmapEncoder encoder = CreateEncoder(dialog.FilterIndex);
            encoder.Frames.Add(CreateBitmapFrame(_image, encoder));

            try
            {
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Could not save the image:\n{ex.Message}",
                    "Save failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            Deactivated += OnDeactivated;
        }
    }
}
