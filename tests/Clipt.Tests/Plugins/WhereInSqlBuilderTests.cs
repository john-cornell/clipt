using Clipt.Plugins.WhereIn;

namespace Clipt.Tests.Plugins;

public class WhereInSqlBuilderTests
{
    private const string Guid1 = "550e8400-e29b-41d4-a716-446655440000";
    private const string Guid2 = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

    [Fact]
    public void Build_WithHeader_UsesFirstLineAsColumnName()
    {
        string input = $"Id\n{Guid1}\n{Guid2}";

        WhereInBuildResult result = WhereInSqlBuilder.Build(input, useFirstLineAsColumnHeader: true);

        Assert.True(result.Success);
        Assert.Contains("Id IN (", result.Sql, StringComparison.Ordinal);
        Assert.Contains($"'{Guid1}'", result.Sql, StringComparison.Ordinal);
        Assert.Contains($"'{Guid2}'", result.Sql, StringComparison.Ordinal);
        Assert.Equal(2, result.GuidCount);
    }

    [Fact]
    public void Build_WithoutHeader_UsesDefaultColumnName()
    {
        string input = $"{Guid1}\n{Guid2}";

        WhereInBuildResult result = WhereInSqlBuilder.Build(input, useFirstLineAsColumnHeader: false);

        Assert.True(result.Success);
        Assert.Contains("Id IN (", result.Sql, StringComparison.Ordinal);
        Assert.Equal(2, result.GuidCount);
    }

    [Fact]
    public void Build_TrimsQuotesAndSkipsInvalidLines()
    {
        string input = $"CustomerId\n'{Guid1}'\nnot-a-guid\n\"{Guid2}\"";

        WhereInBuildResult result = WhereInSqlBuilder.Build(input, useFirstLineAsColumnHeader: true);

        Assert.True(result.Success);
        Assert.Contains("CustomerId IN (", result.Sql, StringComparison.Ordinal);
        Assert.Equal(2, result.GuidCount);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Build_NoValidGuids_Fails()
    {
        WhereInBuildResult result = WhereInSqlBuilder.Build("Id\nfoo\nbar", useFirstLineAsColumnHeader: true);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void HasMultipleLines_RequiresTwoNonEmptyLines()
    {
        Assert.False(WhereInSqlBuilder.HasMultipleLines(Guid1));
        Assert.False(WhereInSqlBuilder.HasMultipleLines($"Id\n"));
        Assert.True(WhereInSqlBuilder.HasMultipleLines($"Id\n{Guid1}"));
    }
}
