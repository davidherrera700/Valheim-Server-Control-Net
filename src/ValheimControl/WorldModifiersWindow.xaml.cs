using System.Windows;
using System.Windows.Controls;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class WorldModifiersWindow : Window
{
    public bool Applied { get; private set; }

    public WorldModifiersWindow()
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Applied = true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// Applies whatever is currently selected in this dialog onto the given
    /// start_server.sh content, returning the updated content. Anything left
    /// at "Default" is a no-op for that field.
    /// </summary>
    public string ApplyTo(string content)
    {
        content = LaunchLineParser.SetPreset(content, SelectedTag(PresetBox));
        content = LaunchLineParser.SetModifier(content, "combat", SelectedTag(CombatBox));
        content = LaunchLineParser.SetModifier(content, "deathpenalty", SelectedTag(DeathPenaltyBox));
        content = LaunchLineParser.SetModifier(content, "resources", SelectedTag(ResourcesBox));
        content = LaunchLineParser.SetModifier(content, "raids", SelectedTag(RaidsBox));
        content = LaunchLineParser.SetModifier(content, "portals", SelectedTag(PortalsBox));

        content = LaunchLineParser.SetSetKey(content, "nomap", NoMapToggle.IsChecked == true);
        content = LaunchLineParser.SetSetKey(content, "playerevents", PlayerEventsToggle.IsChecked == true);
        content = LaunchLineParser.SetSetKey(content, "passivemobs", PassiveMobsToggle.IsChecked == true);
        content = LaunchLineParser.SetSetKey(content, "nobuildcost", NoBuildCostToggle.IsChecked == true);

        return content;
    }

    private static string? SelectedTag(ComboBox box) =>
        box.SelectedItem is ComboBoxItem { Tag: string tag } && tag != "default" ? tag : null;
}
