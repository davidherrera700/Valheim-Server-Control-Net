using System.IO;
using System.Runtime.InteropServices;

namespace ValheimControl.Services;

/// <summary>
/// Creates .lnk shortcuts using late-bound WScript.Shell COM automation.
/// Late binding (rather than a static COM reference) avoids needing any
/// extra project-level COM interop registration.
/// </summary>
public static class ShortcutService
{
    public static void CreateShortcut(string shortcutPath, string targetExePath, string? iconPath, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM component is not available on this system.");

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) throw new InvalidOperationException("Could not create WScript.Shell instance.");

        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = targetExePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath);
                shortcut.Description = description;

                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                {
                    shortcut.IconLocation = iconPath;
                }

                shortcut.Save();
            }
            finally
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    public static string DesktopShortcutPath(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{name}.lnk");

    public static string StartMenuShortcutPath(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{name}.lnk");
}
