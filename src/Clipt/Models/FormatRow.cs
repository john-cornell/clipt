using Clipt.Help;

namespace Clipt.Models;

public sealed record FormatRow(
    uint Id,
    string IdHex,
    string Name,
    long SizeBytes,
    string SizeFormatted,
    string HandleHex,
    bool IsStandard)
{
    public string IdDisplay => $"{Id} ({IdHex})";

    public string ShortDescription =>
        FormatDescriptions.GetDescription(Id, Name)?.Short
        ?? FormatDescriptions.UnknownFormatFallback.Short;

    public string DetailedDescription =>
        FormatDescriptions.GetDescription(Id, Name)?.Detailed
        ?? FormatDescriptions.UnknownFormatFallback.Detailed;
}
