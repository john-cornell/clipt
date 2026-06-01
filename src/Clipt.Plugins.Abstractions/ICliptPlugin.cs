namespace Clipt.Plugins;

/// <summary>
/// Marker for a Clipt plugin discovered at startup.
/// </summary>
public interface ICliptPlugin
{
    string Id { get; }

    string Name { get; }

    string Description { get; }
}
