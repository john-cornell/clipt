using Clipt.Models;
using Clipt.Services;
using Clipt.ViewModels;
using Moq;
using Xunit;

namespace Clipt.Tests.ViewModels;

public class GroupsTabViewModelTests
{
    private readonly Mock<IClipboardGroupService> _groupMock;
    private readonly Mock<IClipboardHistoryService> _historyMock;

    public GroupsTabViewModelTests()
    {
        _groupMock = new Mock<IClipboardGroupService>();
        _historyMock = new Mock<IClipboardHistoryService>();
        _historyMock
            .Setup(h => h.RestoreGroupAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<GroupRestoreMode>()))
            .Returns(Task.CompletedTask);
    }

    private GroupsTabViewModel CreateVm()
    {
        return new GroupsTabViewModel(_groupMock.Object, _historyMock.Object);
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
