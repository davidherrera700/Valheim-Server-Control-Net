using System.Windows;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class WhatsNewWindow : Window
{
    public bool DontShowAgain { get; private set; }

    public WhatsNewWindow(string version, string notes)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        TitleText.Text = $"What's New in v{version}";
        NotesText.Text = notes;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DontShowAgain = DontShowAgainToggle.IsChecked == true;
        Close();
    }
}
