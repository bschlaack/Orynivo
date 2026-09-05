using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Collects a new profile name and the personal-data migration choice.</summary>
internal partial class UserProfileDialog : Window
{
    /// <summary>Gets the requested profile name.</summary>
    internal string ProfileName { get; private set; } = string.Empty;

    /// <summary>Gets whether the existing personal data should be copied to the new profile.</summary>
    internal bool MigrateFavorites => MigrateFavoritesCheckBox.IsChecked == true;

    /// <summary>Initializes the profile creation dialog.</summary>
    internal UserProfileDialog(bool showMigration = true)
    {
        InitializeComponent();
        MigrateFavoritesCheckBox.IsVisible = showMigration;
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void CreateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ProfileName = NameTextBox.Text?.Trim() ?? string.Empty;
        if (ProfileName.Length > 0)
            Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
