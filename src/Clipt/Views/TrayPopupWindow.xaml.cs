using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Clipt.Models;
using Clipt.Services;
using Clipt.ViewModels;

namespace Clipt.Views;

public partial class TrayPopupWindow : Window
{
    private const string PluginTabTag = "PluginTab";
    private const string PluginsTabHeader = "Plugins";

    private DateTime _lastHiddenUtc = DateTime.MinValue;

    public TrayPopupWindow(TrayPopupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TitleText.Text = $"Clipt {MainWindow.GetAppVersion()}";

        Deactivated += OnDeactivated;

        SubscribeToHistoryTab(viewModel.HistoryTab);
        TrackGroupsTab(viewModel.GroupsTab);
        MovePluginsTabToEnd();
        viewModel.PluginTrayTabsChanged += (_, _) => SyncPluginTrayTabs();
        viewModel.TrayTabShowMenuItemsChanged += (_, _) => RebuildShowTabsSubmenu();
        viewModel.OptionalTrayTabVisibilityChanged += (_, _) =>
        {
            UpdateOptionalTabVisibility();
            RebuildShowTabsSubmenu();
        };
        SyncPluginTrayTabs();
        RebuildShowTabsSubmenu();
        UpdateOptionalTabVisibility();

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrayPopupViewModel.HistoryTab))
                SubscribeToHistoryTab(viewModel.HistoryTab);
            if (e.PropertyName == nameof(TrayPopupViewModel.GroupsTab))
                TrackGroupsTab(viewModel.GroupsTab);
            if (e.PropertyName == nameof(TrayPopupViewModel.ShowPluginsTab))
                UpdateOptionalTabVisibility();
        };
    }

    private void SyncPluginTrayTabs()
    {
        var vm = (TrayPopupViewModel)DataContext;

        for (int i = TrayTabControl.Items.Count - 1; i >= 0; i--)
        {
            if (TrayTabControl.Items[i] is TabItem { Tag: string tag } && tag == PluginTabTag)
                TrayTabControl.Items.RemoveAt(i);
        }

        int insertIndex = FindTabIndexByHeader(PluginsTabHeader);
        if (insertIndex < 0)
            insertIndex = TrayTabControl.Items.Count;

        foreach (PluginTrayTabItem tab in vm.PluginTrayTabs)
        {
            TrayTabControl.Items.Insert(insertIndex, CreatePluginTabItem(tab, vm));
            insertIndex++;
        }

        UpdateOptionalTabVisibility();
        RebuildShowTabsSubmenu();
    }

    private void RebuildShowTabsSubmenu()
    {
        if (ShowTabsRootMenuItem is null)
            return;

        ShowTabsRootMenuItem.Items.Clear();

        if (DataContext is not TrayPopupViewModel vm)
            return;

        foreach (TrayTabShowMenuItem entry in vm.TrayTabShowMenuItems)
        {
            TrayTabShowMenuItem captured = entry;
            var item = new MenuItem
            {
                Header = captured.Header,
                IsCheckable = true,
                IsChecked = captured.IsVisible,
            };
            item.Click += (_, _) => captured.IsVisible = item.IsChecked;
            ShowTabsRootMenuItem.Items.Add(item);
        }
    }

    private void UpdateOptionalTabVisibility()
    {
        if (DataContext is not TrayPopupViewModel vm)
            return;

        int pluginsIndex = FindTabIndexByHeader(PluginsTabHeader);
        if (pluginsIndex >= 0 && TrayTabControl.Items[pluginsIndex] is TabItem pluginsTab)
        {
            pluginsTab.Visibility = vm.IsOptionalTrayTabVisible(null)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        foreach (object item in TrayTabControl.Items)
        {
            if (item is not TabItem tabItem || tabItem.Tag is not string tag || tag != PluginTabTag)
                continue;

            if (tabItem.Header is not string header)
                continue;

            PluginTrayTabItem? pluginTab = vm.PluginTrayTabs.FirstOrDefault(t => t.Header == header);
            if (pluginTab is null)
                continue;

            tabItem.Visibility = GetPluginTabVisibility(pluginTab, vm);
        }

        EnsureSelectedTabIsVisible();
    }

    private void EnsureSelectedTabIsVisible()
    {
        if (TrayTabControl.SelectedItem is TabItem selected
            && selected.Visibility == Visibility.Visible)
        {
            return;
        }

        foreach (object item in TrayTabControl.Items)
        {
            if (item is TabItem { Visibility: Visibility.Visible } visibleTab)
            {
                TrayTabControl.SelectedItem = visibleTab;
                return;
            }
        }
    }

    private void MovePluginsTabToEnd()
    {
        int index = FindTabIndexByHeader(PluginsTabHeader);
        if (index < 0 || index >= TrayTabControl.Items.Count - 1)
            return;

        if (TrayTabControl.Items[index] is TabItem pluginsTab)
        {
            TrayTabControl.Items.RemoveAt(index);
            TrayTabControl.Items.Add(pluginsTab);
        }
    }

    private int FindTabIndexByHeader(string header)
    {
        for (int i = 0; i < TrayTabControl.Items.Count; i++)
        {
            if (TrayTabControl.Items[i] is TabItem { Header: string tabHeader }
                && string.Equals(tabHeader, header, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static TabItem CreatePluginTabItem(PluginTrayTabItem tab, TrayPopupViewModel vm) =>
        new()
        {
            Tag = PluginTabTag,
            Header = tab.Header,
            Content = tab.Content,
            Visibility = GetPluginTabVisibility(tab, vm),
        };

    private static Visibility GetPluginTabVisibility(PluginTrayTabItem tab, TrayPopupViewModel vm) =>
        vm.IsOptionalTrayTabVisible(tab.PluginId)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private HistoryTabViewModel? _subscribedHistoryTab;
    private GroupsTabViewModel? _subscribedGroupsTab;

    private void SubscribeToHistoryTab(HistoryTabViewModel? historyTab)
    {
        if (_subscribedHistoryTab is not null)
            _subscribedHistoryTab.ImagePreviewRequested -= OnImagePreviewRequested;

        _subscribedHistoryTab = historyTab;

        if (historyTab is not null)
            historyTab.ImagePreviewRequested += OnImagePreviewRequested;
    }

    private void TrackGroupsTab(GroupsTabViewModel? groupsTab)
    {
        _subscribedGroupsTab = groupsTab;
    }

    public bool WasRecentlyHidden =>
        (DateTime.UtcNow - _lastHiddenUtc).TotalMilliseconds < 300;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (((TrayPopupViewModel)DataContext).IsPinned)
            return;

        if (_subscribedHistoryTab?.DisplayEntries.Any(i => i.IsEditing) == true)
            return;

        if (_subscribedGroupsTab?.AnyGroupEditing == true)
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

    private void HistoryEntryName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryEntryDisplayItem item })
        {
            item.IsEditing = true;
            e.Handled = true;
        }
    }

    private void HistoryEntryNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
        {
            Dispatcher.InvokeAsync(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void CommitNameEdit(HistoryEntryDisplayItem item)
    {
        item.IsEditing = false;
        item.RenameCommand?.Execute(item.Name);
    }

    private void HistoryEntryNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryEntryDisplayItem item })
            CommitNameEdit(item);
    }

    private void HistoryEntryNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntryDisplayItem item })
            return;

        if (e.Key == Key.Enter)
        {
            CommitNameEdit(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private void GroupNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
        {
            Dispatcher.InvokeAsync(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void GroupNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        var tray = (TrayPopupViewModel)DataContext;
        HistoryTabViewModel? history = tray.HistoryTab;
        if (history is null)
            return;

        if (e.Key == Key.Enter)
        {
            if (history.ConfirmSaveGroupCommand.CanExecute(null))
                history.ConfirmSaveGroupCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            history.CancelNamingCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void GroupEntryName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupDisplayItem item })
        {
            item.IsEditing = true;
            e.Handled = true;
        }
    }

    private void GroupEntryNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
        {
            Dispatcher.InvokeAsync(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void CommitGroupNameEdit(GroupDisplayItem item)
    {
        item.IsEditing = false;
        item.RenameCommand?.Execute(item.Name);
    }

    private void GroupEntryNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupDisplayItem item })
            CommitGroupNameEdit(item);
    }

    private void GroupEntryNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GroupDisplayItem item })
            return;

        if (e.Key == Key.Enter)
        {
            CommitGroupNameEdit(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }
}
