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
    /// Expands environment variables (e.g. %USERPROFILE%) in the key path,
    /// same convention the v1 config used.
    /// </summary>
    public string ResolvedSshKeyPath =>
        Environment.ExpandEnvironmentVariables(SshKeyPath);
}
