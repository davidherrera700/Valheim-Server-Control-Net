using System.Linq;
using System.Text.Json;
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
    private static readonly TimeSpan AutoUpdateCheckPollInterval = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private TimeSpan CurrentPollInterval =>
        TimeSpan.FromSeconds(Math.Max(5, _config.PollingIntervalSeconds));

    private readonly AppConfig _config;
    private readonly ApiClient _api;
    private DispatcherTimer? _statusTimer;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _autoUpdateTimer;
    private DateTime? _lastAutoUpdateCheckUtc;
    private int _secondsUntilNextPoll;
    private int _recentJoinCount;
    private bool _isBusy;

    /// <summary>
    /// config and api are both always provided now - App.xaml.cs handles
    /// config loading and the login gate before this window is ever
    /// constructed, so there's no "maybe it failed" scenario to guard
    /// against in here the way there used to be.
    /// </summary>
    public MainWindow(AppConfig config, ApiClient api)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _config = config;
        _api = api;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ApplyAccentColor(_config.AccentColorPreset);

        var apiHost = Uri.TryCreate(_config.ApiBaseUrl, UriKind.Absolute, out var uri) ? uri.Host : _config.ApiBaseUrl;
        HostLabel.Text = $"{_api.Username} @ {apiHost}";

        ShowWhatsNewIfNeeded();

        AppendLog($"Signed in as '{_api.Username}' ({(_api.IsOwner ? "Owner" : _api.RoleName ?? "no role")}) - {_config.ApiBaseUrl}");
        await RefreshStatusAsync();
        await RefreshLastBackupAsync();
        await RefreshWorldAndUptimeAsync();
        await RefreshPlayerActivityAsync();

        _statusTimer = new DispatcherTimer { Interval = CurrentPollInterval };
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
    /// Polls every AutoUpdateCheckPollInterval (a short, fixed interval) but
    /// only actually checks for updates once _config.AutoUpdateCheckIntervalHours
    /// has genuinely elapsed - this means a change to the interval made in
    /// UpdateWindow (which shares this same AppConfig instance) takes effect
    /// on the very next poll, with no need to touch the timer itself.
    /// </summary>
    private async void AutoUpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_config.AutoUpdateEnabled != true || _isBusy) return;

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

            var checkResult = await _api.GetAsync("/api/update/check");
            if (!checkResult.Success)
            {
                AppendLog("(auto-update) Could not check for updates right now - skipping this cycle.");
                return;
            }

            UpdateCheckResponse? check;
            try
            {
                check = JsonSerializer.Deserialize<UpdateCheckResponse>(checkResult.Output, JsonOptions);
            }
            catch
            {
                AppendLog("(auto-update) Got an unexpected response checking for updates - skipping this cycle.");
                return;
            }

            if (check?.InstalledBuild is null || check.LatestBuild is null)
            {
                AppendLog("(auto-update) Could not determine current/latest build - skipping this cycle.");
                return;
            }

            if (!check.UpdateAvailable)
            {
                AppendLog("(auto-update) Already up to date.");
                return;
            }

            AppendLog($"(auto-update) Update available ({check.InstalledBuild} -> {check.LatestBuild}). Running it now...");
            var updateResult = await _api.PostAsync("/api/update/run");
            var (success, output) = ParseActionResult(updateResult);
            AppendLog(success ? "(auto-update) Update completed." : $"(auto-update) Update reported a problem: {output}");

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
        // mid-flight - avoids stacking up concurrent requests.
        if (_isBusy) return;

        _isBusy = true;
        try
        {
            var result = await _api.GetAsync("/api/status");
            if (result.Success)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<StatusResponse>(result.Output, JsonOptions);
                    var previousText = StateText.Text;
                    UpdateStatusLabel(parsed?.Raw ?? "");

                    // Only write to the log when the state actually changes, so the
                    // panel doesn't fill up with a line every poll cycle.
                    if (StateText.Text != previousText)
                    {
                        AppendLog($"(auto-refresh) Status: {StateText.Text}");
                    }
                }
                catch
                {
                    // leave status label as-is on an unexpected response shape
                }
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
        // Self-correct the running timer's interval here too, so a change
        // made in SettingsWindow (which shares this same AppConfig instance)
        // takes effect from the next cycle without needing an app restart.
        if (_statusTimer is not null && _statusTimer.Interval != CurrentPollInterval)
        {
            _statusTimer.Interval = CurrentPollInterval;
        }

        _secondsUntilNextPoll = (int)CurrentPollInterval.TotalSeconds;
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

    /// <summary>
    /// Parses the {success, output} shape almost every action endpoint
    /// returns. Handles 403 Forbidden specially (empty body by design,
    /// since Results.Forbid() sends nothing back) with a clear message
    /// instead of a confusing blank/parse-failure result - the whole
    /// point of building real permissions is for a denial to be obvious,
    /// not silent.
    /// </summary>
    private static (bool Success, string Output) ParseActionResult(ApiResult r)
    {
        if (r.StatusCode == 403)
        {
            return (false, "Permission denied - your account doesn't have this permission.");
        }

        if (!r.Success && string.IsNullOrWhiteSpace(r.Output))
        {
            return (false, $"Request failed ({r.StatusCode}).");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ActionResponse>(r.Output, JsonOptions);
            return (parsed?.Success ?? r.Success, parsed?.Output ?? r.Output);
        }
        catch
        {
            return (r.Success, r.Output);
        }
    }

    private async Task RefreshStatusAsync()
    {
        var result = await _api.GetAsync("/api/status");
        if (!result.Success)
        {
            AppendLog($"Status check failed: {result.Output}");
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<StatusResponse>(result.Output, JsonOptions);
            UpdateStatusLabel(parsed?.Raw ?? "");
            AppendLog(parsed?.Raw ?? result.Output);
        }
        catch
        {
            AppendLog($"Status check returned unexpected data: {result.Output}");
        }
    }

    private async Task RefreshLastBackupAsync()
    {
        var result = await _api.GetAsync("/api/backup/last");
        if (!result.Success)
        {
            LastBackupValue.Text = "unknown";
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LastBackupResponse>(result.Output, JsonOptions);
            if (parsed?.LastBackupUtc is not DateTime backupUtc)
            {
                LastBackupValue.Text = "unknown";
                return;
            }

            var backupTimeLocal = DateTime.SpecifyKind(backupUtc, DateTimeKind.Utc).ToLocalTime();
            LastBackupValue.Text = FormatRelativeTime(DateTime.Now - backupTimeLocal);
            LastBackupValue.ToolTip = backupTimeLocal.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            LastBackupValue.Text = "unknown";
        }
    }

    private static string FormatRelativeTime(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    // ------------------------------------------------------------------
    // World name, uptime, recent player joins - all noticeably simpler
    // now than the SSH-based versions, since the API already returns
    // clean, structured data instead of raw text that needed client-side
    // parsing (systemctl output, /proc/pid/cmdline argv splitting, etc.).
    // ------------------------------------------------------------------

    private async Task RefreshWorldAndUptimeAsync()
    {
        var result = await _api.GetAsync("/api/world-info");
        if (!result.Success)
        {
            WorldValue.Text = "—";
            UptimeValue.Text = "—";
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<WorldInfoResponse>(result.Output, JsonOptions);
            WorldValue.Text = parsed?.World ?? "—";
            UptimeValue.Text = parsed?.UptimeSeconds is long seconds
                ? FormatUptime(TimeSpan.FromSeconds(seconds))
                : "—";
        }
        catch
        {
            WorldValue.Text = "—";
            UptimeValue.Text = "—";
        }
    }

    private static string FormatUptime(TimeSpan span)
    {
        if (span.TotalSeconds < 0) return "—";
        return span.Days > 0
            ? $"{span.Days}d {span.Hours:D2}:{span.Minutes:D2}"
            : $"{span.Hours:D2}:{span.Minutes:D2}";
    }

    private async Task RefreshPlayerActivityAsync()
    {
        var result = await _api.GetAsync("/api/players/recent");

        PlayerJoinDto[] entries = [];
        if (result.Success)
        {
            try
            {
                entries = JsonSerializer.Deserialize<PlayersResponse>(result.Output, JsonOptions)?.Players ?? [];
            }
            catch
            {
                // leave entries empty on an unexpected response shape
            }
        }

        PlayerRosterPanel.Children.Clear();

        if (entries.Length == 0)
        {
            _recentJoinCount = 0;
            PlayerCount.Text = "none yet";
            PlayerRosterPanel.Children.Add(new TextBlock
            {
                Text = "No player joins seen since the last restart.",
                Style = (Style)FindResource("SubtitleTextStyle")
            });
            return;
        }

        _recentJoinCount = entries.Length;
        PlayerCount.Text = $"{entries.Length} seen";

        // Most recent first, capped so the panel doesn't grow unbounded over a long session.
        foreach (var entry in entries.AsEnumerable().Reverse().Take(10))
        {
            var joinedAtLocal = DateTime.SpecifyKind(entry.JoinedAtUtc, DateTimeKind.Utc).ToLocalTime();

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var marker = new Shapes.Rectangle { Style = (Style)FindResource("DiamondMarkerStyle") };
            Grid.SetColumn(marker, 0);

            var nameBlock = new TextBlock { Text = entry.Name, Margin = new Thickness(12, 0, 8, 0) };
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

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        AppendLog("Starting Valheim...");
        var r = await _api.PostAsync("/api/server/start");
        var (success, output) = ParseActionResult(r);
        AppendLog(success ? "Start command sent." : $"Start failed: {output}");
        await Task.Delay(2000);
        await RefreshStatusAsync();
        SetBusy(false);
        ResetPollCountdown();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        AppendLog("Restarting Valheim...");
        var r = await _api.PostAsync("/api/server/restart");
        var (success, output) = ParseActionResult(r);
        AppendLog(success ? "Restart command sent." : $"Restart failed: {output}");
        await Task.Delay(2000);
        await RefreshStatusAsync();
        SetBusy(false);
        ResetPollCountdown();
    }

    /// <summary>
    /// Shows a confirmation dialog for a destructive action, respecting the
    /// ConfirmDestructiveActions guardrail (skips the dialog entirely if the
    /// user turned it off) and appending a recent-joins heads-up when
    /// WarnIfPlayersRecentlyJoined is on and someone's actually joined since
    /// the last restart. Returns true if the action should proceed.
    /// </summary>
    private bool ConfirmDestructiveAction(string title, string message)
    {
        if (_config.ConfirmDestructiveActions == false) return true;

        var fullMessage = message;
        if (_config.WarnIfPlayersRecentlyJoined == true && _recentJoinCount > 0)
        {
            fullMessage += $"\n\nHeads up: {_recentJoinCount} player(s) have joined since the last restart " +
                            "(this doesn't guarantee anyone's online right now, just that someone has been recently).";
        }

        return MessageBox.Show(fullMessage, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = ConfirmDestructiveAction(
            "Confirm Stop",
            "Are you sure you want to stop the Valheim server? Players will be disconnected.");

        if (!confirmed) return;

        SetBusy(true);
        AppendLog("Stopping Valheim...");
        var r = await _api.PostAsync("/api/server/stop");
        var (success, output) = ParseActionResult(r);
        AppendLog(success ? "Stop command sent." : $"Stop failed: {output}");
        await Task.Delay(2000);
        await RefreshStatusAsync();
        SetBusy(false);
        ResetPollCountdown();
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        AppendLog("Running manual backup...");
        var r = await _api.PostAsync("/api/backup/run");
        var (success, output) = ParseActionResult(r);
        AppendLog(success ? "Backup completed." : $"Backup failed: {output}");
        if (!string.IsNullOrWhiteSpace(output)) AppendLog(output);
        if (success) await RefreshLastBackupAsync();
        SetBusy(false);
    }

    /// <summary>
    /// One call now instead of two sequential ones - the API's own
    /// /api/server/backup-and-restart endpoint already does backup then
    /// restart atomically server-side, so there's no client-side
    /// sequencing to manage here anymore.
    /// </summary>
    private async void BackupRestartButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        AppendLog("Running backup, then restarting...");
        var r = await _api.PostAsync("/api/server/backup-and-restart");
        var (success, output) = ParseActionResult(r);
        AppendLog(success ? "Backup + restart completed." : $"Backup + restart failed: {output}");

        await RefreshLastBackupAsync();
        await Task.Delay(2000);
        await RefreshStatusAsync();
        ResetPollCountdown();
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
        SetBusy(true);
        AppendLog("Fetching recent log lines...");
        var r = await _api.GetAsync("/api/logs?lines=50");
        var (success, output) = ParseActionResult(r);
        AppendLog("----- Recent Server Logs -----");
        AppendLog(success ? output : $"Failed: {output}");
        AppendLog("----- End of Logs -----");
        SetBusy(false);
    }

    /// <summary>
    /// One call now instead of three sequential ones - the API's own
    /// /api/server/backup-and-reboot endpoint already does stop, backup,
    /// then reboot atomically server-side (and it's Owner-only there,
    /// enforced fresh against the database on every request).
    /// </summary>
    private async void BackupRebootButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = ConfirmDestructiveAction(
            "Confirm Backup + Reboot",
            "This will:\n" +
            "  1. Stop the Valheim server (disconnecting any players)\n" +
            "  2. Run a manual backup\n" +
            "  3. Reboot the entire Ubuntu host\n\n" +
            "The server will be offline for a few minutes while the host restarts, " +
            "then it will come back online automatically. Continue?");

        if (!confirmed) return;

        SetBusy(true);
        AppendLog("Stopping, backing up, and rebooting the host...");

        var r = await _api.PostAsync("/api/server/backup-and-reboot");
        var (success, output) = ParseActionResult(r);

        if (!success)
        {
            AppendLog($"Backup + Reboot failed: {output}");
            SetBusy(false);
            return;
        }

        await RefreshLastBackupAsync();

        AppendLog("Reboot command sent. The connection dropping here is expected - that means the host is going down.");
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

    // ------------------------------------------------------------------
    // Header buttons - Runestones/Update/Settings still open the old
    // SSH-based windows completely unchanged for now; they're their own
    // separate migration passes, not part of this one.
    // ------------------------------------------------------------------

    private void RunestonesButton_Click(object sender, RoutedEventArgs e)
    {
        var configWindow = new ConfigWindow(_config, _api) { Owner = this };
        configWindow.ShowDialog();
    }

    private void UpdateWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var updateWindow = new UpdateWindow(_config, _api) { Owner = this };
        updateWindow.ShowDialog();
    }

    private void SettingsWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_config, _api) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        var accountWindow = new AccountWindow(_config, _api) { Owner = this };
        accountWindow.ShowDialog();

        if (accountWindow.DidLogOut)
        {
            // Signed out from inside a live session - there's no graceful
            // "downgrade to logged-out dashboard" state, so just restart
            // the whole app cleanly; App.xaml.cs's login gate handles the
            // rest from there.
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// Shows the What's New popup only if there are notes for the currently
    /// running version AND the user hasn't already dismissed it (with the
    /// "don't show again" toggle) for this exact version. A future version
    /// bump means LastDismissedReleaseNotesVersion no longer matches
    /// AppVersion.Current, so this naturally reappears on the next update.
    /// </summary>
    private void ShowWhatsNewIfNeeded()
    {
        var notes = ReleaseNotes.ForCurrentVersion;
        if (notes is null) return;

        if (_config.LastDismissedReleaseNotesVersion == AppVersion.Current) return;

        var window = new WhatsNewWindow(AppVersion.Current, notes) { Owner = this };
        window.ShowDialog();

        if (window.DontShowAgain)
        {
            _config.LastDismissedReleaseNotesVersion = AppVersion.Current;
            ConfigService.Save(_config);
        }
    }

    private async void SwapWorldQuickButton_Click(object sender, RoutedEventArgs e)
    {
        var swapWindow = new SwapWorldWindow(_api) { Owner = this };
        swapWindow.ShowDialog();

        // Whatever happened in there (swapped + restarted, swapped only, or
        // cancelled), refresh so the dashboard reflects current reality.
        await RefreshWorldAndUptimeAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ------------------------------------------------------------------
    // JSON response shapes from the API
    // ------------------------------------------------------------------

    private record StatusResponse(string RequestedBy, string Raw);
    private record ActionResponse(bool Success, string? Output, string? Stage);
    private record LastBackupResponse(DateTime? LastBackupUtc);
    private record WorldInfoResponse(string? World, long? UptimeSeconds);
    private record PlayerJoinDto(string Name, DateTime JoinedAtUtc);
    private record PlayersResponse(PlayerJoinDto[] Players);
    private record UpdateCheckResponse(string? InstalledBuild, string? LatestBuild, bool UpdateAvailable);
}
