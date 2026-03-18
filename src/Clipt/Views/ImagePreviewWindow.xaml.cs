using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Clipt.Views;

public partial class ImagePreviewWindow : Window
{
    private const double MaxScreenFraction = 0.80;
    private const double ChromePadding = 12.0;

    public ImagePreviewWindow(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        InitializeComponent();
        PreviewImage.Source = image;
        SizeToImage(image);
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

        if (imgW > maxW - ChromePadding || imgH > maxH - ChromePadding)
        {
            double scale = Math.Min(
                (maxW - ChromePadding) / imgW,
                (maxH - ChromePadding) / imgH);
            imgW *= scale;
            imgH *= scale;
        }

        Width = imgW + ChromePadding;
        Height = imgH + ChromePadding;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Close();
    }
}
