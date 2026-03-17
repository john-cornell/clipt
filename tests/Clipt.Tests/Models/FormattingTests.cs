using Clipt.Models;

namespace Clipt.Tests.Models;

public class FormattingTests
{
    [Fact]
    public void BuildOffsetColumn_EmptyData_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Formatting.BuildOffsetColumn([], 16));
    }

    [Fact]
    public void BuildOffsetColumn_SingleRow_HasOneOffset()
    {
        string result = Formatting.BuildOffsetColumn(new byte[10], 16);
        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("0000", lines[0]);
    }

    [Fact]
    public void BuildOffsetColumn_MultipleRows_HasCorrectOffsets()
    {
        string result = Formatting.BuildOffsetColumn(new byte[48], 16);
        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("0000", lines[0]);
        Assert.Equal("0010", lines[1]);
        Assert.Equal("0020", lines[2]);
    }

    [Fact]
    public void BuildHexColumn_EmptyData_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Formatting.BuildHexColumn([], 16));
    }

    [Fact]
    public void BuildHexColumn_SingleByte_ContainsHex()
    {
        string result = Formatting.BuildHexColumn([0xAB], 16);
        Assert.Contains("AB", result);
    }

    [Fact]
    public void BuildAsciiColumn_EmptyData_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Formatting.BuildAsciiColumn([], 16));
    }

    [Fact]
    public void BuildAsciiColumn_PrintableAndNonPrintable_Mixed()
    {
        byte[] data = [0x41, 0x01, 0x42];
        string result = Formatting.BuildAsciiColumn(data, 16);
        string trimmed = result.Trim();
        Assert.Equal("A.B", trimmed);
    }

    [Theory]
    [InlineData(0, 16, 0)]
    [InlineData(1, 16, 3)]
    [InlineData(7, 16, 21)]
    [InlineData(8, 16, 25)]
    public void HexCharOffsetForByte_ReturnsExpectedPosition(int byteIdx, int bpr, int expected)
    {
        int result = Formatting.HexCharOffsetForByte(byteIdx, bpr);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 16, 0)]
    [InlineData(3, 16, 1)]
    [InlineData(25, 16, 8)]
    public void ByteIndexFromHexCharOffset_ReturnsCorrectByte(int charOffset, int bpr, int expected)
    {
        int result = Formatting.ByteIndexFromHexCharOffset(charOffset, bpr);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ByteIndexFromHexCharOffset_OnSeparator_ReturnsNegative()
    {
        int result = Formatting.ByteIndexFromHexCharOffset(2, 16);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void AbsByteIndexFromHex_Line0Byte0_ReturnsZero()
    {
        int result = Formatting.AbsByteIndexFromHex(0, 0, 16, 256);
        Assert.Equal(0, result);
    }

    [Fact]
    public void AbsByteIndexFromHex_Line1Byte0_ReturnsBpr()
    {
        int result = Formatting.AbsByteIndexFromHex(1, 0, 16, 256);
        Assert.Equal(16, result);
    }

    [Fact]
    public void AbsByteIndexFromHex_BeyondData_ReturnsNegative()
    {
        int result = Formatting.AbsByteIndexFromHex(100, 0, 16, 32);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void AbsByteIndexFromAscii_Line0Char0_ReturnsZero()
    {
        int result = Formatting.AbsByteIndexFromAscii(0, 0, 16, 256);
        Assert.Equal(0, result);
    }

    [Fact]
    public void AbsByteIndexFromAscii_BeyondRow_ReturnsNegative()
    {
        int result = Formatting.AbsByteIndexFromAscii(0, 20, 16, 256);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void HexLineWidth_16Bpr_Returns49()
    {
        int result = Formatting.HexLineWidth(16);
        Assert.Equal(49, result);
    }

    [Fact]
    public void ClampBytesPerRow_BelowMin_ReturnsOne()
    {
        Assert.Equal(1, Formatting.ClampBytesPerRow(0));
    }

    [Fact]
    public void ClampBytesPerRow_AboveMax_Returns64()
    {
        Assert.Equal(64, Formatting.ClampBytesPerRow(200));
    }

    [Fact]
    public void BuildHexDump_Roundtrip_ContainsHexAndAscii()
    {
        byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F];
        string result = Formatting.BuildHexDump(data, 16);

        Assert.Contains("48 65 6C 6C 6F", result);
        Assert.Contains("Hello", result);
    }

    [Fact]
    public void FormatDataSize_ZeroBytes_ReturnsZeroB()
    {
        Assert.Equal("0 B", Formatting.FormatDataSize(0));
    }

    [Fact]
    public void FormatDataSize_SubKilobyte_ReturnsByteSuffix()
    {
        Assert.Equal("1023 B", Formatting.FormatDataSize(1023));
    }

    [Fact]
    public void FormatDataSize_ExactKilobyte_ReturnsKb()
    {
        Assert.Equal("1.0 KB", Formatting.FormatDataSize(1024));
    }

    [Fact]
    public void FormatDataSize_ExactMegabyte_ReturnsMb()
    {
        Assert.Equal("1.00 MB", Formatting.FormatDataSize(1024 * 1024));
    }

    [Fact]
    public void FormatDataSize_Gigabyte_ReturnsGb()
    {
        Assert.Equal("1.00 GB", Formatting.FormatDataSize(1024L * 1024 * 1024));
    }

    [Fact]
    public void CharRangeToByteRangeUtf16_ZeroLength_ReturnsZeroCount()
    {
        var (startByte, byteCount) = Formatting.CharRangeToByteRangeUtf16(5, 0);
        Assert.Equal(10, startByte);
        Assert.Equal(0, byteCount);
    }

    [Fact]
    public void CharRangeToByteRangeUtf16_FiveChars_ReturnsTenBytes()
    {
        var (startByte, byteCount) = Formatting.CharRangeToByteRangeUtf16(0, 5);
        Assert.Equal(0, startByte);
        Assert.Equal(10, byteCount);
    }

    [Fact]
    public void CharRangeToByteRangeUtf16_OffsetAndLength()
    {
        var (startByte, byteCount) = Formatting.CharRangeToByteRangeUtf16(3, 4);
        Assert.Equal(6, startByte);
        Assert.Equal(8, byteCount);
    }

    [Fact]
    public void ByteRangeToCharRangeUtf16_EvenBoundaries()
    {
        var (charStart, charCount) = Formatting.ByteRangeToCharRangeUtf16(4, 6);
        Assert.Equal(2, charStart);
        Assert.Equal(3, charCount);
    }

    [Fact]
    public void ByteRangeToCharRangeUtf16_OddByteStart_ClampsDown()
    {
        var (charStart, charCount) = Formatting.ByteRangeToCharRangeUtf16(3, 4);
        Assert.Equal(1, charStart);
        Assert.Equal(3, charCount);
    }

    [Fact]
    public void ByteRangeToCharRangeUtf16_ZeroCount_ReturnsZero()
    {
        var (charStart, charCount) = Formatting.ByteRangeToCharRangeUtf16(6, 0);
        Assert.Equal(3, charStart);
        Assert.Equal(0, charCount);
    }

    [Fact]
    public void CharRangeToByteRangeSingleByte_Identity()
    {
        var (startByte, byteCount) = Formatting.CharRangeToByteRangeSingleByte(7, 3);
        Assert.Equal(7, startByte);
        Assert.Equal(3, byteCount);
    }

    [Fact]
    public void ByteRangeToCharRangeSingleByte_Identity()
    {
        var (charStart, charCount) = Formatting.ByteRangeToCharRangeSingleByte(10, 5);
        Assert.Equal(10, charStart);
        Assert.Equal(5, charCount);
    }

    [Theory]
    [InlineData(0, 10, 0, 20)]
    [InlineData(2, 4, 4, 8)]
    [InlineData(0, 0, 0, 0)]
    public void CharRangeToByteRangeUtf16_RoundtripWithByteRangeToChar(
        int charStart, int charCount, int expectedByteStart, int expectedByteCount)
    {
        var (bs, bc) = Formatting.CharRangeToByteRangeUtf16(charStart, charCount);
        Assert.Equal(expectedByteStart, bs);
        Assert.Equal(expectedByteCount, bc);

        if (bc > 0)
        {
            var (cs, cc) = Formatting.ByteRangeToCharRangeUtf16(bs, bc);
            Assert.Equal(charStart, cs);
            Assert.Equal(charCount, cc);
        }
    }

    [Theory]
    [InlineData(0, 10, 0, 10)]
    [InlineData(5, 3, 5, 3)]
    [InlineData(0, 0, 0, 0)]
    public void CharRangeToByteRangeSingleByte_RoundtripWithByteRangeToChar(
        int charStart, int charCount, int expectedByteStart, int expectedByteCount)
    {
        var (bs, bc) = Formatting.CharRangeToByteRangeSingleByte(charStart, charCount);
        Assert.Equal(expectedByteStart, bs);
        Assert.Equal(expectedByteCount, bc);

        if (bc > 0)
        {
            var (cs, cc) = Formatting.ByteRangeToCharRangeSingleByte(bs, bc);
            Assert.Equal(charStart, cs);
            Assert.Equal(charCount, cc);
        }
    }

    [Fact]
    public void ByteRangeToCharRangeUtf16_SingleByteSelection_StillYieldsOneChar()
    {
        var (charStart, charCount) = Formatting.ByteRangeToCharRangeUtf16(0, 1);
        Assert.Equal(0, charStart);
        Assert.Equal(1, charCount);
    }

    [Fact]
    public void ByteRangeToCharRangeUtf16_TwoBytesFromZero_YieldsOneChar()
    {
        var (charStart, charCount) = Formatting.ByteRangeToCharRangeUtf16(0, 2);
        Assert.Equal(0, charStart);
        Assert.Equal(1, charCount);
    }
}
