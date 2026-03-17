using Clipt.Models;

namespace Clipt.Tests.Models;

public class ByteRangeSelectionTests
{
    [Fact]
    public void Empty_HasZeroCountAndNoneSource()
    {
        var empty = ByteRangeSelection.Empty;
        Assert.Equal(0, empty.StartByte);
        Assert.Equal(0, empty.Count);
        Assert.Equal(SelectionSource.None, empty.Source);
        Assert.True(empty.IsEmpty);
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var sel = new ByteRangeSelection(10, 5, SelectionSource.HexColumn);
        Assert.Equal(10, sel.StartByte);
        Assert.Equal(5, sel.Count);
        Assert.Equal(SelectionSource.HexColumn, sel.Source);
    }

    [Fact]
    public void EndByteExclusive_ReturnsStartPlusCount()
    {
        var sel = new ByteRangeSelection(4, 8, SelectionSource.UnicodeText);
        Assert.Equal(12, sel.EndByteExclusive);
    }

    [Fact]
    public void IsEmpty_TrueWhenCountZero()
    {
        var sel = new ByteRangeSelection(5, 0, SelectionSource.AnsiText);
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void IsEmpty_TrueWhenCountNegative()
    {
        var sel = new ByteRangeSelection(5, -1, SelectionSource.OemText);
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void IsEmpty_FalseWhenCountPositive()
    {
        var sel = new ByteRangeSelection(0, 1, SelectionSource.AsciiColumn);
        Assert.False(sel.IsEmpty);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = new ByteRangeSelection(3, 7, SelectionSource.HexColumn);
        var b = new ByteRangeSelection(3, 7, SelectionSource.HexColumn);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentSource_NotEqual()
    {
        var a = new ByteRangeSelection(3, 7, SelectionSource.HexColumn);
        var b = new ByteRangeSelection(3, 7, SelectionSource.AsciiColumn);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(SelectionSource.None)]
    [InlineData(SelectionSource.HexColumn)]
    [InlineData(SelectionSource.AsciiColumn)]
    [InlineData(SelectionSource.UnicodeText)]
    [InlineData(SelectionSource.AnsiText)]
    [InlineData(SelectionSource.OemText)]
    public void AllSelectionSourceValues_AreDistinct(SelectionSource source)
    {
        var sel = new ByteRangeSelection(0, 1, source);
        Assert.Equal(source, sel.Source);
    }
}
