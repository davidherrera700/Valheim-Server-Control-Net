using System.Windows;
using System.Windows.Media;

namespace ValheimControl.Theme;

/// <summary>
/// Swaps the app's accent color at runtime by REPLACING the
/// AccentGoldBrush/AccentGoldBrightBrush resource dictionary entries with
/// new brush instances.
///
/// This relies on every consumer referencing these two resources via
/// DynamicResource, not StaticResource - a StaticResource reference is
/// resolved once, at load time, to a fixed object, so replacing the
/// dictionary entry later wouldn't reach already-open windows.
/// DynamicResource re-evaluates the lookup whenever the dictionary entry
/// changes, which is what makes live swapping possible here.
///
/// (An earlier version of this tried mutating the existing brush's Color
/// property in place instead of replacing it - that silently did nothing,
/// because WPF automatically freezes a shared Freezable brush once it's
/// used as a Style Setter's Value, which this accent color is, in many
/// places throughout the theme. A frozen brush can't be mutated at all.)
/// </summary>
public static class ThemeService
{
    public static readonly Dictionary<string, (string Name, string Base, string Bright)> AccentPresets = new()
    {
        ["gold"] = ("Gold", "#C9A876", "#E0C08A"),
        ["steel"] = ("Steel", "#7C93A8", "#A3B8C9"),
        ["ember"] = ("Ember", "#C97A5C", "#E0A183"),
        ["moss"] = ("Moss", "#7FA87C", "#A3C9A0"),
        ["frost"] = ("Frost", "#9FB3C4", "#C3D3DE"),
    };

    public static void ApplyAccentColor(string presetKey)
    {
        if (!AccentPresets.TryGetValue(presetKey, out var preset))
        {
            preset = AccentPresets["gold"];
        }

        ReplaceBrush("AccentGoldBrush", preset.Base);
        ReplaceBrush("AccentGoldBrightBrush", preset.Bright);
        ReplaceDividerGradient(preset.Base);
    }

    private static void ReplaceBrush(string resourceKey, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        Application.Current.Resources[resourceKey] = new SolidColorBrush(color);
    }

    /// <summary>
    /// Rebuilds RuneDividerBrush (the faded hairline under section titles,
    /// also used by DividerStyle throughout the app) with the new accent
    /// color - matches VikingTheme.xaml's original definition exactly.
    /// </summary>
    private static void ReplaceDividerGradient(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            Opacity = 0.45
        };
        gradient.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        gradient.GradientStops.Add(new GradientStop(color, 0.15));
        gradient.GradientStops.Add(new GradientStop(color, 0.85));
        gradient.GradientStops.Add(new GradientStop(Colors.Transparent, 1));

        Application.Current.Resources["RuneDividerBrush"] = gradient;
    }
}
