using Clipt.Models;
using Clipt.Native;
using Clipt.Services;
using Clipt.ViewModels;
using Moq;

namespace Clipt.Tests.ViewModels;

public class HistoryTabViewModelTests
{
    private readonly Mock<IClipboardHistoryService> _historyMock;
    private readonly Mock<IClipboardService> _clipboardMock;

    public HistoryTabViewModelTests()
    {
        _historyMock = new Mock<IClipboardHistoryService>();
        _historyMock.SetupProperty(h => h.IsSuppressed, false);
        _clipboardMock = new Mock<IClipboardService>();
    }

    private HistoryTabViewModel CreateVm()
    {
        return new HistoryTabViewModel(
            _historyMock.Object,
            _clipboardMock.Object,
            () => (nint)0);
    }

    private static ClipboardHistoryEntry CreateEntry(
        string id, string summary, ContentType type, int minutesAgo = 5)
    {
        return new ClipboardHistoryEntry
        {
            Id = id,
            Name = summary,
            TimestampUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
            SequenceNumber = 1,
            OwnerProcess = "test",
            OwnerPid = 1,
            Summary = summary,
            ContentType = type,
            DataSizeBytes = 100,
        };
    }

    [Fact]
    public void Refresh_EmptyHistory_SetsIsEmpty()
    {
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.True(vm.IsEmpty);
        Assert.Equal("No history", vm.StatusText);
        Assert.Empty(vm.DisplayEntries);
    }

