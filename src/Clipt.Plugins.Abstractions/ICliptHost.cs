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

    Task SaveGroupAsync(string name, IReadOnlyList<string> historyEntryIds);
}
