namespace Clipt.Plugins.PasswordGroup;

public sealed class PasswordGroupPlugin : ICliptHistoryActionPlugin
{
    public const string SaveTopSubActionId = "save-top";

    public string Id => "clipt.plugins.password-group";

    public string Name => "Password Group";

    public string Description =>
        "Save the top history item to a new group named password and today's date.";

    public string SelectionButtonLabel => "Save password";

    public bool UsesTopHistoryEntryOnly => true;

    public IReadOnlyList<CliptPluginHistorySubAction> GetSubActions(ICliptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.GetTopHistoryEntryId() is not null
            ?
            [
                new CliptPluginHistorySubAction
                {
                    Id = SaveTopSubActionId,
                    Label = "Save top item",
                },
            ]
            : [];
    }

    public Task ExecuteAsync(
        string subActionId,
        ICliptHost host,
        IReadOnlyList<string> selectedEntryIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(subActionId, SaveTopSubActionId, StringComparison.Ordinal))
            return Task.CompletedTask;

        string? topId = host.GetTopHistoryEntryId();
        if (topId is null)
            return Task.CompletedTask;

        string groupName = $"password {DateTime.Now:yyyy-MM-dd}";
        return host.SaveGroupAsync(groupName, [topId], new Dictionary<string, string> { [topId] = "Password" });
    }
}
