using System.Windows.Media;

namespace ValheimControl.Theme;

/// <summary>
/// Code-behind counterpart to VikingTheme.xaml - the same palette, exposed as
/// frozen brushes for places (like status text color) that get set from C#
/// rather than bound in XAML.
///
/// Palette: "Carved Longhall" - warm oak ground, brass accents.
/// Keep these hex values in sync with Theme/VikingTheme.xaml.
/// </summary>
public static class AppTheme
{
    // Grounds
    public static readonly SolidColorBrush BgDark = CreateBrush("#16110C");
    public static readonly SolidColorBrush BgTitle = CreateBrush("#1B1510");
    public static readonly SolidColorBrush BgPanel = CreateBrush("#241C15");
    public static readonly SolidColorBrush BgPanelHover = CreateBrush("#2F251B");
    public static readonly SolidColorBrush LogBg = CreateBrush("#0F0B08");

    // Lines
    public static readonly SolidColorBrush BorderMuted = CreateBrush("#3E3125");
    public static readonly SolidColorBrush BorderFaint = CreateBrush("#241C15");

    // Brass
    public static readonly SolidColorBrush AccentGold = CreateBrush("#C9A876");
    public static readonly SolidColorBrush AccentGoldBright = CreateBrush("#E6C892");

    // Ink
    public static readonly SolidColorBrush TextPrimary = CreateBrush("#EDE3D0");
    public static readonly SolidColorBrush TextSecondary = CreateBrush("#9E9384");
    public static readonly SolidColorBrush TextMuted = CreateBrush("#948B7C");
    public static readonly SolidColorBrush LogText = CreateBrush("#C9D1D9");

    // Status
    public static readonly SolidColorBrush StatusOnline = CreateBrush("#7FB685");
    public static readonly SolidColorBrush StatusOffline = CreateBrush("#C1553B");
    public static readonly SolidColorBrush StatusOfflineText = CreateBrush("#E39176");
    public static readonly SolidColorBrush StatusWarning = CreateBrush("#D9A441");

    /// <summary>
    /// Status word + dot color for a server state. Use for both StateText.Foreground
    /// and StateDot.Fill so they never drift apart.
    /// </summary>
    public static SolidColorBrush ForState(ServerState state) => state switch
    {
        ServerState.Online => StatusOnline,
        ServerState.Starting => StatusWarning,
        ServerState.Stopping => StatusWarning,
        ServerState.Offline => StatusOffline,
        _ => TextSecondary,
    };

    private static SolidColorBrush CreateBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public enum ServerState
{
    Unknown,
    Offline,
    Starting,
    Online,
    Stopping,
}
