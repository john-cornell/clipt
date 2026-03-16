using ClipSpy.ViewModels;

namespace ClipSpy.Tests.ViewModels;

public class HexTabViewModelTests
{
    [Fact]
    public void BuildHexDump_EmptyArray_ReturnsEmptyString()
    {
        string result = HexTabViewModel.BuildHexDump([], 16);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildHexDump_SingleByte_ShowsCorrectFormat()
    {
        byte[] data = [0x41];
        string result = HexTabViewModel.BuildHexDump(data, 16);

        Assert.Contains("0000", result);
        Assert.Contains("41", result);
        Assert.Contains("A", result);
    }

    [Fact]
    public void BuildHexDump_FullRow_AlignedCorrectly()
    {
        byte[] data = new byte[16];
        for (int i = 0; i < 16; i++)
            data[i] = (byte)(0x30 + i);

        string result = HexTabViewModel.BuildHexDump(data, 16);

        Assert.Contains("0000", result);
        Assert.Contains("30", result);
        Assert.Contains("3F", result);
    }

    [Fact]
    public void BuildHexDump_NonPrintableBytes_ShowDots()
    {
        byte[] data = [0x01, 0x02, 0x7F, 0xFF];
        string result = HexTabViewModel.BuildHexDump(data, 16);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.EndsWith("....", lines[0].TrimEnd());
    }

    [Fact]
    public void BuildHexDump_MultipleRows_HasCorrectOffsets()
    {
        byte[] data = new byte[48];
        string result = HexTabViewModel.BuildHexDump(data, 16);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("0000", lines[0]);
        Assert.StartsWith("0010", lines[1]);
        Assert.StartsWith("0020", lines[2]);
    }

    [Fact]
    public void BuildHexDump_CustomBytesPerRow_Respected()
    {
        byte[] data = new byte[16];
        string result = HexTabViewModel.BuildHexDump(data, 8);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void BuildHexDump_BytesPerRowClamped_ToMax64()
    {
        byte[] data = new byte[128];
        string result = HexTabViewModel.BuildHexDump(data, 200);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void BuildHexDump_LargeData_UsesWideOffsets()
    {
        byte[] data = new byte[0x10001];
        string result = HexTabViewModel.BuildHexDump(data, 16);

        Assert.StartsWith("00000000", result);
    }
}
