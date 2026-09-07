using Renci.SshNet;
using ValheimControl.Models;

namespace ValheimControl.Services;

public record SshCommandResult(bool Success, string Output, int ExitStatus);

/// <summary>
/// Wraps SSH.NET so the rest of the app never has to deal with connection
/// plumbing directly. Unlike the v1 PowerShell tool, this does not shell out
/// to ssh.exe - the SSH protocol is implemented natively in-process, so the
/// OpenSSH Windows client no longer needs to be installed for the app to run.
/// (ssh-keygen is still handy for generating keys during setup, but the app
/// itself doesn't depend on ssh.exe being on PATH.)
/// </summary>
public class SshService
{
    private readonly AppConfig _config;

    public SshService(AppConfig config)
    {
        _config = config;
    }

    public async Task<SshCommandResult> RunCommandAsync(string command, int timeoutSeconds = 20)
    {
        try
        {
            var keyFile = new PrivateKeyFile(_config.ResolvedSshKeyPath);
            var authMethod = new PrivateKeyAuthenticationMethod(_config.User, keyFile);
            var connectionInfo = new ConnectionInfo(_config.Server, _config.SshPort, _config.User, authMethod)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            using var client = new SshClient(connectionInfo);

            await Task.Run(() => client.Connect());

            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            var stdout = await Task.Run(() => cmd.Execute());
            var stderr = cmd.Error;

            client.Disconnect();

            var combined = string.Join(
                Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

            return new SshCommandResult(
                Success: cmd.ExitStatus == 0,
                Output: string.IsNullOrWhiteSpace(combined) ? "(no output)" : combined,
                ExitStatus: cmd.ExitStatus ?? -1);
        }
        catch (Exception ex)
        {
            return new SshCommandResult(
                Success: false,
                Output: $"SSH error: {ex.Message}",
                ExitStatus: -1);
        }
    }

    public async Task<SshCommandResult> CheckStatusAsync()
    {
        return await RunCommandAsync($"systemctl show {_config.ServiceName} -p ActiveState -p SubState -p MainPID");
    }

    public async Task<SshCommandResult> RunBackupAsync()
    {
        return await RunCommandAsync(_config.BackupCommand, timeoutSeconds: 60);
    }

    public async Task<SshCommandResult> StartServiceAsync() =>
        await RunCommandAsync($"sudo systemctl start {_config.ServiceName}");

    public async Task<SshCommandResult> StopServiceAsync() =>
        await RunCommandAsync($"sudo systemctl stop {_config.ServiceName}");

    public async Task<SshCommandResult> RestartServiceAsync() =>
        await RunCommandAsync($"sudo systemctl restart {_config.ServiceName}");

    public async Task<SshCommandResult> GetRecentLogsAsync() =>
        await RunCommandAsync($"sudo journalctl -u {_config.ServiceName} -n {_config.LogLines} --no-pager");

    public async Task<SshCommandResult> GetLastBackupInfoAsync() =>
        await RunCommandAsync(
            "find /opt/valheim/backups -type f -printf '%T@ %p\\n' 2>/dev/null | sort -rn | head -n 1");

    /// <summary>
    /// Reboots the Ubuntu host. The SSH connection will typically drop before
    /// a response comes back (the host goes down mid-command), so a "failure"
    /// here often just means the reboot actually happened - callers should
    /// treat this as fire-and-forget rather than a strict success/failure check.
    /// </summary>
    public async Task<SshCommandResult> RebootHostAsync() =>
        await RunCommandAsync("sudo reboot", timeoutSeconds: 10);

    /// <summary>
    /// Unix epoch seconds of when the service last became active
    /// (systemd's ActiveEnterTimestamp), used to compute uptime. Empty output
    /// if the service isn't currently running.
    /// </summary>
    public async Task<SshCommandResult> GetUptimeEpochAsync() =>
        await RunCommandAsync(
            $"ts=\"$(systemctl show {_config.ServiceName} -p ActiveEnterTimestamp --value)\"; " +
            "if [ -n \"$ts\" ] && [ \"$ts\" != \"n/a\" ]; then date -d \"$ts\" +%s; fi");

    /// <summary>
    /// Raw argv of the running Valheim game process (one argument per line), read
    /// from /proc/&lt;pid&gt;/cmdline. The caller looks for "-world" and takes the
    /// following line as the world name.
    ///
    /// MainPID from systemd can point at a wrapper shell script rather than the
    /// actual valheim_server.x86_64 process (if the script launches it without
    /// `exec`), so this checks MainPID first, then falls back to searching its
    /// child processes, then a system-wide search by process name.
    /// </summary>
    public async Task<SshCommandResult> GetWorldArgvAsync() =>
        await RunCommandAsync(
            $"pid=\"$(systemctl show {_config.ServiceName} -p MainPID --value)\"; " +
            "target=\"\"; " +
            "if [ -n \"$pid\" ] && [ \"$pid\" != \"0\" ] && grep -aq 'valheim_server' \"/proc/$pid/cmdline\" 2>/dev/null; then " +
            "  target=\"$pid\"; " +
            "elif [ -n \"$pid\" ] && [ \"$pid\" != \"0\" ]; then " +
            "  target=\"$(pgrep -P \"$pid\" -f valheim_server | head -n1)\"; " +
            "fi; " +
            "if [ -z \"$target\" ]; then target=\"$(pgrep -f valheim_server.x86_64 | head -n1)\"; fi; " +
            "if [ -n \"$target\" ] && [ -r \"/proc/$target/cmdline\" ]; then tr '\\0' '\\n' < \"/proc/$target/cmdline\"; fi");

    /// <summary>
    /// Player "Got character ZDOID from ..." join lines since the service's
    /// last start, with ISO timestamps. NOTE: Valheim doesn't log a reliable
    /// disconnect-with-name event, so this is a best-effort "recent joins"
    /// feed, not a true currently-connected roster - callers should present
    /// it that way rather than as a live player count.
    /// </summary>
    public async Task<SshCommandResult> GetRecentPlayerJoinsAsync(int maxLines = 20) =>
        await RunCommandAsync(
            $"since=\"$(systemctl show {_config.ServiceName} -p ActiveEnterTimestamp --value)\"; " +
            $"if [ -n \"$since\" ] && [ \"$since\" != \"n/a\" ]; then " +
            $"sudo journalctl -u {_config.ServiceName} -o short-iso --since \"$since\" --no-pager " +
            $"| grep 'Got character ZDOID from' | tail -n {maxLines}; fi");

    // ------------------------------------------------------------------
    // Runestones: config file editing, world listing, updates
    // ------------------------------------------------------------------

    /// <summary>Filenames the server-side read_config.sh / write_config.sh scripts allow.</summary>
    public static readonly string[] AllowedConfigFiles =
        { "start_server.sh", "adminlist.txt", "permittedlist.txt", "bannedlist.txt" };

    public async Task<SshCommandResult> ReadConfigFileAsync(string filename)
    {
        if (!AllowedConfigFiles.Contains(filename))
            return new SshCommandResult(false, $"'{filename}' is not an allowed config file.", -1);

        return await RunCommandAsync($"sudo /opt/valheim/read_config.sh {filename}");
    }

    /// <summary>
    /// Writes new content to one of the allowed config files. Content is sent
    /// base64-encoded on the command line rather than piped over stdin - this
    /// sidesteps any shell-quoting issues with quotes/newlines/special
    /// characters in the file content (e.g. a password containing a quote
    /// mark in start_server.sh), without needing SSH.NET's less-common
    /// stdin-streaming API.
    /// </summary>
    public async Task<SshCommandResult> WriteConfigFileAsync(string filename, string content)
    {
        if (!AllowedConfigFiles.Contains(filename))
            return new SshCommandResult(false, $"'{filename}' is not an allowed config file.", -1);

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
        return await RunCommandAsync(
            $"echo '{encoded}' | base64 -d | sudo /opt/valheim/write_config.sh {filename}",
            timeoutSeconds: 20);
    }

    /// <summary>
    /// Real world save names on the server (Valheim's own automatic backup
    /// snapshots are filtered out server-side in list_worlds.sh).
    /// </summary>
    public async Task<SshCommandResult> ListWorldsAsync() =>
        await RunCommandAsync("sudo /opt/valheim/list_worlds.sh");

    /// <summary>
    /// Archives (moves, never permanently deletes) a world's save files.
    /// The server-side script itself refuses to touch the currently
    /// configured world or anything that looks like an auto-backup snapshot -
    /// this is a second, client-side layer of the same check for a faster,
    /// clearer error before even reaching the server.
    /// </summary>
    public async Task<SshCommandResult> DeleteWorldAsync(string worldName)
    {
        // Single-quote the argument so a world name containing spaces (e.g.
        // "Test World") is passed as one argument, not split by the shell.
        // Standard trick for embedding a literal single quote inside a
        // single-quoted string: close the quote, escape one, reopen it.
        var escaped = worldName.Replace("'", "'\\''");
        return await RunCommandAsync($"sudo /opt/valheim/delete_world.sh '{escaped}'");
    }

    /// <summary>
    /// Sets/updates the shared delete-confirmation password used across all
    /// PCs running this app against this server. Only a hash is ever stored
    /// server-side - the plain-text value is base64-encoded purely to survive
    /// the SSH command line safely (same technique as WriteConfigFileAsync),
    /// not for any security purpose.
    /// </summary>
    public async Task<SshCommandResult> SetDeletePasswordAsync(string newPassword)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newPassword));
        return await RunCommandAsync($"sudo /opt/valheim/set_delete_password.sh '{encoded}'");
    }

    /// <summary>
    /// Checks a candidate password against the server-side hash. Result is
    /// one of "NOT_SET" (no password configured yet), "MATCH", or "NO_MATCH" -
    /// the real password is never returned by the server.
    /// </summary>
    public async Task<SshCommandResult> VerifyDeletePasswordAsync(string candidate)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(candidate));
        return await RunCommandAsync($"sudo /opt/valheim/verify_delete_password.sh '{encoded}'");
    }

    /// <summary>Currently installed SteamCMD build ID for the dedicated server.</summary>
    public async Task<SshCommandResult> GetInstalledBuildIdAsync() =>
        await RunCommandAsync("sudo /opt/valheim/check_update.sh");

    /// <summary>
    /// Runs the full update: the server's own update.sh handles
    /// backup -> stop -> SteamCMD update -> start as one atomic operation.
    /// Can take a while depending on patch size, hence the longer timeout.
    /// </summary>
    public async Task<SshCommandResult> RunServerUpdateAsync() =>
        await RunCommandAsync("sudo /opt/valheim/update.sh", timeoutSeconds: 300);

    /// <summary>Current recent-backup retention (minutes), daily retention (days), and interval.</summary>
    public async Task<SshCommandResult> GetBackupSettingsAsync() =>
        await RunCommandAsync("sudo /opt/valheim/get_backup_settings.sh");

    /// <summary>Updates how long recent (minutes) and daily (days) backups are kept.</summary>
    public async Task<SshCommandResult> SetBackupRetentionAsync(int recentMinutes, int dailyDays) =>
        await RunCommandAsync($"sudo /opt/valheim/set_backup_retention.sh {recentMinutes} {dailyDays}");

    /// <summary>
    /// Updates how often backups run. Applies immediately (daemon-reload +
    /// timer restart happens server-side as part of the same script).
    /// </summary>
    public async Task<SshCommandResult> SetBackupIntervalAsync(string systemdInterval) =>
        await RunCommandAsync($"sudo /opt/valheim/set_backup_interval.sh {systemdInterval}");

    /// <summary>
    /// Reads /opt/valheim/app_version.txt - the server's declared required
    /// app version, checked on every launch. Output is key=value lines:
    /// RequiredVersion (always present), and optionally ReleaseNotes/DownloadUrl.
    /// </summary>
    public async Task<SshCommandResult> GetAppVersionInfoAsync() =>
        await RunCommandAsync("sudo /opt/valheim/get_app_version.sh");
}
