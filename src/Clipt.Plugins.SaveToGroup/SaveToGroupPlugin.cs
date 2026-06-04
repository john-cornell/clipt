namespace Clipt.Plugins.SaveToGroup;

public sealed class SaveToGroupPlugin : ICliptHistoryActionPlugin
{
    public string Id => "clipt.plugins.save-to-group";

    public string Name => "Save to Group";

    public string Description => "Add selected history items to an existing saved group.";

    public string SelectionButtonLabel => "Add to group";

    public IReadOnlyList<CliptPluginHistorySubAction> GetSubActions(ICliptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.GetSavedGroups()
            .Select(static g => new CliptPluginHistorySubAction { Id = g.Id, Label = g.Name })
            .ToArray();
    }

    public Task ExecuteAsync(
        string subActionId,
        ICliptHost host,
        IReadOnlyList<string> selectedEntryIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.AddEntriesToGroupAsync(subActionId, selectedEntryIds);
    }
}
