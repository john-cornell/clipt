namespace Clipt.Services;

public interface ITrayIconService : IDisposable
{
    event EventHandler? TrayIconClicked;
    event EventHandler? OpenFullRequested;
    event EventHandler? ExitRequested;
    event EventHandler? ClearHistoryRequested;

    void Initialize();
    void UpdateIcon(bool hasClipboardData);
}
