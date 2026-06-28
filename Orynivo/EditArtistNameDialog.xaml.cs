using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// Modal dialog for renaming an artist. Owns the complete confirmation lifecycle:
/// the supplied rename callback runs before the dialog closes, and collisions
/// are returned to the caller for an explicit merge decision.
/// </summary>
public partial class EditArtistNameDialog : Window
{
    private readonly long _artistId;
    private readonly Func<long, string, Task<(ArtistRenameResult? Result, ArtistInfo? MatchingArtist)>> _commitAsync;
    private string _artistName = string.Empty;

    /// <summary>Gets the trimmed artist name captured when the dialog was confirmed.</summary>
    public string ArtistName => _artistName;

    /// <summary>Gets the committed rename result, or <see langword="null"/> when a name collision was detected.</summary>
    public ArtistRenameResult? Result { get; private set; }

    /// <summary>Gets the colliding artist when the entered name is already taken, or <see langword="null"/> when no collision exists.</summary>
    public ArtistInfo? MatchingArtist { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance with an empty artist name.
    /// </summary>
    public EditArtistNameDialog()
        : this(0, string.Empty)
    {
    }

    /// <summary>
    /// Initializes the dialog for the given artist.
    /// </summary>
    /// <param name="artistId">Database ID of the artist to rename.</param>
    /// <param name="artistName">Current display name shown in the text field.</param>
    public EditArtistNameDialog(long artistId, string artistName)
        : this(artistId, artistName, CommitLocalRenameAsync)
    {
    }

    /// <summary>
    /// Initializes the dialog for the given artist using a caller-provided commit callback.
    /// </summary>
    /// <param name="artistId">Database ID of the artist to rename.</param>
    /// <param name="artistName">Current display name shown in the text field.</param>
    /// <param name="commitAsync">Callback that commits the rename or reports a matching artist.</param>
    public EditArtistNameDialog(
        long artistId,
        string artistName,
        Func<long, string, Task<(ArtistRenameResult? Result, ArtistInfo? MatchingArtist)>> commitAsync)
    {
        _artistId = artistId;
        _commitAsync = commitAsync;
        InitializeComponent();
        NameTextBox.Text = artistName;
        _artistName = artistName.Trim();
        RenameButton.IsEnabled = _artistName.Length > 0;
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        => RenameButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void NameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RenameButton.IsEnabled)
        {
            e.Handled = true;
            CommitAsync();
        }
    }

    private void RenameButton_OnClick(object? sender, RoutedEventArgs e) => CommitAsync();

    private async void CommitAsync()
    {
        var name = NameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        RenameButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        NameTextBox.IsEnabled = false;
        StatusTextBlock.Text = Localization.LocalizationManager.Current.ArtistInfoLoading;
        StatusTextBlock.IsVisible = true;
        try
        {
            var (result, matching) = await _commitAsync(_artistId, name);
            Result = result;
            MatchingArtist = matching;
            _artistName = name;
            Close(true);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Artist rename");
            StatusTextBlock.Text = Localization.LocalizationManager.Current.ArtistRenameFailed;
            StatusTextBlock.IsVisible = true;
            RenameButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            NameTextBox.IsEnabled = true;
            NameTextBox.Focus();
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private static Task<(ArtistRenameResult? Result, ArtistInfo? MatchingArtist)> CommitLocalRenameAsync(
        long artistId,
        string artistName)
        => Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            var match = db.FindArtistByName(artistName, artistId);
            if (match is not null)
                return ((ArtistRenameResult?)null, match);
            return ((ArtistRenameResult?)db.RenameArtist(artistId, artistName), (ArtistInfo?)null);
        });
}
