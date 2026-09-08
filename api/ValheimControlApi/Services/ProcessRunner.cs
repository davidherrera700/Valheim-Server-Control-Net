using System.Diagnostics;

namespace ValheimControlApi.Services;

/// <summary>
/// Runs a local command and captures its output. Used for everything that,
/// in the old SSH-based app, needed a remote command string built carefully
/// to survive shell quoting - here it's just a normal local process call,
/// since this code already runs on the server.
///
/// Arguments are passed as a string array (ArgumentList), never a single
/// pre-joined string - this sidesteps shell-quoting bugs entirely, since
/// each argument reaches the process as its own exact value regardless of
/// spaces or special characters inside it (e.g. a systemd timestamp like
/// "Mon 2026-09-07 10:15:22 UTC" stays one argument, never gets split).
/// </summary>
public static class ProcessRunner
{
    public record Result(bool Success, string Output, int ExitCode);

    public static async Task<Result> RunAsync(string fileName, string[]? arguments = null, int timeoutSeconds = 30)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (arguments is not null)
        {
            foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        }

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Could not start {fileName}.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000));
            if (!completed)
            {
                try { proc.Kill(); } catch { /* best effort */ }
                return new Result(false, $"Command timed out after {timeoutSeconds}s.", -1);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.Join(
                Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new Result(
                proc.ExitCode == 0,
                string.IsNullOrWhiteSpace(combined) ? "(no output)" : combined.Trim(),
                proc.ExitCode);
        }
        catch (Exception ex)
        {
            return new Result(false, $"Failed to run {fileName}: {ex.Message}", -1);
        }
    }
}
