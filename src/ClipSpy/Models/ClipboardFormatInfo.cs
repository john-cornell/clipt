namespace ClipSpy.Models;

public sealed class ClipboardFormatInfo
{
    public required uint FormatId { get; init; }
    public required string FormatName { get; init; }
    public required bool IsStandard { get; init; }
    public required long DataSize { get; init; }
    public required MemoryInfo Memory { get; init; }
    public required byte[] RawData { get; init; }

    public string FormatIdHex => $"0x{FormatId:X4}";
    public string DataSizeFormatted => Formatting.FormatDataSize(DataSize);
}
