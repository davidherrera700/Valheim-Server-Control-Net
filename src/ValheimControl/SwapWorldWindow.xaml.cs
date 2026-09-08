using System.Text.Json;
using System.Windows;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class SwapWorldWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ApiClient _api;
    private string _currentWorld = "";

    public SwapWorldWindow(ApiClient api)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _api = api;
        Loaded += SwapWorldWindow_Loaded;
        WorldPicker.SelectionChanged += (_, _) => SwapButton.IsEnabled = WorldPicker.SelectedItem is string;
    }

    private async void SwapWorldWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Loading...";
        WorldPicker.IsEnabled = false;

        var worldInfoResult = await _api.GetAsync("/api/world-info");
        _currentWorld = "(unknown)";
        if (worldInfoResult.Success)
        {
            try
            {
                var info = JsonSerializer.Deserialize<WorldInfoResponse>(worldInfoResult.Output, JsonOptions);
                _currentWorld = info?.World ?? "(unknown)";
            }
            catch
            {
                // leave _currentWorld as "(unknown)"
            }
        }
        CurrentWorldText.Text = _currentWorld;

        var worldsResult = await _api.GetAsync("/api/worlds");
        WorldPicker.Items.Clear();

        if (worldsResult.Success)
        {
            try
            {
                var worlds = JsonSerializer.Deserialize<WorldsResponse>(worldsResult.Output, JsonOptions)?.Worlds ?? [];
                foreach (var world in worlds)
                {
                    if (!string.Equals(world, _currentWorld, StringComparison.OrdinalIgnoreCase))
                    {
                        WorldPicker.Items.Add(world);
                    }
                }
            }
            catch
            {
                // leave the picker empty on an unexpected response shape
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

        var writeResult = await _api.PostAsync("/api/worlds/swap", new { world = newWorld });

        if (!writeResult.Success)
        {
            var message = writeResult.StatusCode == 403
                ? "Permission denied - your account doesn't have world-swap permission."
                : $"Save failed: {writeResult.Output}";
            StatusText.Text = message;
            SwapButton.IsEnabled = true;
            WorldPicker.IsEnabled = true;
            return;
        }

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
            var restartResult = await _api.PostAsync("/api/server/restart");
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

    private record WorldInfoResponse(string? World, long? UptimeSeconds);
    private record WorldsResponse(string[] Worlds);
}
