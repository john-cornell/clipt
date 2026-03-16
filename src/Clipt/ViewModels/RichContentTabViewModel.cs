using System.Text;
using Clipt.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

public sealed partial class RichContentTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _htmlSource = string.Empty;

    [ObservableProperty]
    private string _htmlHeaders = string.Empty;

    [ObservableProperty]
    private string _rtfSource = string.Empty;

    [ObservableProperty]
    private bool _hasHtml;

    [ObservableProperty]
    private bool _hasRtf;

    public void Update(ClipboardSnapshot snapshot)
    {
        var htmlFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatName.Equals("HTML Format", StringComparison.OrdinalIgnoreCase));
        var rtfFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatName.Equals("Rich Text Format", StringComparison.OrdinalIgnoreCase));

        if (htmlFormat is not null && htmlFormat.RawData.Length > 0)
        {
            string raw = Encoding.UTF8.GetString(htmlFormat.RawData).TrimEnd('\0');
            ParseHtml(raw);
            HasHtml = true;
        }
        else
        {
            HtmlSource = string.Empty;
            HtmlHeaders = string.Empty;
            HasHtml = false;
        }

        if (rtfFormat is not null && rtfFormat.RawData.Length > 0)
        {
            RtfSource = Encoding.ASCII.GetString(rtfFormat.RawData).TrimEnd('\0');
            HasRtf = true;
        }
        else
        {
            RtfSource = string.Empty;
            HasRtf = false;
        }
    }

    private void ParseHtml(string raw)
    {
        var headerLines = new StringBuilder();
        var lines = raw.Split('\n');
        int headerEnd = 0;

        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("Version:", StringComparison.Ordinal) ||
                trimmed.StartsWith("StartHTML:", StringComparison.Ordinal) ||
                trimmed.StartsWith("EndHTML:", StringComparison.Ordinal) ||
                trimmed.StartsWith("StartFragment:", StringComparison.Ordinal) ||
                trimmed.StartsWith("EndFragment:", StringComparison.Ordinal) ||
                trimmed.StartsWith("StartSelection:", StringComparison.Ordinal) ||
                trimmed.StartsWith("EndSelection:", StringComparison.Ordinal) ||
                trimmed.StartsWith("SourceURL:", StringComparison.Ordinal))
            {
                headerLines.AppendLine(trimmed);
                headerEnd += line.Length + 1;
            }
            else
            {
                break;
            }
        }

        HtmlHeaders = headerLines.ToString().TrimEnd();
        HtmlSource = headerEnd < raw.Length ? raw[headerEnd..] : raw;
    }
}
