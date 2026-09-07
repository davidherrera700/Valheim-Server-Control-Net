using System.Windows;
using System.Windows.Controls;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class SwapWorldWindow : Window
{
    private const string StartServerFile = "start_server.sh";

    private readonly SshService _ssh;
    private string _currentContent = "";
    private string _currentWorld = "";

    public SwapWorldWindow(AppConfig config)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _ssh = new SshService(config);
        Loaded += SwapWorldWindow_Loaded;
        WorldPicker.SelectionChanged += (_, _) => SwapButton.IsEnabled = WorldPicker.SelectedItem is string;
    }

    private async void SwapWorldWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Loading...";
        WorldPicker.IsEnabled = false;

        var scriptResult = await _ssh.ReadConfigFileAsync(StartServerFile);
        if (!scriptResult.Success)
        {
            StatusText.Text = $"Couldn't load current configuration: {scriptResult.Output}";
            return;
        }

        _currentContent = scriptResult.Output == "(no output)" ? "" : scriptResult.Output;
        _currentWorld = LaunchLineParser.GetQuotedValue(_currentContent, "world") ?? "(unknown)";
        CurrentWorldText.Text = _currentWorld;

        var worldsResult = await _ssh.ListWorldsAsync();
        WorldPicker.Items.Clear();

        if (worldsResult.Success && worldsResult.Output != "(no output)")
        {
            var worlds = worldsResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var world in worlds)
            {
                if (!string.Equals(world, _currentWorld, StringComparison.OrdinalIgnoreCase))
                {
                    WorldPicker.Items.Add(world);
                }
            }
        }

        if (WorldPicker.Items.Count == 0)
        {
            WorldPicker.Items.Add("(no other worlds found)");
        }

        WorldPicker.IsEnabled = true;
        StatusText.Text = "";
    }

    private async void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorldPicker.SelectedItem is not string newWorld) return;

        var confirm = MessageBox.Show(
            $"This switches the server from world '{_currentWorld}' to '{newWorld}'.\n\n" +
            "This doesn't delete anything - the current world's save stays right where it is. " +
            "The server will load the new world after its next restart.\n\n" +
            "Strongly recommended: run a Manual Backup before restarting, just in case.\n\n" +
            "Save this change?",
            "Confirm World Swap",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SwapButton.IsEnabled = false;
        WorldPicker.IsEnabled = false;
        StatusText.Text = "Saving...";

        var updatedContent = LaunchLineParser.SetQuotedValue(_currentContent, "world", newWorld);
        var writeResult = await _ssh.WriteConfigFileAsync(StartServerFile, updatedContent);

        if (!writeResult.Success)
        {
            StatusText.Text = $"Save failed: {writeResult.Output}";
            SwapButton.IsEnabled = true;
            WorldPicker.IsEnabled = true;
            return;
        }

        _currentContent = updatedContent;
        StatusText.Text = "Saved.";

        var restartNow = MessageBox.Show(
            "World saved. Restart the server now to apply it? Anyone currently online will be disconnected.\n\n" +
            "(You can say no and restart later from the main window instead.)",
            "Restart Now?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (restartNow == MessageBoxResult.Yes)
        {
            StatusText.Text = "Restarting server...";
            var restartResult = await _ssh.RestartServiceAsync();
            StatusText.Text = restartResult.Success
                ? "Restarted. The main window will pick up the new world on its next refresh."
                : $"Restart failed: {restartResult.Output}";
        }

        SwapButton.IsEnabled = false;
        CancelButton.Content = "CLOSE";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
