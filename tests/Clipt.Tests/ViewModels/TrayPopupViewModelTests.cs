using System.Collections.Immutable;
using Clipt.Models;
using Clipt.Native;
using Clipt.Services;
using Clipt.ViewModels;

namespace Clipt.Tests.ViewModels;

public class TrayPopupViewModelTests
{
    [Fact]
    public void Update_EmptySnapshot_SetsIsEmpty()
    {
        var vm = new TrayPopupViewModel();

        vm.Update(ClipboardSnapshot.Empty);

        Assert.True(vm.IsEmpty);
        Assert.Equal("Clipboard is empty", vm.ClipboardSummary);
        Assert.Empty(vm.PreviewText);
        Assert.Null(vm.PreviewImage);
        Assert.Empty(vm.PreviewFiles);
        Assert.False(vm.HasText);
        Assert.False(vm.HasImage);
        Assert.False(vm.HasFiles);
    }

    [Fact]
    public void Update_WithUnicodeText_SetsPreviewText()
    {
        string expected = "Hello World";
        byte[] textData = System.Text.Encoding.Unicode.GetBytes(expected + "\0");
        var snapshot = CreateSnapshot(ClipboardConstants.CF_UNICODETEXT, textData);

        var vm = new TrayPopupViewModel();
        vm.Update(snapshot);

        Assert.False(vm.IsEmpty);
        Assert.True(vm.HasText);
        Assert.Equal(expected, vm.PreviewText);
        Assert.Equal("1 format on clipboard", vm.ClipboardSummary);
        Assert.False(vm.TextPreviewStepControlsVisible);
    }

    [Fact]
    public void Update_LongText_TruncatesTo500Characters()
    {
        string longText = new string('A', 600);
        byte[] textData = System.Text.Encoding.Unicode.GetBytes(longText + "\0");
        var snapshot = CreateSnapshot(ClipboardConstants.CF_UNICODETEXT, textData);

        var vm = new TrayPopupViewModel();
        vm.Update(snapshot);

        Assert.True(vm.HasText);
        Assert.Equal(500, vm.PreviewText.Length);
        Assert.True(vm.TextPreviewStepControlsVisible);
    }

    [Fact]
    public void Update_MultipleFormats_ShowsPluralSummary()
    {
        byte[] textData = System.Text.Encoding.Unicode.GetBytes("Test\0");
        byte[] ansiData = System.Text.Encoding.ASCII.GetBytes("Test\0");
        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(
                new ClipboardFormatInfo
                {
                    FormatId = ClipboardConstants.CF_UNICODETEXT,
                    FormatName = "CF_UNICODETEXT",
                    IsStandard = true,
                    DataSize = textData.Length,
                    Memory = new MemoryInfo("0x0", "0x0", textData.Length, textData),
                    RawData = textData,
                },
                new ClipboardFormatInfo
                {
                    FormatId = ClipboardConstants.CF_TEXT,
                    FormatName = "CF_TEXT",
                    IsStandard = true,
                    DataSize = ansiData.Length,
                    Memory = new MemoryInfo("0x0", "0x0", ansiData.Length, ansiData),
                    RawData = ansiData,
                }),
        };

        var vm = new TrayPopupViewModel();
        vm.Update(snapshot);

        Assert.Equal("2 formats on clipboard", vm.ClipboardSummary);
    }

    [Fact]
    public void Update_NoTextFormat_HasTextIsFalse()
    {
        byte[] fakeData = [1, 2, 3, 4];
        var snapshot = CreateSnapshot(ClipboardConstants.CF_PALETTE, fakeData);

        var vm = new TrayPopupViewModel();
        vm.Update(snapshot);

        Assert.False(vm.HasText);
        Assert.Empty(vm.PreviewText);
    }

    [Fact]
    public void Update_AfterNonEmpty_ThenEmpty_ClearsAll()
    {
        byte[] textData = System.Text.Encoding.Unicode.GetBytes("Hello\0");
        var nonEmpty = CreateSnapshot(ClipboardConstants.CF_UNICODETEXT, textData);

        var vm = new TrayPopupViewModel();
        vm.Update(nonEmpty);
        Assert.True(vm.HasText);
        Assert.False(vm.IsEmpty);

        vm.Update(ClipboardSnapshot.Empty);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasText);
        Assert.Empty(vm.PreviewText);
    }

    [Fact]
    public void ExpandToFullCommand_RaisesEvent()
    {
        var vm = new TrayPopupViewModel();
        bool raised = false;
        vm.ExpandToFullRequested += (_, _) => raised = true;

        vm.ExpandToFullCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void DecodeUtf16Truncated_EmptyData_ReturnsEmpty()
    {
        string result = ClipboardHistoryService.DecodeUtf16Truncated([], 100);
        Assert.Empty(result);
    }

    [Fact]
    public void DecodeUtf16Truncated_NullTerminatedShortString_ReturnsUpToNull()
    {
        byte[] data = System.Text.Encoding.Unicode.GetBytes("AB\0");
        string result = ClipboardHistoryService.DecodeUtf16Truncated(data, 100);
        Assert.Equal("AB", result);
    }

    [Fact]
    public void DecodeUtf16Truncated_RespectsMaxChars()
    {
        byte[] data = System.Text.Encoding.Unicode.GetBytes("ABCDEF\0");
        string result = ClipboardHistoryService.DecodeUtf16Truncated(data, 3);
        Assert.Equal("ABC", result);
    }

    [Fact]
    public void IsPinned_DefaultsToFalse()
    {
        var vm = new TrayPopupViewModel();
        Assert.False(vm.IsPinned);
    }

    [Fact]
    public void IsPinned_CanBeToggled()
    {
        var vm = new TrayPopupViewModel();

        vm.IsPinned = true;
        Assert.True(vm.IsPinned);

        vm.IsPinned = false;
        Assert.False(vm.IsPinned);
    }

    [Fact]
    public void IsPinned_RaisesPropertyChanged()
    {
        var vm = new TrayPopupViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.IsPinned = true;

        Assert.Contains("IsPinned", changed);
    }

    private static ClipboardSnapshot CreateSnapshot(uint formatId, byte[] data)
    {
        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(
                new ClipboardFormatInfo
                {
                    FormatId = formatId,
                    FormatName = $"Format_{formatId}",
                    IsStandard = true,
                    DataSize = data.Length,
                    Memory = new MemoryInfo("0x0", "0x0", data.Length, data),
                    RawData = data,
                }),
        };
    }
}
