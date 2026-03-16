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

    public static string BuildHexDump(byte[] data, int bytesPerRow)
    {
        if (data.Length == 0)
            return string.Empty;

        int bpr = Math.Clamp(bytesPerRow, 1, 64);
        int offsetWidth = data.Length > 0xFFFF ? 8 : 4;
        var sb = new StringBuilder((data.Length / bpr + 1) * (offsetWidth + 3 + bpr * 3 + 2 + bpr + 2));

        for (int offset = 0; offset < data.Length; offset += bpr)
        {
            int count = Math.Min(bpr, data.Length - offset);

            sb.Append(offset.ToString($"X{offsetWidth}"));
            sb.Append("  ");

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

            sb.Append(' ');

            for (int i = 0; i < count; i++)
            {
                byte b = data[offset + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
