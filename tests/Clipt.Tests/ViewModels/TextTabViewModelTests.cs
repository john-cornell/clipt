using System.Collections.Immutable;
using Clipt.Models;
using Clipt.ViewModels;

namespace Clipt.Tests.ViewModels;

public class TextTabViewModelTests
{
    [Fact]
    public void Update_WithUnicodeText_SetsTextAndStats()
    {
        var vm = new TextTabViewModel();
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes("Hello\0");
        var snapshot = MakeSnapshot(13, utf16);

        vm.Update(snapshot);

        Assert.True(vm.HasText);
        Assert.Equal("Hello", vm.UnicodeText);
        Assert.Equal(5, vm.CharacterCount);
        Assert.Equal(1, vm.LineCount);
        Assert.Equal(5, vm.NullTerminatorPosition);
    }

    [Fact]
    public void Update_WithMultilineText_CountsLines()
    {
        var vm = new TextTabViewModel();
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes("Line1\nLine2\nLine3\0");
        var snapshot = MakeSnapshot(13, utf16);

        vm.Update(snapshot);

        Assert.Equal(3, vm.LineCount);
    }

    [Fact]
    public void Update_EmptyClipboard_ClearsAll()
    {
        var vm = new TextTabViewModel();
        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
        };

        vm.Update(snapshot);

        Assert.False(vm.HasText);
        Assert.Equal(string.Empty, vm.UnicodeText);
        Assert.Equal(0, vm.CharacterCount);
    }

    [Fact]
    public void Update_WithLocale_ParsesLcid()
    {
        var vm = new TextTabViewModel();
        byte[] localeData = BitConverter.GetBytes((uint)1033);

        var formats = ImmutableArray.Create(
            MakeFormat(13, System.Text.Encoding.Unicode.GetBytes("Test\0")),
            MakeFormat(16, localeData));

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = formats,
        };

        vm.Update(snapshot);

        Assert.Contains("1033", vm.LocaleInfo);
        Assert.Contains("0x0409", vm.LocaleInfo);
    }

    [Fact]
    public void Update_WithNonAsciiCharacters_ReportsEncodingStats()
    {
        var vm = new TextTabViewModel();
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes("café\0");
        var snapshot = MakeSnapshot(13, utf16);

        vm.Update(snapshot);

        Assert.Contains("Non-ASCII: 1", vm.EncodingStats);
    }

    private static ClipboardSnapshot MakeSnapshot(uint formatId, byte[] data)
    {
        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(MakeFormat(formatId, data)),
        };
    }

    private static ClipboardFormatInfo MakeFormat(uint formatId, byte[] data)
    {
        return new ClipboardFormatInfo
        {
            FormatId = formatId,
            FormatName = $"Format_{formatId}",
            IsStandard = true,
            DataSize = data.Length,
            Memory = new MemoryInfo("0x0", "0x0", data.Length, data.Length > 256 ? data[..256] : data),
            RawData = data,
        };
    }

    [Fact]
    public void IsEditing_DefaultsFalse()
    {
        var vm = new TextTabViewModel();
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void ToggleEditingCommand_TogglesIsEditing()
    {
        var vm = new TextTabViewModel();
        vm.ToggleEditingCommand.Execute(null);
        Assert.True(vm.IsEditing);
        vm.ToggleEditingCommand.Execute(null);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void Update_WhileEditing_DoesNotOverwrite()
    {
        var vm = new TextTabViewModel();
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes("Original\0");
        vm.Update(MakeSnapshot(13, utf16));

        vm.IsEditing = true;

        byte[] newUtf16 = System.Text.Encoding.Unicode.GetBytes("Replaced\0");
        vm.Update(MakeSnapshot(13, newUtf16));

        Assert.Equal("Original", vm.UnicodeText);
    }

    [Fact]
    public void ApplyTextToClipboard_WithoutService_SetsErrorMessage()
    {
        var vm = new TextTabViewModel();
        vm.UnicodeText = "Test";
        vm.ApplyTextToClipboardCommand.Execute(null);

        Assert.Equal("Clipboard service unavailable.", vm.EditStatusMessage);
    }

    [Fact]
    public void ApplyTextToClipboard_WithService_CallsSetClipboardText()
    {
        var mockService = new Moq.Mock<Clipt.Services.IClipboardService>();
        var vm = new TextTabViewModel(mockService.Object, () => (nint)0);

        vm.UnicodeText = "Hello";
        vm.IsEditing = true;
        vm.ApplyTextToClipboardCommand.Execute(null);

        mockService.Verify(s => s.SetClipboardText("Hello", (nint)0), Moq.Times.Once);
        Assert.Contains("Applied", vm.EditStatusMessage);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void DecodeUtf16_NoNullTerminator_DecodesFullBuffer()
    {
        byte[] data = System.Text.Encoding.Unicode.GetBytes("NoNull");
        string result = TextTabViewModel.DecodeUtf16(data, out int nullPos);
        Assert.Equal(-1, nullPos);
        Assert.Equal("NoNull", result);
    }

    [Fact]
    public void CountLines_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, TextTabViewModel.CountLines(""));
    }

    [Fact]
    public void CountLines_SingleLine_ReturnsOne()
    {
        Assert.Equal(1, TextTabViewModel.CountLines("hello"));
    }

    [Fact]
    public void CountLines_ThreeLines_ReturnsThree()
    {
        Assert.Equal(3, TextTabViewModel.CountLines("a\nb\nc"));
    }
}
