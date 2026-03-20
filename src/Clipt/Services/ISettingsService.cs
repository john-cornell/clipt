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

    bool LoadPurgeHistoryOnStartup();
    void SavePurgeHistoryOnStartup(bool enabled);

    /// <summary>
    /// When true, clearing history (tray or popup) also clears the system clipboard after the history clear.
    /// </summary>
    bool LoadClearClipboardWhenClearingHistory();
    void SaveClearClipboardWhenClearingHistory(bool enabled);

    IReadOnlySet<ContentType> LoadDisabledHistoryTypes();
    void SaveDisabledHistoryTypes(IReadOnlySet<ContentType> disabled);

    bool LoadRunOnStartup();
    void SaveRunOnStartup(bool enabled);

    AppLogLevel LoadLogLevel();
    void SaveLogLevel(AppLogLevel level);
}
