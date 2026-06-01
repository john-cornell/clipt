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
    public void TryAcquireMutex_WhenUnheld_ReturnsTrueWithoutBlocking()
    {
        string name = @"Local\Clipt_TestAcquire_" + Guid.NewGuid();

        Assert.True(SingleInstanceActivationTryAcquireMutexForTest(name, out Mutex? mutex, out bool owns));
        Assert.NotNull(mutex);
        Assert.True(owns);

        mutex!.ReleaseMutex();
        mutex.Dispose();
    }

    private static bool SingleInstanceActivationTryAcquireMutexForTest(
        string mutexName,
        out Mutex? mutex,
        out bool ownsMutex)
    {
        mutex = null;
        ownsMutex = false;
        try
        {
            mutex = new Mutex(initiallyOwned: false, mutexName, out _);
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                mutex.Dispose();
                mutex = null;
            }

            return ownsMutex;
        }
        catch (UnauthorizedAccessException)
        {
            mutex?.Dispose();
            mutex = null;
            return false;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            mutex?.Dispose();
            mutex = null;
            return false;
        }
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
