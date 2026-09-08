using System.Diagnostics;
using System.Windows;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace ValheimControl;

public partial class AppUpdateRequiredWindow : Window
{
    // Same repo the dashboard's own update chip checks - duplicated here
    // as a constant since this window is shown before MainWindow exists.
    private const string AppUpdateRepoUrl = "https://github.com/davidherrera700/Valheim-Server-Control-Net";

    private readonly string? _downloadUrl;
    private bool _allowClose;

    public AppUpdateRequiredWindow(string currentVersion, string requiredVersion, string? releaseNotes, string? downloadUrl)
    {
        InitializeComponent();
        Theme.DarkTitleBarHelper.Apply(this);

        _downloadUrl = downloadUrl;

        CurrentVersionText.Text = currentVersion;
        RequiredVersionText.Text = requiredVersion;

        if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            ReleaseNotesText.Text = releaseNotes;
            ReleaseNotesText.Visibility = Visibility.Visible;
        }

        ViewReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(downloadUrl);
    }

    /// <summary>
    /// Real self-update, reusing the same Velopack mechanism proven on the
    /// dashboard. VIEW RELEASE stays as a manual fallback for the cases
    /// this can't handle (a dev build with no genuine Velopack install, or
    /// any other failure) rather than being the only option.
    /// </summary>
    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateNowButton.IsEnabled = false;
        ExitButton.IsEnabled = false;
        ViewReleaseButton.IsEnabled = false;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "Checking for the update...";

        try
        {
            var mgr = new UpdateManager(new GithubSource(AppUpdateRepoUrl, null, false));
            var newVersion = await mgr.CheckForUpdatesAsync();

            if (newVersion is null)
            {
                UpdateStatusText.Text = "No update found on GitHub yet - it may still be propagating. " +
                                         "Try again in a moment, or use VIEW RELEASE to download manually.";
                UpdateNowButton.IsEnabled = true;
                ExitButton.IsEnabled = true;
                ViewReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(_downloadUrl);
                return;
            }

            UpdateStatusText.Text = $"Downloading v{newVersion.TargetFullRelease.Version}...";
            await mgr.DownloadUpdatesAsync(newVersion);

            UpdateStatusText.Text = "Installing and restarting...";
            _allowClose = true;
            mgr.ApplyUpdatesAndRestart(newVersion);
        }
        catch (NotInstalledException)
        {
            UpdateStatusText.Text = "Self-update isn't available for this build (not installed via the real " +
                                     "installer) - use VIEW RELEASE to download manually instead.";
            ExitButton.IsEnabled = true;
            ViewReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(_downloadUrl);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Update failed: {ex.Message}. Use VIEW RELEASE to download manually instead.";
            UpdateNowButton.IsEnabled = true;
            ExitButton.IsEnabled = true;
            ViewReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(_downloadUrl);
        }
    }

    private void ViewReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show($"Couldn't open the link automatically. Copy it manually:\n\n{_downloadUrl}",
                "Open Release Page", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    /// <summary>
    /// This window is deliberately the only way in or out at startup when an
    /// update is required - closing it any other way (Alt+F4, the X button)
    /// should also exit the app rather than silently letting it slip through
    /// to the main window.
    /// </summary>
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        Application.Current.Shutdown();
    }
}
