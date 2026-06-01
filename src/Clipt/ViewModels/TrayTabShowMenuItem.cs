using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

public sealed partial class TrayTabShowMenuItem : ObservableObject
{
    private readonly TrayPopupViewModel _owner;
    private bool _suppressChange;

    public TrayTabShowMenuItem(TrayPopupViewModel owner, string header, string? pluginId, bool isVisible)
    {
        _owner = owner;
        Header = header;
        PluginId = pluginId;
        _isVisible = isVisible;
    }

    public string Header { get; }

    /// <summary>Null for the built-in Plugins tab.</summary>
    public string? PluginId { get; }

    [ObservableProperty]
    private bool _isVisible;

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_suppressChange)
            _owner.OnTrayTabShowMenuItemChanged(PluginId, value);
    }

    internal void SetIsVisibleSilently(bool value)
    {
        _suppressChange = true;
        IsVisible = value;
        _suppressChange = false;
    }
}
