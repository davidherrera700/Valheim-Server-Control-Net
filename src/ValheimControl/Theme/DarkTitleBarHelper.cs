using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ValheimControl.Theme;

/// <summary>
/// Enables Windows' built-in dark title bar (the same mechanism Explorer and
/// other native dark-mode apps use) via DwmSetWindowAttribute. This keeps the
/// window's caption bar (minimize/close buttons, title text) visually
/// consistent with the app's dark theme, without the complexity and edge
/// cases of fully replacing the window chrome.
/// </summary>
public static class DarkTitleBarHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // pre-20H1 builds

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int enable = 1;
            var result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enable, sizeof(int));
            if (result != 0)
            {
                // Fall back for older Windows 10 builds that used a different attribute id.
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref enable, sizeof(int));
            }
        }
        catch
        {
            // Unsupported OS version - the native title bar just stays its default color.
            // Not worth failing setup or showing an error over a cosmetic detail.
        }
    }
}
