using Clipt.Models;
using Clipt.Services;

namespace Clipt.Tests.Services;

public sealed class SingleInstanceActivationTests
{
    [Theory]
    [InlineData(0, StartupMode.FullWindow)]
    [InlineData(1, StartupMode.Collapsed)]
    public void StartupModeFromWParam_ReturnsDefinedModes(int wParam, StartupMode expected)
    {
        Assert.Equal(expected, SingleInstanceActivation.StartupModeFromWParam(wParam));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void StartupModeFromWParam_UnknownValue_FallsBackToFullWindow(int wParam)
    {
        Assert.Equal(StartupMode.FullWindow, SingleInstanceActivation.StartupModeFromWParam(wParam));
    }

    [Fact]
    public void Mutex_SecondHandleInSameProcess_IsNotCreator()
    {
        string name = @"Local\Clipt_Test_" + Guid.NewGuid();
        using var first = new Mutex(initiallyOwned: true, name, out bool createdFirst);
        Assert.True(createdFirst);

        using var second = new Mutex(initiallyOwned: true, name, out bool createdSecond);
        Assert.False(createdSecond);
    }

    [Fact]
    public void SecondInstanceActivateEventArgs_StoresWParam()
    {
        var args = new SecondInstanceActivateEventArgs(42);
        Assert.Equal(42, args.ModeWParam);
    }
}
