using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ValheimControl.Services;
using ValheimControl.Theme;

namespace ValheimControl;

public partial class UsersRolesWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly (string Key, string Label)[] AllPermissions =
    [
        ("server.start", "Start Server"),
        ("server.restart", "Restart Server"),
        ("server.stop", "Stop Server"),
        ("backup.run", "Run Manual Backup"),
        ("backup.restart", "Backup + Restart"),
        ("backup.reboot", "Backup + Reboot Host"),
        ("worlds.swap", "Swap / Create World"),
        ("worlds.delete", "Delete World"),
        ("config.write", "Edit Config Files"),
        ("update.run", "Run Server Update"),
        ("backupsettings.write", "Change Backup Settings"),
    ];

    private readonly ApiClient _api;
    private readonly Dictionary<string, System.Windows.Controls.Primitives.ToggleButton> _permissionToggles = new();
    private RoleDto[] _roles = [];
    private int? _editingRoleId;

    public UsersRolesWindow(ApiClient api)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _api = api;
        BuildPermissionToggles();
        Loaded += async (_, _) => await LoadRolesAsync();
    }

    private void BuildPermissionToggles()
    {
        PermissionsPanel.Children.Clear();
        _permissionToggles.Clear();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < AllPermissions.Length; i++)
        {
            var (key, label) = AllPermissions[i];
            var row = i / 2;
            while (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var toggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("SwitchToggleStyle"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            cell.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 150 });
            cell.Children.Add(toggle);

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, i % 2);
            grid.Children.Add(cell);

            _permissionToggles[key] = toggle;
        }

        PermissionsPanel.Children.Add(grid);
    }

    // ------------------------------------------------------------------
    // View switching
    // ------------------------------------------------------------------

    private void RolesViewButton_Click(object sender, RoutedEventArgs e)
    {
        RolesPanel.Visibility = Visibility.Visible;
        UsersPanel.Visibility = Visibility.Collapsed;
    }

    private async void UsersViewButton_Click(object sender, RoutedEventArgs e)
    {
        RolesPanel.Visibility = Visibility.Collapsed;
        UsersPanel.Visibility = Visibility.Visible;
        await LoadUsersAsync();
    }

    // ------------------------------------------------------------------
    // Roles
    // ------------------------------------------------------------------

    private async Task LoadRolesAsync()
    {
        var result = await _api.GetAsync("/api/roles");
        if (!result.Success)
        {
            RoleStatusText.Text = $"Failed to load roles: {result.Output}";
            return;
        }

        var parsed = JsonSerializer.Deserialize<RolesResponse>(result.Output, JsonOptions);
        _roles = parsed?.Roles ?? [];

        RoleList.Items.Clear();
        foreach (var role in _roles)
        {
            RoleList.Items.Add(new ListBoxItem
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = role.Name },
                        new TextBlock
                        {
                            Text = $"{role.Permissions.Length} permission(s)",
                            Style = (Style)FindResource("SubtitleTextStyle"),
                            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                            FontSize = 10
                        }
                    }
                },
                Padding = new Thickness(12, 8, 12, 8),
                Tag = role.Id
            });
        }

        ClearRoleForm();
    }

    private void RoleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoleList.SelectedItem is not ListBoxItem { Tag: int roleId }) return;

        var role = _roles.FirstOrDefault(r => r.Id == roleId);
        if (role is null) return;

        _editingRoleId = role.Id;
        RoleNameBox.Text = role.Name;
        RoleNameBox.IsEnabled = true;
        PermissionsPanel.IsEnabled = true;

        foreach (var (key, toggle) in _permissionToggles)
        {
            toggle.IsChecked = role.Permissions.Contains(key);
        }

        SaveRoleButton.IsEnabled = true;
        DeleteRoleButton.IsEnabled = true;
        RoleStatusText.Text = "";
    }

    private void NewRoleButton_Click(object sender, RoutedEventArgs e)
    {
        RoleList.SelectedItem = null;
        _editingRoleId = null;
        RoleNameBox.Text = "";
        RoleNameBox.IsEnabled = true;
        PermissionsPanel.IsEnabled = true;

        foreach (var toggle in _permissionToggles.Values) toggle.IsChecked = false;

        SaveRoleButton.IsEnabled = true;
        DeleteRoleButton.IsEnabled = false;
        RoleStatusText.Text = "Creating a new role";
    }

    private void ClearRoleForm()
    {
        RoleList.SelectedItem = null;
        _editingRoleId = null;
        RoleNameBox.Text = "";
        RoleNameBox.IsEnabled = false;
        PermissionsPanel.IsEnabled = false;
        foreach (var toggle in _permissionToggles.Values) toggle.IsChecked = false;
        SaveRoleButton.IsEnabled = false;
        DeleteRoleButton.IsEnabled = false;
    }

    private async void SaveRoleButton_Click(object sender, RoutedEventArgs e)
    {
        var name = RoleNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            RoleStatusText.Text = "Role name is required.";
            return;
        }

        var selectedPermissions = _permissionToggles
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToArray();

        SaveRoleButton.IsEnabled = false;
        RoleStatusText.Text = "Saving...";

        var body = new { name, permissions = selectedPermissions };
        var result = _editingRoleId is int id
            ? await _api.PutAsync($"/api/roles/{id}", body)
            : await _api.PostAsync("/api/roles", body);

        if (!result.Success)
        {
            RoleStatusText.Text = $"Failed: {result.Output}";
            SaveRoleButton.IsEnabled = true;
            return;
        }

        RoleStatusText.Text = "Saved.";
        await LoadRolesAsync();
    }

    private async void DeleteRoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRoleId is not int id) return;

        var confirm = MessageBox.Show(
            $"Delete the '{RoleNameBox.Text}' role? Anyone currently assigned to it will lose it - " +
            "they'll keep only universally-allowed actions until reassigned to a different role.",
            "Confirm Delete Role",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _api.DeleteAsync($"/api/roles/{id}");
        if (!result.Success)
        {
            RoleStatusText.Text = $"Failed: {result.Output}";
            return;
        }

        await LoadRolesAsync();
    }

    // ------------------------------------------------------------------
    // Users
    // ------------------------------------------------------------------

    private async Task LoadUsersAsync()
    {
        var rolesResult = await _api.GetAsync("/api/roles");
        var rolesParsed = rolesResult.Success
            ? JsonSerializer.Deserialize<RolesResponse>(rolesResult.Output, JsonOptions)?.Roles ?? []
            : [];
        _roles = rolesParsed;

        NewUserRoleBox.Items.Clear();
        NewUserRoleBox.Items.Add(new ComboBoxItem { Content = "(no role)", Tag = null });
        foreach (var role in _roles)
        {
            NewUserRoleBox.Items.Add(new ComboBoxItem { Content = role.Name, Tag = role.Id });
        }
        NewUserRoleBox.SelectedIndex = 0;

        var usersResult = await _api.GetAsync("/api/users");
        if (!usersResult.Success)
        {
            UserStatusText.Text = $"Failed to load users: {usersResult.Output}";
            return;
        }

        var users = JsonSerializer.Deserialize<UsersResponse>(usersResult.Output, JsonOptions)?.Users ?? [];

        UsersListPanel.Children.Clear();
        foreach (var user in users)
        {
            UsersListPanel.Children.Add(BuildUserRow(user));
        }
    }

    private Border BuildUserRow(UserDto user)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var nameStack = new StackPanel();
        nameStack.Children.Add(new TextBlock { Text = user.Username, FontWeight = FontWeights.Bold });
        nameStack.Children.Add(new TextBlock
        {
            Text = user.IsOwner ? "Owner" : (user.RoleName ?? "No role assigned"),
            Style = (Style)FindResource("SubtitleTextStyle"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            FontSize = 10
        });
        Grid.SetColumn(nameStack, 0);
        row.Children.Add(nameStack);

        if (user.IsOwner)
        {
            var ownerLabel = new TextBlock
            {
                Text = "Full access - can't be changed here",
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("SubtitleTextStyle"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush")
            };
            Grid.SetColumn(ownerLabel, 1);
            Grid.SetColumnSpan(ownerLabel, 2);
            row.Children.Add(ownerLabel);
        }
        else
        {
            var roleBox = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
            roleBox.Items.Add(new ComboBoxItem { Content = "(no role)", Tag = null });
            foreach (var role in _roles)
            {
                roleBox.Items.Add(new ComboBoxItem { Content = role.Name, Tag = role.Id });
            }
            roleBox.SelectedIndex = 0;
            foreach (ComboBoxItem item in roleBox.Items)
            {
                if (item.Tag is int roleId && roleId == user.RoleId) roleBox.SelectedItem = item;
            }

            roleBox.SelectionChanged += async (_, _) =>
            {
                if (roleBox.SelectedItem is not ComboBoxItem { Tag: var tag }) return;
                var newRoleId = tag as int?;
                await _api.PutAsync($"/api/users/{user.Id}/role", new { roleId = newRoleId });
            };
            Grid.SetColumn(roleBox, 1);
            row.Children.Add(roleBox);

            var deleteButton = new Button
            {
                Content = "DELETE",
                Style = (Style)FindResource("GhostButtonStyle"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteButton.Click += async (_, _) =>
            {
                var confirm = MessageBox.Show(
                    $"Delete the account '{user.Username}'? This can't be undone.",
                    "Confirm Delete User", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                await _api.DeleteAsync($"/api/users/{user.Id}");
                await LoadUsersAsync();
            };
            Grid.SetColumn(deleteButton, 2);
            row.Children.Add(deleteButton);
        }

        return new Border
        {
            Child = row,
            Background = (System.Windows.Media.Brush)FindResource("BgTitleBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderMutedBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private async void CreateUserButton_Click(object sender, RoutedEventArgs e)
    {
        var username = NewUsernameBox.Text.Trim();
        var password = NewUserPasswordBox.Password;
        var roleId = NewUserRoleBox.SelectedItem is ComboBoxItem { Tag: int rid } ? rid : (int?)null;

        if (string.IsNullOrWhiteSpace(username))
        {
            UserStatusText.Text = "Username is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            UserStatusText.Text = "Password must be at least 6 characters.";
            return;
        }

        UserStatusText.Text = "Creating...";
        var result = await _api.PostAsync("/api/users", new { username, password, roleId });

        if (!result.Success)
        {
            UserStatusText.Text = $"Failed: {result.Output}";
            return;
        }

        NewUsernameBox.Text = "";
        NewUserPasswordBox.Password = "";
        NewUserRoleBox.SelectedIndex = 0;
        UserStatusText.Text = "User created.";
        await LoadUsersAsync();
    }

    private record RoleDto(int Id, string Name, string[] Permissions);
    private record UserDto(int Id, string Username, bool IsOwner, int? RoleId, string? RoleName);
    private record RolesResponse(RoleDto[] Roles);
    private record UsersResponse(UserDto[] Users);
}
