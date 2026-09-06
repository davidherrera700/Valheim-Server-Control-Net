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
                ExitStatus: cmd.ExitStatus);
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
}
