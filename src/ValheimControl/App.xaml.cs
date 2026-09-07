using System.Windows;
using ValheimControl.Models;
using ValheimControl.Services;

namespace ValheimControl;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        new MainWindow().Show();
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
