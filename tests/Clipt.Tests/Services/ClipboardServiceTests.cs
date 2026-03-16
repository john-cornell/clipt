using Clipt.Models;

namespace Clipt.Tests.Services;

public class ClipboardServiceTests
{
    [Theory]
    [InlineData(100, "100 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(10485760, "10.00 MB")]
    public void DataSizeFormatted_ReturnsCorrectString(long size, string expected)
    {
        var info = new ClipboardFormatInfo
        {
            FormatId = 1,
            FormatName = "test",
            IsStandard = true,
            DataSize = size,
            Memory = new MemoryInfo("0x0", "0x0", size, []),
            RawData = [],
        };

        Assert.Equal(expected, info.DataSizeFormatted);
    }

    [Fact]
    public void FormatIdHex_ReturnsCorrectHexString()
    {
        var info = new ClipboardFormatInfo
        {
            FormatId = 13,
            FormatName = "CF_UNICODETEXT",
            IsStandard = true,
            DataSize = 0,
            Memory = new MemoryInfo("0x0", "0x0", 0, []),
            RawData = [],
        };

        Assert.Equal("0x000D", info.FormatIdHex);
    }

    [Fact]
    public void ClipboardSnapshot_Empty_HasSensibleDefaults()
    {
        var empty = ClipboardSnapshot.Empty;

        Assert.Equal(DateTime.MinValue, empty.Timestamp);
        Assert.Equal(0u, empty.SequenceNumber);
        Assert.Equal("(none)", empty.OwnerProcessName);
        Assert.Equal(0, empty.OwnerProcessId);
        Assert.Empty(empty.Formats);
    }

    [Fact]
    public void MemoryInfo_Record_EqualityWorks()
    {
        var a = new MemoryInfo("0x1", "0x2", 100, [0x41]);
        var b = new MemoryInfo("0x1", "0x2", 100, [0x41]);

        Assert.NotEqual(a, b);

        var c = a with { AllocationSize = 200 };
        Assert.Equal(200, c.AllocationSize);
    }
}
