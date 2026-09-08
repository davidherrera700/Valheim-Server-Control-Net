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
    };

    public static string? ForCurrentVersion =>
        ByVersion.TryGetValue(AppVersion.Current, out var notes) ? notes : null;
}
