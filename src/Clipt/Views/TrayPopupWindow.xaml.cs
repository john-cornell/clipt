using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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

        SubscribeToHistoryTab(viewModel.HistoryTab);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrayPopupViewModel.HistoryTab))
                SubscribeToHistoryTab(viewModel.HistoryTab);
        };
    }

    private HistoryTabViewModel? _subscribedHistoryTab;

    private void SubscribeToHistoryTab(HistoryTabViewModel? historyTab)
    {
        if (_subscribedHistoryTab is not null)
            _subscribedHistoryTab.ImagePreviewRequested -= OnImagePreviewRequested;

        _subscribedHistoryTab = historyTab;

        if (historyTab is not null)
            historyTab.ImagePreviewRequested += OnImagePreviewRequested;
    }

    public bool WasRecentlyHidden =>
        (DateTime.UtcNow - _lastHiddenUtc).TotalMilliseconds < 300;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (((TrayPopupViewModel)DataContext).IsPinned)
            return;

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

    private void HistoryEntry_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntryDisplayItem item })
            return;

        if (item.ContentType != Models.ContentType.Image)
        {
            e.Handled = true;
            return;
        }

        if (item.PreviewThumbnail is not null)
            return;

        var vm = ((TrayPopupViewModel)DataContext).HistoryTab;
        if (vm is null)
            return;

        _ = vm.LoadThumbnailAsync(item.Id);
    }

    private void OnImagePreviewRequested(BitmapSource image)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var previewWindow = new ImagePreviewWindow(image);
            previewWindow.Show();
        });
    }
}