    [Fact]
    public void Refresh_WithEntries_PopulatesDisplayEntries()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
            CreateEntry("b", "Image", ContentType.Image, minutesAgo: 30),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.False(vm.IsEmpty);
        Assert.Equal("2 entries", vm.StatusText);
        Assert.Equal(2, vm.DisplayEntries.Count);
        Assert.Equal("Hello", vm.DisplayEntries[0].Summary);
        Assert.Equal("Text", vm.DisplayEntries[0].ContentTypeLabel);
        Assert.Equal("Image", vm.DisplayEntries[1].Summary);
        Assert.Equal("Image", vm.DisplayEntries[1].ContentTypeLabel);
    }

    [Fact]
    public void Refresh_SingleEntry_ShowsSingularStatus()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Solo", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Equal("1 entry", vm.StatusText);
    }

    [Fact]
    public async Task ClearAllCommand_CallsServiceClear()
    {
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        await vm.ClearAllCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.ClearAsync(), Times.Once);
    }

    [Fact]
    public void FormatContentType_AllTypes()
    {
        Assert.Equal("Text", HistoryTabViewModel.FormatContentType(ContentType.Text));
        Assert.Equal("Image", HistoryTabViewModel.FormatContentType(ContentType.Image));
        Assert.Equal("Files", HistoryTabViewModel.FormatContentType(ContentType.Files));
        Assert.Equal("Data", HistoryTabViewModel.FormatContentType(ContentType.Other));
        Assert.Equal("", HistoryTabViewModel.FormatContentType(ContentType.Empty));
    }

    [Fact]
    public void FormatRelativeTime_JustNow()
    {
        string result = HistoryTabViewModel.FormatRelativeTime(DateTime.UtcNow.AddSeconds(-2));
        Assert.Equal("just now", result);
    }

    [Fact]
    public void FormatRelativeTime_Seconds()
    {
        string result = HistoryTabViewModel.FormatRelativeTime(DateTime.UtcNow.AddSeconds(-30));
        Assert.Equal("30s ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Minutes()
    {
        string result = HistoryTabViewModel.FormatRelativeTime(DateTime.UtcNow.AddMinutes(-15));
        Assert.Equal("15m ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Hours()
    {
        string result = HistoryTabViewModel.FormatRelativeTime(DateTime.UtcNow.AddHours(-3));
        Assert.Equal("3h ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Days()
    {
        string result = HistoryTabViewModel.FormatRelativeTime(DateTime.UtcNow.AddDays(-4));
        Assert.Equal("4d ago", result);
    }

    [Fact]
    public void FormatRelativeTime_OlderThanWeek_ShowsDate()
    {
        var ts = DateTime.UtcNow.AddDays(-10);
        string result = HistoryTabViewModel.FormatRelativeTime(ts);
        Assert.Contains(ts.ToLocalTime().ToString("MMM"), result);
    }

    [Fact]
    public async Task RestoreCommand_InvokesRestoreAndSetsClipboard()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("abc", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 24,
                Memory = new MemoryInfo("0x0", "0x0", 24, []),
                RawData = System.Text.Encoding.Unicode.GetBytes("Hello\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("abc")).ReturnsAsync(snapshot);

        var vm = CreateVm();
        vm.Refresh();

        bool restoredEventRaised = false;
        vm.ClipboardRestored += (_, _) => restoredEventRaised = true;

        await vm.DisplayEntries[0].RestoreCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RestoreAsync("abc"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()), Times.Once);
        Assert.True(restoredEventRaised);
    }

    [Fact]
    public async Task DeleteCommand_InvokesRemove()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("keep1", "First", ContentType.Text),
            CreateEntry("del1", "ToDelete", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.RemoveAsync("del1"))
            .Callback(() => entries.RemoveAll(e => e.Id == "del1"))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[1].DeleteCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RemoveAsync("del1"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCommand_ActiveEntry_WithNextEntry_RestoresNextToClipboard()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("active1", "Active", ContentType.Text),
            CreateEntry("next1", "Next", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.RemoveAsync("active1"))
            .Callback(() => entries.RemoveAll(e => e.Id == "active1"))
            .Returns(Task.CompletedTask);

        var nextSnapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 2,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 14,
                Memory = new MemoryInfo("0x0", "0x0", 14, []),
                RawData = System.Text.Encoding.Unicode.GetBytes("Next\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("next1")).ReturnsAsync(nextSnapshot);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].DeleteCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RemoveAsync("active1"), Times.Once);
        _historyMock.Verify(h => h.RestoreAsync("next1"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText("Next", It.IsAny<nint>()), Times.Once);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCommand_ActiveEntry_NoNextEntry_ClearsClipboard()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("only1", "OnlyEntry", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.RemoveAsync("only1"))
            .Callback(() => entries.Clear())
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].DeleteCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RemoveAsync("only1"), Times.Once);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCommand_NonActiveEntry_DoesNotTouchClipboard()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("keep1", "Active", ContentType.Text),
            CreateEntry("del1", "Older", ContentType.Text, minutesAgo: 10),
            CreateEntry("del2", "Oldest", ContentType.Text, minutesAgo: 20),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.RemoveAsync("del1"))
            .Callback(() => entries.RemoveAll(e => e.Id == "del1"))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[1].DeleteCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RemoveAsync("del1"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()), Times.Never);
        _clipboardMock.Verify(c => c.SetMultipleClipboardData(It.IsAny<IReadOnlyList<(uint, byte[])>>(), It.IsAny<nint>()), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCommand_ActiveEntry_SuppressesDuringRestore()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("active1", "Active", ContentType.Text),
            CreateEntry("next1", "Next", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.RemoveAsync("active1"))
            .Callback(() => entries.RemoveAll(e => e.Id == "active1"))
            .Returns(Task.CompletedTask);

        var nextSnapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 2,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 14,
                Memory = new MemoryInfo("0x0", "0x0", 14, []),
                RawData = System.Text.Encoding.Unicode.GetBytes("Next\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("next1")).ReturnsAsync(nextSnapshot);

        bool wasSuppressedDuringSet = false;
        _clipboardMock.Setup(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()))
            .Callback(() => wasSuppressedDuringSet = _historyMock.Object.IsSuppressed);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].DeleteCommand.ExecuteAsync(null);

        Assert.True(wasSuppressedDuringSet);
        await Task.Delay(600);
        Assert.False(_historyMock.Object.IsSuppressed);
    }

    [Fact]
    public async Task DeleteCommand_ActiveEntry_RestoreReturnsNull_NoClipboardWrite()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("active1", "Active", ContentType.Text),
            CreateEntry("next1", "Next", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.RemoveAsync("active1"))
            .Callback(() => entries.RemoveAll(e => e.Id == "active1"))
            .Returns(Task.CompletedTask);

        _historyMock.Setup(h => h.RestoreAsync("next1")).ReturnsAsync((ClipboardSnapshot?)null);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].DeleteCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RestoreAsync("next1"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task RestoreCommand_SetsAndClearsSuppression()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("sup1", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 24,
                Memory = new MemoryInfo("0x0", "0x0", 24, []),
                RawData = System.Text.Encoding.Unicode.GetBytes("Hello\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("sup1")).ReturnsAsync(snapshot);

        bool wasSuppressedDuringSet = false;
        _clipboardMock.Setup(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()))
            .Callback(() => wasSuppressedDuringSet = _historyMock.Object.IsSuppressed);

        var vm = CreateVm();
        vm.Refresh();
        await vm.DisplayEntries[0].RestoreCommand.ExecuteAsync(null);

        Assert.True(wasSuppressedDuringSet);
        await Task.Delay(600);
        Assert.False(_historyMock.Object.IsSuppressed);
    }

    [Fact]
    public async Task LoadThumbnailAsync_NonImageEntry_DoesNotCallRestore()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("txt1", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.LoadThumbnailAsync("txt1");

        _historyMock.Verify(h => h.RestoreAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoadThumbnailAsync_UnknownEntry_DoesNotCallRestore()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img1", "Image", ContentType.Image),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RestoreAsync("img1")).ReturnsAsync((ClipboardSnapshot?)null);

        var vm = CreateVm();
        vm.Refresh();

        await vm.LoadThumbnailAsync("nonexistent");

        _historyMock.Verify(h => h.RestoreAsync("nonexistent"), Times.Never);
    }

    [Fact]
    public async Task LoadThumbnailAsync_ImageEntry_NullSnapshot_DoesNotCrash()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img1", "Image", ContentType.Image),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RestoreAsync("img1")).ReturnsAsync((ClipboardSnapshot?)null);

        var vm = CreateVm();
        vm.Refresh();

        await vm.LoadThumbnailAsync("img1");

        _historyMock.Verify(h => h.RestoreAsync("img1"), Times.AtLeastOnce);
        Assert.Null(vm.DisplayEntries[0].PreviewThumbnail);
    }

    [Fact]
    public async Task LoadThumbnailAsync_ImageEntry_NoImageFormats_DoesNotCrash()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img2", "Image", ContentType.Image),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 10,
                Memory = new MemoryInfo("0x0", "0x0", 10, []),
                RawData = System.Text.Encoding.Unicode.GetBytes("text\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("img2")).ReturnsAsync(snapshot);

        var vm = CreateVm();
        vm.Refresh();

        await vm.LoadThumbnailAsync("img2");

        Assert.Null(vm.DisplayEntries[0].PreviewThumbnail);
    }

    [Fact]
    public async Task LoadInlineThumbnailsAsync_SkipsNonImageEntries()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("txt1", "Hello", ContentType.Text),
            CreateEntry("file1", "report.pdf", ContentType.Files, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.LoadInlineThumbnailsAsync();

        _historyMock.Verify(h => h.RestoreAsync(It.IsAny<string>()), Times.Never);
        Assert.Null(vm.DisplayEntries[0].InlineThumbnail);
        Assert.Null(vm.DisplayEntries[1].InlineThumbnail);
    }

    [Fact]
    public async Task LoadInlineThumbnailsAsync_ImageEntry_NullSnapshot_DoesNotCrash()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img3", "1920\u00D71080", ContentType.Image),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RestoreAsync("img3")).ReturnsAsync((ClipboardSnapshot?)null);

        var vm = CreateVm();
        vm.Refresh();

        await vm.LoadInlineThumbnailsAsync();

        _historyMock.Verify(h => h.RestoreAsync("img3"), Times.AtLeastOnce);
        Assert.Null(vm.DisplayEntries[0].InlineThumbnail);
    }

    [Fact]
    public void PreviewCommand_IsNullForNonImageEntries()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("txt1", "Hello", ContentType.Text),
            CreateEntry("file1", "report.pdf", ContentType.Files, minutesAgo: 10),
            CreateEntry("other1", "Data", ContentType.Other, minutesAgo: 15),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Null(vm.DisplayEntries[0].PreviewCommand);
        Assert.Null(vm.DisplayEntries[1].PreviewCommand);
        Assert.Null(vm.DisplayEntries[2].PreviewCommand);
    }

    [Fact]
    public void PreviewCommand_IsNotNullForImageEntries()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img1", "1920\u00D71080", ContentType.Image),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RestoreAsync("img1")).ReturnsAsync((ClipboardSnapshot?)null);

        var vm = CreateVm();
        vm.Refresh();

        Assert.NotNull(vm.DisplayEntries[0].PreviewCommand);
    }

    [Fact]
    public async Task RestoreCommand_ImageEntry_CallsSetMultipleClipboardData()
    {
        var dibData = new byte[44];
        dibData[0] = 40; // biSize = 40 for BITMAPINFOHEADER
        BitConverter.GetBytes(100).CopyTo(dibData, 4); // width
        BitConverter.GetBytes(100).CopyTo(dibData, 8); // height

        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img1", "100\u00D7100", ContentType.Image, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(
                new ClipboardFormatInfo
                {
                    FormatId = ClipboardConstants.CF_BITMAP,
                    FormatName = "CF_BITMAP",
                    IsStandard = true,
                    DataSize = 0,
                    Memory = new MemoryInfo("0x0", "(GDI handle)", 0, []),
                    RawData = [],
                },
                new ClipboardFormatInfo
                {
                    FormatId = ClipboardConstants.CF_DIB,
                    FormatName = "CF_DIB",
                    IsStandard = true,
                    DataSize = dibData.Length,
                    Memory = new MemoryInfo("0x0", "0x0", dibData.Length, dibData),
                    RawData = dibData,
                }),
        };
        _historyMock.Setup(h => h.RestoreAsync("img1")).ReturnsAsync(snapshot);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].RestoreCommand.ExecuteAsync(null);

        _clipboardMock.Verify(
            c => c.SetMultipleClipboardData(
                It.Is<IReadOnlyList<(uint FormatId, byte[] Data)>>(
                    list => list.Count == 1 && list[0].FormatId == ClipboardConstants.CF_DIB),
                It.IsAny<nint>()),
            Times.Once);
    }

    [Fact]
    public async Task RestoreCommand_ImageEntry_SkipsGdiHandleFormats()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("img2", "Image", ContentType.Image, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(
                new ClipboardFormatInfo
                {
                    FormatId = ClipboardConstants.CF_BITMAP,
                    FormatName = "CF_BITMAP",
                    IsStandard = true,
                    DataSize = 0,
                    Memory = new MemoryInfo("0x0", "(GDI handle)", 0, []),
                    RawData = [],
                },
                new ClipboardFormatInfo
                {
                    FormatId = ClipboardConstants.CF_ENHMETAFILE,
                    FormatName = "CF_ENHMETAFILE",
                    IsStandard = true,
                    DataSize = 0,
                    Memory = new MemoryInfo("0x0", "(GDI handle)", 0, []),
                    RawData = [],
                }),
        };
        _historyMock.Setup(h => h.RestoreAsync("img2")).ReturnsAsync(snapshot);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].RestoreCommand.ExecuteAsync(null);

        _clipboardMock.Verify(
            c => c.SetMultipleClipboardData(It.IsAny<IReadOnlyList<(uint, byte[])>>(), It.IsAny<nint>()),
            Times.Never);
        _clipboardMock.Verify(
            c => c.SetClipboardData(It.IsAny<uint>(), It.IsAny<byte[]>(), It.IsAny<nint>()),
            Times.Never);
    }

    [Fact]
    public void Refresh_PopulatesNameFromEntry()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Equal("Hello", vm.DisplayEntries[0].Name);
    }

    [Fact]
    public void Refresh_SetsRenameCommand()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.NotNull(vm.DisplayEntries[0].RenameCommand);
    }

    [Fact]
    public async Task RenameCommand_CallsServiceRename()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].RenameCommand!.ExecuteAsync("New Name");

        _historyMock.Verify(h => h.RenameAsync("a", "New Name"), Times.Once);
    }

    [Fact]
    public async Task RenameCommand_EmptyName_DoesNotCallService()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].RenameCommand!.ExecuteAsync("   ");

        _historyMock.Verify(h => h.RenameAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RenameCommand_TrimsWhitespace()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].RenameCommand!.ExecuteAsync("  Trimmed  ");

        _historyMock.Verify(h => h.RenameAsync("a", "Trimmed"), Times.Once);
    }
}
