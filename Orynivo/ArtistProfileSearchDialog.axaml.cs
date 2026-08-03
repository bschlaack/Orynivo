using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Orynivo;

/// <summary>Collects an editable external lookup name for an artist profile refresh.</summary>
internal partial class ArtistProfileSearchDialog : Window
{
    /// <summary>Initializes the dialog with the library artist name as the default query.</summary>
    /// <param name="artistName">Artist name currently stored in the library.</param>
    internal ArtistProfileSearchDialog(string artistName)
    {
        InitializeComponent();
        QueryTextBox.Text = artistName;
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            QueryTextBox.Focus();
            QueryTextBox.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    /// <summary>Gets the confirmed profile lookup query.</summary>
    internal string? Query { get; private set; }

    private void QueryTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Confirm();
    }

    private void LoadButton_OnClick(object? sender, RoutedEventArgs e) => Confirm();

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm()
    {
        var query = QueryTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;
        Query = query;
        Close(true);
    }
}
