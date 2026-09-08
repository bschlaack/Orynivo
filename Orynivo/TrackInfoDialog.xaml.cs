using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Orynivo;

/// <summary>Displays the complete metadata available for one track.</summary>
public partial class TrackInfoDialog : Window
{
    /// <summary>One localized label/value pair displayed in the track information dialog.</summary>
    public sealed record TrackInfoEntry(string Label, string Value);

    /// <summary>Gets the localized dialog title.</summary>
    public string DialogTitle { get; }

    /// <summary>Gets the metadata rows in display order.</summary>
    public IReadOnlyList<TrackInfoEntry> Rows { get; }

    /// <summary>Creates a track information dialog.</summary>
    /// <param name="title">Localized dialog title.</param>
    /// <param name="rows">Metadata rows, with the physical path first.</param>
    public TrackInfoDialog(string title, IReadOnlyList<TrackInfoEntry> rows)
    {
        DialogTitle = title;
        Rows = rows;
        InitializeComponent();
        DataContext = this;
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
                Close();
        };
    }

    /// <summary>Initializes an empty instance for Avalonia tooling.</summary>
    public TrackInfoDialog()
        : this(string.Empty, [])
    {
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
