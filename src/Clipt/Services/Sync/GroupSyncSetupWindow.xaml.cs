using System.Windows;

namespace Clipt.Services.Sync;

public partial class GroupSyncSetupWindow : Window
{
    private readonly IGroupSyncService _syncService;

    public GroupSyncSetupWindow(IGroupSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        InitializeComponent();

        if (syncService.IsConfigured)
        {
            DisableButton.Visibility = Visibility.Visible;
            ChangeServerCheckBox.Visibility = Visibility.Visible;
            ChangeServerCheckBox.IsChecked = false;
            ServerUrlPanel.Visibility = Visibility.Collapsed;
            TokenPanel.Visibility = Visibility.Collapsed;
            EnableButton.Content = "Unlock";
        }
        else
        {
            DisableButton.Visibility = Visibility.Collapsed;
            ChangeServerCheckBox.Visibility = Visibility.Collapsed;
        }
    }

    private void ChangeServerCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        bool changingServer = ChangeServerCheckBox.IsChecked == true;
        ServerUrlPanel.Visibility = changingServer ? Visibility.Visible : Visibility.Collapsed;
        TokenPanel.Visibility = changingServer ? Visibility.Visible : Visibility.Collapsed;
        EnableButton.Content = changingServer ? "Update" : "Unlock";
    }

    private async void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;
        EnableButton.IsEnabled = false;

        try
        {
            string passphrase = PassphraseBox.Password;
            if (string.IsNullOrEmpty(passphrase))
            {
                ShowError("The passphrase is required.");
                return;
            }

            bool needsFullSetup = !_syncService.IsConfigured || ChangeServerCheckBox.IsChecked == true;
            if (needsFullSetup)
            {
                if (string.IsNullOrWhiteSpace(ServerUrlBox.Text) || !Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out Uri? serverUrl))
                {
                    ShowError("Enter a valid server URL, e.g. https://clipt.monkeyskin.au/");
                    return;
                }

                string token = TokenBox.Password;
                if (string.IsNullOrEmpty(token))
                {
                    ShowError("The bearer token is required.");
                    return;
                }

                await _syncService.EnableAsync(serverUrl, token, passphrase);
            }
            else
            {
                await _syncService.UnlockAsync(passphrase);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Could not enable sync: {ex.Message}");
        }
        finally
        {
            EnableButton.IsEnabled = true;
        }
    }

    private void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        _syncService.Disable();
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
