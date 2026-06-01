using System.Text;

namespace Clipt.Plugins.WhereIn;

public sealed class WhereInBuildResult
{
    public required bool Success { get; init; }

    public string? Sql { get; init; }

    public string? ErrorMessage { get; init; }

    public int GuidCount { get; init; }

    public int SkippedCount { get; init; }

    public static WhereInBuildResult Succeeded(string sql, int guidCount, int skippedCount) =>
        new() { Success = true, Sql = sql, GuidCount = guidCount, SkippedCount = skippedCount };

    public static WhereInBuildResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Builds a multi-line SQL WHERE IN clause from newline-separated input.
/// Mirrors the validation rules from wherein.bat (GUID structure, quote trimming).
/// </summary>
public static class WhereInSqlBuilder
{
    public const string UseFirstLineAsColumnHeaderOptionKey = "useFirstLineAsColumnHeader";

    public static WhereInBuildResult Build(string input, bool useFirstLineAsColumnHeader)
    {
        if (string.IsNullOrWhiteSpace(input))
            return WhereInBuildResult.Failed("Clipboard text is empty.");

        string[] rawLines = input.Split(['\r', '\n'], StringSplitOptions.None);
        string columnName = "Id";
        int startIndex = 0;

        if (useFirstLineAsColumnHeader)
        {
            if (rawLines.Length == 0)
                return WhereInBuildResult.Failed("No lines found in clipboard text.");

            string? header = NormalizeLine(rawLines[0]);
            if (string.IsNullOrEmpty(header))
                return WhereInBuildResult.Failed("First line is empty; disable header mode or provide a column name.");

            columnName = header;
            startIndex = 1;
        }

        var guids = new List<string>();
        int skipped = 0;

        for (int i = startIndex; i < rawLines.Length; i++)
        {
            string? line = NormalizeLine(rawLines[i]);
            if (line is null)
                continue;

            if (IsGuid(line))
                guids.Add(line);
            else
                skipped++;
        }

        if (guids.Count == 0)
            return WhereInBuildResult.Failed("No valid GUID lines found.");

        var sql = new StringBuilder();
        sql.AppendLine("WHERE");
        sql.AppendLine($"    {columnName} IN (");
        for (int i = 0; i < guids.Count; i++)
        {
            string prefix = i == 0 ? "        " : "        ,";
            sql.AppendLine($"{prefix}'{guids[i]}'");
        }

        sql.Append("    )");

        return WhereInBuildResult.Succeeded(sql.ToString(), guids.Count, skipped);
    }

    internal static string? NormalizeLine(string rawLine)
    {
        string line = rawLine.Trim();
        line = line.Replace("'", string.Empty, StringComparison.Ordinal)
                   .Replace("\"", string.Empty, StringComparison.Ordinal);
        line = line.Trim();
        return line.Length == 0 ? null : line;
    }

    internal static bool IsGuid(string line)
    {
        if (line.Length != 36)
            return false;

        if (line[8] != '-' || line[13] != '-' || line[18] != '-' || line[23] != '-')
            return false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '-')
                continue;

            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }

    public static bool HasMultipleLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int lineCount = 0;
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.None))
        {
            if (NormalizeLine(rawLine) is not null)
            {
                lineCount++;
                if (lineCount >= 2)
                    return true;
            }
        }

        return false;
    }
}
