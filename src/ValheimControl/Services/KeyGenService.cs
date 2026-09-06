using System.Diagnostics;

namespace ValheimControl.Services;

/// <summary>
/// Wraps ssh-keygen.exe to generate a new ED25519 key pair during setup.
/// This is the one place the app still touches the Windows OpenSSH client -
/// runtime SSH connections use SSH.NET natively and don't need it.
/// </summary>
public static class KeyGenService
{
    public static async Task<(bool Success, string Output)> GenerateEd25519KeyAsync(string keyPath, string comment)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ssh-keygen.exe",
            Arguments = $"-t ed25519 -f \"{keyPath}\" -N \"\" -C \"{comment}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start ssh-keygen.exe.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var combined = string.Join(
                Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return (proc.ExitCode == 0, combined);
        }
        catch (Exception ex)
        {
            return (false,
                $"Failed to run ssh-keygen.exe: {ex.Message}\n\n" +
                "Make sure the Windows OpenSSH Client optional feature is installed " +
                "(Settings > Apps > Optional Features > OpenSSH Client).");
        }
    }
}
