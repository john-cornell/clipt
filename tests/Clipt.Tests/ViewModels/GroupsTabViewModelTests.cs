using System.Collections.Immutable;
using System.Text;
using Clipt.Models;
using Clipt.Native;
using Clipt.Services;
using Clipt.ViewModels;
using Moq;
using Xunit;

namespace Clipt.Tests.ViewModels;

public class GroupsTabViewModelTests
{
    private readonly Mock<IClipboardGroupService> _groupMock;
    private readonly Mock<IClipboardHistoryService> _historyMock;
    private readonly Mock<IClipboardService> _clipboardMock;

    public GroupsTabViewModelTests()
    {
        _groupMock = new Mock<IClipboardGroupService>();
        _historyMock = new Mock<IClipboardHistoryService>();
        _historyMock.SetupProperty(h => h.IsSuppressed, false);
        _clipboardMock = new Mock<IClipboardService>();
        _historyMock.Setup(h => h.Entries).Returns(Array.Empty<ClipboardHistoryEntry>().AsReadOnly());
        _historyMock
            .Setup(h => h.RestoreGroupAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<GroupRestoreMode>()))
            .Returns(Task.CompletedTask);
    }

    private GroupsTabViewModel CreateVm()
    {
        return new GroupsTabViewModel(
            _groupMock.Object,
            _historyMock.Object,
            _clipboardMock.Object,
            () => (nint)42);
    }

    private static ClipboardHistoryEntry CreateEntry(string id, string summary, ContentType type)
    {
        return new ClipboardHistoryEntry
        {
            Id = id,
            Name = summary,
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcess = "test",
            OwnerPid = 1,
            Summary = summary,
            ContentType = type,
            DataSizeBytes = 100,
        };
    }

    [Fact]
    public void Refresh_EmptyGroups_SetsIsEmpty()
    {
        _groupMock.Setup(g => g.Groups).Returns(Array.Empty<ClipboardGroup>());

        var vm = CreateVm();
        vm.Refresh();

        Assert.True(vm.IsEmpty);
        Assert.Equal("No groups", vm.StatusText);
        Assert.Empty(vm.DisplayGroups);
    }

    [Fact]
    public void Refresh_WithGroups_PopulatesDisplay()
    {
        var groups = new List<ClipboardGroup>
        {
            new()
            {
                Id = "g1",
                Name = "Work",
                CreatedUtc = DateTime.UtcNow.AddDays(-1),
                EntryIds = new[] { "a", "b" },
            },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.False(vm.IsEmpty);
        Assert.Single(vm.DisplayGroups);
        Assert.Equal("Work", vm.DisplayGroups[0].Name);
        Assert.Equal("2 items", vm.DisplayGroups[0].ItemCountText);
    }

    [Fact]
    public async Task RestoreCommand_DelegatesToHistoryService()
    {
        var groups = new List<ClipboardGroup>
        {
            new()
            {
                Id = "g1",
                Name = "G",
                CreatedUtc = DateTime.UtcNow,
                EntryIds = new[] { "e1", "e2" },
            },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayGroups[0].RestoreCommand.ExecuteAsync(GroupRestoreMode.AddToTop);

        _historyMock.Verify(
            h => h.RestoreGroupAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 2 && ids[0] == "e1"),
                GroupRestoreMode.AddToTop),
            Times.Once);
    }

    [Fact]
    public async Task RestoreCommand_AfterGroupRestore_PutsTopHistoryEntryOnClipboard()
    {
        var topEntry = CreateEntry("top-id", "Top line", ContentType.Text);
        var entries = new List<ClipboardHistoryEntry> { topEntry };
        _historyMock.Setup(h => h.Entries).Returns(entries.AsReadOnly());

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
                RawData = Encoding.Unicode.GetBytes("Top line\0"),
            }),
        };
        _historyMock.Setup(h => h.RestoreAsync("top-id")).ReturnsAsync(snapshot);

        var groups = new List<ClipboardGroup>
        {
            new()
            {
                Id = "g1",
                Name = "G",
                CreatedUtc = DateTime.UtcNow,
                EntryIds = new[] { "top-id" },
            },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayGroups[0].RestoreCommand.ExecuteAsync(GroupRestoreMode.ClearAndRestore);

        _historyMock.Verify(h => h.RestoreAsync("top-id"), Times.Once);
        _clipboardMock.Verify(c => c.SetClipboardText("Top line", (nint)42), Times.Once);
    }

    [Fact]
    public async Task DeleteCommand_CallsGroupService()
    {
        _groupMock.Setup(g => g.DeleteGroupAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var groups = new List<ClipboardGroup>
        {
            new()
            {
                Id = "gid",
                Name = "G",
                CreatedUtc = DateTime.UtcNow,
                EntryIds = new[] { "a" },
            },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        await vm.DisplayGroups[0].DeleteCommand.ExecuteAsync(null);

        _groupMock.Verify(s => s.DeleteGroupAsync("gid"), Times.Once);
    }
}
