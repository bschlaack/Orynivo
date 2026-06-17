using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Orynivo;

public partial class EditArtistNameDialog : Window
{
    public string ArtistName => NameTextBox.Text?.Trim() ?? string.Empty;

    /// <summary>
    /// Initializes a runtime-loader instance with an empty artist name.
    /// </summary>
    public EditArtistNameDialog()
        : this(string.Empty)
    {
    }

    public EditArtistNameDialog(string artistName)
    {
        InitializeComponent();
        NameTextBox.Text = artistName;
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
        };
    }

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        => RenameButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void NameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RenameButton.IsEnabled)
            Close(true);
    }

    private void RenameButton_OnClick(object? sender, RoutedEventArgs e) => Close(true);
    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
