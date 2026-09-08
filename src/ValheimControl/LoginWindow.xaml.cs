using System.Windows;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class LoginWindow : Window
{
    private readonly AppConfig _config;
    private readonly bool _blockingMode;
    public ApiClient? Api { get; private set; }

    /// <summary>
    /// True once a real, successful sign-in has happened (fresh or
    /// restored). Callers check THIS, not ShowDialog()'s return value -
    /// setting Window.DialogResult from inside an async continuation has
    /// known WPF timing fragility that can crash the whole app with an
    /// unhandled exception (async void methods can't be caught normally),
    /// so this window just closes itself and exposes success explicitly.
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>
    /// blockingMode=true turns this into a real startup gate rather than a
    /// standalone testing tool: a successful sign-in (fresh or restored)
    /// closes the window immediately instead of showing the signed-in
    /// banner, and closing it any other way without having signed in
    /// exits the whole app - same "no bypass" pattern as
    /// AppUpdateRequiredWindow.
    /// </summary>
    public LoginWindow(AppConfig config, bool blockingMode = false)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _config = config;
        _blockingMode = blockingMode;
        Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ServerAddressBox.Text = _config.ApiBaseUrl ?? "";

            if (string.IsNullOrWhiteSpace(_config.ApiBaseUrl) || string.IsNullOrWhiteSpace(_config.ApiSessionToken))
            {
                return; // nothing to restore - just show the normal login form
            }

            AppendLog("Found a remembered session - checking if it's still valid...");

            Api = new ApiClient(_config.ApiBaseUrl);
            Api.RestoreToken(_config.ApiSessionToken);
            var (valid, error) = await Api.ValidateSessionAsync();

            if (!valid)
            {
                AppendLog($"Remembered session is no longer valid: {error}");
                _config.ApiSessionToken = null;
                ConfigService.Save(_config);
                Api = null;
                return;
            }

            AppendLog($"Still signed in as '{Api.Username}' (Role: {Api.RoleName ?? "none"}, Owner: {Api.IsOwner}).");
            Success = true;

            if (_blockingMode)
            {
                Close();
                return;
            }

            ShowSignedInState();
        }
        catch (Exception ex)
        {
            // async void handlers that throw take the whole app down with
            // them in WPF - never let that happen. Fall back to a normal
            // login form and let the person try manually instead.
            AppendLog($"Couldn't restore the saved session: {ex.Message}");
            Api = null;
        }
    }

    private void ShowSignedInState()
    {
        if (Api is null) return;

        LoginFormPanel.Visibility = Visibility.Collapsed;
        SignedInBanner.Visibility = Visibility.Visible;
        SignedInAsText.Text = $"Signed in as {Api.Username}";
        SignedInDetailText.Text = Api.IsOwner
            ? "Owner - full access"
            : $"Role: {Api.RoleName ?? "none assigned"}";

        TestStatusButton.IsEnabled = true;
        SignOutButton.IsEnabled = true;
    }

    private void ShowLoginForm()
    {
        LoginFormPanel.Visibility = Visibility.Visible;
        SignedInBanner.Visibility = Visibility.Collapsed;

        TestStatusButton.IsEnabled = false;
        SignOutButton.IsEnabled = false;
    }

    private void AppendLog(string text)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        OutputBox.AppendText($"[{timestamp}] {text}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var address = ServerAddressBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show("Server address, username, and password are all required.", "Missing Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoginButton.IsEnabled = false;

        try
        {
            AppendLog($"Signing in to {address} as {username}...");

            Api = new ApiClient(address);
            var (success, error) = await Api.LoginAsync(username, password);

            if (!success)
            {
                AppendLog($"Sign in failed: {error}");
                Api = null;
                return;
            }

            // Remember-me: only the token is ever saved, never the password.
            _config.ApiBaseUrl = address;
            _config.ApiSessionToken = Api.Token;
            ConfigService.Save(_config);

            AppendLog($"Signed in as '{Api.Username}' (Owner: {Api.IsOwner}).");
            Success = true;

            if (_blockingMode)
            {
                Close();
                return;
            }

            ShowSignedInState();
        }
        catch (Exception ex)
        {
            AppendLog($"Sign in failed unexpectedly: {ex.Message}");
            Api = null;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void TestStatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (Api is null || !Api.IsLoggedIn)
        {
            AppendLog("Not signed in yet.");
            return;
        }

        AppendLog("Calling GET /api/status ...");
        var result = await Api.GetAsync("/api/status");
        AppendLog(result.Success ? $"Success: {result.Output}" : $"Failed ({result.StatusCode}): {result.Output}");
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        Api?.LogOut();
        Api = null;

        _config.ApiSessionToken = null;
        ConfigService.Save(_config);

        UsernameBox.Text = "";
        PasswordBox.Password = "";
        ShowLoginForm();
        AppendLog("Signed out. The remembered session was cleared.");
    }

    private void AccountPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (Api is null || !Api.IsLoggedIn) return;

        var accountWindow = new AccountWindow(_config, Api) { Owner = this };
        accountWindow.ShowDialog();

        if (accountWindow.DidLogOut)
        {
            Api = null;
            UsernameBox.Text = "";
            PasswordBox.Password = "";
            ShowLoginForm();
            AppendLog("Signed out from the Account page.");
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_blockingMode && !Success)
        {
            Application.Current.Shutdown();
        }
    }
}
