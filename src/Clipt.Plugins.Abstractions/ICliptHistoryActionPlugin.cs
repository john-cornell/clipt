namespace Clipt.Plugins;

/// <summary>
/// A plugin that contributes action buttons to the History tab multi-select chrome.
/// Each plugin shows as a dropdown button; sub-actions are its menu items.
/// </summary>
public interface ICliptHistoryActionPlugin : ICliptPlugin
{
    /// <summary>Label shown on the dropdown button in the multi-select toolbar.</summary>
    string SelectionButtonLabel { get; }

    /// <summary>
    /// Returns sub-actions available right now. Empty list hides the button.
    /// Called each time the user enters selection mode or saved groups change.
    /// </summary>
    IReadOnlyList<CliptPluginHistorySubAction> GetSubActions(ICliptHost host);

    /// <summary>Execute the chosen sub-action on the selected history entry IDs.</summary>
    Task ExecuteAsync(
        string subActionId,
        ICliptHost host,
        IReadOnlyList<string> selectedEntryIds,
        CancellationToken cancellationToken);
}

public sealed class CliptPluginHistorySubAction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}
