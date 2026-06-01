namespace Clipt.Plugins;

public interface ICliptClipboardFilterPlugin : ICliptPlugin
{
    /// <summary>Called before history add. Return Block to skip history for this snapshot.</summary>
    CliptPluginFilterVerdict Evaluate(CliptPluginClipboardSnapshot snapshot);
}
