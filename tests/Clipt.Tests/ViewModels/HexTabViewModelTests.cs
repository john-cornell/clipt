using Clipt.Help;
using Clipt.Models;
using Clipt.ViewModels;

namespace Clipt.Tests.ViewModels;

public class HexTabViewModelTests
{
    [Fact]
    public void BuildHexDump_EmptyArray_ReturnsEmptyString()
    {
        string result = Formatting.BuildHexDump([], 16);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildHexDump_SingleByte_ShowsCorrectFormat()
    {
        byte[] data = [0x41];
        string result = Formatting.BuildHexDump(data, 16);

        Assert.Contains("0000", result);
        Assert.Contains("41", result);
        Assert.Contains("A", result);
    }

    [Fact]
    public void BuildHexDump_FullRow_AlignedCorrectly()
    {
        byte[] data = new byte[16];
        for (int i = 0; i < 16; i++)
            data[i] = (byte)(0x30 + i);

        string result = Formatting.BuildHexDump(data, 16);

        Assert.Contains("0000", result);
        Assert.Contains("30", result);
        Assert.Contains("3F", result);
    }

    [Fact]
    public void BuildHexDump_NonPrintableBytes_ShowDots()
    {
        byte[] data = [0x01, 0x02, 0x7F, 0xFF];
        string result = Formatting.BuildHexDump(data, 16);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.EndsWith("....", lines[0].TrimEnd());
    }

    [Fact]
    public void BuildHexDump_MultipleRows_HasCorrectOffsets()
    {
        byte[] data = new byte[48];
        string result = Formatting.BuildHexDump(data, 16);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("0000", lines[0]);
        Assert.StartsWith("0010", lines[1]);
        Assert.StartsWith("0020", lines[2]);
    }

    [Fact]
    public void BuildHexDump_CustomBytesPerRow_Respected()
    {
        byte[] data = new byte[16];
        string result = Formatting.BuildHexDump(data, 8);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void BuildHexDump_BytesPerRowClamped_ToMax64()
    {
        byte[] data = new byte[128];
        string result = Formatting.BuildHexDump(data, 200);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void BuildHexDump_LargeData_UsesWideOffsets()
    {
        byte[] data = new byte[0x10001];
        string result = Formatting.BuildHexDump(data, 16);

        Assert.StartsWith("00000000", result);
    }

    [Fact]
    public void SelectedFormat_KnownFormat_PopulatesDescriptions()
    {
        var vm = new HexTabViewModel();
        var snapshot = MakeSnapshot(MakeFormat(13, "CF_UNICODETEXT", true, 100));

        vm.Update(snapshot);

        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedFormatShortDescription));
        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedFormatDetailedDescription));
        Assert.Contains("UTF-16", vm.SelectedFormatShortDescription);
    }

    [Fact]
    public void SelectedFormat_UnknownFormat_UsesFallback()
    {
        var vm = new HexTabViewModel();
        var snapshot = MakeSnapshot(MakeFormat(0xC999, "TotallyRandomFormat", false, 32));

        vm.Update(snapshot);

        Assert.Equal(FormatDescriptions.UnknownFormatFallback.Short, vm.SelectedFormatShortDescription);
        Assert.Equal(FormatDescriptions.UnknownFormatFallback.Detailed, vm.SelectedFormatDetailedDescription);
    }

    [Fact]
    public void SelectedFormat_NullFormat_ClearsDescriptions()
    {
        var vm = new HexTabViewModel();
        var snapshot = MakeSnapshot(MakeFormat(13, "CF_UNICODETEXT", true, 100));

        vm.Update(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedFormatShortDescription));

        vm.SelectedFormat = null;
        Assert.Equal(string.Empty, vm.SelectedFormatShortDescription);
        Assert.Equal(string.Empty, vm.SelectedFormatDetailedDescription);
    }

    [Fact]
    public void SelectedFormat_Change_UpdatesDescriptions()
    {
        var vm = new HexTabViewModel();
        var snapshot = MakeSnapshot(
            MakeFormat(13, "CF_UNICODETEXT", true, 100),
            MakeFormat(1, "CF_TEXT", true, 50));

        vm.Update(snapshot);

        var firstShort = vm.SelectedFormatShortDescription;

        vm.SelectedFormat = vm.AvailableFormats[1];

        Assert.NotEqual(firstShort, vm.SelectedFormatShortDescription);
        Assert.Contains("ANSI", vm.SelectedFormatShortDescription);
    }

    private static ClipboardSnapshot MakeSnapshot(params ClipboardFormatInfo[] formats) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = [.. formats],
        };

    private static ClipboardFormatInfo MakeFormat(uint id, string name, bool isStandard, long size) =>
        new()
        {
            FormatId = id,
            FormatName = name,
            IsStandard = isStandard,
            DataSize = size,
            Memory = new MemoryInfo("0x0", "0x0", size, []),
            RawData = new byte[Math.Min(size, 256)],
        };

    [Fact]
    public void Update_PopulatesOffsetColumn()
    {
        var vm = new HexTabViewModel();
        var snapshot = MakeSnapshot(MakeFormat(13, "CF_UNICODETEXT", true, 48));

        vm.Update(snapshot);

        Assert.False(string.IsNullOrWhiteSpace(vm.OffsetColumn));
        Assert.Contains("0000", vm.OffsetColumn);
    }

    [Fact]
    public void Update_PopulatesHexAndAsciiColumns()
    {
        var vm = new HexTabViewModel();
        var snapshot = MakeSnapshot(MakeFormat(13, "CF_UNICODETEXT", true, 16));

        vm.Update(snapshot);

        Assert.False(string.IsNullOrWhiteSpace(vm.HexColumn));
        Assert.False(string.IsNullOrWhiteSpace(vm.AsciiColumn));
    }

    [Fact]
    public void Update_SetsCurrentRawData()
    {
        var vm = new HexTabViewModel();
        var fmt = MakeFormat(1, "CF_TEXT", true, 10);
        var snapshot = MakeSnapshot(fmt);

        vm.Update(snapshot);

        Assert.Equal(fmt.RawData.Length, vm.CurrentRawData.Length);
    }

    [Fact]
    public void Update_WhileEditing_DoesNotOverwrite()
    {
        var vm = new HexTabViewModel();
        var snapshot1 = MakeSnapshot(MakeFormat(1, "CF_TEXT", true, 10));
        vm.Update(snapshot1);

        string original = vm.HexColumn;
        vm.IsEditing = true;

        var snapshot2 = MakeSnapshot(MakeFormat(1, "CF_TEXT", true, 20));
        vm.Update(snapshot2);

        Assert.Equal(original, vm.HexColumn);
    }

    [Fact]
    public void SelectedByteOffset_DefaultsToNegativeOne()
    {
        var vm = new HexTabViewModel();
        Assert.Equal(-1, vm.SelectedByteOffset);
    }

    [Fact]
    public void SelectedByteCount_DefaultsToZero()
    {
        var vm = new HexTabViewModel();
        Assert.Equal(0, vm.SelectedByteCount);
    }

    [Fact]
    public void ParseHexColumn_EmptyString_ReturnsEmptyArray()
    {
        byte[]? result = HexTabViewModel.ParseHexColumn("");
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseHexColumn_ValidHex_ParsesCorrectly()
    {
        byte[]? result = HexTabViewModel.ParseHexColumn("48 65 6C 6C 6F\r\n57 6F 72 6C 64");
        Assert.NotNull(result);
        Assert.Equal([0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x57, 0x6F, 0x72, 0x6C, 0x64], result);
    }

    [Fact]
    public void ParseHexColumn_InvalidToken_ReturnsNull()
    {
        byte[]? result = HexTabViewModel.ParseHexColumn("GG HH");
        Assert.Null(result);
    }

    [Fact]
    public void ParseHexColumn_TokenTooLong_ReturnsNull()
    {
        byte[]? result = HexTabViewModel.ParseHexColumn("ABC");
        Assert.Null(result);
    }

    [Fact]
    public void ParseHexColumn_WhitespaceOnly_ReturnsEmptyArray()
    {
        byte[]? result = HexTabViewModel.ParseHexColumn("   \n  \n  ");
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
