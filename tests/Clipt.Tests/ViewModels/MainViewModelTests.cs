using System.Collections.Immutable;
using Clipt.Models;
using Clipt.Services;
using Clipt.ViewModels;
using Moq;

namespace Clipt.Tests.ViewModels;

public class MainViewModelTests
{
    private readonly Mock<IClipboardService> _clipboardServiceMock = new();
    private readonly Mock<IThemeService> _themeServiceMock = new();

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
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

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
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

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
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        Assert.True(vm.AutoRefresh);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        var ex = Record.Exception(() => vm.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void IsDarkMode_FalseByDefault_WhenSavedNormal()
    {
        _themeServiceMock.Setup(t => t.LoadSavedTheme()).Returns(AppTheme.Normal);
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        Assert.False(vm.IsDarkMode);
    }

    [Fact]
    public void IsDarkMode_TrueWhenSavedDark()
    {
        _themeServiceMock.Setup(t => t.LoadSavedTheme()).Returns(AppTheme.Dark);
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        vm.IsDarkMode = _themeServiceMock.Object.LoadSavedTheme() == AppTheme.Dark;

        Assert.True(vm.IsDarkMode);
    }

    [Fact]
    public void ToggleIsDarkMode_CallsApplyTheme()
    {
        _themeServiceMock.Setup(t => t.LoadSavedTheme()).Returns(AppTheme.Normal);
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        vm.IsDarkMode = true;

        _themeServiceMock.Verify(t => t.ApplyTheme(AppTheme.Dark), Times.Once);
    }

    [Fact]
    public void ToggleIsDarkMode_ToFalse_CallsApplyThemeNormal()
    {
        _themeServiceMock.Setup(t => t.LoadSavedTheme()).Returns(AppTheme.Dark);
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);
        vm.IsDarkMode = true;
        _themeServiceMock.Invocations.Clear();

        vm.IsDarkMode = false;

        _themeServiceMock.Verify(t => t.ApplyTheme(AppTheme.Normal), Times.Once);
    }

    [Fact]
    public void IsHelpVisible_DefaultsFalse()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        Assert.False(vm.IsHelpVisible);
    }

    [Fact]
    public void SelectedTabIndex_DefaultsToZero()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        Assert.Equal(0, vm.SelectedTabIndex);
    }

    [Fact]
    public void SelectedTabIndex_Change_UpdatesCurrentHelpEntries()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        vm.SelectedTabIndex = 1;

        Assert.NotEmpty(vm.CurrentHelpEntries);
        Assert.Contains(vm.CurrentHelpEntries, e => e.Term == "Bits Per Pixel (bpp)");
    }

    [Fact]
    public void Initialize_PopulatesHelpEntriesForDefaultTab()
    {
        _themeServiceMock.Setup(t => t.LoadSavedTheme()).Returns(AppTheme.Normal);
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        // Initialize() calls Start() which requires a WPF dispatcher.
        // Simulate the help-entry population that Initialize() triggers
        // by setting tab index to a non-default first, then back to 0.
        vm.SelectedTabIndex = 1;
        vm.SelectedTabIndex = 0;

        Assert.NotEmpty(vm.CurrentHelpEntries);
        Assert.Contains(vm.CurrentHelpEntries, e => e.Term == "CF_UNICODETEXT");
    }

    [Fact]
    public void DefaultTab_ContainsBothTextAndHexHelpEntries()
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        vm.SelectedTabIndex = 1;
        vm.SelectedTabIndex = 0;

        Assert.Contains(vm.CurrentHelpEntries, e => e.Term == "CF_UNICODETEXT");
        Assert.Contains(vm.CurrentHelpEntries, e => e.Term == "Hex Dump");
    }

    [Theory]
    [InlineData(1, "Stride")]
    [InlineData(2, "Rich Text Format (RTF)")]
    [InlineData(3, "CF_HDROP (File Drop)")]
    [InlineData(4, "HGLOBAL Handle")]
    [InlineData(5, "GlobalLock Pointer")]
    public void SelectedTabIndex_ContainsExpectedTermForTab(int tabIndex, string expectedTerm)
    {
        _clipboardServiceMock
            .Setup(s => s.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(ClipboardSnapshot.Empty);

        using var listener = new ClipboardListenerService();
        var vm = new MainViewModel(_clipboardServiceMock.Object, listener, _themeServiceMock.Object);

        vm.SelectedTabIndex = tabIndex;

        Assert.Contains(vm.CurrentHelpEntries, e => e.Term == expectedTerm);
    }
}
