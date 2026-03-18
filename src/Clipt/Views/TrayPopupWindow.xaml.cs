using System.Windows;
using System.Windows.Input;
using Clipt.ViewModels;

namespace Clipt.Views;

public partial class TrayPopupWindow : Window
{
    private DateTime _lastHiddenUtc = DateTime.MinValue;

    public TrayPopupWindow(TrayPopupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TitleText.Text = $"Clipt {MainWindow.GetAppVersion()}";

        Deactivated += OnDeactivated;
    }

    public bool WasRecentlyHidden =>
        (DateTime.UtcNow - _lastHiddenUtc).TotalMilliseconds < 300;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _lastHiddenUtc = DateTime.UtcNow;
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    public void ShowNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 8;
        Top = workArea.Bottom - Height - 8;
        Show();
        Activate();
    }
}
