using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using Clipt.Models;
using Clipt.Native;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class TrayPopupViewModel : ObservableObject
{
    private const int MaxPreviewLength = 500;

    [ObservableProperty]
    private string _clipboardSummary = "Clipboard is empty";

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private ObservableCollection<string> _previewFiles = [];

    [ObservableProperty]
    private bool _hasText;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _hasFiles;

    [ObservableProperty]
    private bool _isEmpty = true;

    public event EventHandler? ExpandToFullRequested;

    [RelayCommand]
    private void ExpandToFull()
    {
        ExpandToFullRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Update(ClipboardSnapshot snapshot)
    {
        int formatCount = snapshot.Formats.Length;

        if (formatCount == 0)
        {
            ClearAll();
            return;
        }

        IsEmpty = false;
        ClipboardSummary = formatCount == 1
            ? "1 format on clipboard"
            : $"{formatCount} formats on clipboard";

        UpdateTextPreview(snapshot);
        UpdateImagePreview(snapshot);
        UpdateFilePreview(snapshot);
    }

    private void UpdateTextPreview(ClipboardSnapshot snapshot)
    {
        var unicodeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);

        if (unicodeFormat is null || unicodeFormat.RawData.Length == 0)
        {
            PreviewText = string.Empty;
            HasText = false;
            return;
        }

        string decoded = DecodeUtf16Truncated(unicodeFormat.RawData, MaxPreviewLength);
        PreviewText = decoded;
        HasText = decoded.Length > 0;
    }

    private void UpdateImagePreview(ClipboardSnapshot snapshot)
    {
        var dibv5 = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIBV5);
        var dib = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIB);
        ClipboardFormatInfo? imageFormat = dibv5 ?? dib;

        if (imageFormat is null || imageFormat.RawData.Length < 40)
        {
            TryWpfImageFallback();
            return;
        }

        try
        {
            var bmp = ImageTabViewModel.DecodeDeviceIndependentBitmap(imageFormat.RawData);
            if (bmp is not null)
            {
                if (!bmp.IsFrozen)
                    bmp.Freeze();
                PreviewImage = bmp;
                HasImage = true;
                return;
            }
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
        }

        TryWpfImageFallback();
    }

    private void TryWpfImageFallback()
    {
        try
        {
            var bmp = System.Windows.Clipboard.GetImage();
            if (bmp is not null)
            {
                if (!bmp.IsFrozen)
                    bmp.Freeze();
                PreviewImage = bmp;
                HasImage = true;
                return;
            }
        }
        catch (Exception ex) when (
            ex is ExternalException
              or InvalidOperationException
              or ThreadStateException)
        {
        }

        PreviewImage = null;
        HasImage = false;
    }

    private void UpdateFilePreview(ClipboardSnapshot snapshot)
    {
        var hdropFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_HDROP);

        PreviewFiles.Clear();

        if (hdropFormat is null || hdropFormat.RawData.Length < 20)
        {
            HasFiles = false;
            return;
        }

        var paths = ParseFilePaths(hdropFormat.RawData);
        foreach (string path in paths)
            PreviewFiles.Add(path);

        HasFiles = PreviewFiles.Count > 0;
    }

    private void ClearAll()
    {
        ClipboardSummary = "Clipboard is empty";
        PreviewText = string.Empty;
        PreviewImage = null;
        PreviewFiles.Clear();
        HasText = false;
        HasImage = false;
        HasFiles = false;
        IsEmpty = true;
    }

    internal static string DecodeUtf16Truncated(byte[] data, int maxChars)
    {
        int charCount = 0;
        int byteEnd = 0;

        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
                break;

            charCount++;
            byteEnd = i + 2;

            if (charCount >= maxChars)
                break;
        }

        if (byteEnd == 0)
            return string.Empty;

        return Encoding.Unicode.GetString(data, 0, byteEnd);
    }

    private static List<string> ParseFilePaths(byte[] data)
    {
        var result = new List<string>();

        int fileOffset = BitConverter.ToInt32(data, 0);
        bool isWide = BitConverter.ToInt32(data, 16) != 0;

        if (fileOffset >= data.Length)
            return result;

        if (isWide)
        {
            int pos = fileOffset;
            while (pos + 1 < data.Length)
            {
                int start = pos;
                while (pos + 1 < data.Length && (data[pos] != 0 || data[pos + 1] != 0))
                    pos += 2;

                if (pos > start)
                    result.Add(Encoding.Unicode.GetString(data, start, pos - start));

                pos += 2;
                if (pos + 1 >= data.Length || (data[pos] == 0 && data[pos + 1] == 0))
                    break;
            }
        }
        else
        {
            int pos = fileOffset;
            while (pos < data.Length)
            {
                int start = pos;
                while (pos < data.Length && data[pos] != 0)
                    pos++;

                if (pos > start)
                    result.Add(Encoding.Default.GetString(data, start, pos - start));

                pos++;
                if (pos >= data.Length || data[pos] == 0)
                    break;
            }
        }

        return result;
    }
}
