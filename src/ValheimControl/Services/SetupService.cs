using Renci.SshNet;

namespace ValheimControl.Services;

/// <summary>
/// One-time setup operations that use password authentication - copying this
/// PC's new public key into the server's authorized_keys. After this succeeds,
/// all normal app usage goes through SshService with key-based auth instead.
/// </summary>
public static class SetupService
{
    public static async Task<(bool Success, string Output)> InstallPublicKeyAsync(
        string host, int port, string user, string password, string publicKeyContent)
    {
        try
        {
            var connectionInfo = new ConnectionInfo(
                host, port, user,
                new PasswordAuthenticationMethod(user, password))
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            using var client = new SshClient(connectionInfo);
            await Task.Run(() => client.Connect());

            var trimmedKey = publicKeyContent.Trim();
            var remoteCommand =
                "mkdir -p ~/.ssh && chmod 700 ~/.ssh && " +
                $"echo \"{trimmedKey}\" >> ~/.ssh/authorized_keys && " +
                "chmod 600 ~/.ssh/authorized_keys";

            using var cmd = client.CreateCommand(remoteCommand);
            cmd.CommandTimeout = TimeSpan.FromSeconds(15);

            var output = await Task.Run(() => cmd.Execute());
            var success = (cmd.ExitStatus ?? -1) == 0;

            client.Disconnect();

            var message = string.IsNullOrWhiteSpace(output)
                ? (success ? "Public key installed on server." : "Command failed with no output.")
                : output;

            return (success, message);
        }
        catch (Exception ex)
        {
            return (false, $"Password authentication or connection failed: {ex.Message}");
        }
    }
}
