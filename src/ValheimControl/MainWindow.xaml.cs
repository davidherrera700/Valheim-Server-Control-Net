using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;
using Shapes = System.Windows.Shapes;

namespace ValheimControl;

public partial class MainWindow : Window
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AutoUpdateCheckPollInterval = TimeSpan.FromMinutes(15);

    private AppConfig? _config;
    private SshService? _ssh;
    private readonly SteamUpdateService _steam = new();
    private DispatcherTimer? _statusTimer;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _autoUpdateTimer;
    private DateTime? _lastAutoUpdateCheckUtc;
    private int _secondsUntilNextPoll;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
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

        HostLabel.Text = _config.Server;

        AppendLog($"Connected config: {_config.User}@{_config.Server} (port {_config.SshPort})");
        await RefreshStatusAsync();
        await RefreshLastBackupAsync();
        await RefreshWorldAndUptimeAsync();
        await RefreshPlayerActivityAsync();

        _statusTimer = new DispatcherTimer { Interval = StatusPollInterval };
        _statusTimer.Tick += StatusTimer_Tick;
        _statusTimer.Start();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();

        _autoUpdateTimer = new DispatcherTimer { Interval = AutoUpdateCheckPollInterval };
        _autoUpdateTimer.Tick += AutoUpdateTimer_Tick;
        _autoUpdateTimer.Start();
        _lastAutoUpdateCheckUtc = DateTime.UtcNow;

        ResetPollCountdown();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _statusTimer?.Stop();
        _countdownTimer?.Stop();
        _autoUpdateTimer?.Stop();
    }

    /// <summary>
    /// Fires every AutoUpdateCheckInterval regardless of the toggle state -
    /// the toggle just decides whether this tick actually does anything.
    /// Both windows share the same in-memory AppConfig instance, so a change
    /// made in UpdateWindow is visible here immediately without reloading.
    /// </summary>
    /// <summary>
    /// Polls every AutoUpdateCheckPollInterval (a short, fixed interval) but
    /// only actually checks for updates once _config.AutoUpdateCheckIntervalHours
    /// has genuinely elapsed - this means a change to the interval made in
    /// UpdateWindow (which shares this same AppConfig instance) takes effect
    /// on the very next poll, with no need to touch the timer itself.
    /// </summary>
    private async void AutoUpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_config?.AutoUpdateEnabled != true || _ssh is null || _isBusy) return;

        var intervalHours = Math.Max(1, _config.AutoUpdateCheckIntervalHours);
        if (_lastAutoUpdateCheckUtc is not null &&
            DateTime.UtcNow - _lastAutoUpdateCheckUtc.Value < TimeSpan.FromHours(intervalHours))
        {
            return; // not due yet
        }

        _lastAutoUpdateCheckUtc = DateTime.UtcNow;

        _isBusy = true;
        try
        {
            AppendLog("(auto-update) Checking for updates...");

            var installedResult = await _ssh.GetInstalledBuildIdAsync();
            var latestBuild = await _steam.GetLatestPublicBuildIdAsync();

            if (!installedResult.Success || installedResult.Output is "unknown" or "(no output)" || latestBuild is null)
            {
                AppendLog("(auto-update) Could not check for updates right now - skipping this cycle.");
                return;
            }

            var installedBuild = installedResult.Output.Trim();
            if (installedBuild == latestBuild)
            {
                AppendLog("(auto-update) Already up to date.");
                return;
            }

            AppendLog($"(auto-update) Update available ({installedBuild} -> {latestBuild}). Running it now...");
            var updateResult = await _ssh.RunServerUpdateAsync();
            AppendLog(updateResult.Success
                ? "(auto-update) Update completed."
                : $"(auto-update) Update reported a problem: {updateResult.Output}");

            await RefreshStatusAsync();
            await RefreshWorldAndUptimeAsync();
            await RefreshLastBackupAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        // Skip this tick if a button action (or an overlapping tick) is already
        // mid-flight - avoids stacking up concurrent SSH connections.
        if (_isBusy || _ssh is null) return;

        _isBusy = true;
        try
        {
            var result = await _ssh.CheckStatusAsync();
            var previousText = StateText.Text;
            UpdateStatusLabel(result.Output);

            // Only write to the log when the state actually changes, so the
            // panel doesn't fill up with a line every 30 seconds.
            if (StateText.Text != previousText)
            {
                AppendLog($"(auto-refresh) Status: {StateText.Text}");
            }

            await RefreshLastBackupAsync();
            await RefreshWorldAndUptimeAsync();
            await RefreshPlayerActivityAsync();
        }
        finally
        {
            _isBusy = false;
            ResetPollCountdown();
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        if (_secondsUntilNextPoll > 0) _secondsUntilNextPoll--;
        RefreshCountdown.Text = FormatCountdown(_secondsUntilNextPoll);
    }

    private void ResetPollCountdown()
    {
        _secondsUntilNextPoll = (int)StatusPollInterval.TotalSeconds;
        RefreshCountdown.Text = FormatCountdown(_secondsUntilNextPoll);
    }

    private static string FormatCountdown(int totalSeconds) =>
        TimeSpan.FromSeconds(Math.Max(0, totalSeconds)).ToString(@"mm\:ss");

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
        _isBusy = busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        ActionProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { StartButton, RestartButton, StopButton, BackupButton, BackupRestartButton, StatusButton, LogsButton, BackupRebootButton })
        {
            button.IsEnabled = !busy;
        }
    }

    private void UpdateStatusLabel(string rawOutput)
    {
        var match = Regex.Match(rawOutput, @"ActiveState=(\S+)");
        var state = match.Success ? match.Groups[1].Value : "unknown";

        (string text, Brush color) = state switch
        {
            "active" => ("ONLINE", AppTheme.StatusOnline),
            "inactive" => ("OFFLINE", AppTheme.StatusOffline),
            "failed" => ("FAILED", AppTheme.StatusOffline),
            _ => (state.ToUpperInvariant(), AppTheme.StatusWarning)
        };

        StateText.Text = text;
        StateText.Foreground = color;
        StateDot.Fill = color;
    }

    private async Task RefreshStatusAsync()
    {
        if (_ssh is null) return;
        var result = await _ssh.CheckStatusAsync();
        UpdateStatusLabel(result.Output);
        AppendLog(result.Output);
    }

    private async Task RefreshLastBackupAsync()
    {
        if (_ssh is null) return;

        var result = await _ssh.GetLastBackupInfoAsync();
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output) || result.Output == "(no output)")
        {
            LastBackupValue.Text = "unknown";
            return;
        }

        var firstLine = result.Output.Split('\n')[0].Trim();
        var parts = firstLine.Split(' ', 2);

        if (parts.Length < 1 || !double.TryParse(
                parts[0], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var epochSeconds))
        {
            LastBackupValue.Text = "unknown";
            return;
        }

        var backupTimeLocal = DateTimeOffset.FromUnixTimeSeconds((long)epochSeconds).LocalDateTime;
        var relative = FormatRelativeTime(DateTime.Now - backupTimeLocal);

        LastBackupValue.Text = relative;
        LastBackupValue.ToolTip = backupTimeLocal.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatRelativeTime(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    // ------------------------------------------------------------------
    // Phase 2: World name, uptime, recent player joins
    // ------------------------------------------------------------------

    private async Task RefreshWorldAndUptimeAsync()
    {
        if (_ssh is null) return;

        var uptimeResult = await _ssh.GetUptimeEpochAsync();
        if (uptimeResult.Success &&
            long.TryParse(uptimeResult.Output.Trim(), out var startEpoch) &&
            startEpoch > 0)
        {
            var startUtc = DateTimeOffset.FromUnixTimeSeconds(startEpoch).UtcDateTime;
            var span = DateTime.UtcNow - startUtc;
            UptimeValue.Text = FormatUptime(span);
        }
        else
        {
            UptimeValue.Text = "—";
        }

        var worldResult = await _ssh.GetWorldArgvAsync();
        WorldValue.Text = worldResult.Success
            ? ParseWorldName(worldResult.Output) ?? "—"
            : "—";
    }

    private static string FormatUptime(TimeSpan span)
    {
        if (span.TotalSeconds < 0) return "—";
        return span.Days > 0
            ? $"{span.Days}d {span.Hours:D2}:{span.Minutes:D2}"
            : $"{span.Hours:D2}:{span.Minutes:D2}";
    }

    private static string? ParseWorldName(string argvOutput)
    {
        if (string.IsNullOrWhiteSpace(argvOutput) || argvOutput == "(no output)") return null;

        var lines = argvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].Trim() == "-world")
            {
                return lines[i + 1].Trim().Trim('"');
            }
        }
        return null;
    }

    private async Task RefreshPlayerActivityAsync()
    {
        if (_ssh is null) return;

        var result = await _ssh.GetRecentPlayerJoinsAsync();
        var entries = ParsePlayerJoins(result.Output);

        PlayerRosterPanel.Children.Clear();

        if (entries.Count == 0)
        {
            PlayerCount.Text = "none yet";
            PlayerRosterPanel.Children.Add(new TextBlock
            {
                Text = "No player joins seen since the last restart.",
                Style = (Style)FindResource("SubtitleTextStyle")
            });
            return;
        }

        PlayerCount.Text = $"{entries.Count} seen";

        // Most recent first, capped so the panel doesn't grow unbounded over a long session.
        foreach (var (name, joinedAtLocal) in entries.AsEnumerable().Reverse().Take(10))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var marker = new Shapes.Rectangle { Style = (Style)FindResource("DiamondMarkerStyle") };
            Grid.SetColumn(marker, 0);

            var nameBlock = new TextBlock { Text = name, Margin = new Thickness(12, 0, 8, 0) };
            Grid.SetColumn(nameBlock, 1);

            var timeBlock = new TextBlock
            {
                Text = FormatRelativeTime(DateTime.Now - joinedAtLocal),
                Style = (Style)FindResource("MonoTextStyle"),
                FontSize = 11,
                Foreground = AppTheme.TextSecondary
            };
            Grid.SetColumn(timeBlock, 2);

            row.Children.Add(marker);
            row.Children.Add(nameBlock);
            row.Children.Add(timeBlock);

            PlayerRosterPanel.Children.Add(row);
        }
    }

    private static List<(string Name, DateTime JoinedAtLocal)> ParsePlayerJoins(string output)
    {
        var results = new List<(string, DateTime)>();
        if (string.IsNullOrWhiteSpace(output) || output == "(no output)") return results;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"Got character ZDOID from (.+?)\s*:");
            if (!match.Success) continue;

            var name = match.Groups[1].Value.Trim();
            var tsToken = line.Split(' ', 2)[0];

            var joinedAt = DateTimeOffset.TryParse(
                tsToken, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var ts)
                ? ts.LocalDateTime
                : DateTime.Now;

            results.Add((name, joinedAt));
        }

        return results;
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
        ResetPollCountdown();
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
        ResetPollCountdown();
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
        ResetPollCountdown();
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh is null) return;
        SetBusy(true);
        AppendLog("Running manual backup...");
        var r = await _ssh.RunBackupAsync();
        AppendLog(r.Success ? "Backup completed." : $"Backup failed: {r.Output}");
        if (!string.IsNullOrWhiteSpace(r.Output)) AppendLog(r.Output);
        if (r.Success) await RefreshLastBackupAsync();
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
            await RefreshLastBackupAsync();
            AppendLog("Restarting Valheim...");
            var restartResult = await _ssh.RestartServiceAsync();
            AppendLog(restartResult.Success ? "Restart command sent." : $"Restart failed: {restartResult.Output}");
            await Task.Delay(2000);
            await RefreshStatusAsync();
            ResetPollCountdown();
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
        ResetPollCountdown();
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

    private async void BackupRebootButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "This will:\n" +
            "  1. Stop the Valheim server (disconnecting any players)\n" +
            "  2. Run a manual backup\n" +
            "  3. Reboot the entire Ubuntu host\n\n" +
            "The server will be offline for a few minutes while the host restarts, " +
            "then it will come back online automatically. Continue?",
            "Confirm Backup + Reboot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes || _ssh is null) return;

        SetBusy(true);

        AppendLog("Stopping Valheim before reboot...");
        var stopResult = await _ssh.StopServiceAsync();
        AppendLog(stopResult.Success ? "Valheim stopped." : $"Stop failed: {stopResult.Output}");

        if (!stopResult.Success)
        {
            AppendLog("Aborting - refusing to reboot while the stop command failed.");
            SetBusy(false);
            return;
        }

        AppendLog("Running backup...");
        var backupResult = await _ssh.RunBackupAsync();
        AppendLog(backupResult.Success ? "Backup completed." : $"Backup failed: {backupResult.Output}");

        if (!backupResult.Success)
        {
            AppendLog("Aborting reboot - backup did not complete successfully. Valheim remains stopped; " +
                      "start it manually or investigate the backup failure first.");
            SetBusy(false);
            return;
        }

        LastBackupValue.Text = "verifying...";
        await RefreshLastBackupAsync();

        AppendLog("Rebooting Ubuntu host...");
        await _ssh.RebootHostAsync();
        AppendLog("Reboot command sent. The SSH connection dropping here is expected - that means the host is going down.");
        AppendLog("valheim.service is enabled to auto-start on boot, so no further action is needed.");
        AppendLog("Use Check Status in a minute or two to confirm the server has come back online.");

        StateText.Text = "REBOOTING...";
        StateText.Foreground = AppTheme.StatusWarning;
        StateDot.Fill = AppTheme.StatusWarning;

        SetBusy(false);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        await RefreshStatusAsync();
        await RefreshLastBackupAsync();
        await RefreshWorldAndUptimeAsync();
        await RefreshPlayerActivityAsync();
        SetBusy(false);
        ResetPollCountdown();
    }

    private void RunestonesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        var configWindow = new ConfigWindow(_config) { Owner = this };
        configWindow.ShowDialog();
    }

    private void UpdateWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        var updateWindow = new UpdateWindow(_config) { Owner = this };
        updateWindow.ShowDialog();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
