namespace ClipSpy.Models;

public sealed record FormatSelection(uint FormatId, string Name, long Size)
{
    public override string ToString() => $"{Name} ({Size} bytes)";
}
