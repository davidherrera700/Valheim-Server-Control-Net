using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ValheimControl.Models;
using ValheimControl.Services;

namespace ValheimControl;

public partial class MainWindow : Window
{
    private AppConfig? _config;
    private SshService? _ssh;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!ConfigService.ConfigExists)
        {
            MessageBox.Show(
                $"No configuration file found.\n\nExpected at:\n{ConfigService.ConfigFilePath}\n\n" +
                "Run the installer first, or copy config.example.json to that location and fill in your server details.",
                "Valheim Control - Missing Config",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

        try
        {
            _config = ConfigService.Load();
            _ssh = new SshService(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load configuration:\n{ex.Message}", "Valheim Control - Config Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        AppendLog($"Connected config: {_config.User}@{_config.Server} (port {_config.SshPort})");
        await RefreshStatusAsync();
    }

    // ------------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------------

    private void AppendLog(string text)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{timestamp}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        foreach (var button in new[] { StartButton, RestartButton, StopButton, BackupButton, BackupRestartButton, StatusButton, LogsButton })
        {
            button.IsEnabled = !busy;
        }
    }

    private void UpdateStatusLabel(string rawOutput)
    {
        var match = Regex.Match(rawOutput, @"ActiveState=(\S+)");
        var state = match.Success ? match.Groups[1].Value : "Unknown";

        (string text, Brush color) = state switch
        {
            "active" => ("Status: Online", Brushes.ForestGreen),
            "inactive" => ("Status: Offline", Brushes.Firebrick),
            "failed" => ("Status: Failed", Brushes.Firebrick),
            _ => ($"Status: {state}", Brushes.DarkOrange)
        };

        StatusText.Text = text;
        StatusText.Foreground = color;
    }

    private async Task RefreshStatusAsync()
    {
        if (_ssh is null) return;
        var result = await _ssh.CheckStatusAsync();
        UpdateStatusLabel(result.Output);
        AppendLog(result.Output);
    }

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh is null) return;
        SetBusy(true);
        AppendLog("Starting Valheim...");
        var r = await _ssh.StartServiceAsync();
        AppendLog(r.Success ? "Start command sent." : $"Start failed: {r.Output}");
        await Task.Delay(2000);
        await RefreshStatusAsync();
        SetBusy(false);
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh is null) return;
        SetBusy(true);
        AppendLog("Restarting Valheim...");
        var r = await _ssh.RestartServiceAsync();
        AppendLog(r.Success ? "Restart command sent." : $"Restart failed: {r.Output}");
        await Task.Delay(2000);
        await RefreshStatusAsync();
        SetBusy(false);
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to stop the Valheim server? Players will be disconnected.",
            "Confirm Stop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes || _ssh is null) return;

        SetBusy(true);
        AppendLog("Stopping Valheim...");
        var r = await _ssh.StopServiceAsync();
        AppendLog(r.Success ? "Stop command sent." : $"Stop failed: {r.Output}");
        await Task.Delay(2000);
        await RefreshStatusAsync();
        SetBusy(false);
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh is null) return;
        SetBusy(true);
        AppendLog("Running manual backup...");
        var r = await _ssh.RunBackupAsync();
        AppendLog(r.Success ? "Backup completed." : $"Backup failed: {r.Output}");
        if (!string.IsNullOrWhiteSpace(r.Output)) AppendLog(r.Output);
        if (r.Success) LastBackupText.Text = $"Last Backup: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        SetBusy(false);
    }

    private async void BackupRestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh is null) return;
        SetBusy(true);
        AppendLog("Running backup before restart...");
        var backupResult = await _ssh.RunBackupAsync();
        AppendLog(backupResult.Success ? "Backup completed." : $"Backup failed: {backupResult.Output}");

        if (backupResult.Success)
        {
            LastBackupText.Text = $"Last Backup: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            AppendLog("Restarting Valheim...");
            var restartResult = await _ssh.RestartServiceAsync();
            AppendLog(restartResult.Success ? "Restart command sent." : $"Restart failed: {restartResult.Output}");
            await Task.Delay(2000);
            await RefreshStatusAsync();
        }
        else
        {
            AppendLog("Skipping restart because backup failed.");
        }

        SetBusy(false);
    }

    private async void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        await RefreshStatusAsync();
        SetBusy(false);
    }

    private async void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh is null) return;
        SetBusy(true);
        AppendLog("Fetching recent log lines...");
        var r = await _ssh.GetRecentLogsAsync();
        AppendLog("----- Recent Server Logs -----");
        AppendLog(r.Output);
        AppendLog("----- End of Logs -----");
        SetBusy(false);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
