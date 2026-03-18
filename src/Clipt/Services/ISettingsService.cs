using Clipt.Models;

namespace Clipt.Services;

public interface ISettingsService
{
    StartupMode LoadStartupMode();
    void SaveStartupMode(StartupMode mode);
}
