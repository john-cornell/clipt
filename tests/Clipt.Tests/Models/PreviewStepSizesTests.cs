using Clipt.Models;

namespace Clipt.Tests.Models;

public class PreviewStepSizesTests
{
    [Theory]
    [InlineData(0, 100, 100)]
    [InlineData(0, 300, 256)]
    [InlineData(1, 300, 300)]
    [InlineData(4, 10_000, 10_000)]
    public void GetByteSliceLength_ReturnsExpected(int stepIndex, int captured, int expected)
    {
        Assert.Equal(expected, PreviewStepSizes.GetByteSliceLength(stepIndex, captured));
    }

    [Fact]
    public void CanAdvanceByteStep_FalseWhenAtFullCapture()
    {
        Assert.False(PreviewStepSizes.CanAdvanceByteStep(4, 500));
        Assert.False(PreviewStepSizes.CanAdvanceByteStep(3, 100));
    }

    [Fact]
    public void CanAdvanceByteStep_TrueWhenMoreCapturedBytesExist()
    {
        Assert.True(PreviewStepSizes.CanAdvanceByteStep(0, 10_000));
        Assert.True(PreviewStepSizes.CanAdvanceByteStep(2, 100_000));
    }

    [Theory]
    [InlineData(0, 600, 500)]
    [InlineData(1, 600, 600)]
    [InlineData(3, 50, 50)]
    public void GetTrayTextMaxChars_MatchesSteps(int stepIndex, int fullLen, int expected)
    {
        Assert.Equal(expected, PreviewStepSizes.GetTrayTextMaxChars(stepIndex, fullLen));
    }

    [Fact]
    public void HistoryExpand_FirstLoadAllowsAdvanceWhenEllipsis()
    {
        Assert.True(PreviewStepSizes.CanAdvanceHistorySummaryExpand(0, 0, false, true));
    }

    [Fact]
    public void HistoryExpand_NoAdvanceWhenCompactNotTruncatedAndNotLoaded()
    {
        Assert.False(PreviewStepSizes.CanAdvanceHistorySummaryExpand(0, 0, false, false));
    }

    [Fact]
    public void GetHistorySummaryDisplayLimit_Level1_Is500Cap()
    {
        Assert.Equal(500, PreviewStepSizes.GetHistorySummaryDisplayLimit(1, 10_000));
        Assert.Equal(100, PreviewStepSizes.GetHistorySummaryDisplayLimit(1, 100));
    }
}
