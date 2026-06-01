namespace Clipt.Plugins;

public interface ICliptPluginLifetime : ICliptPlugin
{
    void Initialize(ICliptHost host);

    void Shutdown();
}
