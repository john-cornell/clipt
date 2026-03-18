namespace Clipt.Services;

public interface ITrayIconService : IDisposable
{
    event EventHandler? TrayIconClicked;
    event EventHandler? OpenFullRequested;
    event EventHandler? ExitRequested;

    void Initialize();
    void UpdateIcon(bool hasClipboardData);
}
