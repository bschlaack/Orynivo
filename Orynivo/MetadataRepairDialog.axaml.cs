using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Reviews MusicBrainz release candidates for one physical album folder.</summary>
public partial class MetadataRepairDialog : Window
{
    private sealed record PreviewRow(int TrackNumber, string CurrentValues, string ProposedValues);

    private readonly MetadataFolderCandidate _candidate;
    private List<MetadataReleaseMatch> _matches = [];
    private CancellationTokenSource? _searchCts;
    private bool _closed;

    /// <summary>Initializes an empty designer instance.</summary>
    public MetadataRepairDialog()
        : this(new MetadataFolderCandidate(string.Empty, [], 0, 0, 0, 0, false))
    {
    }

    /// <summary>Initializes the review dialog for a physical folder candidate.</summary>
    /// <param name="candidate">Folder candidate to identify.</param>
    /// <param name="searchOnOpen">Whether opening starts a MusicBrainz query immediately.</param>
    public MetadataRepairDialog(MetadataFolderCandidate candidate, bool searchOnOpen = true)
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
        CurrentTracksDataGrid.ItemsSource = LibraryMetadataRepairService.OrderTracks(candidate.Tracks);
        AlbumQueryLabel.Text = LocalizationManager.Current.MetadataAlbumQuery;
        ArtistQueryLabel.Text = LocalizationManager.Current.MetadataArtistQuery;
        SearchAgainButton.Content = LocalizationManager.Current.SearchAgain;
        PreviewLabel.Text = LocalizationManager.Current.MetadataCorrectionPreview;
        AlbumQueryTextBox.Text = MostCommon(candidate.Tracks.Select(track => track.Album));
        ArtistQueryTextBox.Text = MostCommon(candidate.Tracks.Select(track => track.AlbumArtist));
        if (string.IsNullOrWhiteSpace(ArtistQueryTextBox.Text))
            ArtistQueryTextBox.Text = MostCommon(candidate.Tracks.Select(track => track.Artist));
        Closed += (_, _) => { _closed = true; _searchCts?.Cancel(); };
        if (searchOnOpen)
            Opened += async (_, _) => await SearchAsync();
    }

    /// <summary>Gets the MusicBrainz match confirmed by the user.</summary>
    public MetadataReleaseMatch? SelectedMatch { get; private set; }

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _searchCts = cts;
        var activity = new MetadataReviewActivity();
        activity.Report(new("search", 0, 0));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void UpdateProgress()
        {
            var snapshot = activity.Snapshot();
            StatusTextBlock.Text = snapshot.Text;
            SearchProgressBar.IsIndeterminate = !snapshot.Percent.HasValue;
            SearchProgressBar.Value = snapshot.Percent ?? 0;
        }
        timer.Tick += (_, _) => UpdateProgress();
        SearchProgressBar.IsVisible = true;
        UpdateProgress();
        timer.Start();
        ReleaseListBox.SelectedIndex = -1;
        ReleaseListBox.ItemsSource = null;
        PreviewDataGrid.ItemsSource = null;
        SelectionDetailTextBlock.Text = string.Empty;
        _matches = [];
        SearchAgainButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        var searchFailed = false;
        try
        {
            _matches = await LibraryMetadataRepairService.LookupAsync(
                _candidate,
                AlbumQueryTextBox.Text,
                ArtistQueryTextBox.Text, cts.Token, activity);
        }
        catch (OperationCanceledException) { return; }
        catch
        {
            _matches = [];
            searchFailed = true;
        }
        finally
        {
            timer.Stop();
            if (_searchCts == cts)
                _searchCts = null;
            if (!_closed)
            {
                SearchProgressBar.IsVisible = false;
                SearchAgainButton.IsEnabled = true;
            }
        }
        if (_closed)
            return;

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
            PreviewDataGrid.ItemsSource = null;
            return;
        }
        var match = _matches[index];
        var currentAlbum = MostCommon(_candidate.Tracks.Select(track => track.Album));
        var currentArtist = MostCommon(_candidate.Tracks.Select(track => track.AlbumArtist));
        SelectionDetailTextBlock.Text =
            $"{currentAlbum} — {currentArtist} → {match.Title} — {match.AlbumArtist} · " +
            $"{match.Tracks.Count} {LocalizationManager.Current.MetadataTrackCount} · " +
            $"{Math.Round(match.Confidence * 100):0}%";
        var localTracks = LibraryMetadataRepairService.OrderTracks(_candidate.Tracks);
        ApplyButton.IsEnabled = localTracks.Count == match.Tracks.Count;
        PreviewDataGrid.ItemsSource = localTracks.Zip(match.Tracks, (local, proposed) =>
            new PreviewRow(
                proposed.Position,
                FormatTrackValues(local.Title, local.Artist),
                FormatTrackValues(proposed.Title, proposed.Artist)))
            .ToList();
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

    private static string FormatTrackValues(string? title, string? artist) =>
        $"{title ?? string.Empty} — {artist ?? string.Empty}";

    private static string MostCommon(IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;
}
