using Clipt.Help;

namespace Clipt.Tests.Help;

public class HelpContentTests
{
    [Fact]
    public void AllTabTermKeys_ExistInDetailedHelp()
    {
        foreach (var (tabName, termKeys) in HelpContent.TabTerms)
        {
            foreach (var key in termKeys)
            {
                Assert.True(
                    HelpContent.DetailedHelp.ContainsKey(key),
                    $"Tab '{tabName}' references term '{key}' which is missing from DetailedHelp.");
            }
        }
    }

    [Fact]
    public void NoTab_HasEmptyTermList()
    {
        foreach (var (tabName, termKeys) in HelpContent.TabTerms)
        {
            Assert.True(
                termKeys.Length > 0,
                $"Tab '{tabName}' has an empty term list.");
        }
    }

    [Fact]
    public void AllDetailedHelpStrings_AreNonEmptyAndMinimumLength()
    {
        const int minimumLength = 50;

        foreach (var (key, description) in HelpContent.DetailedHelp)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(description),
                $"DetailedHelp['{key}'] is null or whitespace.");
            Assert.True(
                description.Length >= minimumLength,
                $"DetailedHelp['{key}'] is only {description.Length} chars, expected at least {minimumLength}.");
        }
    }

    [Fact]
    public void AllDetailedHelpKeys_AreReferencedByAtLeastOneTab()
    {
        var allReferencedKeys = new HashSet<string>();
        foreach (var (_, termKeys) in HelpContent.TabTerms)
        {
            foreach (var key in termKeys)
                allReferencedKeys.Add(key);
        }

        foreach (var key in HelpContent.DetailedHelp.Keys)
        {
            Assert.True(
                allReferencedKeys.Contains(key),
                $"DetailedHelp key '{key}' is not referenced by any tab.");
        }
    }

    [Theory]
    [InlineData(0, "Text")]
    [InlineData(1, "Hex")]
    [InlineData(2, "Image")]
    [InlineData(3, "RichContent")]
    [InlineData(4, "FileDrop")]
    [InlineData(5, "AllFormats")]
    [InlineData(6, "Native")]
    public void GetTabNameByIndex_ReturnsExpectedName(int index, string expectedName)
    {
        Assert.Equal(expectedName, HelpContent.GetTabNameByIndex(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(100)]
    public void GetTabNameByIndex_OutOfRange_ReturnsEmpty(int index)
    {
        Assert.Equal(string.Empty, HelpContent.GetTabNameByIndex(index));
    }

    [Fact]
    public void TabCount_MatchesTabTermsCount()
    {
        Assert.Equal(HelpContent.TabTerms.Count, HelpContent.TabCount);
    }

    [Fact]
    public void EveryTabIndex_HasMatchingTabTermsEntry()
    {
        for (var i = 0; i < HelpContent.TabCount; i++)
        {
            var tabName = HelpContent.GetTabNameByIndex(i);
            Assert.True(
                HelpContent.TabTerms.ContainsKey(tabName),
                $"Tab index {i} ('{tabName}') has no entry in TabTerms.");
        }
    }

    [Fact]
    public void AllTabTermKeys_HaveDisplayNames()
    {
        foreach (var (tabName, termKeys) in HelpContent.TabTerms)
        {
            foreach (var key in termKeys)
            {
                Assert.True(
                    HelpContent.DisplayNames.ContainsKey(key),
                    $"Tab '{tabName}' references term '{key}' which is missing from DisplayNames.");
            }
        }
    }

    [Fact]
    public void AllDisplayNames_AreNonEmpty()
    {
        foreach (var (key, displayName) in HelpContent.DisplayNames)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(displayName),
                $"DisplayNames['{key}'] is null or whitespace.");
        }
    }
}
