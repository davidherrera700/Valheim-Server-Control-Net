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
    };

    public static string? ForCurrentVersion =>
        ByVersion.TryGetValue(AppVersion.Current, out var notes) ? notes : null;
}
