namespace Clipt.Plugins;

public interface ICliptHost
{
    string PluginId { get; }

    T? LoadSettings<T>() where T : class, new();

    void SaveSettings<T>(T settings) where T : class;

    Task RemoveHistoryByOwnerProcessAsync(string processName);

    event EventHandler<CliptPluginClipboardEventArgs>? ClipboardProcessed;

    Task BlockOwnerAsync(string? processName, string? windowClass);

    IReadOnlySet<string> GetBlockedProcessNames();

    IReadOnlySet<string> GetBlockedWindowClassPrefixes();

    void NotifyHistoryOwnerBlockUiChanged();

    IReadOnlyList<CliptPluginSavedGroup> GetSavedGroups();

    Task AddEntriesToGroupAsync(string groupId, IReadOnlyList<string> historyEntryIds);

    /// <summary>Id of the most recent history entry, or null when history is empty.</summary>
    string? GetTopHistoryEntryId();

    /// <summary>
    /// <paramref name="entryNameOverrides"/> optionally maps a history entry id (from
    /// <paramref name="historyEntryIds"/>) to the name its archived clip should get, instead of
    /// inheriting the history entry's own name (e.g. raw clipboard text).
    /// </summary>
    Task SaveGroupAsync(
        string name,
        IReadOnlyList<string> historyEntryIds,
        IReadOnlyDictionary<string, string>? entryNameOverrides = null);
}
