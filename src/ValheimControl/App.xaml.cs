using System.Windows;
using ValheimControl.Models;
using ValheimControl.Services;
using Velopack;

namespace ValheimControl;

public partial class App : Application
{
    /// <summary>
    /// Velopack requires bootstrapping as early as possible in the app's
    /// lifetime - earlier than WPF's normal auto-generated entry point
    /// allows, which is why this custom Main exists (and why App.xaml's
    /// build action had to change from ApplicationDefinition to Page in
    /// the .csproj - only one class can be "the" entry point).
    /// VelopackApp.Build().Run() is a no-op on a normal launch; it only
    /// does something meaningful during an actual install/update/uninstall
    /// operation, briefly intercepting startup to handle that instead of
    /// running the app normally.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await RunStartupAsync();
        }
        catch (Exception ex)
        {
            // Console output isn't showing anything useful for this crash,
            // so force it into a guaranteed-visible message box instead -
            // this is temporary diagnostic instrumentation, not permanent.
            MessageBox.Show(
                $"Startup failed with an unhandled exception:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                "Valheim Control - Startup Crash",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task RunStartupAsync()
    {
        if (!ConfigService.ConfigExists)
        {
            new SetupWindow().Show();
            return;
        }

        AppConfig config;
        try
        {
            config = ConfigService.Load();
        }
        catch
        {
            // Config exists but is unreadable/corrupt - let SetupWindow's own
            // error handling deal with it rather than duplicating that here.
            new SetupWindow().Show();
            return;
        }

        var mustUpdate = await CheckForRequiredUpdateAsync(config);
        if (mustUpdate) return; // AppUpdateRequiredWindow already triggered shutdown

        var loginWindow = new LoginWindow(config, blockingMode: true);

        // WPF's default ShutdownMode is OnLastWindowClose - closing this
        // dialog on a successful sign-in would otherwise be "the last
        // window closing" and silently kill the whole app before we get a
        // chance to show MainWindow next. Suspend that just for this one
        // dialog, then restore it immediately after.
        var previousShutdownMode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        loginWindow.ShowDialog();
        ShutdownMode = previousShutdownMode;

        if (!loginWindow.Success || loginWindow.Api is null)
        {
            // No windows are open at this point, and ShutdownMode is back
            // to normal - the app exits cleanly here on its own, which is
            // exactly what should happen when login didn't succeed.
            return;
        }

        new MainWindow(config, loginWindow.Api).Show();
    }

    /// <summary>
    /// Checks the server's declared required version against this build.
    /// Fails open on any problem (unreachable server, missing/malformed
    /// version file, unparseable version string) - a version check that
    /// can't complete should never be the reason someone can't use the app.
    /// Returns true only when the blocking AppUpdateRequiredWindow was shown.
    /// </summary>
    private static async Task<bool> CheckForRequiredUpdateAsync(AppConfig config)
    {
        try
        {
            var ssh = new SshService(config);
            var result = await ssh.GetAppVersionInfoAsync();
            if (!result.Success) return false;

            var values = new Dictionary<string, string>();
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
            }

            if (!values.TryGetValue("RequiredVersion", out var requiredStr)) return false;
            if (!Version.TryParse(requiredStr, out var required)) return false;
            if (!Version.TryParse(AppVersion.Current, out var current)) return false;

            if (current >= required) return false; // already up to date

            values.TryGetValue("ReleaseNotes", out var notes);
            values.TryGetValue("DownloadUrl", out var url);

            var window = new AppUpdateRequiredWindow(AppVersion.Current, requiredStr, notes, url);
            window.ShowDialog();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
