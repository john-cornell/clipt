using System.Text;
using Clipt.Models;
using Clipt.Native;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

public sealed partial class TextTabViewModel : ObservableObject
{
    static TextTabViewModel()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [ObservableProperty]
    private string _unicodeText = string.Empty;

    [ObservableProperty]
    private string _ansiText = string.Empty;

    [ObservableProperty]
    private string _oemText = string.Empty;

    [ObservableProperty]
    private int _characterCount;

    [ObservableProperty]
    private int _lineCount;

    [ObservableProperty]
    private int _nullTerminatorPosition = -1;

    [ObservableProperty]
    private string _localeInfo = string.Empty;

    [ObservableProperty]
    private string _encodingStats = string.Empty;

    [ObservableProperty]
    private bool _hasText;

    public void Update(ClipboardSnapshot snapshot)
    {
        var unicodeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);
        var ansiFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_TEXT);
        var oemFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_OEMTEXT);
        var localeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_LOCALE);

        if (unicodeFormat is not null && unicodeFormat.RawData.Length > 0)
        {
            UnicodeText = DecodeUtf16(unicodeFormat.RawData, out int nullPos);
            NullTerminatorPosition = nullPos;
            CharacterCount = UnicodeText.Length;
            LineCount = CountLines(UnicodeText);
            HasText = true;

            int asciiCount = 0, nonAsciiCount = 0, controlCount = 0;
            foreach (char c in UnicodeText)
            {
                if (c < 0x20 && c != '\r' && c != '\n' && c != '\t')
                    controlCount++;
                else if (c < 0x80)
                    asciiCount++;
                else
                    nonAsciiCount++;
            }
            EncodingStats = $"ASCII: {asciiCount}  |  Non-ASCII: {nonAsciiCount}  |  Control: {controlCount}";
        }
        else
        {
            UnicodeText = string.Empty;
            NullTerminatorPosition = -1;
            CharacterCount = 0;
            LineCount = 0;
            HasText = false;
            EncodingStats = string.Empty;
        }

        AnsiText = ansiFormat is not null && ansiFormat.RawData.Length > 0
            ? DecodeAnsi(ansiFormat.RawData)
            : string.Empty;

        OemText = oemFormat is not null && oemFormat.RawData.Length > 0
            ? DecodeOem(oemFormat.RawData)
            : string.Empty;

        if (localeFormat is not null && localeFormat.RawData.Length >= 4)
        {
            uint lcid = BitConverter.ToUInt32(localeFormat.RawData, 0);
            try
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo((int)lcid);
                LocaleInfo = $"LCID: 0x{lcid:X4} ({lcid}) — {culture.DisplayName}";
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                LocaleInfo = $"LCID: 0x{lcid:X4} ({lcid}) — Unknown";
            }
        }
        else
        {
            LocaleInfo = "No locale data";
        }
    }

    private static string DecodeUtf16(byte[] data, out int nullTermPos)
    {
        nullTermPos = -1;
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                nullTermPos = i / 2;
                return Encoding.Unicode.GetString(data, 0, i);
            }
        }
        return Encoding.Unicode.GetString(data);
    }

    private static string DecodeAnsi(byte[] data)
    {
        int end = Array.IndexOf(data, (byte)0);
        int len = end >= 0 ? end : data.Length;
        return Encoding.Default.GetString(data, 0, len);
    }

    private static string DecodeOem(byte[] data)
    {
        int end = Array.IndexOf(data, (byte)0);
        int len = end >= 0 ? end : data.Length;
        try
        {
            var oem = Encoding.GetEncoding(437);
            return oem.GetString(data, 0, len);
        }
        catch (NotSupportedException)
        {
            return Encoding.ASCII.GetString(data, 0, len);
        }
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;

        int count = 1;
        foreach (char c in text)
        {
            if (c == '\n')
                count++;
        }
        return count;
    }
}
