using System.Diagnostics;
using System.Windows;

namespace ValheimControl;

public partial class AppUpdateRequiredWindow : Window
{
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
