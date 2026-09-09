using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ApiClient _api;
    private bool _suppressChangeEvents;

    // (Display, systemd value, minutes) - the systemd value and minutes both
    // matter: the value is what gets sent to set_backup_interval.sh (which
    // only accepts this exact whitelist), the minutes are for the disk
    // estimate math.
    private static readonly (string Display, string Value, int Minutes)[] IntervalOptions =
    {
        ("5 minutes", "5min", 5),
        ("10 minutes", "10min", 10),
        ("15 minutes", "15min", 15),
        ("30 minutes", "30min", 30),
        ("1 hour", "1h", 60),
        ("2 hours", "2h", 120),
        ("6 hours", "6h", 360),
    };

    private static readonly (string Display, int Minutes)[] RecentRetentionOptions =
    {
        ("1 hour", 60),
        ("2 hours", 120),
        ("4 hours", 240),
        ("6 hours", 360),
        ("8 hours", 480),
        ("12 hours", 720),
        ("24 hours", 1440),
    };

    private static readonly (string Display, int Days)[] DailyRetentionOptions =
    {
        ("3 days", 3),
        ("7 days", 7),
        ("14 days", 14),
        ("21 days", 21),
        ("30 days", 30),
        ("60 days", 60),
        ("90 days", 90),
    };

    // Rough per-backup size estimate (compressed world save), used only for
    // the on-screen disk usage estimate - not an exact figure.
    private const double EstimatedGigabytesPerBackup = 0.3;

    public SettingsWindow(AppConfig config, ApiClient api)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _config = config;
        _api = api;
        Loaded += SettingsWindow_Loaded;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressChangeEvents = true;

        foreach (ComboBoxItem item in PollingIntervalBox.Items)
        {
            if ((string)item.Tag == _config.PollingIntervalSeconds.ToString())
            {
                PollingIntervalBox.SelectedItem = item;
                break;
            }
        }
        PollingIntervalBox.SelectedItem ??= PollingIntervalBox.Items[1]; // default: 30 seconds

        ConfirmDestructiveToggle.IsChecked = _config.ConfirmDestructiveActions;
        WarnPlayersToggle.IsChecked = _config.WarnIfPlayersRecentlyJoined;

        BuildBackupDropdowns();

        _suppressChangeEvents = false;

        BuildAccentSwatches();
        await LoadBackupSettingsAsync();
    }

    private void PollingIntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        if (PollingIntervalBox.SelectedItem is not ComboBoxItem { Tag: string secondsText }) return;
        if (!int.TryParse(secondsText, out var seconds)) return;

        _config.PollingIntervalSeconds = seconds;
        ConfigService.Save(_config);
    }

    private void ConfirmDestructiveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        _config.ConfirmDestructiveActions = ConfirmDestructiveToggle.IsChecked == true;
        ConfigService.Save(_config);
    }

    private void WarnPlayersToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        _config.WarnIfPlayersRecentlyJoined = WarnPlayersToggle.IsChecked == true;
        ConfigService.Save(_config);
    }

    // ------------------------------------------------------------------
    // Backup retention / interval
    // ------------------------------------------------------------------

    private void BuildBackupDropdowns()
    {
        BackupIntervalBox.Items.Clear();
        foreach (var opt in IntervalOptions)
        {
            BackupIntervalBox.Items.Add(new ComboBoxItem { Content = opt.Display, Tag = opt.Value });
        }

        RecentRetentionBox.Items.Clear();
        foreach (var opt in RecentRetentionOptions)
        {
            RecentRetentionBox.Items.Add(new ComboBoxItem { Content = opt.Display, Tag = opt.Minutes.ToString() });
        }

        DailyRetentionBox.Items.Clear();
        foreach (var opt in DailyRetentionOptions)
        {
            DailyRetentionBox.Items.Add(new ComboBoxItem { Content = opt.Display, Tag = opt.Days.ToString() });
        }
    }

    private async Task LoadBackupSettingsAsync()
    {
        SaveBackupSettingsButton.IsEnabled = false;
        BackupSettingsStatus.Text = "Loading current settings...";

        var result = await _api.GetAsync("/api/backup/settings");
        if (!result.Success)
        {
            BackupSettingsStatus.Text = $"Couldn't load current settings: {result.Output}";
            return;
        }

        BackupSettingsDto? settings;
        try
        {
            settings = System.Text.Json.JsonSerializer.Deserialize<BackupSettingsDto>(
                result.Output, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            BackupSettingsStatus.Text = "Couldn't load current settings: unexpected response.";
            return;
        }

        _suppressChangeEvents = true;

        if (settings?.Interval is not null) SelectComboItemByTag(BackupIntervalBox, settings.Interval);
        if (settings?.RecentRetentionMinutes is int recentMin) SelectComboItemByTag(RecentRetentionBox, recentMin.ToString());
        if (settings?.DailyRetentionDays is int dailyDays) SelectComboItemByTag(DailyRetentionBox, dailyDays.ToString());

        _suppressChangeEvents = false;

        BackupSettingsStatus.Text = "";
        UpdateDiskEstimate();
    }

    private record BackupSettingsDto(int? RecentRetentionMinutes, int? DailyRetentionDays, string? Interval);

    private static void SelectComboItemByTag(ComboBox box, string tagValue)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((string)item.Tag == tagValue)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private void BackupSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChangeEvents) return;
        UpdateDiskEstimate();
        SaveBackupSettingsButton.IsEnabled = true;
        BackupSettingsStatus.Text = "Unsaved changes";
    }

    private void UpdateDiskEstimate()
    {
        if (BackupIntervalBox.SelectedItem is not ComboBoxItem { Tag: string intervalValue }) return;
        if (RecentRetentionBox.SelectedItem is not ComboBoxItem { Tag: string recentMinText }) return;
        if (DailyRetentionBox.SelectedItem is not ComboBoxItem { Tag: string dailyDaysText }) return;

        var intervalMinutes = IntervalOptions.FirstOrDefault(o => o.Value == intervalValue).Minutes;
        if (intervalMinutes <= 0 ||
            !int.TryParse(recentMinText, out var recentMinutes) ||
            !int.TryParse(dailyDaysText, out var dailyDays))
        {
            return;
        }

        var recentBackupCount = recentMinutes / (double)intervalMinutes;
        var recentGb = recentBackupCount * EstimatedGigabytesPerBackup;
        var dailyGb = dailyDays * EstimatedGigabytesPerBackup;
        var totalGb = recentGb + dailyGb;

        DiskEstimateText.Text =
            $"Estimated disk usage: ~{totalGb:0.#} GB (~{recentBackupCount:0} recent + {dailyDays} daily, " +
            $"at ~{EstimatedGigabytesPerBackup * 1024:0} MB/backup)";
    }

    private async void SaveBackupSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupIntervalBox.SelectedItem is not ComboBoxItem { Tag: string intervalValue }) return;
        if (RecentRetentionBox.SelectedItem is not ComboBoxItem { Tag: string recentMinText }) return;
        if (DailyRetentionBox.SelectedItem is not ComboBoxItem { Tag: string dailyDaysText }) return;
        if (!int.TryParse(recentMinText, out var recentMinutes)) return;
        if (!int.TryParse(dailyDaysText, out var dailyDays)) return;

        var confirm = MessageBox.Show(
            $"Apply these backup settings?\n\n{DiskEstimateText.Text}\n\n" +
            "This changes the interval immediately (the backup timer restarts) and applies the new " +
            "retention windows the next time old backups are cleaned up.",
            "Confirm Backup Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SaveBackupSettingsButton.IsEnabled = false;
        BackupSettingsStatus.Text = "Saving...";

        var result = await _api.PutAsync("/api/backup/settings", new
        {
            recentRetentionMinutes = recentMinutes,
            dailyRetentionDays = dailyDays,
            interval = intervalValue
        });

        if (!result.Success)
        {
            BackupSettingsStatus.Text = result.StatusCode == 403
                ? "Permission denied - your account doesn't have backup-settings permission."
                : $"Failed to save: {result.Output}";
            SaveBackupSettingsButton.IsEnabled = true;
            return;
        }

        BackupSettingsStatus.Text = "Saved";
    }

    // ------------------------------------------------------------------
    // Accent color swatches
    // ------------------------------------------------------------------

    private void BuildAccentSwatches()
    {
        AccentSwatchPanel.Children.Clear();

        foreach (var (key, preset) in ThemeService.AccentPresets)
        {
            var isSelected = _config.AccentColorPreset == key;

            var swatch = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Base)!),
                BorderBrush = isSelected
                    ? (Brush)FindResource("TextPrimaryBrush")
                    : (Brush)FindResource("BorderMutedBrush"),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Cursor = Cursors.Hand,
                ToolTip = preset.Name,
                Tag = key
            };

            swatch.MouseLeftButtonUp += AccentSwatch_Click;
            AccentSwatchPanel.Children.Add(swatch);
        }
    }

    private void AccentSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string key }) return;

        _config.AccentColorPreset = key;
        ConfigService.Save(_config);
        ThemeService.ApplyAccentColor(key);

        BuildAccentSwatches(); // redraw so the selection ring moves to the new choice
    }

    /// <summary>
    /// Triggers the exact same uninstall Velopack registers with Windows'
    /// own "Apps & Features" - Update.exe sits one level up from the
    /// running app's own folder (...\ValheimControl\current\ becomes
    /// ...\ValheimControl\Update.exe). There's no managed Velopack API for
    /// this specifically (only Check/Download/Apply are exposed), so this
    /// calls the documented CLI directly: "update.exe uninstall".
    /// </summary>
    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "This removes Valheim Control from THIS PC only - shortcuts, files, and registry entries.\n\n" +
            "This doesn't affect the server, your account, or anyone else's PC - you'd need to reinstall " +
            "and sign back in to use it again from here.\n\n" +
            "Uninstall now?",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        // AppContext.BaseDirectory always ends with a trailing separator
        // (e.g. "...\ValheimControl\current\") - Directory.GetParent can
        // unreliably return that SAME folder instead of walking up one
        // level when given a path like that. DirectoryInfo.Parent doesn't
        // have this ambiguity.
        var installDir = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
        var updateExePath = installDir is null ? null : Path.Combine(installDir, "Update.exe");

        if (updateExePath is null || !File.Exists(updateExePath))
        {
            MessageBox.Show(
                "Couldn't find Update.exe - this only works for a real installed copy " +
                "(via ValheimControl-win-Setup.exe), not a dev build.\n\n" +
                $"Looked for it at:\n{updateExePath ?? "(could not compute a path)"}\n\n" +
                "For a dev build, just delete the project's build output folder manually instead.",
                "Uninstall Not Available",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // --silent: we've already shown our own confirmation above, so
            // this skips Update.exe's own redundant prompt.
            Process.Start(new ProcessStartInfo(updateExePath, "uninstall --silent")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't start the uninstaller:\n\n{ex.Message}", "Uninstall Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Must exit promptly - Velopack's docs are explicit that files
        // still in use inside the "current" folder can't be deleted, so
        // hanging around here would leave a broken partial uninstall.
        Application.Current.Shutdown();
    }
}
