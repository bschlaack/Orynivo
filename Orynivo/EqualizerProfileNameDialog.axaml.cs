using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Collects a unique name for a new equalizer profile.</summary>
internal partial class EqualizerProfileNameDialog : Window
{
    private readonly HashSet<string> _existingNames;

    /// <summary>Initializes the dialog with the names that are already in use.</summary>
    /// <param name="existingNames">Existing equalizer profile names.</param>
    internal EqualizerProfileNameDialog(IEnumerable<string> existingNames)
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    /// <summary>Gets the confirmed equalizer profile name.</summary>
    internal string? ProfileName { get; private set; }

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        ValidateName();

    private void NameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ValidateName())
            Confirm();
    }

    private void CreateButton_OnClick(object? sender, RoutedEventArgs e) => Confirm();

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private bool ValidateName()
    {
        var name = NameTextBox.Text?.Trim();
        var duplicate = !string.IsNullOrEmpty(name) && _existingNames.Contains(name);
        CreateButton.IsEnabled = !string.IsNullOrEmpty(name) && !duplicate;
        ValidationTextBlock.Text = duplicate
            ? LocalizationManager.Current.EqualizerNameExists
            : string.Empty;
        return CreateButton.IsEnabled;
    }

    private void Confirm()
    {
        if (!ValidateName())
            return;
        ProfileName = NameTextBox.Text!.Trim();
        Close(true);
    }
}
