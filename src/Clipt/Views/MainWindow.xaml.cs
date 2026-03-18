using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Clipt.ViewModels;

namespace Clipt.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"Clipt {GetAppVersion()} — Clipboard Inspector";
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Initialize();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    internal static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is not null
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : "v0.0.0";
    }
}
