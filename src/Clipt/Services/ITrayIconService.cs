namespace Clipt.Services;

public interface ITrayIconService : IDisposable
{
    event EventHandler? TrayIconClicked;
    event EventHandler? OpenFullRequested;
    event EventHandler? ExitRequested;
    event EventHandler? ClearHistoryRequested;

    void Initialize();
    void UpdateIcon(bool hasClipboardData);

    /// <summary>
    /// Keeps the popup "clear clipboard" checkbox in sync when the tray menu toggles the same setting.
    /// </summary>
    void SetClearClipboardPreferenceSync(Action<bool>? sync);

    /// <summary>
    /// Keeps tray popup tab visibility in sync when the tray menu toggles Show &gt; Plugins / Debug.
    /// </summary>
    void SetTrayTabVisibilitySync(Action<bool, bool>? sync);

    /// <summary>
    /// Updates the tray menu check state when the popup checkbox changes.
    /// </summary>
    void SetClearClipboardWhenClearingHistoryChecked(bool value);
}
