using Avalonia.Controls;
using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Reviews MusicBrainz release candidates for one physical album folder.</summary>
public partial class MetadataRepairDialog : Window
{
    private readonly MetadataFolderCandidate _candidate;
    private List<MetadataReleaseMatch> _matches = [];

    /// <summary>Initializes an empty designer instance.</summary>
    public MetadataRepairDialog()
        : this(new MetadataFolderCandidate(string.Empty, [], 0, 0, 0, 0, false))
    {
    }

    /// <summary>Initializes the review dialog for a physical folder candidate.</summary>
    /// <param name="candidate">Folder candidate to identify.</param>
    public MetadataRepairDialog(MetadataFolderCandidate candidate)
    {
        _candidate = candidate;
        InitializeComponent();
        Title = LocalizationManager.Current.MetadataReviewTitle;
        DataContext = new
        {
            FolderPath = candidate.FolderPath,
            FoundReleasesLabel = LocalizationManager.Current.MetadataFoundReleases,
            CancelLabel = LocalizationManager.Current.Cancel,
            ApplyLabel = LocalizationManager.Current.MetadataApplyCorrection
        };
        CurrentTracksDataGrid.ItemsSource = candidate.Tracks;
        AlbumQueryLabel.Text = LocalizationManager.Current.MetadataAlbumQuery;
        ArtistQueryLabel.Text = LocalizationManager.Current.MetadataArtistQuery;
        SearchAgainButton.Content = LocalizationManager.Current.SearchAgain;
        AlbumQueryTextBox.Text = MostCommon(candidate.Tracks.Select(track => track.Album));
        ArtistQueryTextBox.Text = MostCommon(candidate.Tracks.Select(track => track.AlbumArtist));
        Opened += async (_, _) => await SearchAsync();
    }

    /// <summary>Gets the MusicBrainz match confirmed by the user.</summary>
    public MetadataReleaseMatch? SelectedMatch { get; private set; }

    private async Task SearchAsync()
    {
        SearchAgainButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.MetadataSearching;
        var searchFailed = false;
        try
        {
            _matches = await LibraryMetadataRepairService.LookupAsync(
                _candidate,
                AlbumQueryTextBox.Text,
                ArtistQueryTextBox.Text);
        }
        catch
        {
            _matches = [];
            searchFailed = true;
        }

        ReleaseListBox.ItemsSource = _matches.Select(match =>
        {
            var confidence = Math.Round(match.Confidence * 100);
            var disc = match.MediumCount > 1 ? $" · CD {match.MediumPosition}/{match.MediumCount}" : string.Empty;
            var year = match.Year.HasValue ? $" · {match.Year}" : string.Empty;
            return $"{match.Title} — {match.AlbumArtist}{year}{disc} · {confidence:0}%";
        }).ToList();
        StatusTextBlock.Text = _matches.Count == 0
            ? searchFailed
                ? LocalizationManager.Current.MetadataSearchFailed
                : LocalizationManager.Current.MetadataNoMatch
            : string.Empty;
        if (_matches.Count > 0)
            ReleaseListBox.SelectedIndex = 0;
        SearchAgainButton.IsEnabled = true;
    }

    private async void SearchAgainButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchAsync();

    private void ReleaseListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = ReleaseListBox.SelectedIndex;
        ApplyButton.IsEnabled = index >= 0 && index < _matches.Count;
        if (!ApplyButton.IsEnabled)
        {
            SelectionDetailTextBlock.Text = string.Empty;
            return;
        }
        var match = _matches[index];
        SelectionDetailTextBlock.Text =
            $"{match.Tracks.Count} {LocalizationManager.Current.MetadataTrackCount} · " +
            $"{Math.Round(match.Confidence * 100):0}%";
    }

    private void ApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var index = ReleaseListBox.SelectedIndex;
        if (index < 0 || index >= _matches.Count)
            return;
        SelectedMatch = _matches[index];
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private static string MostCommon(IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;
}
