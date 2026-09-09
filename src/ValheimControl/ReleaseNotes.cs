namespace ValheimControl;

/// <summary>
/// Release notes shown in the "What's New" popup on launch, keyed by exact
/// version string. Add an entry here every time you bump AppVersion.Current -
/// if there's no entry for the running version, the popup simply doesn't
/// show (no crash, no blank dialog).
/// </summary>
public static class ReleaseNotes
{
    public static readonly Dictionary<string, string> ByVersion = new()
    {
        ["2.2.0"] =
            "- Added a Swap World button right on the main dashboard - no need to " +
            "open Runestones just to switch worlds\n" +
            "- Enforced app updates: outdated PCs now get a clear notice instead of " +
            "silently drifting out of sync with the server\n" +
            "- This \"What's New\" popup itself",
        ["3.0.0"] =
            "Major update - the app now runs on a real backend with real user accounts:\n" +
            "- Sign in with your own username and password (a shared SSH key is no " +
            "longer the only way in)\n" +
            "- Real Roles & Permissions - Owner can grant specific people specific " +
            "actions, enforced on every request\n" +
            "- New Account page - see your own permissions, change your password, log out\n" +
            "- Manage Users & Roles (Owner-only) - create accounts and roles right " +
            "from the app, no more command-line setup\n" +
            "- Every window (dashboard, Runestones, Update, Settings) now runs on this " +
            "new system\n" +
            "- The old shared \"delete password\" is gone - deleting a world now just " +
            "checks that you're Owner",
        ["3.1.0"] =
            "- The app can now update itself right from the dashboard - no more " +
            "manually downloading a new .exe from GitHub each time\n" +
            "- A gold \"App Update Available\" chip appears in the header only when " +
            "there's genuinely something new - click it to install and restart " +
            "automatically",
        ["3.1.1"] =
            "Test release - confirming self-update works.",
        ["3.2.0"] =
            "- The server is now reachable over your local network too, not just " +
            "Tailscale - no VPN needed for PCs on the same LAN\n" +
            "- The first-time setup wizard now moves on to sign-in automatically " +
            "after a clean connection, instead of requiring an extra click\n" +
            "- Fixed a crash on the sign-in screen caused by a missing web address " +
            "format\n" +
            "- Fixed the setup wizard's input fields and log window not matching " +
            "the rest of the app's look\n" +
            "- Added a First-Time Setup Guide, linked from the project's GitHub page",
        ["3.2.3"] =
            "- New: Uninstall from right inside the app (Settings > Danger Zone) - " +
            "no need to go through Windows separately\n" +
            "- The server status now honestly shows \"Unreachable\" during a real " +
            "outage (like the scheduled 5am reboot) instead of silently leaving a " +
            "stale \"Online\" label showing",
    };

    public static string? ForCurrentVersion =>
        ByVersion.TryGetValue(AppVersion.Current, out var notes) ? notes : null;
}
