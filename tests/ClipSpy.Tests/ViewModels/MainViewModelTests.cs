using System.Collections.Immutable;
using ClipSpy.Models;
using ClipSpy.Services;
using ClipSpy.ViewModels;
using Moq;

namespace ClipSpy.Tests.ViewModels;

public class MainViewModelTests
{
    private readonly Mock<IClipboardService> _clipboardServiceMock = new();

    [Fact]
    public void Refresh_PopulatesAllTabViewModels()
    {
        byte[] textData = System.Text.Encoding.Unicode.GetBytes("Hello World\0");
        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 42,
            OwnerProcessName = "notepad",
            OwnerProcessId = 1234,
            Formats = ImmutableArray.Create(
                new ClipboardFormatInfo
                {
                    FormatId = 13,
                    FormatName = "CF_UNICODETEXT",
                    IsStandard = true,
                    DataSize = textData.Length,
                    Memory = new MemoryInfo("0x00000001", "0x00000002", textData.Length, textData),
                    RawData = textData,
                }),
        };

        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(snapshot);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener);

        vm.Refresh();

        Assert.Equal("Seq: 42", vm.StatusSequence);
        Assert.Equal("Formats: 1", vm.StatusFormats);
        Assert.Contains("notepad", vm.StatusOwner);
        Assert.Contains("1234", vm.StatusOwner);
        Assert.True(vm.TextTab.HasText);
        Assert.Equal("Hello World", vm.TextTab.UnicodeText);
        Assert.Equal(1, vm.FormatsTab.TotalFormats);
    }

    [Fact]
    public void Refresh_EmptyClipboard_ClearsState()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener);

        vm.Refresh();

        Assert.False(vm.TextTab.HasText);
        Assert.Equal(0, vm.FormatsTab.TotalFormats);
        Assert.Equal("Formats: 0", vm.StatusFormats);
    }

    [Fact]
    public void AutoRefresh_DefaultsToTrue()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener);

        Assert.True(vm.AutoRefresh);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener);

        var ex = Record.Exception(() => vm.Dispose());
        Assert.Null(ex);
    }
}
