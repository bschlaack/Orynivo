using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Orynivo;

public partial class NewPlaylistDialog : Window
{
    public string? PlaylistName { get; private set; }

    public NewPlaylistDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
        };
    }

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        => CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void NameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(NameTextBox.Text))
            Confirm();
    }

    private void CreateButton_OnClick(object? sender, RoutedEventArgs e) => Confirm();

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm()
    {
        var name = NameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        PlaylistName = name;
        Close(true);
    }
}
