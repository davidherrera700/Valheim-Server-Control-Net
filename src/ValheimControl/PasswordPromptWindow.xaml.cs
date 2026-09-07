using System.Windows;
using System.Windows.Input;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class PasswordPromptWindow : Window
{
    public string? EnteredPassword { get; private set; }

    public PasswordPromptWindow(string message, string title = "Confirm")
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        Title = title;
        MessageText.Text = message;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        EnteredPassword = PasswordInput.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, e);
        else if (e.Key == Key.Escape) Cancel_Click(sender, e);
    }
}
