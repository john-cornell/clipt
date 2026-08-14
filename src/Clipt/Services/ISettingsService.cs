using Clipt.Models;

namespace Clipt.Services;

public interface ISettingsService
{
    StartupMode LoadStartupMode();
    void SaveStartupMode(StartupMode mode);

    int LoadMaxHistoryEntries();
    void SaveMaxHistoryEntries(int count);

    long LoadMaxHistorySizeBytes();
    void SaveMaxHistorySizeBytes(long bytes);

    HistorySizeOverflowMode LoadHistorySizeOverflowMode();
    void SaveHistorySizeOverflowMode(HistorySizeOverflowMode mode);

    long LoadMaxClipboardFormatCaptureBytes();
    void SaveMaxClipboardFormatCaptureBytes(long bytes);

    ClipboardFormatOversizeMode LoadClipboardFormatOversizeMode();
    void SaveClipboardFormatOversizeMode(ClipboardFormatOversizeMode mode);

    bool LoadPurgeHistoryOnStartup();
    void SavePurgeHistoryOnStartup(bool enabled);

    /// <summary>
    /// When true, clearing history (tray or popup) also clears the system clipboard after the history clear.
    /// </summary>
    bool LoadClearClipboardWhenClearingHistory();
    void SaveClearClipboardWhenClearingHistory(bool enabled);

    bool LoadShowPluginsTrayTab();
    void SaveShowPluginsTrayTab(bool show);

    bool LoadShowBlockerTrayTab();
    void SaveShowBlockerTrayTab(bool show);

    bool IsPluginTrayTabVisible(string pluginId);
    void SetPluginTrayTabVisible(string pluginId, bool visible);

    bool LoadPluginEnabled(string pluginId);
    void SavePluginEnabled(string pluginId, bool enabled);

    IReadOnlySet<ContentType> LoadDisabledHistoryTypes();
    void SaveDisabledHistoryTypes(IReadOnlySet<ContentType> disabled);

    bool LoadRunOnStartup();
    /// <returns>False if startup registration could not be written (e.g. no usable .exe path).</returns>
    bool SaveRunOnStartup(bool enabled);

    AppLogLevel LoadLogLevel();
    void SaveLogLevel(AppLogLevel level);

    GroupSortMode LoadGroupSortMode();
    void SaveGroupSortMode(GroupSortMode mode);

    /// <summary>Collapse state of the Groups tab's Ungrouped section (not tied to any one folder).</summary>
    bool LoadGroupsUngroupedCollapsed();
    void SaveGroupsUngroupedCollapsed(bool collapsed);

    (double Width, double Height) LoadTrayPopupSize();
    void SaveTrayPopupSize(double width, double height);
}
