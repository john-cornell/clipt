using System.Collections.Immutable;
using Clipt.Models;
using Clipt.ViewModels;

namespace Clipt.Tests.ViewModels;

public class FormatsTabViewModelTests
{
    [Fact]
    public void Update_PopulatesFormatsCollection()
    {
        var vm = new FormatsTabViewModel();
        var snapshot = MakeSnapshot(
            MakeFormat(13, "CF_UNICODETEXT", true, 100),
            MakeFormat(1, "CF_TEXT", true, 50),
            MakeFormat(49322, "HTML Format", false, 2048));

        vm.Update(snapshot);

        Assert.Equal(3, vm.TotalFormats);
        Assert.Equal(3, vm.Formats.Count);
    }

    [Fact]
    public void Update_FormatsHaveCorrectDisplayValues()
    {
        var vm = new FormatsTabViewModel();
        var snapshot = MakeSnapshot(MakeFormat(13, "CF_UNICODETEXT", true, 256));

        vm.Update(snapshot);

        var row = vm.Formats[0];
        Assert.Equal((uint)13, row.Id);
        Assert.Equal("0x000D", row.IdHex);
        Assert.Equal("CF_UNICODETEXT", row.Name);
        Assert.Equal(256, row.SizeBytes);
        Assert.True(row.IsStandard);
        Assert.Equal("13 (0x000D)", row.IdDisplay);
    }

    [Fact]
    public void Update_EmptySnapshot_ClearsFormats()
    {
        var vm = new FormatsTabViewModel();
        vm.Update(MakeSnapshot(MakeFormat(1, "CF_TEXT", true, 10)));
        Assert.Equal(1, vm.TotalFormats);

        vm.Update(new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 2,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
        });

        Assert.Equal(0, vm.TotalFormats);
        Assert.Empty(vm.Formats);
    }

    [Fact]
    public void Update_ReplacesOldFormatsCompletely()
    {
        var vm = new FormatsTabViewModel();
        vm.Update(MakeSnapshot(
            MakeFormat(1, "CF_TEXT", true, 10),
            MakeFormat(13, "CF_UNICODETEXT", true, 20)));

        Assert.Equal(2, vm.Formats.Count);

        vm.Update(MakeSnapshot(MakeFormat(8, "CF_DIB", true, 5000)));

        Assert.Single(vm.Formats);
        Assert.Equal("CF_DIB", vm.Formats[0].Name);
    }

    [Fact]
    public void SelectedFormatRow_Null_ByDefault()
    {
        var vm = new FormatsTabViewModel();

        Assert.Null(vm.SelectedFormatRow);
        Assert.Equal(string.Empty, vm.SelectedFormatDescription);
    }

    [Fact]
    public void SelectedFormatRow_PopulatesDescription()
    {
        var vm = new FormatsTabViewModel();
        vm.Update(MakeSnapshot(
            MakeFormat(13, "CF_UNICODETEXT", true, 100),
            MakeFormat(1, "CF_TEXT", true, 50)));

        vm.SelectedFormatRow = vm.Formats[0];

        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedFormatDescription));
        Assert.Contains("UTF-16", vm.SelectedFormatDescription);
    }

    [Fact]
    public void SelectedFormatRow_ClearedOnUpdate()
    {
        var vm = new FormatsTabViewModel();
        vm.Update(MakeSnapshot(MakeFormat(13, "CF_UNICODETEXT", true, 100)));
        vm.SelectedFormatRow = vm.Formats[0];

        Assert.NotNull(vm.SelectedFormatRow);

        vm.Update(MakeSnapshot(MakeFormat(1, "CF_TEXT", true, 50)));

        Assert.Null(vm.SelectedFormatRow);
        Assert.Equal(string.Empty, vm.SelectedFormatDescription);
    }

    [Fact]
    public void SelectedFormatRow_UnknownFormat_UsesFallback()
    {
        var vm = new FormatsTabViewModel();
        vm.Update(MakeSnapshot(MakeFormat(0xC123, "SomeRandomAppFormat", false, 64)));

        vm.SelectedFormatRow = vm.Formats[0];

        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedFormatDescription));
        Assert.Contains("registered at runtime", vm.SelectedFormatDescription);
    }

    private static ClipboardSnapshot MakeSnapshot(params ClipboardFormatInfo[] formats)
    {
        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = [.. formats],
        };
    }

    private static ClipboardFormatInfo MakeFormat(uint id, string name, bool isStandard, long size)
    {
        return new ClipboardFormatInfo
        {
            FormatId = id,
            FormatName = name,
            IsStandard = isStandard,
            DataSize = size,
            Memory = new MemoryInfo("0x0", "0x0", size, []),
            RawData = new byte[Math.Min(size, 256)],
        };
    }
}
