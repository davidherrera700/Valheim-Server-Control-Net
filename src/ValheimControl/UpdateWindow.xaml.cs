using System.Windows;
using System.Windows.Controls;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class UpdateWindow : Window
{
    private readonly AppConfig _config;
    private readonly SshService _ssh;
    private readonly SteamUpdateService _steam = new();

    public UpdateWindow(AppConfig config)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _config = config;
        _ssh = new SshService(config);
        Loaded += UpdateWindow_Loaded;
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AutoUpdateToggle.IsChecked = _config.AutoUpdateEnabled;

        foreach (ComboBoxItem item in AutoUpdateIntervalBox.Items)
        {
            if ((string)item.Tag == _config.AutoUpdateCheckIntervalHours.ToString())
            {
                AutoUpdateIntervalBox.SelectedItem = item;
                break;
            }
        }
        AutoUpdateIntervalBox.SelectedItem ??= AutoUpdateIntervalBox.Items[2]; // default: 6 hours

        await CheckForUpdatesAsync();
    }

    private void AutoUpdateIntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoUpdateIntervalBox.SelectedItem is not ComboBoxItem { Tag: string hoursText }) return;
        if (!int.TryParse(hoursText, out var hours)) return;

        _config.AutoUpdateCheckIntervalHours = hours;
        ConfigService.Save(_config);
    }

    private void AutoUpdateToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabling = AutoUpdateToggle.IsChecked == true;

        if (enabling)
        {
            var confirm = MessageBox.Show(
                "When enabled, this app checks for Valheim updates periodically while it's open, and " +
                "automatically runs the full backup -> stop -> update -> start process the moment one's " +
                "found - without asking first. Anyone online at the time will be disconnected.\n\n" +
                "This only runs while the app is actually open on this PC (it isn't a background service).\n\n" +
                "Enable auto-update?",
                "Enable Auto-Update?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                AutoUpdateToggle.IsChecked = false;
                return;
            }
        }

        _config.AutoUpdateEnabled = enabling;
        ConfigService.Save(_config);
        AppendOutput(enabling ? "Auto-update enabled." : "Auto-update disabled.");
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        RunUpdateButton.IsEnabled = false;
        StatusText.Text = "Checking...";
        StatusDot.Fill = AppTheme.TextSecondary;
        InstalledBuildText.Text = "—";
        LatestBuildText.Text = "—";
        AppendOutput("Checking installed build...");

        var installedResult = await _ssh.GetInstalledBuildIdAsync();
        string? installedBuild = null;

        if (installedResult.Success &&
            installedResult.Output != "unknown" &&
            installedResult.Output != "(no output)")
        {
            installedBuild = installedResult.Output.Trim();
            InstalledBuildText.Text = installedBuild;
            AppendOutput($"Installed build: {installedBuild}");
        }
        else
        {
            InstalledBuildText.Text = "unknown";
            AppendOutput($"Could not read installed build: {installedResult.Output}");
        }

        AppendOutput("Checking latest build on Steam...");
        var latestBuild = await _steam.GetLatestPublicBuildIdAsync();

        if (latestBuild is not null)
        {
            LatestBuildText.Text = latestBuild;
            AppendOutput($"Latest public build on Steam: {latestBuild}");
        }
        else
        {
            LatestBuildText.Text = "unable to check";
            AppendOutput("Could not reach Steam to check the latest build (network issue, or the API changed).");
        }

        if (installedBuild is null || latestBuild is null)
        {
            StatusText.Text = "Unable to fully check";
            StatusDot.Fill = AppTheme.StatusWarning;
        }
        else if (installedBuild == latestBuild)
        {
            StatusText.Text = "Up to date";
            StatusDot.Fill = AppTheme.StatusOnline;
        }
        else
        {
            StatusText.Text = "Update available";
            StatusDot.Fill = AppTheme.StatusWarning;
        }

        RunUpdateButton.IsEnabled = true;
    }

    private void AppendOutput(string text)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        OutputBox.AppendText($"[{timestamp}] {text}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
    }

    private async void RunUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "This will:\n" +
            "  1. Back up the current world\n" +
            "  2. Stop the Valheim server (disconnecting any players)\n" +
            "  3. Update the server files via SteamCMD\n" +
            "  4. Start the server again\n\n" +
            "This can take a few minutes depending on the size of the update. Continue?",
            "Confirm Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        RunUpdateButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Updating...";
        StatusDot.Fill = AppTheme.StatusWarning;
        AppendOutput("Starting update (backup -> stop -> SteamCMD update -> start)...");
        AppendOutput("This can take a few minutes - please wait.");

        var result = await _ssh.RunServerUpdateAsync();

        AppendOutput(result.Success ? "Update script completed." : $"Update script reported a problem: {result.Output}");
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            AppendOutput("----- Update Output -----");
            AppendOutput(result.Output);
            AppendOutput("----- End Output -----");
        }

        RefreshButton.IsEnabled = true;
        await CheckForUpdatesAsync();
    }
}
