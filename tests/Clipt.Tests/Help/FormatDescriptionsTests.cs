using Clipt.Help;

namespace Clipt.Tests.Help;

public class FormatDescriptionsTests
{
    private static readonly uint[] StandardFormatIds =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
        0x0080, 0x0081, 0x0082, 0x0083, 0x008E,
    ];

    [Theory]
    [MemberData(nameof(StandardFormatIdData))]
    public void StandardFormat_ReturnsDescription(uint formatId)
    {
        var result = FormatDescriptions.GetDescription(formatId, string.Empty);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Short));
        Assert.False(string.IsNullOrWhiteSpace(result.Detailed));
    }

    public static TheoryData<uint> StandardFormatIdData()
    {
        var data = new TheoryData<uint>();
        foreach (var id in StandardFormatIds)
            data.Add(id);
        return data;
    }

    [Theory]
    [InlineData("HTML Format")]
    [InlineData("Rich Text Format")]
    [InlineData("Chromium internal source RFH token")]
    [InlineData("Chromium internal source URL")]
    [InlineData("Chromium Web Custom MIME Data Format")]
    [InlineData("DataObject")]
    [InlineData("Preferred DropEffect")]
    [InlineData("Shell IDList Array")]
    [InlineData("FileName")]
    [InlineData("FileNameW")]
    [InlineData("UniformResourceLocator")]
    [InlineData("UniformResourceLocatorW")]
    [InlineData("text/html")]
    [InlineData("text/x-moz-url")]
    [InlineData("Ole Private Data")]
    public void RegisteredFormat_KnownName_ReturnsDescription(string formatName)
    {
        var result = FormatDescriptions.GetDescription(0xC000, formatName);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Short));
        Assert.False(string.IsNullOrWhiteSpace(result.Detailed));
    }

    [Fact]
    public void RegisteredFormat_CaseInsensitiveLookup()
    {
        var lower = FormatDescriptions.GetDescription(0xC000, "html format");
        var upper = FormatDescriptions.GetDescription(0xC000, "HTML FORMAT");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal(lower.Short, upper.Short);
    }

    [Fact]
    public void UnknownFormat_ReturnsNull()
    {
        var result = FormatDescriptions.GetDescription(9999, "TotallyMadeUp_NotReal_Format");

        Assert.Null(result);
    }

    [Fact]
    public void StandardFormat_TakesPrecedenceOverRegisteredName()
    {
        var result = FormatDescriptions.GetDescription(13, "HTML Format");

        Assert.NotNull(result);
        Assert.Contains("UTF-16", result.Short);
    }

    [Fact]
    public void AllShortDescriptions_Under200Chars()
    {
        foreach (var (id, desc) in FormatDescriptions.AllStandardFormats)
        {
            Assert.True(
                desc.Short.Length <= 200,
                $"Standard format {id}: Short description is {desc.Short.Length} chars, expected <= 200.");
        }

        foreach (var (name, desc) in FormatDescriptions.AllRegisteredFormats)
        {
            Assert.True(
                desc.Short.Length <= 200,
                $"Registered format '{name}': Short description is {desc.Short.Length} chars, expected <= 200.");
        }
    }

    [Fact]
    public void AllDetailedDescriptions_AtLeast50Chars()
    {
        foreach (var (id, desc) in FormatDescriptions.AllStandardFormats)
        {
            Assert.True(
                desc.Detailed.Length >= 50,
                $"Standard format {id}: Detailed description is only {desc.Detailed.Length} chars, expected >= 50.");
        }

        foreach (var (name, desc) in FormatDescriptions.AllRegisteredFormats)
        {
            Assert.True(
                desc.Detailed.Length >= 50,
                $"Registered format '{name}': Detailed description is only {desc.Detailed.Length} chars, expected >= 50.");
        }
    }

    [Fact]
    public void UnknownFormatFallback_IsValid()
    {
        Assert.False(string.IsNullOrWhiteSpace(FormatDescriptions.UnknownFormatFallback.Short));
        Assert.False(string.IsNullOrWhiteSpace(FormatDescriptions.UnknownFormatFallback.Detailed));
        Assert.True(FormatDescriptions.UnknownFormatFallback.Detailed.Length >= 50);
    }

    [Fact]
    public void AllShortDescriptions_EndWithPeriod()
    {
        foreach (var (id, desc) in FormatDescriptions.AllStandardFormats)
        {
            Assert.True(
                desc.Short.EndsWith('.'),
                $"Standard format {id}: Short description does not end with a period.");
        }

        foreach (var (name, desc) in FormatDescriptions.AllRegisteredFormats)
        {
            Assert.True(
                desc.Short.EndsWith('.'),
                $"Registered format '{name}': Short description does not end with a period.");
        }
    }
}
