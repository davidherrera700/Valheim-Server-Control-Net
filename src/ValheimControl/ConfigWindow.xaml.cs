using System.Windows;
using System.Windows.Controls;
using ValheimControl.Models;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class ConfigWindow : Window
{
    private const string StartServerFile = "start_server.sh";

    private readonly AppConfig _config;
    private readonly SshService _ssh;
    private string? _selectedFile;
    private string _originalContent = "";
    private string _currentContent = "";
    private bool _formView = true;
    private bool _suppressChangeEvents;
    private List<string> _knownWorlds = new();

    public ConfigWindow(AppConfig config)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _config = config;
        _ssh = new SshService(config);
        Loaded += ConfigWindow_Loaded;
    }

    private static readonly Dictionary<string, string> FileDescriptions = new()
    {
        ["start_server.sh"] = "Launch settings & world modifiers",
        ["adminlist.txt"] = "Admin permissions",
        ["permittedlist.txt"] = "Whitelist",
        ["bannedlist.txt"] = "Blocklist",
    };

    private async void ConfigWindow_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var file in SshService.AllowedConfigFiles)
        {
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = file });

            if (FileDescriptions.TryGetValue(file, out var subtitle))
            {
                row.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Style = (Style)FindResource("SubtitleTextStyle"),
                    FontSize = 10,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            FileList.Items.Add(new ListBoxItem
            {
                Content = row,
                Padding = new Thickness(16, 11, 16, 11),
                Tag = file
            });
        }

        if (FileList.Items.Count > 0)
        {
            FileList.SelectedIndex = 0; // triggers FileList_SelectionChanged, loading it automatically
        }

        await LoadWorldPickerAsync();
    }

    private async Task LoadWorldPickerAsync()
    {
        WorldPicker.Items.Clear();
        WorldPicker.Items.Add("Loading...");
        WorldPicker.SelectedIndex = 0;
        WorldPicker.IsEnabled = false;

        var result = await _ssh.ListWorldsAsync();
        WorldPicker.Items.Clear();
        _knownWorlds.Clear();

        if (result.Success && result.Output != "(no output)")
        {
            var worlds = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var world in worlds)
            {
                WorldPicker.Items.Add(world);
                _knownWorlds.Add(world);
            }
        }

        if (WorldPicker.Items.Count == 0)
        {
            WorldPicker.Items.Add("(no worlds found)");
        }

        WorldPicker.SelectedIndex = 0;
        WorldPicker.IsEnabled = true;
    }

    private void SwapWorldButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorldPicker.SelectedItem is not string selected) return;
        if (selected == "Loading..." || selected == "(no worlds found)") return;

        WorldBox.Text = selected;
    }

    private void CreateWorldButton_Click(object sender, RoutedEventArgs e)
    {
        var newName = NewWorldNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show("Enter a name for the new world first.", "New World",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_knownWorlds.Any(w => string.Equals(w, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                $"A world named '{newName}' already exists on the server. Use Swap instead if you want to switch to it.",
                "World Already Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var modifiersDialog = new WorldModifiersWindow { Owner = this };
        if (modifiersDialog.ShowDialog() != true || !modifiersDialog.Applied)
        {
            return; // cancelled - don't create the world after all
        }

        _currentContent = modifiersDialog.ApplyTo(_currentContent);
        WorldBox.Text = newName;
        NewWorldNameBox.Text = "";
    }

    private async void DeleteWorldButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorldPicker.SelectedItem is not string selected) return;
        if (selected == "Loading..." || selected == "(no worlds found)") return;

        var currentActiveWorld = LaunchLineParser.GetQuotedValue(_originalContent, "world");
        if (string.Equals(selected, currentActiveWorld, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"'{selected}' is the world currently configured to run. Swap to a different world, save, and restart the server before deleting this one.",
                "Can't Delete Active World",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"This moves '{selected}'s save files on the server into an archive folder " +
            "(/opt/valheim/deleted_worlds) - it won't show up in this list anymore, but the files " +
            "aren't permanently erased and could be restored manually later if needed.\n\n" +
            $"Delete '{selected}'?",
            "Confirm Delete World",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (!await ConfirmDeletePasswordAsync()) return;

        DeleteWorldButton.IsEnabled = false;
        var result = await _ssh.DeleteWorldAsync(selected);
        DeleteWorldButton.IsEnabled = true;

        if (result.Success)
        {
            await LoadWorldPickerAsync();
            MessageBox.Show($"'{selected}' archived successfully.", "Deleted",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Delete failed: {result.Output}", "Delete Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Local "type to confirm" gate before a deletion actually runs - not a
    /// real security boundary (the SSH key already is one), just a guard
    /// against an accidental click. The password itself is shared across
    /// every PC controlling this server (stored as a hash server-side, not
    /// per-PC), so setting it up once here means it applies everywhere.
    /// </summary>
    private async Task<bool> ConfirmDeletePasswordAsync()
    {
        var statusCheck = await _ssh.VerifyDeletePasswordAsync("");
        var isConfigured = statusCheck.Success && statusCheck.Output.Trim() != "NOT_SET";

        if (!isConfigured)
        {
            var setupChoice = MessageBox.Show(
                "No delete confirmation password is set yet for this server. Set one now? " +
                "It'll be required on every PC using this app to control this server, not just this one.\n\n" +
                "(This is a safety net against accidental clicks, not real security - only a hash is " +
                "stored on the server, never the actual password. Choose No to skip and proceed with " +
                "this deletion anyway.)",
                "Set a Shared Delete Password?",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (setupChoice == MessageBoxResult.Cancel) return false;

            if (setupChoice == MessageBoxResult.Yes)
            {
                var setupDialog = new PasswordPromptWindow(
                    "Enter a password that will be required before any world deletion, on any PC using this app.",
                    "Set Delete Password") { Owner = this };

                if (setupDialog.ShowDialog() != true || string.IsNullOrEmpty(setupDialog.EnteredPassword))
                    return false;

                var setResult = await _ssh.SetDeletePasswordAsync(setupDialog.EnteredPassword);
                if (!setResult.Success)
                {
                    MessageBox.Show($"Failed to set password: {setResult.Output}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                // Fall through - also require it for this deletion, for consistency.
            }
            else
            {
                return true; // proceed without a password this one time
            }
        }

        var dialog = new PasswordPromptWindow(
            "Enter the delete confirmation password to proceed.",
            "Confirm Deletion") { Owner = this };

        if (dialog.ShowDialog() != true) return false;

        var verifyResult = await _ssh.VerifyDeletePasswordAsync(dialog.EnteredPassword ?? "");
        if (!verifyResult.Success || verifyResult.Output.Trim() != "MATCH")
        {
            MessageBox.Show("Incorrect password. Deletion cancelled.", "Incorrect Password",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Loading a file
    // ------------------------------------------------------------------

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not ListBoxItem { Tag: string filename }) return;

        if (IsDirty())
        {
            var choice = MessageBox.Show(
                $"You have unsaved changes to {_selectedFile}. Discard them and switch files?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
            {
                // Revert the selection back to the file that was actually loaded.
                _suppressChangeEvents = true;
                foreach (ListBoxItem item in FileList.Items)
                {
                    if ((string)item.Tag == _selectedFile) { FileList.SelectedItem = item; break; }
                }
                _suppressChangeEvents = false;
                return;
            }
        }

        await LoadFileAsync(filename);
    }

    private async Task LoadFileAsync(string filename)
    {
        EmptyStateText.Visibility = Visibility.Collapsed;
        EditorArea.Visibility = Visibility.Visible;
        FileTitle.Text = filename;
        FileMeta.Text = "Loading...";
        SaveButton.IsEnabled = false;

        var result = await _ssh.ReadConfigFileAsync(filename);

        if (!result.Success)
        {
            FileMeta.Text = $"Failed to load: {result.Output}";
            return;
        }

        _selectedFile = filename;
        _originalContent = result.Output == "(no output)" ? "" : result.Output;
        _currentContent = _originalContent;

        var isStartServer = filename == StartServerFile;
        ViewSwitchPanel.Visibility = Visibility.Visible;
        RawViewLabel.Text = isStartServer ? "RAW LAUNCH SCRIPT" : "RAW FILE CONTENT";
        FileMeta.Text = "Loaded from server";

        _suppressChangeEvents = true;
        RawBox.Text = _currentContent;
        _suppressChangeEvents = false;

        // Default to Form view for every file - the launch script gets the
        // field-based editor, the three list files get the entry editor.
        ShowFormView();

        UpdateDirtyState();
    }

    // ------------------------------------------------------------------
    // Form <-> Raw view switching
    // ------------------------------------------------------------------

    private void FormViewButton_Click(object sender, RoutedEventArgs e) => ShowFormView();

    private void RawViewButton_Click(object sender, RoutedEventArgs e) => ShowRawView();

    private void ShowFormView()
    {
        _suppressChangeEvents = true;

        if (_selectedFile == StartServerFile)
        {
            PopulateFormFields(_currentContent);
            FormFieldsPanel.Visibility = Visibility.Visible;
            ListEditorPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            PopulateListEditor(_currentContent);
            FormFieldsPanel.Visibility = Visibility.Collapsed;
            ListEditorPanel.Visibility = Visibility.Visible;
        }

        _suppressChangeEvents = false;

        _formView = true;
        RawViewPanel.Visibility = Visibility.Collapsed;
        FormViewButton.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentGoldBrush");
        RawViewButton.ClearValue(Button.BorderBrushProperty);
    }

    private void ShowRawView()
    {
        _suppressChangeEvents = true;
        RawBox.Text = _currentContent;
        _suppressChangeEvents = false;

        _formView = false;
        FormFieldsPanel.Visibility = Visibility.Collapsed;
        ListEditorPanel.Visibility = Visibility.Collapsed;
        RawViewPanel.Visibility = Visibility.Visible;
        RawViewButton.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentGoldBrush");
        FormViewButton.ClearValue(Button.BorderBrushProperty);
    }

    private void PopulateFormFields(string content)
    {
        ServerNameBox.Text = LaunchLineParser.GetQuotedValue(content, "name") ?? "";
        WorldBox.Text = LaunchLineParser.GetQuotedValue(content, "world") ?? "";
        PortBox.Text = LaunchLineParser.GetBareValue(content, "port") ?? "";
        WorldPasswordBox.Text = LaunchLineParser.GetQuotedValue(content, "password") ?? "";
        PublicToggle.IsChecked = LaunchLineParser.GetBareValue(content, "public") == "1";
        CrossplayToggle.IsChecked = LaunchLineParser.GetCrossplayEnabled(content);
    }

    // ------------------------------------------------------------------
    // List editor (adminlist.txt / permittedlist.txt / bannedlist.txt)
    // ------------------------------------------------------------------

    private List<string> _listComments = new();

    private void PopulateListEditor(string content)
    {
        var (comments, entries) = ListFileParser.Parse(content);
        _listComments = comments;

        ListEntriesPanel.Children.Clear();
        foreach (var entry in entries)
        {
            AddListEntryRow(entry);
        }
    }

    private void AddListEntryRow(string idValue)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox { Text = idValue, Margin = new Thickness(0, 0, 8, 0) };
        box.TextChanged += (_, _) =>
        {
            if (_suppressChangeEvents || !_formView) return;
            ApplyListEditorToContent();
            UpdateDirtyState();
        };
        Grid.SetColumn(box, 0);

        var removeButton = new Button
        {
            Content = "REMOVE",
            Style = (Style)FindResource("GhostButtonStyle"),
            Padding = new Thickness(10, 6, 10, 6)
        };
        Grid.SetColumn(removeButton, 1);
        removeButton.Click += (_, _) =>
        {
            ListEntriesPanel.Children.Remove(row);
            ApplyListEditorToContent();
            UpdateDirtyState();
        };

        row.Children.Add(box);
        row.Children.Add(removeButton);
        ListEntriesPanel.Children.Add(row);
    }

    private void AddEntryButton_Click(object sender, RoutedEventArgs e)
    {
        AddListEntryRow("");
        UpdateDirtyState();
    }

    private void ApplyListEditorToContent()
    {
        var entries = new List<string>();
        foreach (var child in ListEntriesPanel.Children)
        {
            if (child is Grid row && row.Children.Count > 0 && row.Children[0] is TextBox box)
            {
                var value = box.Text.Trim();
                if (!string.IsNullOrWhiteSpace(value)) entries.Add(value);
            }
        }

        _currentContent = ListFileParser.Build(_listComments, entries);
    }

    private void ApplyFormFieldsToContent()
    {
        var updated = _currentContent;
        updated = LaunchLineParser.SetQuotedValue(updated, "name", ServerNameBox.Text);
        updated = LaunchLineParser.SetQuotedValue(updated, "world", WorldBox.Text);
        updated = LaunchLineParser.SetBareValue(updated, "port", PortBox.Text);
        updated = LaunchLineParser.SetQuotedValue(updated, "password", WorldPasswordBox.Text);
        updated = LaunchLineParser.SetBareValue(updated, "public", PublicToggle.IsChecked == true ? "1" : "0");
        updated = LaunchLineParser.SetCrossplayEnabled(updated, CrossplayToggle.IsChecked == true);
        _currentContent = updated;
    }

    // ------------------------------------------------------------------
    // Change tracking
    // ------------------------------------------------------------------

    private void FormField_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents || !_formView) return;
        ApplyFormFieldsToContent();
        UpdateDirtyState();
    }

    private void RawBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChangeEvents || _formView) return;
        _currentContent = RawBox.Text;
        UpdateDirtyState();
    }

    private bool IsDirty() => _currentContent != _originalContent;

    private static bool HasAnyModifiers(string content) =>
        LaunchLineParser.GetPreset(content) is not null ||
        LaunchLineParser.GetModifier(content, "combat") is not null ||
        LaunchLineParser.GetModifier(content, "deathpenalty") is not null ||
        LaunchLineParser.GetModifier(content, "resources") is not null ||
        LaunchLineParser.GetModifier(content, "raids") is not null ||
        LaunchLineParser.GetModifier(content, "portals") is not null ||
        LaunchLineParser.GetSetKey(content, "nomap") ||
        LaunchLineParser.GetSetKey(content, "playerevents") ||
        LaunchLineParser.GetSetKey(content, "passivemobs") ||
        LaunchLineParser.GetSetKey(content, "nobuildcost");

    private void UpdateDirtyState()
    {
        var dirty = IsDirty();
        DirtyNotice.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = dirty;
    }

    // ------------------------------------------------------------------
    // Save / Revert
    // ------------------------------------------------------------------

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFile is null) return;

        if (_formView)
        {
            if (_selectedFile == StartServerFile) ApplyFormFieldsToContent();
            else ApplyListEditorToContent();
        }

        var isStartServer = _selectedFile == StartServerFile;
        var worldChanged = isStartServer &&
            LaunchLineParser.GetQuotedValue(_originalContent, "world") != LaunchLineParser.GetQuotedValue(_currentContent, "world");

        string confirmMessage;
        if (worldChanged)
        {
            var oldWorld = LaunchLineParser.GetQuotedValue(_originalContent, "world");
            var newWorld = LaunchLineParser.GetQuotedValue(_currentContent, "world") ?? "";
            var isExistingWorld = _knownWorlds.Any(w => string.Equals(w, newWorld, StringComparison.OrdinalIgnoreCase));

            confirmMessage = isExistingWorld
                ? $"This switches the server from world '{oldWorld}' to the existing world '{newWorld}'.\n\n" +
                  "This doesn't delete anything - the old world's save files stay right where they are. " +
                  "The server will load the new world after its next restart.\n\n" +
                  "Strongly recommended: run a Manual Backup from the main window before restarting, " +
                  "just in case.\n\nSave this change?"
                : $"This switches the server from world '{oldWorld}' to '{newWorld}', which doesn't exist yet.\n\n" +
                  "Valheim will generate a brand-new world the first time the server starts with this name" +
                  (HasAnyModifiers(_currentContent) ? ", using the modifiers you configured" : "") +
                  ". The old world isn't touched or deleted.\n\nSave this change?";
        }
        else
        {
            confirmMessage = isStartServer
                ? "Save changes to start_server.sh? A previous version is kept as a timestamped backup on the server, but the running server won't use the new settings until it's restarted."
                : $"Save changes to {_selectedFile}?";
        }

        var confirm = MessageBox.Show(
            confirmMessage,
            "Confirm Save",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SaveButton.IsEnabled = false;
        FileMeta.Text = "Saving...";

        var result = await _ssh.WriteConfigFileAsync(_selectedFile, _currentContent);

        if (result.Success)
        {
            _originalContent = _currentContent;
            FileMeta.Text = "Saved";
            UpdateDirtyState();

            if (isStartServer)
            {
                await OfferRestartAsync();
            }
        }
        else
        {
            FileMeta.Text = $"Save failed: {result.Output}";
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// After a successful save to start_server.sh, offers to restart the
    /// server right away so the change actually takes effect - saving the
    /// file alone doesn't do anything until the process restarts and
    /// re-reads its launch arguments.
    /// </summary>
    private async Task OfferRestartAsync()
    {
        var restartConfirm = MessageBox.Show(
            "Restart the server now to apply this change? Anyone currently online will be disconnected.\n\n" +
            "(You can also say no here and restart later from the main window instead.)",
            "Restart Now?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (restartConfirm != MessageBoxResult.Yes)
        {
            FileMeta.Text = "Saved - restart the server manually when you're ready to apply this.";
            return;
        }

        FileMeta.Text = "Restarting server...";

        var restartResult = await _ssh.RestartServiceAsync();
        if (!restartResult.Success)
        {
            FileMeta.Text = $"Restart failed: {restartResult.Output}";
            return;
        }

        // Give it a few seconds before checking - matches the same pattern
        // the main window uses after Start/Restart/Stop.
        await Task.Delay(3000);
        var statusResult = await _ssh.CheckStatusAsync();
        var isOnline = statusResult.Output.Contains("ActiveState=active");

        FileMeta.Text = isOnline
            ? "Restarted - server is back online."
            : "Restart sent - check status on the main window.";

        // A world swap/create only actually exists on disk after the server
        // has started with it, so refresh the picker now that it has.
        await LoadWorldPickerAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty())
        {
            var choice = MessageBox.Show(
                "You have unsaved changes. Discard them and reload from the server?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes) return;
        }

        await LoadWorldPickerAsync();

        if (_selectedFile is not null)
        {
            await LoadFileAsync(_selectedFile);
        }
    }

    private async void ChangeDeletePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var statusCheck = await _ssh.VerifyDeletePasswordAsync("");
        var isConfigured = statusCheck.Success && statusCheck.Output.Trim() != "NOT_SET";

        if (isConfigured)
        {
            // Require proving you know the current password before allowing
            // a change - otherwise anyone could silently reset it and the
            // "confirm you meant it" barrier would mean nothing.
            var currentDialog = new PasswordPromptWindow(
                "Enter the current delete password to change it.",
                "Verify Current Password") { Owner = this };

            if (currentDialog.ShowDialog() != true) return;

            var verifyResult = await _ssh.VerifyDeletePasswordAsync(currentDialog.EnteredPassword ?? "");
            if (!verifyResult.Success || verifyResult.Output.Trim() != "MATCH")
            {
                MessageBox.Show("Incorrect current password. Password not changed.", "Incorrect Password",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var newDialog = new PasswordPromptWindow(
            isConfigured
                ? "Enter a new delete password. This replaces the current one on every PC using this app against this server."
                : "Enter a delete password to set for this server.",
            "Set Delete Password") { Owner = this };

        if (newDialog.ShowDialog() != true || string.IsNullOrEmpty(newDialog.EnteredPassword)) return;

        var setResult = await _ssh.SetDeletePasswordAsync(newDialog.EnteredPassword);
        if (setResult.Success)
        {
            MessageBox.Show(
                "Delete password updated. This applies to every PC using this app against this server.",
                "Password Changed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Failed to update password: {setResult.Output}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFile is null) return;

        _currentContent = _originalContent;

        _suppressChangeEvents = true;
        RawBox.Text = _currentContent;
        if (_selectedFile == StartServerFile) PopulateFormFields(_currentContent);
        else PopulateListEditor(_currentContent);
        _suppressChangeEvents = false;

        UpdateDirtyState();
    }
}
