namespace Clipt.Plugins;

/// <summary>
/// A plugin that reads the current clipboard and can write transformed text back.
/// </summary>
public interface ICliptTrayActionPlugin : ICliptPlugin
{
    IReadOnlyList<CliptPluginOption> Options { get; }

    bool CanExecute(CliptPluginContext context);

    Task<CliptPluginResult> ExecuteAsync(CliptPluginContext context, CancellationToken cancellationToken);
}
