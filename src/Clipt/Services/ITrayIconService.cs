using Clipt.Models;

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
    /// Registers the handler invoked when a Show tabs menu item is toggled.
    /// <paramref name="pluginId"/> is null for the built-in Plugins tab.
    /// </summary>
    void SetShowTabsMenuToggleHandler(Action<string?, bool>? handler);

    /// <summary>Rebuilds Show tabs submenu entries (Plugins + plugin tray tabs).</summary>
    void RebuildShowTabsMenu(IReadOnlyList<TrayTabShowMenuEntry> entries);

    /// <summary>
    /// Updates the tray menu check state when the popup checkbox changes.
    /// </summary>
    void SetClearClipboardWhenClearingHistoryChecked(bool value);
}
