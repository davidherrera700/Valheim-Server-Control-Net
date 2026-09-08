namespace ValheimControl;

/// <summary>
/// Bump this with every official release you push (part of your publish
/// checklist alongside updating /opt/valheim/app_version.txt on the server -
/// see TODO.md "Enforced app updates"). Compared against the server's
/// declared required version on every launch to decide whether this PC
/// needs to update before it's allowed to use the app.
/// </summary>
public static class AppVersion
{
    public const string Current = "3.2.0";
}
