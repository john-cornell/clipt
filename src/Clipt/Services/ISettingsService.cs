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
}
