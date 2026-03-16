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
}
