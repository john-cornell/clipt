namespace Clipt.Plugins;

public interface ICliptTrayTabPlugin : ICliptPlugin
{
    string TabHeader { get; }

    int TabOrder { get; }

    object CreateViewModel(ICliptHost host);
}
