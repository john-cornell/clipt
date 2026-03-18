using Clipt.Views;

namespace Clipt.Tests.Views;

public class HexTabViewSelectionTests
{
    private const int Bpr = 16;
    private const int HexLineWidth = Bpr * 3 + 1; // 49
    private const int HexLineLen = HexLineWidth + 2; // 51 (\r\n)
    private const int AsciiLineLen = Bpr + 2; // 18 (\r\n)

    [Fact]
    public void ResolveByteRangeFromHex_SingleByte_FirstByte()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 0, selLength: 2, bpr: Bpr, lineLen: HexLineLen, dataLength: 32);

        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }

    [Fact]
    public void ResolveByteRangeFromHex_SingleByte_SecondByte()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 3, selLength: 2, bpr: Bpr, lineLen: HexLineLen, dataLength: 32);

        Assert.Equal(1, start);
        Assert.Equal(1, end);
    }

    [Fact]
    public void ResolveByteRangeFromHex_MultipleBytes_SameLine()
    {
        // Select bytes 0-3 on line 0: chars 0 through 11 (4 bytes * 3 chars/byte - 1)
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 0, selLength: 11, bpr: Bpr, lineLen: HexLineLen, dataLength: 32);

        Assert.Equal(0, start);
        Assert.True(end >= 3);
    }

    [Fact]
    public void ResolveByteRangeFromHex_SpanningMultipleLines()
    {
        // Start at byte 15 (last byte of line 0), select into line 1
        int startCharForByte15 = 15 * 3 + 1; // extra char for mid-gap
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: startCharForByte15, selLength: HexLineLen + 3,
            bpr: Bpr, lineLen: HexLineLen, dataLength: 64);

        Assert.True(start >= 0);
        Assert.True(end > start);
        Assert.True(end <= 63);
    }

    [Fact]
    public void ResolveByteRangeFromHex_ZeroLength_ReturnsSameStartAndEnd()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 0, selLength: 0, bpr: Bpr, lineLen: HexLineLen, dataLength: 32);

        Assert.Equal(start, end);
    }

    [Fact]
    public void ResolveByteRangeFromHex_SelectionBeyondData_ClampedToDataLength()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 0, selLength: HexLineLen * 10, bpr: Bpr, lineLen: HexLineLen, dataLength: 8);

        if (start >= 0)
            Assert.True(end < 8);
    }

    [Fact]
    public void ResolveByteRangeFromHex_EmptyData_ReturnsNegative()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 0, selLength: 2, bpr: Bpr, lineLen: HexLineLen, dataLength: 0);

        Assert.True(start < 0);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_SingleByte_FirstByte()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: 0, selLength: 1, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 32);

        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_MultipleBytes_SameLine()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: 2, selLength: 5, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 32);

        Assert.Equal(2, start);
        Assert.Equal(6, end);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_SpanningMultipleLines()
    {
        // Start at byte 14 on line 0, extend into line 1
        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: 14, selLength: AsciiLineLen, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 64);

        Assert.True(start >= 0);
        Assert.True(end > start);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_ZeroLength_ReturnsSameStartAndEnd()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: 5, selLength: 0, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 32);

        Assert.Equal(start, end);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_EmptyData_ReturnsNegative()
    {
        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: 0, selLength: 1, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 0);

        Assert.True(start < 0);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_SelectionAtBoundary_LastByteOfData()
    {
        // 32 bytes = 2 lines; last byte is index 31 at char offset 15 on line 1
        int selStart = AsciiLineLen + 15; // line 1, char 15
        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: selStart, selLength: 1, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 32);

        Assert.Equal(31, start);
        Assert.Equal(31, end);
    }

    [Fact]
    public void ResolveByteRangeFromHex_StartAndEnd_AreOrdered()
    {
        for (int selStart = 0; selStart < HexLineLen * 2; selStart += 7)
        {
            for (int selLen = 1; selLen <= HexLineLen; selLen += 5)
            {
                var (start, end) = HexTabView.ResolveByteRangeFromHex(
                    selStart, selLen, Bpr, HexLineLen, 48);

                if (start >= 0)
                    Assert.True(start <= end, $"start={start} > end={end} at selStart={selStart}, selLen={selLen}");
            }
        }
    }

    [Fact]
    public void ResolveByteRangeFromAscii_StartAndEnd_AreOrdered()
    {
        for (int selStart = 0; selStart < AsciiLineLen * 3; selStart += 5)
        {
            for (int selLen = 1; selLen <= AsciiLineLen; selLen += 3)
            {
                var (start, end) = HexTabView.ResolveByteRangeFromAscii(
                    selStart, selLen, Bpr, AsciiLineLen, 48);

                if (start >= 0)
                    Assert.True(start <= end, $"start={start} > end={end} at selStart={selStart}, selLen={selLen}");
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ResolveByteRangeFromHex_DifferentBytesPerRow(int bpr)
    {
        int hexLineWidth = bpr * 3 + 1;
        int lineLen = hexLineWidth + 2;

        var (start, end) = HexTabView.ResolveByteRangeFromHex(
            selStart: 0, selLength: 5, bpr: bpr, lineLen: lineLen, dataLength: bpr * 2);

        Assert.True(start >= 0);
        Assert.True(end >= start);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ResolveByteRangeFromAscii_DifferentBytesPerRow(int bpr)
    {
        int asciiLineLen = bpr + 2;

        var (start, end) = HexTabView.ResolveByteRangeFromAscii(
            selStart: 0, selLength: 3, bpr: bpr, asciiLineLen: asciiLineLen, dataLength: bpr * 2);

        Assert.Equal(0, start);
        Assert.Equal(2, end);
    }

    [Fact]
    public void ResolveByteRangeFromAscii_SelectionInNewlineGap_ReturnsNegative()
    {
        // Char offset 16 on a 16-bpr line is the \r of \r\n — beyond the ASCII data chars
        var (start, _) = HexTabView.ResolveByteRangeFromAscii(
            selStart: Bpr, selLength: 1, bpr: Bpr, asciiLineLen: AsciiLineLen, dataLength: 32);

        Assert.True(start < 0);
    }
}
