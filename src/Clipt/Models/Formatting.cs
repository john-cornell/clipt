using System.Text;

namespace Clipt.Models;

internal static class Formatting
{
    public static string FormatDataSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
    };

    public static int ClampBytesPerRow(int bytesPerRow) => Math.Clamp(bytesPerRow, 1, 64);

    public static int OffsetWidth(int dataLength) => dataLength > 0xFFFF ? 8 : 4;

    public static string BuildHexDump(byte[] data, int bytesPerRow)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            return string.Empty;

        int bpr = ClampBytesPerRow(bytesPerRow);
        int offsetWidth = OffsetWidth(data.Length);
        var sb = new StringBuilder((data.Length / bpr + 1) * (offsetWidth + 3 + bpr * 3 + 2 + bpr + 2));

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            int count = Math.Min(bpr, data.Length - offset);

            sb.Append(offset.ToString($"X{offsetWidth}"));
            sb.Append("  ");

            AppendHexBytes(sb, data, offset, count, bpr);

            sb.Append(' ');

            AppendAsciiSidebar(sb, data, offset, count);

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildOffsetColumn(byte[] data, int bytesPerRow)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            return string.Empty;

        int bpr = ClampBytesPerRow(bytesPerRow);
        int ow = OffsetWidth(data.Length);
        var sb = new StringBuilder();

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            sb.AppendLine(offset.ToString($"X{ow}"));
        }

        return sb.ToString();
    }

    public static string BuildHexColumn(byte[] data, int bytesPerRow)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            return string.Empty;

        int bpr = ClampBytesPerRow(bytesPerRow);
        var sb = new StringBuilder();

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            int count = Math.Min(bpr, data.Length - offset);
            AppendHexBytes(sb, data, offset, count, bpr);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildAsciiColumn(byte[] data, int bytesPerRow)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            return string.Empty;

        int bpr = ClampBytesPerRow(bytesPerRow);
        var sb = new StringBuilder();

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            int count = Math.Min(bpr, data.Length - offset);
            AppendAsciiSidebar(sb, data, offset, count);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the character offset within a single hex-column line where the given
    /// byte index (0-based within the row) begins. Each byte occupies 3 chars ("XX "),
    /// with an extra space after the midpoint.
    /// </summary>
    public static int HexCharOffsetForByte(int byteIndexInRow, int bytesPerRow)
    {
        int bpr = ClampBytesPerRow(bytesPerRow);
        int mid = bpr / 2;
        int pos = byteIndexInRow * 3;
        if (byteIndexInRow >= mid)
            pos += 1;
        return pos;
    }

    /// <summary>
    /// Given a character offset within a hex-column line, returns the byte index
    /// (0-based within that row), or -1 if the position is on whitespace/padding.
    /// </summary>
    public static int ByteIndexFromHexCharOffset(int charOffset, int bytesPerRow)
    {
        int bpr = ClampBytesPerRow(bytesPerRow);
        int mid = bpr / 2;
        int midCharPos = mid * 3;

        int adjusted = charOffset;
        if (adjusted >= midCharPos)
            adjusted -= 1;

        if (adjusted < 0)
            return -1;

        int byteIdx = adjusted / 3;
        int withinByte = adjusted % 3;

        if (withinByte == 2)
            return -1;

        return byteIdx < bpr ? byteIdx : -1;
    }

    /// <summary>
    /// Returns the width of a single hex-column line (without newline) for the
    /// given bytes-per-row value.
    /// </summary>
    public static int HexLineWidth(int bytesPerRow)
    {
        int bpr = ClampBytesPerRow(bytesPerRow);
        return bpr * 3 + 1;
    }

    /// <summary>
    /// Converts a (line, charOffsetInLine) in the hex column to an absolute byte
    /// index in the data array, or -1 if invalid.
    /// </summary>
    public static int AbsByteIndexFromHex(int line, int charOffsetInLine, int bytesPerRow, int dataLength)
    {
        int bpr = ClampBytesPerRow(bytesPerRow);
        int byteInRow = ByteIndexFromHexCharOffset(charOffsetInLine, bpr);
        if (byteInRow < 0)
            return -1;

        int absIdx = line * bpr + byteInRow;
        return absIdx < dataLength ? absIdx : -1;
    }

    /// <summary>
    /// Converts a (line, charOffsetInLine) in the ASCII column to an absolute byte
    /// index in the data array, or -1 if invalid.
    /// </summary>
    public static int AbsByteIndexFromAscii(int line, int charOffsetInLine, int bytesPerRow, int dataLength)
    {
        int bpr = ClampBytesPerRow(bytesPerRow);
        if (charOffsetInLine < 0 || charOffsetInLine >= bpr)
            return -1;

        int absIdx = line * bpr + charOffsetInLine;
        return absIdx < dataLength ? absIdx : -1;
    }

    public static (int startByte, int byteCount) CharRangeToByteRangeUtf16(int charStart, int charCount)
    {
        if (charCount <= 0)
            return (charStart * 2, 0);

        return (charStart * 2, charCount * 2);
    }

    public static (int charStart, int charCount) ByteRangeToCharRangeUtf16(int byteStart, int byteCount)
    {
        if (byteCount <= 0)
            return (byteStart / 2, 0);

        int alignedStart = byteStart / 2;
        int endByte = byteStart + byteCount;
        int alignedEnd = (endByte + 1) / 2;
        return (alignedStart, alignedEnd - alignedStart);
    }

    public static (int startByte, int byteCount) CharRangeToByteRangeSingleByte(int charStart, int charCount)
    {
        return (charStart, charCount);
    }

    public static (int charStart, int charCount) ByteRangeToCharRangeSingleByte(int byteStart, int byteCount)
    {
        return (byteStart, byteCount);
    }

    private static void AppendHexBytes(StringBuilder sb, byte[] data, int offset, int count, int bpr)
    {
        for (int i = 0; i < bpr; i++)
        {
            if (i < count)
            {
                sb.Append(data[offset + i].ToString("X2"));
                sb.Append(' ');
            }
            else
            {
                sb.Append("   ");
            }

            if (i == bpr / 2 - 1)
                sb.Append(' ');
        }
    }

    private static void AppendAsciiSidebar(StringBuilder sb, byte[] data, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte b = data[offset + i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
    }
}
