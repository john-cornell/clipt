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
            EnableButton.Content = "Update";
        }
        else
        {
            DisableButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;
        EnableButton.IsEnabled = false;

        try
        {
            if (string.IsNullOrWhiteSpace(ServerUrlBox.Text) || !Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out Uri? serverUrl))
            {
                ShowError("Enter a valid server URL, e.g. https://clipt.monkeyskin.au/");
                return;
            }

            string token = TokenBox.Password;
            string passphrase = PassphraseBox.Password;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(passphrase))
            {
                ShowError("Both the token and the passphrase are required.");
                return;
            }

            await _syncService.EnableAsync(serverUrl, token, passphrase);
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
