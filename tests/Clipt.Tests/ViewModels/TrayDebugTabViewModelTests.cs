using System.Collections.Immutable;
using Clipt.Models;
using Clipt.Services;
using Clipt.ViewModels;
using Moq;

namespace Clipt.Tests.ViewModels;

public class TrayDebugTabViewModelTests
{
    private static (TrayDebugTabViewModel Vm, Mock<ISettingsService> Settings, Mock<IClipboardHistoryService> History) CreateVm()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.LoadBlockedHistoryProcessNames())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        settings.Setup(s => s.LoadBlockedHistoryWindowClassPrefixes())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var history = new Mock<IClipboardHistoryService>();
        history.Setup(h => h.RemoveByOwnerProcessAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var vm = new TrayDebugTabViewModel(settings.Object, history.Object);
        return (vm, settings, history);
    }

    [Fact]
    public void RecordEvent_AddsToRecentEvents_WithOwnerMetadata()
    {
        var (vm, _, _) = CreateVm();
        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 42,
            OwnerProcessName = "Wispr",
            OwnerProcessId = 999,
            OwnerWindowHandle = 0x1234,
            OwnerWindowTitle = "Wispr Flow",
            OwnerWindowClass = "Chrome_WidgetWin_1",
            Formats = ImmutableArray.Create(
                new ClipboardFormatInfo
                {
                    FormatId = 1,
                    FormatName = "CF_UNICODETEXT",
                    IsStandard = true,
                    DataSize = 0,
                    Memory = new MemoryInfo("0x0", "0x0", 0, []),
                    RawData = [],
                }),
        };

        vm.RecordEvent(snapshot, HistoryAddResult.Added);

        Assert.Single(vm.RecentEvents);
        Assert.NotNull(vm.RecentEvents[0].BlockOwnerCommand);
        Assert.Equal("Wispr", vm.RecentEvents[0].BlockableProcessName);
        Assert.True(vm.RecentEvents[0].CanBlockOwner);
    }

    [Fact]
    public async Task BlockOwnerCommand_FromEventRow_BlocksAndPurgesHistory()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.LoadBlockedHistoryProcessNames())
            .Returns(() => blocked);
        settings.Setup(s => s.SaveBlockedHistoryProcessNames(It.IsAny<IReadOnlySet<string>>()))
            .Callback<IReadOnlySet<string>>(names =>
            {
                blocked.Clear();
                foreach (string name in names)
                    blocked.Add(name);
            });
        settings.Setup(s => s.LoadBlockedHistoryWindowClassPrefixes())
            .Returns(() => blockedClasses);
        settings.Setup(s => s.SaveBlockedHistoryWindowClassPrefixes(It.IsAny<IReadOnlySet<string>>()))
            .Callback<IReadOnlySet<string>>(names =>
            {
                blockedClasses.Clear();
                foreach (string name in names)
                    blockedClasses.Add(name);
            });

        var history = new Mock<IClipboardHistoryService>();
        history.Setup(h => h.RemoveByOwnerProcessAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var vm = new TrayDebugTabViewModel(settings.Object, history.Object);
        vm.RecordEvent(new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "Wispr Flow Helper",
            OwnerProcessId = 24444,
            OwnerWindowClass = "WisprClipboard_d6745597",
            Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
        }, HistoryAddResult.SkippedEmptyFormats);

        await vm.RecentEvents[0].BlockOwnerCommand!.ExecuteAsync(null);

        history.Verify(h => h.RemoveByOwnerProcessAsync("Wispr Flow Helper"), Times.Once);
        settings.Verify(s => s.SaveBlockedHistoryWindowClassPrefixes(
            It.Is<IReadOnlySet<string>>(set => set.Contains("WisprClipboard_"))), Times.Once);
        Assert.False(vm.RecentEvents[0].CanBlockOwner);
        Assert.Single(vm.BlockedProcessItems);
        Assert.Equal("Wispr Flow Helper", vm.BlockedProcessItems[0].DisplayName);
        Assert.Single(vm.BlockedWindowClassItems);
        Assert.Equal("WisprClipboard_", vm.BlockedWindowClassItems[0].DisplayName);
    }

    [Fact]
    public void ClearRecentEvents_RemovesAllEvents()
    {
        var (vm, _, _) = CreateVm();
        vm.RecordEvent(new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray<ClipboardFormatInfo>.Empty,
        }, HistoryAddResult.SkippedEmptyFormats);

        vm.ClearRecentEventsCommand.Execute(null);

        Assert.Empty(vm.RecentEvents);
    }
}
