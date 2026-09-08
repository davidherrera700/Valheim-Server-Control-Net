namespace ValheimControlApi.Services;

/// <summary>
/// The fixed set of real, permission-gateable actions in this app. Not an
/// open-ended concept - a role can only ever grant a combination of these
/// exact keys. Read-only actions (status, logs, world-info, listing
/// worlds/config/roles) aren't in this list at all - they stay universally
/// available to any logged-in user, matching how the old WPF app also
/// always allowed Check Status/View Logs for everyone.
/// </summary>
public static class Permissions
{
    public const string ServerStart = "server.start";
    public const string ServerRestart = "server.restart";
    public const string ServerStop = "server.stop";
    public const string BackupRun = "backup.run";
    public const string BackupAndRestart = "backup.restart";
    public const string BackupAndReboot = "backup.reboot";
    public const string WorldsSwap = "worlds.swap";
    public const string WorldsDelete = "worlds.delete";
    public const string ConfigWrite = "config.write";
    public const string UpdateRun = "update.run";
    public const string BackupSettingsWrite = "backupsettings.write";

    public static readonly string[] All =
    [
        ServerStart, ServerRestart, ServerStop,
        BackupRun, BackupAndRestart, BackupAndReboot,
        WorldsSwap, WorldsDelete,
        ConfigWrite,
        UpdateRun,
        BackupSettingsWrite
    ];
}
