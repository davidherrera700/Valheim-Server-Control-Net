using System.Text.Json.Serialization;

namespace ValheimControl.Models;

/// <summary>
/// Mirrors the config.json shape used by the v1 PowerShell tool, so upgrading
/// from v1 to v2 on a PC that's already installed does not require re-running
/// the installer or re-copying SSH keys.
/// </summary>
public class AppConfig
{
    public string Server { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string SshKeyPath { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string ServiceName { get; set; } = "valheim.service";
    public string BackupCommand { get; set; } = "cd /opt/valheim && sudo -u valheim /opt/valheim/backup.sh";
    public int LogLines { get; set; } = 50;

    /// <summary>
    /// When true, MainWindow periodically checks for Valheim updates while
    /// the app is open and runs the full backup/stop/update/start cycle
    /// automatically when one's found - no confirmation, since the whole
    /// point is hands-off. Off by default; turning it on shows a one-time
    /// warning explaining exactly what it does.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; }

    /// <summary>How often, in hours, MainWindow checks for updates when AutoUpdateEnabled is true.</summary>
    public int AutoUpdateCheckIntervalHours { get; set; } = 6;

    /// <summary>
    /// Expands environment variables (e.g. %USERPROFILE%) in the key path,
    /// same convention the v1 config used. Computed at runtime, not stored -
    /// JsonIgnore keeps it out of config.json.
    /// </summary>
    [JsonIgnore]
    public string ResolvedSshKeyPath =>
        Environment.ExpandEnvironmentVariables(SshKeyPath);
}
