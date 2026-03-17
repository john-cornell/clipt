namespace Clipt.Models;

public sealed record ByteRangeSelection(int StartByte, int Count, SelectionSource Source)
{
    public static readonly ByteRangeSelection Empty = new(0, 0, SelectionSource.None);

    public int EndByteExclusive => StartByte + Count;

    public bool IsEmpty => Count <= 0;
}
