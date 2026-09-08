using System.Windows;
using System.Windows.Controls;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class AccountWindow : Window
{
    private static readonly Dictionary<string, string> PermissionLabels = new()
    {
        ["server.start"] = "Start Server",
        ["server.restart"] = "Restart Server",
        ["server.stop"] = "Stop Server",
        ["backup.run"] = "Run Manual Backup",
        ["backup.restart"] = "Backup + Restart",
        ["backup.reboot"] = "Backup + Reboot Host",
        ["worlds.swap"] = "Swap / Create World",
        ["worlds.delete"] = "Delete World",
        ["config.write"] = "Edit Config Files",
        ["update.run"] = "Run Server Update",
        ["backupsettings.write"] = "Change Backup Settings",
    };

    private readonly AppConfig _config;
    private readonly ApiClient _api;

    /// <summary>Set to true if the user logs out from this window, so the caller can refresh its own state.</summary>
    public bool DidLogOut { get; private set; }

    public AccountWindow(AppConfig config, ApiClient api)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _config = config;
        _api = api;
        Loaded += AccountWindow_Loaded;
    }

    private void AccountWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UsernameText.Text = _api.Username ?? "—";
        RoleText.Text = _api.IsOwner ? "Owner - full access" : $"Role: {_api.RoleName ?? "none assigned"}";
        ManageUsersButton.Visibility = _api.IsOwner ? Visibility.Visible : Visibility.Collapsed;

        PermissionsPanel.Children.Clear();

        if (_api.IsOwner)
        {
            PermissionsPanel.Children.Add(new TextBlock
            {
                Text = "• Everything - Owner has unrestricted access",
                Margin = new Thickness(0, 0, 0, 4)
            });
        }
        else if (_api.Permissions.Length == 0)
        {
            PermissionsPanel.Children.Add(new TextBlock
            {
                Text = "No permissions assigned yet - ask an Owner to assign you a role.",
                Style = (Style)FindResource("SubtitleTextStyle"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var permission in _api.Permissions)
            {
                var label = PermissionLabels.TryGetValue(permission, out var friendly) ? friendly : permission;
                PermissionsPanel.Children.Add(new TextBlock
                {
                    Text = $"• {label}",
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }
        }
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentPasswordBox.Password;
        var newPassword = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(newPassword))
        {
            PasswordStatusText.Text = "All fields are required.";
            return;
        }

        if (newPassword != confirm)
        {
            PasswordStatusText.Text = "New password and confirmation don't match.";
            return;
        }

        if (newPassword.Length < 6)
        {
            PasswordStatusText.Text = "New password must be at least 6 characters.";
            return;
        }

        ChangePasswordButton.IsEnabled = false;
        PasswordStatusText.Text = "Updating...";

        var (success, error) = await _api.ChangePasswordAsync(current, newPassword);

        if (!success)
        {
            PasswordStatusText.Text = error;
            ChangePasswordButton.IsEnabled = true;
            return;
        }

        CurrentPasswordBox.Password = "";
        NewPasswordBox.Password = "";
        ConfirmPasswordBox.Password = "";
        PasswordStatusText.Text = "Password updated.";
        ChangePasswordButton.IsEnabled = true;
    }

    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new UsersRolesWindow(_api) { Owner = this };
        window.ShowDialog();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Log out of this account? You'll need your password again to sign back in.",
            "Confirm Log Out",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _api.LogOut();
        _config.ApiSessionToken = null;
        ConfigService.Save(_config);

        DidLogOut = true;
        Close();
    }
}
