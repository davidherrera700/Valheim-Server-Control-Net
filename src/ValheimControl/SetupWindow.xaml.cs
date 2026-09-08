using System.IO;
using System.Windows;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class SetupWindow : Window
{
    private const string KeyFileName = "valheim_control_ed25519";

    public SetupWindow()
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
    }

    private void AppendLog(string text)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{timestamp}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        InstallButton.IsEnabled = !busy;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var server = ServerBox.Text.Trim();
        var user = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(user))
        {
            MessageBox.Show("Server address and SSH username are required.", "Missing Information",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port <= 0)
        {
            MessageBox.Show("SSH port must be a valid number.", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            var confirm = MessageBox.Show(
                "No password entered. This is only OK if the public key has already been added to the " +
                "server manually. Continue without password-based key install?",
                "No Password Provided",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;
        }

        SetBusy(true);
        LogBox.Clear();

        try
        {
            await RunSetupAsync(server, user, port, password);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunSetupAsync(string server, string user, int port, string password)
    {
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        Directory.CreateDirectory(sshDir);

        var keyPath = Path.Combine(sshDir, KeyFileName);
        var pubKeyPath = $"{keyPath}.pub";

        // 1. Generate SSH key if needed
        if (File.Exists(keyPath))
        {
            AppendLog($"Existing SSH key found at {keyPath} - reusing it.");
        }
        else
        {
            AppendLog("Generating a new ED25519 SSH key for this PC...");
            var comment = $"valheim-control-{Environment.MachineName}";
            var (keyGenSuccess, keyGenOutput) = await KeyGenService.GenerateEd25519KeyAsync(keyPath, comment);

            if (!keyGenSuccess)
            {
                AppendLog($"Key generation failed:\n{keyGenOutput}");
                return;
            }
            AppendLog("SSH key generated.");
        }

        if (!File.Exists(pubKeyPath))
        {
            AppendLog($"Expected public key at {pubKeyPath} but it wasn't found. Aborting.");
            return;
        }

        var publicKeyContent = await File.ReadAllTextAsync(pubKeyPath);

        // 2. Copy public key to server (password auth, one-time)
        if (!string.IsNullOrEmpty(password))
        {
            AppendLog($"Copying public key to {user}@{server}:~/.ssh/authorized_keys ...");
            var (copySuccess, copyOutput) = await SetupService.InstallPublicKeyAsync(server, port, user, password, publicKeyContent);
            AppendLog(copySuccess ? "Public key installed on server." : $"Key install failed: {copyOutput}");

            if (!copySuccess)
            {
                AppendLog("You can add the key manually - see the public key file at:");
                AppendLog(pubKeyPath);
                return;
            }
        }
        else
        {
            AppendLog("Skipping automatic key install (no password provided). Assuming the key is already authorized.");
        }

        // 3. Write config.json
        var config = new AppConfig
        {
            Server = server,
            User = user,
            SshKeyPath = keyPath.Replace(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            SshPort = port,
            ServiceName = "valheim.service",
            BackupCommand = "cd /opt/valheim && sudo -u valheim /opt/valheim/backup.sh",
            LogLines = 50
        };
        ConfigService.Save(config);
        AppendLog($"Configuration saved to {ConfigService.ConfigFilePath}");

        // 4. Test passwordless connection
        AppendLog("Testing passwordless SSH connection...");
        var ssh = new SshService(config);
        var testResult = await ssh.RunCommandAsync("echo OK");
        var connectionFullyWorking = testResult.Success && testResult.Output.Contains("OK");

        if (connectionFullyWorking)
        {
            AppendLog("Passwordless SSH connection successful.");
        }
        else
        {
            AppendLog($"Passwordless SSH test did not succeed: {testResult.Output}");
            AppendLog("You can still continue, but double-check server-side authorized_keys and sudoers rules.");
        }

        // Note: Desktop/Start Menu shortcuts are created automatically by
        // the Velopack installer (ValheimControl-win-Setup.exe) itself -
        // no need to duplicate that here. This used to be a manual step
        // back when the app was distributed as a raw published .exe.

        AppendLog("=== Setup complete ===");
        ContinueButton.IsEnabled = true;

        // Only auto-advance on a genuinely clean success - if the
        // connection test came back with a warning, deliberately require
        // the manual click instead, so the person has to actually notice
        // and consider that warning rather than being swept past it.
        if (connectionFullyWorking)
        {
            AppendLog("Moving on to sign-in in a moment...");
            await Task.Delay(1500);
            await ProceedToLoginAsync();
        }
    }

    private async void ContinueButton_Click(object sender, RoutedEventArgs e) => await ProceedToLoginAsync();

    private async Task ProceedToLoginAsync()
    {
        AppConfig config;
        try
        {
            config = ConfigService.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load the configuration that was just written:\n{ex.Message}",
                "Valheim Control - Config Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // SSH setup (worlds/backups/config editing) is done - now also
        // require signing into the new API-based system before reaching
        // the dashboard, same login gate normal startup uses.
        var loginWindow = new LoginWindow(config, blockingMode: true);

        var previousShutdownMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        loginWindow.ShowDialog();
        Application.Current.ShutdownMode = previousShutdownMode;

        if (!loginWindow.Success || loginWindow.Api is null)
        {
            return; // login didn't succeed - stay on this window rather than proceeding
        }

        var main = new MainWindow(config, loginWindow.Api);
        main.Show();
        Close();
    }
}
