namespace Clipt.Plugins;

public interface ICliptTrayTabViewFactory : ICliptTrayTabPlugin
{
    object CreateView(object viewModel);
}
