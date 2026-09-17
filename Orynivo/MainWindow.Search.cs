using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Local and remote library search, result scoring, and the shared three-section
/// search result grids for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSaveSmartPlaylistButtonState();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async Task ShowSearchResultsAsync(string query)
    {
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        UpdateEntityFavoritesFilterToggle(null);
        if (string.IsNullOrWhiteSpace(query))
        {
            if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag })
                await ShowTopLevelViewAsync(tag);
            return;
        }

        if (!SearchResultsScrollViewer.IsVisible)
            PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        UpdateAlphabetIndex(null, false);
        UpdateLibraryIntroCard(null);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        ContentDataGrid.IsVisible = false;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = true;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        HideAlbumDetailHeader();

        // Honour the active Tracks facet filters during search: the source facet gates which
        // sources contribute results at all, while favourite/genre/format/bitrate additionally
        // filter the track section. An empty source facet means "all sources".
        var includeLocal = _selectedTrackSources.Count == 0 ||
                           _selectedTrackSources.Contains(LocalSourceKey);
        var applyTrackFacets = HasActiveFilters;
        var result = await Task.Run(() =>
        {
            if (!includeLocal)
                return (Tracks: new List<ContentRow>(), Albums: new List<ContentRow>(), Artists: new List<ContentRow>());

            var ids = TrackSearchIndex.SearchByCategory(query);
            using var db = AudioDatabase.OpenDefault();

            var trackIds = ids.Tracks.Ids.ToList();
            if (applyTrackFacets)
            {
                var allowed = db.GetTrackFacets()
                    .Where(facet => MatchesTrackFilters(facet))
                    .Select(facet => facet.Id)
                    .ToHashSet();
                trackIds = trackIds.Where(allowed.Contains).ToList();
            }

            var albumScores = BuildEntityScores(db.GetAlbumIdsByTrackIds(ids.Albums.Ids), ids.Albums.Scores);
            var artistScores = MergeSearchScores(
                BuildEntityScores(db.GetArtistIdsByTrackIds(ids.Artists.Ids), ids.Artists.Scores),
                BuildEntityScores(db.GetAlbumArtistIdsByTrackIds(ids.AlbumArtists.Ids), ids.AlbumArtists.Scores));
            return (
                Tracks: SortBySearchScore(
                    db.GetTrackListByIds(trackIds).Select(ToTrackContentRow),
                    ids.Tracks.Scores),
                Albums: SortBySearchScore(
                    db.GetAlbumsByTrackIds(ids.Albums.Ids).Select(ToAlbumContentRow),
                    albumScores),
                Artists: SortBySearchScore(
                    db.GetArtistsByIds(artistScores.Keys).Select(ToArtistContentRow),
                    artistScores));
        });
        await AddRemoteSearchResultsAsync(query, result.Tracks, result.Albums, result.Artists);
        result.Albums = MergeLogicalAlbumRows(result.Albums);
        result.Artists = MergeUnifiedArtistRows(result.Artists);

        ApplySearchColumns();
        SearchTracksDataGrid.ItemsSource = result.Tracks;
        SearchAlbumsDataGrid.ItemsSource = result.Albums;
        SearchArtistsDataGrid.ItemsSource = result.Artists;
        UpdateSearchEmptyState(SearchTracksEmptyTextBlock, result.Tracks.Count, LocalizationManager.Current.SearchTermNotFoundInTracks, query);
        UpdateSearchEmptyState(SearchAlbumsEmptyTextBlock, result.Albums.Count, LocalizationManager.Current.SearchTermNotFoundInAlbums, query);
        UpdateSearchEmptyState(SearchArtistsEmptyTextBlock, result.Artists.Count, LocalizationManager.Current.SearchTermNotFoundInArtists, query);
        ContentCountTextBlock.Text = string.Format(
            LocalizationManager.Current.SearchResultSummary,
            result.Tracks.Count,
            result.Albums.Count,
            result.Artists.Count);
    }

    private static void UpdateSearchEmptyState(TextBlock textBlock, int count, string format, string query)
    {
        textBlock.Text = string.Format(format, query);
        textBlock.IsVisible = count == 0;
    }

    /// <summary>Adds matching Orynivo Server rows to the shared local search result lists.</summary>
    /// <param name="query">Search query.</param>
    /// <param name="trackRows">Track rows to extend.</param>
    /// <param name="albumRows">Album rows to extend.</param>
    /// <param name="artistRows">Artist rows to extend.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddRemoteSearchResultsAsync(
        string query,
        List<ContentRow> trackRows,
        List<ContentRow> albumRows,
        List<ContentRow> artistRows)
    {
        foreach (var server in _settings.OrynivoServers ?? [])
        {
            // The source facet gates which servers may contribute search results.
            if (_selectedTrackSources.Count > 0 &&
                !_selectedTrackSources.Contains(GetServerSourceKey(server.Id)))
                continue;

            try
            {
                var provider = CreateOrynivoCatalogProvider(server);
                var (tracks, albums, artists) = await provider.SearchFullAsync(query.Trim(), 50);
                IEnumerable<LibraryCatalogTrack> matchingTracks = HasActiveFilters
                    ? tracks.Where(track => RemoteSearchTrackMatchesFilters(server, track))
                    : tracks;
                trackRows.AddRange(matchingTracks.Select(track => ToCatalogTrackContentRow(track, server)));
                albumRows.AddRange(albums.Select(album => ToCatalogAlbumContentRow(album, server)));
                artistRows.AddRange(artists.Select(artist => ToCatalogArtistContentRow(artist, server)));
            }
            catch
            {
                // An unavailable server must not hide local search results.
            }
        }
    }

    /// <summary>
    /// Evaluates a remote Orynivo Server search track against the active Tracks facet filters,
    /// applying the client-side favourite state for that server's tracks.
    /// </summary>
    /// <param name="server">The server the track belongs to.</param>
    /// <param name="track">The remote catalog track to test.</param>
    /// <returns><see langword="true"/> when the track satisfies the active facet filters.</returns>
    private bool RemoteSearchTrackMatchesFilters(OrynivoServerSettings server, LibraryCatalogTrack track)
    {
        var facet = new TrackFacetInfo(
            track.Id,
            IsOrynivoFavorite(server, "Track", track.Id),
            track.Genre,
            track.Format,
            track.Bitrate,
            GetServerSourceKey(server.Id),
            track.AlbumId);
        return MatchesTrackFilters(facet);
    }

    /// <summary>
    /// Shows the shared three-section (tracks/albums/artists) search result view for the
    /// active remote Orynivo Server, mirroring the local <see cref="ShowSearchResultsAsync"/>.
    /// </summary>
    /// <param name="query">The search query; an empty value restores the remote Tracks list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ShowOrynivoSearchResultsAsync(string query)
    {
        if (_activeOrynivoServer is not { } server)
            return;

        if (string.IsNullOrWhiteSpace(query))
        {
            // Clearing the search box restores the full remote Tracks list.
            await LoadOrynivoViewAsync();
            return;
        }

        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        UpdateEntityFavoritesFilterToggle(null);

        if (!SearchResultsScrollViewer.IsVisible)
            PushCurrentNavigationState();
        UpdateAlphabetIndex(null, false);
        UpdateLibraryIntroCard(null);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        SaveSmartPlaylistButton.IsVisible = false;
        ContentDataGrid.IsVisible = false;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = true;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        HideAlbumDetailHeader();

        var provider = CreateOrynivoCatalogProvider(server);
        var (tracks, albums, artists) = await provider.SearchFullAsync(query.Trim(), 50);

        // A newer query may have started while this request was in flight.
        if (!string.Equals((SearchTextBox.Text ?? string.Empty).Trim(), query.Trim(), StringComparison.Ordinal))
            return;

        IEnumerable<LibraryCatalogTrack> matchingTracks = HasActiveFilters
            ? tracks.Where(track => RemoteSearchTrackMatchesFilters(server, track))
            : tracks;
        var trackRows = matchingTracks.Select(track => ToCatalogTrackContentRow(track, server)).ToList();
        var albumRows = MergeLogicalAlbumRows(albums.Select(album => ToCatalogAlbumContentRow(album, server)));
        var artistRows = artists.Select(artist => ToCatalogArtistContentRow(artist, server)).ToList();

        ApplySearchColumns();
        SearchTracksDataGrid.ItemsSource = trackRows;
        SearchAlbumsDataGrid.ItemsSource = albumRows;
        SearchArtistsDataGrid.ItemsSource = artistRows;
        UpdateSearchEmptyState(SearchTracksEmptyTextBlock, trackRows.Count, LocalizationManager.Current.SearchTermNotFoundInTracks, query);
        UpdateSearchEmptyState(SearchAlbumsEmptyTextBlock, albumRows.Count, LocalizationManager.Current.SearchTermNotFoundInAlbums, query);
        UpdateSearchEmptyState(SearchArtistsEmptyTextBlock, artistRows.Count, LocalizationManager.Current.SearchTermNotFoundInArtists, query);
        ContentCountTextBlock.Text = string.Format(
            LocalizationManager.Current.SearchResultSummary,
            trackRows.Count,
            albumRows.Count,
            artistRows.Count);
    }

    private static Dictionary<long, float> BuildEntityScores(
        IReadOnlyDictionary<long, long> trackToEntityIds,
        IReadOnlyDictionary<long, float> trackScores)
    {
        var result = new Dictionary<long, float>();
        foreach (var (trackId, entityId) in trackToEntityIds)
        {
            if (!trackScores.TryGetValue(trackId, out var score))
                continue;
            if (!result.TryGetValue(entityId, out var existing) || score > existing)
                result[entityId] = score;
        }
        return result;
    }

    private static Dictionary<long, float> MergeSearchScores(params IReadOnlyDictionary<long, float>[] sources)
    {
        var result = new Dictionary<long, float>();
        foreach (var source in sources)
        {
            foreach (var (id, score) in source)
            {
                if (!result.TryGetValue(id, out var existing) || score > existing)
                    result[id] = score;
            }
        }
        return result;
    }

    private static List<ContentRow> SortBySearchScore(
        IEnumerable<ContentRow> rows,
        IReadOnlyDictionary<long, float> scores)
        => rows
            .OrderByDescending(row => row.Id is long id && scores.TryGetValue(id, out var score) ? score : 0)
            .ThenBy(row => row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static ContentRow ToAlbumContentRow(AlbumInfo a) => new()
    {
        Id = a.Id,
        AlbumId = a.Id,
        Title = string.IsNullOrEmpty(a.Album) ? LocalizationManager.Current.Unknown : a.Album,
        Artist = string.IsNullOrEmpty(a.DisplayArtist) ? null : a.DisplayArtist,
        Year = a.Year?.ToString(),
        ArtworkPath = a.ArtworkPath,
        ThumbnailPath = a.ThumbnailPath,
        IsFavorite = a.IsFavorite,
        EntityType = "Album"
    };

    private static ContentRow ToArtistContentRow(ArtistInfo a) => new()
    {
        Id = a.Id,
        ArtistId = a.Id,
        Title = string.IsNullOrEmpty(a.Artist) ? LocalizationManager.Current.Unknown : a.Artist,
        IsFavorite = a.IsFavorite,
        ArtworkPath = a.ImagePath,
        ThumbnailPath = a.ImagePath,
        Biography = a.Biography,
        SourceUrl = a.SourceUrl,
        ProfileLanguage = a.ProfileLanguage,
        ProfileFetchedAt = a.ProfileFetchedAt,
        ImageIsManual = a.ImageIsManual,
        EntityType = "Artist"
    };

    private void ApplySearchColumns()
    {
        ConfigureSearchGrid(SearchTracksDataGrid,
            (LocalizationManager.Current.Title, nameof(ContentRow.Title), 240, "title", true),
            (LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180, "artist", true),
            (LocalizationManager.Current.Album, nameof(ContentRow.Album), 220, "album", true),
            (LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 90, "duration", true),
            (LocalizationManager.Current.Format, nameof(ContentRow.Format), 80, "format", true),
            (LocalizationManager.Current.AlbumArtist, nameof(ContentRow.AlbumArtist), 180, "albumArtist", false),
            (LocalizationManager.Current.Year, nameof(ContentRow.Year), 80, "year", false),
            (LocalizationManager.Current.Genre, nameof(ContentRow.Genre), 150, "genre", false),
            (LocalizationManager.Current.Bitrate, nameof(ContentRow.Bitrate), 100, "bitrate", false),
            (LocalizationManager.Current.SampleRate, nameof(ContentRow.SampleRate), 110, "sampleRate", false),
            (LocalizationManager.Current.BitDepth, nameof(ContentRow.BitDepth), 90, "bitDepth", false),
            (LocalizationManager.Current.Composer, nameof(ContentRow.Composer), 180, "composer", false),
            (LocalizationManager.Current.FileName, nameof(ContentRow.FileName), 220, "fileName", false));
        ConfigureSearchGrid(SearchAlbumsDataGrid,
            (LocalizationManager.Current.Album, nameof(ContentRow.Title), 260, "album", true),
            (LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist", true),
            (LocalizationManager.Current.Year, nameof(ContentRow.Year), 90, "year", true));
        ConfigureSearchGrid(SearchArtistsDataGrid,
            (LocalizationManager.Current.Artist, nameof(ContentRow.Title), 320, "artist", true));
    }

    private void ConfigureSearchGrid(
        DataGrid grid,
        params (string Header, string Binding, double Width, string Key, bool DefaultVisible)[] columns)
    {
        var widthKey = grid.Name switch
        {
            nameof(SearchTracksDataGrid) => "SearchTracks",
            nameof(SearchAlbumsDataGrid) => "SearchAlbums",
            _ => "SearchArtists"
        };
        CaptureColumnWidths(widthKey, grid);
        grid.Columns.Clear();
        grid.Columns.Add(CreateFavoriteColumn());
        grid.Columns.Add(CreateSourceBadgeColumn());
        foreach (var column in columns)
        {
            var entityType = grid == SearchArtistsDataGrid && column.Binding == nameof(ContentRow.Title)
                ? "Artist"
                : grid == SearchAlbumsDataGrid && column.Binding == nameof(ContentRow.Title)
                    ? "Album"
                    : column.Binding == nameof(ContentRow.Artist)
                        ? "Artist"
                        : column.Binding == nameof(ContentRow.Album)
                            ? "Album"
                            : null;
            DataGridColumn dataGridColumn = entityType is null
                ? new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding(column.Binding),
                    SortMemberPath = GetContentRowSortMemberPath(column.Binding),
                    Width = new DataGridLength(column.Width),
                    Tag = column.Key,
                    IsVisible = column.DefaultVisible
                }
                : CreateEntityLinkColumn(column.Header, column.Binding, column.Width, false, entityType);
            dataGridColumn.Tag = column.Key;
            dataGridColumn.IsVisible = column.DefaultVisible;
            grid.Columns.Add(dataGridColumn);
        }
        DataGridColumnChooser.Apply(grid, widthKey, _settings);
        RestoreColumnWidths(widthKey, grid);
    }
}
