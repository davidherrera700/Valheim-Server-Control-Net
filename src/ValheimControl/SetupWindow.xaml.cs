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

        if (testResult.Success && testResult.Output.Contains("OK"))
        {
            AppendLog("Passwordless SSH connection successful.");
        }
        else
        {
            AppendLog($"Passwordless SSH test did not succeed: {testResult.Output}");
            AppendLog("You can still continue, but double-check server-side authorized_keys and sudoers rules.");
        }

        // 5. Create shortcuts
        try
        {
            var exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            if (string.IsNullOrEmpty(exePath))
            {
                AppendLog("Could not determine this app's executable path - skipping shortcut creation.");
            }
            else
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "valheim.ico");
                var desktopPath = ShortcutService.DesktopShortcutPath("Valheim Control");
                var startMenuPath = ShortcutService.StartMenuShortcutPath("Valheim Control");

                ShortcutService.CreateShortcut(desktopPath, exePath, iconPath, "Valheim Server Control");
                ShortcutService.CreateShortcut(startMenuPath, exePath, iconPath, "Valheim Server Control");

                AppendLog("Shortcuts created on Desktop and in Start Menu.");
                AppendLog("Note: if you're running via 'dotnet run', the shortcut points at a temporary build " +
                          "output. Re-run setup after publishing the final .exe (dotnet publish) to point the " +
                          "shortcut at the permanent location.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Shortcut creation failed (non-fatal): {ex.Message}");
        }

        AppendLog("=== Setup complete ===");
        ContinueButton.IsEnabled = true;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        var main = new MainWindow();
        main.Show();
        Close();
    }
}
