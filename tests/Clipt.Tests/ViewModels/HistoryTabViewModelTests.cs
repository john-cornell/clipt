using System.Collections.Immutable;
using System.Text;
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
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly Mock<ITrayIconService> _trayMock;
    private readonly Mock<IClipboardGroupService> _groupMock;

    public HistoryTabViewModelTests()
    {
        _historyMock = new Mock<IClipboardHistoryService>();
        _historyMock.SetupProperty(h => h.IsSuppressed, false);
        _clipboardMock = new Mock<IClipboardService>();
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.LoadClearClipboardWhenClearingHistory()).Returns(false);
        _trayMock = new Mock<ITrayIconService>();
        _groupMock = new Mock<IClipboardGroupService>();
        _groupMock.Setup(g => g.Groups).Returns(Array.Empty<ClipboardGroup>());
    }

    private HistoryTabViewModel CreateVm()
    {
        return new HistoryTabViewModel(
            _historyMock.Object,
            _clipboardMock.Object,
            () => (nint)0,
            _settingsMock.Object,
            _trayMock.Object,
            _groupMock.Object);
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
    public async Task ClearAllCommand_EmptyClipboard_CallsClearAsync()
    {
        _clipboardMock.Setup(c => c.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(new ClipboardSnapshot
            {
                Timestamp = DateTime.UtcNow,
                SequenceNumber = 1,
                OwnerProcessName = "test",
                OwnerProcessId = 1,
                Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
            });

        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        await vm.ClearAllCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.ClearAsync(), Times.Once);
        _historyMock.Verify(h => h.ClearExceptMatchingContentHashAsync(It.IsAny<string>()), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task ClearAllCommand_WithClipboard_CallsClearExceptMatchingHash()
    {
        byte[] data = Encoding.Unicode.GetBytes("KeepHash\0");
        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 99,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = data.Length,
                Memory = new MemoryInfo("0x0", "0x0", data.Length, []),
                RawData = data,
            }),
        };
        string expectedHash = ClipboardHistoryService.ComputeContentHash(snapshot);

        _clipboardMock.Setup(c => c.CaptureSnapshot(It.IsAny<nint>())).Returns(snapshot);
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        await vm.ClearAllCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.ClearExceptMatchingContentHashAsync(expectedHash), Times.Once);
        _historyMock.Verify(h => h.ClearAsync(), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public async Task ClearAllCommand_AlsoClearClipboardTrue_CallsClearClipboardAfterHistory()
    {
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        vm.AlsoClearClipboardOnClearHistory = true;
        await vm.ClearAllCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.ClearAsync(), Times.Once);
        _historyMock.Verify(h => h.ClearExceptMatchingContentHashAsync(It.IsAny<string>()), Times.Never);
        _clipboardMock.Verify(c => c.CaptureSnapshot(It.IsAny<nint>()), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Once);
        Assert.False(_historyMock.Object.IsSuppressed,
            "Clear path must never touch IsSuppressed (suppression is only for restore)");
    }

    [Fact]
    public async Task ClearAllCommand_AlsoClearClipboardFalse_LeavesIsNotSuppressed()
    {
        _clipboardMock.Setup(c => c.CaptureSnapshot(It.IsAny<nint>()))
            .Returns(new ClipboardSnapshot
            {
                Timestamp = DateTime.UtcNow,
                SequenceNumber = 1,
                OwnerProcessName = "test",
                OwnerProcessId = 1,
                Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
            });
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        vm.AlsoClearClipboardOnClearHistory = false;
        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.False(_historyMock.Object.IsSuppressed, "IsSuppressed must be false after clear without clipboard option");
    }

    [Fact]
    public void ShowMoveControls_DefaultsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.ShowMoveControls);
    }

    [Fact]
    public void ShowTextExpandControls_DefaultsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.ShowTextExpandControls);
    }

    [Fact]
    public async Task ClearAllIncludingClipboardCommand_AlwaysClearsHistoryAndClipboard()
    {
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        await vm.ClearAllIncludingClipboardCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.ClearAsync(), Times.Once);
        _historyMock.Verify(h => h.ClearExceptMatchingContentHashAsync(It.IsAny<string>()), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Once);
    }

    [Fact]
    public async Task ClearHistoryOnlyCommand_CallsClearExceptMatchingHash()
    {
        byte[] data = Encoding.Unicode.GetBytes("KeepHash\0");
        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 99,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = data.Length,
                Memory = new MemoryInfo("0x0", "0x0", data.Length, []),
                RawData = data,
            }),
        };
        string expectedHash = ClipboardHistoryService.ComputeContentHash(snapshot);

        _clipboardMock.Setup(c => c.CaptureSnapshot(It.IsAny<nint>())).Returns(snapshot);
        _historyMock.Setup(h => h.Entries)
            .Returns(new List<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        await vm.ClearHistoryOnlyCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.ClearExceptMatchingContentHashAsync(expectedHash), Times.Once);
        _historyMock.Verify(h => h.ClearAsync(), Times.Never);
        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Never);
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

    [Fact]
    public void IsEditing_DefaultsFalse()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.False(vm.DisplayEntries[0].IsEditing);
    }

    [Fact]
    public void IsEditing_TogglesAndRaisesPropertyChanged()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        var item = vm.DisplayEntries[0];
        var changedProperties = new List<string>();
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                changedProperties.Add(e.PropertyName);
        };

        item.IsEditing = true;

        Assert.True(item.IsEditing);
        Assert.Contains(nameof(HistoryEntryDisplayItem.IsEditing), changedProperties);
    }

    [Fact]
    public void IsEditing_SetFalse_AfterTrue_RaisesPropertyChanged()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        var item = vm.DisplayEntries[0];
        item.IsEditing = true;

        var changedProperties = new List<string>();
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                changedProperties.Add(e.PropertyName);
        };

        item.IsEditing = false;

        Assert.False(item.IsEditing);
        Assert.Contains(nameof(HistoryEntryDisplayItem.IsEditing), changedProperties);
    }

    [Fact]
    public async Task RenameCommand_AfterEditingToggle_CommitsNewName()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Original", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        var item = vm.DisplayEntries[0];
        item.IsEditing = true;
        item.Name = "Renamed";
        item.IsEditing = false;

        await item.RenameCommand!.ExecuteAsync("Renamed");

        _historyMock.Verify(h => h.RenameAsync("a", "Renamed"), Times.Once);
    }

    [Fact]
    public void Name_SetWhileEditing_RaisesPropertyChanged()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        var item = vm.DisplayEntries[0];
        item.IsEditing = true;

        var changedProperties = new List<string>();
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                changedProperties.Add(e.PropertyName);
        };

        item.Name = "NewName";

        Assert.Equal("NewName", item.Name);
        Assert.Contains(nameof(HistoryEntryDisplayItem.Name), changedProperties);
    }

    [Fact]
    public void DisplayEntries_AnyIsEditing_ReturnsTrueWhenItemEditing()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Second", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.DoesNotContain(vm.DisplayEntries, i => i.IsEditing);

        vm.DisplayEntries[1].IsEditing = true;

        Assert.Contains(vm.DisplayEntries, i => i.IsEditing);

        vm.DisplayEntries[1].IsEditing = false;

        Assert.DoesNotContain(vm.DisplayEntries, i => i.IsEditing);
    }

    [Fact]
    public void Refresh_SetsIsFirstAndIsLast_Correctly()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Middle", ContentType.Text, minutesAgo: 10),
            CreateEntry("c", "Last", ContentType.Text, minutesAgo: 20),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.True(vm.DisplayEntries[0].IsFirst);
        Assert.False(vm.DisplayEntries[0].IsLast);

        Assert.False(vm.DisplayEntries[1].IsFirst);
        Assert.False(vm.DisplayEntries[1].IsLast);

        Assert.False(vm.DisplayEntries[2].IsFirst);
        Assert.True(vm.DisplayEntries[2].IsLast);
    }

    [Fact]
    public void Refresh_SingleEntry_IsFirstAndIsLastBothTrue()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("only", "Only", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.True(vm.DisplayEntries[0].IsFirst);
        Assert.True(vm.DisplayEntries[0].IsLast);
    }

    [Fact]
    public void MoveUpCommand_IsNull_ForFirstEntry()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Second", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Null(vm.DisplayEntries[0].MoveUpCommand);
        Assert.NotNull(vm.DisplayEntries[1].MoveUpCommand);
    }

    [Fact]
    public void MoveDownCommand_IsNull_ForLastEntry()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Second", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.NotNull(vm.DisplayEntries[0].MoveDownCommand);
        Assert.Null(vm.DisplayEntries[1].MoveDownCommand);
    }

    [Fact]
    public void MoveCommands_SingleEntry_BothNull()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("only", "Only", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Null(vm.DisplayEntries[0].MoveUpCommand);
        Assert.Null(vm.DisplayEntries[0].MoveDownCommand);
    }

    [Fact]
    public async Task MoveDownCommand_TopEntry_CallsMoveAndRestoresNewTop()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Second", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.MoveAsync("a", +1))
            .Callback(() =>
            {
                var tmp = entries[0];
                entries[0] = entries[1];
                entries[1] = tmp;
            })
            .Returns(Task.CompletedTask);

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 16,
                Memory = new MemoryInfo("0x0", "0x0", 16, []),
                RawData = Encoding.Unicode.GetBytes("Second\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("b")).ReturnsAsync(snapshot);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[0].MoveDownCommand!.ExecuteAsync(null);

        _historyMock.Verify(h => h.MoveAsync("a", +1), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText("Second", It.IsAny<nint>()), Times.Once);
    }

    [Fact]
    public async Task MoveUpCommand_NonTopEntry_NoClipboardRestore()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Second", ContentType.Text, minutesAgo: 10),
            CreateEntry("c", "Third", ContentType.Text, minutesAgo: 20),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        _historyMock.Setup(h => h.MoveAsync("c", -1))
            .Callback(() =>
            {
                var tmp = entries[2];
                entries[2] = entries[1];
                entries[1] = tmp;
            })
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayEntries[2].MoveUpCommand!.ExecuteAsync(null);

        _historyMock.Verify(h => h.MoveAsync("c", -1), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText(It.IsAny<string>(), It.IsAny<nint>()), Times.Never);
        _clipboardMock.Verify(c => c.SetMultipleClipboardData(It.IsAny<IReadOnlyList<(uint, byte[])>>(), It.IsAny<nint>()), Times.Never);
    }

    [Fact]
    public void EnterSaveSelectionMode_SetsFlowAndClearsCheckboxes()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.DisplayEntries[0].IsSelected = true;

        vm.EnterSaveSelectionModeCommand.Execute(null);

        Assert.Equal(HistorySelectionFlow.SaveGroup, vm.SelectionFlow);
        Assert.True(vm.IsSelectionMode);
        Assert.False(vm.IsNamingGroup);
        Assert.False(vm.DisplayEntries[0].IsSelected);
        Assert.False(vm.SelectAllHeaderChecked);
    }

    [Fact]
    public void BeginSaveSelected_WithoutSelection_IsDisabled()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterSaveSelectionModeCommand.Execute(null);

        Assert.False(vm.BeginSaveSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void BeginSaveSelected_WithSelection_OpensNamingPrompt()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterSaveSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;

        vm.BeginSaveSelectedCommand.Execute(null);

        Assert.True(vm.IsNamingGroup);
        Assert.Equal("New group", vm.NewGroupName);
    }

    [Fact]
    public async Task ConfirmSaveGroupAsync_InvokesGroupServiceWithOrderedIds()
    {
        _groupMock
            .Setup(g => g.SaveGroupAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(Task.CompletedTask);

        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "First", ContentType.Text),
            CreateEntry("b", "Second", ContentType.Text, minutesAgo: 10),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterSaveSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;
        vm.DisplayEntries[1].IsSelected = true;
        vm.BeginSaveSelectedCommand.Execute(null);
        vm.NewGroupName = "Pack";

        await vm.ConfirmSaveGroupCommand.ExecuteAsync(null);

        _groupMock.Verify(
            g => g.SaveGroupAsync(
                "Pack",
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 2 && ids[0] == "a" && ids[1] == "b")),
            Times.Once);
    }

    [Fact]
    public void EnterSaveOrDeleteSelectionMode_EmptyHistory_IsDisabled()
    {
        _historyMock.Setup(h => h.Entries).Returns(Array.Empty<ClipboardHistoryEntry>().AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.EnterSaveSelectionModeCommand.CanExecute(null));
        Assert.False(vm.EnterDeleteSelectionModeCommand.CanExecute(null));
    }

    [Fact]
    public void EnterDeleteSelectionMode_SetsFlowAndClearsCheckboxes()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "Hello", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.DisplayEntries[0].IsSelected = true;

        vm.EnterDeleteSelectionModeCommand.Execute(null);

        Assert.Equal(HistorySelectionFlow.DeleteEntries, vm.SelectionFlow);
        Assert.True(vm.ShowDeleteSelectedInChrome);
        Assert.False(vm.ShowSaveSelectedInChrome);
        Assert.False(vm.DisplayEntries[0].IsSelected);
    }

    [Fact]
    public async Task DeleteSelected_RemovesEachSelectedEntry()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "A", ContentType.Text),
            CreateEntry("b", "B", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RemoveAsync(It.IsAny<string>()))
            .Callback((string id) => entries.RemoveAll(e => e.Id == id))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterDeleteSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;
        vm.DisplayEntries[1].IsSelected = true;

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RemoveAsync("a"), Times.Once);
        _historyMock.Verify(h => h.RemoveAsync("b"), Times.Once);
        Assert.Equal(HistorySelectionFlow.None, vm.SelectionFlow);
    }

    [Fact]
    public async Task DeleteSelected_WhenOnlyTopSelected_RestoresNextToClipboard()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("top", "Top", ContentType.Text),
            CreateEntry("next", "Next", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RemoveAsync(It.IsAny<string>()))
            .Callback((string id) => entries.RemoveAll(e => e.Id == id))
            .Returns(Task.CompletedTask);

        var nextSnapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 2,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 14,
                Memory = new MemoryInfo("0x0", "0x0", 14, []),
                RawData = Encoding.Unicode.GetBytes("Next\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("next")).ReturnsAsync(nextSnapshot);

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterDeleteSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        _historyMock.Verify(h => h.RestoreAsync("next"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText("Next", It.IsAny<nint>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSelected_WhenTopAndRestSelected_ClearsClipboardWhenHistoryEmpty()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("top", "Top", ContentType.Text),
            CreateEntry("next", "Next", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());
        _historyMock.Setup(h => h.RemoveAsync(It.IsAny<string>()))
            .Callback((string id) => entries.RemoveAll(e => e.Id == id))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterDeleteSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;
        vm.DisplayEntries[1].IsSelected = true;

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        _clipboardMock.Verify(c => c.ClearClipboard(It.IsAny<nint>()), Times.Once);
        _historyMock.Verify(h => h.RestoreAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void SelectAllHeader_SyncsCheckedWhenAllRowsSelectedManually()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "A", ContentType.Text),
            CreateEntry("b", "B", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterSaveSelectionModeCommand.Execute(null);

        Assert.False(vm.SelectAllHeaderChecked);
        vm.DisplayEntries[0].IsSelected = true;
        Assert.False(vm.SelectAllHeaderChecked);
        vm.DisplayEntries[1].IsSelected = true;
        Assert.True(vm.SelectAllHeaderChecked);
    }

    [Fact]
    public void SelectAllHeader_UnchecksWhenOneRowUncheckedFromFullSet_KeepsOtherSelections()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "A", ContentType.Text),
            CreateEntry("b", "B", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterSaveSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;
        vm.DisplayEntries[1].IsSelected = true;

        Assert.True(vm.SelectAllHeaderChecked);
        vm.DisplayEntries[1].IsSelected = false;

        Assert.False(vm.SelectAllHeaderChecked);
        Assert.True(vm.DisplayEntries[0].IsSelected);
    }

    [Fact]
    public void SelectAllHeader_Check_SelectsAllRows()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "A", ContentType.Text),
            CreateEntry("b", "B", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterDeleteSelectionModeCommand.Execute(null);
        vm.SelectAllHeaderChecked = true;

        Assert.True(vm.DisplayEntries[0].IsSelected);
        Assert.True(vm.DisplayEntries[1].IsSelected);
        Assert.True(vm.SelectAllHeaderChecked);
    }

    [Fact]
    public void SelectAllHeader_Uncheck_DeselectsAllRows()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "A", ContentType.Text),
            CreateEntry("b", "B", ContentType.Text, minutesAgo: 1),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterSaveSelectionModeCommand.Execute(null);
        vm.SelectAllHeaderChecked = true;
        vm.SelectAllHeaderChecked = false;

        Assert.False(vm.DisplayEntries[0].IsSelected);
        Assert.False(vm.DisplayEntries[1].IsSelected);
    }

    [Fact]
    public void BeginSaveSelected_DisabledInDeleteFlow()
    {
        var entries = new List<ClipboardHistoryEntry>
        {
            CreateEntry("a", "A", ContentType.Text),
        };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();
        vm.EnterDeleteSelectionModeCommand.Execute(null);
        vm.DisplayEntries[0].IsSelected = true;

        Assert.False(vm.BeginSaveSelectedCommand.CanExecute(null));
        Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
    }
}
