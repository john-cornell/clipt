using Clipt.Models;
using Clipt.Services;

namespace Clipt.Tests.Services;

public class ClipboardBlockRulesTests
{
    [Fact]
    public void IsSnapshotBlocked_MatchesProcessName_CaseInsensitive()
    {
        var settings = new TestSettings
        {
            BlockedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wispr Flow Helper" },
        };

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "wispr flow helper",
            OwnerProcessId = 1,
            Formats = [],
        };

        Assert.True(ClipboardBlockRules.IsSnapshotBlocked(settings, snapshot));
    }

    [Fact]
    public void IsSnapshotBlocked_MatchesWisprWindowClassPrefix()
    {
        var settings = new TestSettings
        {
            BlockedClassPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WisprClipboard_" },
        };

        var snapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            OwnerProcessName = "(no owner)",
            OwnerProcessId = 0,
            OwnerWindowClass = "WisprClipboard_d6745597",
            Formats = [],
        };

        Assert.True(ClipboardBlockRules.IsSnapshotBlocked(settings, snapshot));
    }

    [Fact]
    public void BlockSnapshotSource_SavesProcessAndWisprClassPrefix()
    {
        var settings = new TestSettings();

        ClipboardBlockRules.BlockSnapshotSource(
            settings,
            "Wispr Flow Helper",
            "WisprClipboard_d6745597");

        Assert.Contains("Wispr Flow Helper", settings.BlockedProcesses);
        Assert.Contains("WisprClipboard_", settings.BlockedClassPrefixes);
    }

    private sealed class TestSettings : ISettingsService
    {
        public HashSet<string> BlockedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BlockedClassPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> LoadBlockedHistoryProcessNames() => BlockedProcesses;
        public void SaveBlockedHistoryProcessNames(IReadOnlySet<string> processNames)
        {
            BlockedProcesses = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlySet<string> LoadBlockedHistoryWindowClassPrefixes() => BlockedClassPrefixes;
        public void SaveBlockedHistoryWindowClassPrefixes(IReadOnlySet<string> classPrefixes)
        {
            BlockedClassPrefixes = new HashSet<string>(classPrefixes, StringComparer.OrdinalIgnoreCase);
        }

        public StartupMode LoadStartupMode() => StartupMode.Collapsed;
        public void SaveStartupMode(StartupMode mode) { }
        public int LoadMaxHistoryEntries() => 10;
        public void SaveMaxHistoryEntries(int count) { }
        public long LoadMaxHistorySizeBytes() => 0;
        public void SaveMaxHistorySizeBytes(long bytes) { }
        public HistorySizeOverflowMode LoadHistorySizeOverflowMode() => HistorySizeOverflowMode.TrimOldest;
        public void SaveHistorySizeOverflowMode(HistorySizeOverflowMode mode) { }
        public long LoadMaxClipboardFormatCaptureBytes() => 64 * 1024;
        public void SaveMaxClipboardFormatCaptureBytes(long bytes) { }
        public ClipboardFormatOversizeMode LoadClipboardFormatOversizeMode() => ClipboardFormatOversizeMode.TruncateToCap;
        public void SaveClipboardFormatOversizeMode(ClipboardFormatOversizeMode mode) { }
        public bool LoadPurgeHistoryOnStartup() => false;
        public void SavePurgeHistoryOnStartup(bool enabled) { }
        public bool LoadClearClipboardWhenClearingHistory() => false;
        public void SaveClearClipboardWhenClearingHistory(bool enabled) { }
        public bool LoadShowPluginsTrayTab() => true;
        public void SaveShowPluginsTrayTab(bool show) { }
        public bool LoadShowDebugTrayTab() => true;
        public void SaveShowDebugTrayTab(bool show) { }
        public IReadOnlySet<ContentType> LoadDisabledHistoryTypes() => new HashSet<ContentType>();
        public void SaveDisabledHistoryTypes(IReadOnlySet<ContentType> disabled) { }
        public bool LoadRunOnStartup() => false;
        public bool SaveRunOnStartup(bool enabled) => true;
        public AppLogLevel LoadLogLevel() => AppLogLevel.Off;
        public void SaveLogLevel(AppLogLevel level) { }
    }
}
