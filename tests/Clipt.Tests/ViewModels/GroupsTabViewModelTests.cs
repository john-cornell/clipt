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
    private readonly Mock<ISettingsService> _settingsMock;

    public GroupsTabViewModelTests()
    {
        _groupMock = new Mock<IClipboardGroupService>();
        _groupMock.Setup(g => g.Folders).Returns(Array.Empty<ClipboardGroupFolder>());
        _historyMock = new Mock<IClipboardHistoryService>();
        _historyMock.SetupProperty(h => h.IsSuppressed, false);
        _clipboardMock = new Mock<IClipboardService>();
        _historyMock.Setup(h => h.Entries).Returns(Array.Empty<ClipboardHistoryEntry>().AsReadOnly());
        _historyMock
            .Setup(h => h.RestoreGroupAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<GroupRestoreMode>()))
            .Returns(Task.CompletedTask);
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.LoadGroupSortMode()).Returns(GroupSortMode.DateCreated);
        _settingsMock.Setup(s => s.LoadGroupsUngroupedCollapsed()).Returns(false);
    }

    private GroupsTabViewModel CreateVm()
    {
        return new GroupsTabViewModel(
            _groupMock.Object,
            _historyMock.Object,
            _clipboardMock.Object,
            _settingsMock.Object,
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
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void Refresh_WithGroups_PopulatesUngroupedSection()
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
        Assert.Single(vm.Sections);
        GroupSectionDisplayItem ungrouped = vm.Sections[0];
        Assert.False(ungrouped.IsFolder);
        Assert.Equal("Ungrouped", ungrouped.Name);
        Assert.Single(ungrouped.Groups);
        Assert.Equal("Work", ungrouped.Groups[0].Name);
        Assert.Equal("2 items", ungrouped.Groups[0].ItemCountText);
    }

    [Fact]
    public void Refresh_GroupsInFolder_BuildsFolderSectionAfterUngrouped()
    {
        var folders = new List<ClipboardGroupFolder>
        {
            new() { Id = "f1", Name = "Clients", CreatedUtc = DateTime.UtcNow, IsCollapsed = false },
        };
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "Ungrouped one", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" } },
            new() { Id = "g2", Name = "Filed one", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "b" }, FolderId = "f1" },
        };
        _groupMock.Setup(g => g.Folders).Returns(folders.AsReadOnly());
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Equal(2, vm.Sections.Count);
        Assert.False(vm.Sections[0].IsFolder);
        Assert.Single(vm.Sections[0].Groups);
        Assert.Equal("Ungrouped one", vm.Sections[0].Groups[0].Name);

        Assert.True(vm.Sections[1].IsFolder);
        Assert.Equal("Clients", vm.Sections[1].Name);
        Assert.Single(vm.Sections[1].Groups);
        Assert.Equal("Filed one", vm.Sections[1].Groups[0].Name);
        Assert.Single(vm.FolderSections);
    }

    [Fact]
    public void Refresh_AlphabeticalSortMode_OrdersGroupsWithinSection()
    {
        _settingsMock.Setup(s => s.LoadGroupSortMode()).Returns(GroupSortMode.Alphabetical);
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "Zebra", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" } },
            new() { Id = "g2", Name = "Apple", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "b" } },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Equal("Apple", vm.Sections[0].Groups[0].Name);
        Assert.Equal("Zebra", vm.Sections[0].Groups[1].Name);
    }

    [Fact]
    public void MoveUpDownCommands_NullUnlessCustomSortMode()
    {
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "A", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" } },
            new() { Id = "g2", Name = "B", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "b" } },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.Null(vm.Sections[0].Groups[0].MoveUpCommand);
        Assert.Null(vm.Sections[0].Groups[0].MoveDownCommand);
        Assert.False(vm.ShowMoveControls);
    }

    [Fact]
    public void MoveUpDownCommands_CustomSortMode_NullOnlyAtBoundaries()
    {
        _settingsMock.Setup(s => s.LoadGroupSortMode()).Returns(GroupSortMode.Custom);
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "A", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" } },
            new() { Id = "g2", Name = "B", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "b" } },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        Assert.True(vm.ShowMoveControls);
        Assert.Null(vm.Sections[0].Groups[0].MoveUpCommand);
        Assert.NotNull(vm.Sections[0].Groups[0].MoveDownCommand);
        Assert.NotNull(vm.Sections[0].Groups[1].MoveUpCommand);
        Assert.Null(vm.Sections[0].Groups[1].MoveDownCommand);
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

        await vm.Sections[0].Groups[0].RestoreCommand.ExecuteAsync(GroupRestoreMode.AddToTop);

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

        await vm.Sections[0].Groups[0].RestoreCommand.ExecuteAsync(GroupRestoreMode.ClearAndRestore);

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

        await vm.Sections[0].Groups[0].DeleteCommand.ExecuteAsync(null);

        _groupMock.Verify(s => s.DeleteGroupAsync("gid"), Times.Once);
    }

    [Fact]
    public async Task MoveToFolderCommand_CallsGroupService()
    {
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "gid", Name = "G", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" } },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());
        _groupMock.Setup(g => g.MoveGroupToFolderAsync("gid", "f1")).Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        await vm.Sections[0].Groups[0].MoveToFolderCommand.ExecuteAsync("f1");

        _groupMock.Verify(s => s.MoveGroupToFolderAsync("gid", "f1"), Times.Once);
    }

    [Fact]
    public async Task CreateFolderCommand_CallsGroupService()
    {
        _groupMock.Setup(g => g.Groups).Returns(Array.Empty<ClipboardGroup>());
        _groupMock.Setup(g => g.CreateFolderAsync("Untitled folder")).Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        await vm.CreateFolderCommand.ExecuteAsync(null);

        _groupMock.Verify(s => s.CreateFolderAsync("Untitled folder"), Times.Once);
    }

    [Fact]
    public async Task FolderRenameCommand_CallsGroupService()
    {
        var folders = new List<ClipboardGroupFolder>
        {
            new() { Id = "f1", Name = "Old", CreatedUtc = DateTime.UtcNow, IsCollapsed = false },
        };
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "G", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" }, FolderId = "f1" },
        };
        _groupMock.Setup(g => g.Folders).Returns(folders.AsReadOnly());
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());
        _groupMock.Setup(g => g.RenameFolderAsync("f1", "New")).Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Refresh();

        GroupSectionDisplayItem folderSection = Assert.Single(vm.FolderSections);
        Assert.NotNull(folderSection.RenameCommand);
        await folderSection.RenameCommand!.ExecuteAsync("New");

        _groupMock.Verify(s => s.RenameFolderAsync("f1", "New"), Times.Once);
    }

    [Fact]
    public void UngroupedSection_HasNoRenameOrDeleteOrMoveCommands()
    {
        var groups = new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "G", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" } },
        };
        _groupMock.Setup(g => g.Groups).Returns(groups.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        GroupSectionDisplayItem ungrouped = vm.Sections[0];
        Assert.Null(ungrouped.RenameCommand);
        Assert.Null(ungrouped.DeleteCommand);
        Assert.Null(ungrouped.MoveUpCommand);
        Assert.Null(ungrouped.MoveDownCommand);
    }

    [Fact]
    public void FolderMoveCommands_NullOnlyAtBoundaries()
    {
        var folders = new List<ClipboardGroupFolder>
        {
            new() { Id = "f1", Name = "First", CreatedUtc = DateTime.UtcNow },
            new() { Id = "f2", Name = "Second", CreatedUtc = DateTime.UtcNow },
        };
        _groupMock.Setup(g => g.Folders).Returns(folders.AsReadOnly());
        _groupMock.Setup(g => g.Groups).Returns(new List<ClipboardGroup>
        {
            new() { Id = "g1", Name = "G", CreatedUtc = DateTime.UtcNow, EntryIds = new[] { "a" }, FolderId = "f1" },
        }.AsReadOnly());

        var vm = CreateVm();
        vm.Refresh();

        // Sections[0] is Ungrouped (empty here but always present), folders start at [1].
        GroupSectionDisplayItem first = vm.Sections[1];
        GroupSectionDisplayItem second = vm.Sections[2];
        Assert.Null(first.MoveUpCommand);
        Assert.NotNull(first.MoveDownCommand);
        Assert.NotNull(second.MoveUpCommand);
        Assert.Null(second.MoveDownCommand);
    }
}
