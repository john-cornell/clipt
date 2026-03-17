using Clipt.Help;

namespace Clipt.Models;

public sealed record FormatSelection(uint FormatId, string Name, long Size)
{
    public string Description =>
        FormatDescriptions.GetDescription(FormatId, Name)?.Short
        ?? FormatDescriptions.UnknownFormatFallback.Short;

    public override string ToString() => $"{Name} ({Size} bytes)";
}
