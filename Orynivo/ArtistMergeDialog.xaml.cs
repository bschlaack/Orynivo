using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Localization;

namespace Orynivo;

public partial class ArtistMergeDialog : Window
{
    private readonly long _currentArtistId;
    private readonly long _matchingArtistId;

    public long PreferredArtistId { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance without artist data.
    /// </summary>
    public ArtistMergeDialog()
        : this(0, string.Empty, 0, string.Empty)
    {
    }

    public ArtistMergeDialog(
        long currentArtistId,
        string currentArtistName,
        long matchingArtistId,
        string matchingArtistName)
    {
        InitializeComponent();
        _currentArtistId = currentArtistId;
        _matchingArtistId = matchingArtistId;
        MessageTextBlock.Text = string.Format(
            LocalizationManager.Current.ArtistNameExistsMessage,
            matchingArtistName);
        CurrentArtistButton.Content = string.Format(
            LocalizationManager.Current.KeepArtistProfile,
            currentArtistName);
        MatchingArtistButton.Content = string.Format(
            LocalizationManager.Current.KeepArtistProfile,
            matchingArtistName);
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
        };
    }

    private void CurrentArtistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PreferredArtistId = _currentArtistId;
        Close(true);
    }

    private void MatchingArtistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PreferredArtistId = _matchingArtistId;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
