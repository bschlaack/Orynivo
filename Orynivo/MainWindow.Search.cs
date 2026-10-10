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
    /// <summary>Abandons the previous query immediately before starting the existing debounce timer.</summary>
    /// <param name="sender">Search text box.</param>
    /// <param name="e">Text change event.</param>
    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        CancelLibrarySearch();
        UpdateSaveSmartPlaylistButtonState();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private CancellationTokenSource? _librarySearchCts;
    private int _librarySearchVersion;
    private int? _librarySearchRetryVersion;
    private LibraryLoadResult<ContentRow>? _librarySearchLoadResult;
    private string? _librarySearchQuery;
    private OrynivoServerSettings? _librarySearchOnlyServer;

    /// <summary>Searches selected local and remote sources through the shared cancellable search owner.</summary>
    /// <param name="query">Original search text.</param>
    /// <returns>A task completing after publication or abandonment.</returns>
    private Task ShowSearchResultsAsync(string query) => ShowLibrarySearchResultsAsync(query);

    /// <summary>Cancels pending searches and debounce work before changing navigation or source context.</summary>
    /// <param name="clearResult">Whether navigation also clears the published result and retry context.</param>
    private void CancelLibrarySearch(bool clearResult = true)
    {
        if (clearResult)
            _searchTimer?.Stop();
        CancelAndDispose(ref _librarySearchCts);
        ++_librarySearchVersion;
        _librarySearchRetryVersion = null;
        if (clearResult)
        {
            _librarySearchLoadResult = null;
            _librarySearchQuery = null;
            _librarySearchOnlyServer = null;
        }
        UpdateUnifiedLibraryLoadNotice();
    }

    /// <summary>Loads one captured search context, retaining successful sources and rejecting stale publication.</summary>
    /// <param name="query">Original search text.</param>
    /// <param name="onlyServer">Optional server-scoped search context.</param>
    /// <param name="isRetry">Whether usable rows and view position should be retained.</param>
    /// <returns>A task completing after publication or abandonment.</returns>
    private async Task ShowLibrarySearchResultsAsync(string query, OrynivoServerSettings? onlyServer = null, bool isRetry = false)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            CancelLibrarySearch();
            if (onlyServer is not null)
                await LoadOrynivoViewAsync();
            else if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag })
                await ShowTopLevelViewAsync(tag);
            return;
        }

        var previousResult = isRetry ? _librarySearchLoadResult : null;
        if (!SearchResultsScrollViewer.IsVisible)
            PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _librarySearchCts = new CancellationTokenSource();
        var token = _librarySearchCts.Token;
        var version = ++_librarySearchVersion;
        var generation = _unifiedLibraryViewCache.Generation;
        _librarySearchQuery = query;
        _librarySearchOnlyServer = onlyServer;
        _librarySearchLoadResult = previousResult;
        _librarySearchRetryVersion = isRetry ? version : null;
        var filters = new TrackFacetFilterSnapshot(_trackFavoritesOnly, _selectedTrackGenres,
            _selectedTrackFormats, _selectedTrackBitrates, _selectedTrackSources);
        var favorites = new HashSet<string>(ActiveUserProfile.OrynivoServerFavorites, StringComparer.Ordinal);
        var configuredServers = onlyServer is null ? (_settings.OrynivoServers ?? []).ToArray() : new[] { onlyServer };
        var servers = configuredServers
            .Where(server => filters.IncludesSource(GetServerSourceKey(server.Id)))
            .Select(server => new OrynivoServerSettings
            {
                Id = server.Id, Name = server.Name, BaseUrl = server.BaseUrl, ApiKey = server.ApiKey,
                ProfileId = server.ProfileId, StreamingFormat = server.StreamingFormat,
                StreamingBitrateKbps = server.StreamingBitrateKbps
            }).ToArray();
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        UpdateEntityFavoritesFilterToggle(null);
        UpdateAlphabetIndex(null, false);
        UpdateLibraryIntroCard(null);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        if (onlyServer is not null)
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

        UpdateUnifiedLibraryLoadNotice();
        bool IsCurrent() => !token.IsCancellationRequested && version == _librarySearchVersion &&
                            generation == _unifiedLibraryViewCache.Generation && SearchResultsScrollViewer.IsVisible;
        try
        {
            var localTask = onlyServer is null && filters.IncludesSource(LocalSourceKey)
                ? LibrarySourceLoader.LoadAsync<LibrarySearchRows>(LocalSourceKey,
                    async cancellation => new[] { await Task.Run(() => LoadLocalSearchRows(query, filters, cancellation), cancellation) }, token)
                : Task.FromResult(new LibraryLoadResult<LibrarySearchRows>([], []));
            var remoteTask = LibrarySourceBatchLoader.LoadAsync<OrynivoServerSettings, LibrarySearchRows>(servers,
                server => GetServerSourceKey(server.Id), async (server, cancellation) =>
                {
                    var provider = new OrynivoServerLibraryCatalogProvider(server, _orynivoClient,
                        (kind, id) => favorites.Contains(GetOrynivoFavoriteKey(server.Id, kind, id)), requireComplete: true);
                    var (tracks, albums, artists) = await provider.SearchFullAsync(query.Trim(), 50, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    return new[] { new LibrarySearchRows(
                        tracks.Where(track => filters.Matches(new TrackFacetInfo(track.Id, track.IsFavorite,
                            track.Genre, track.Format, track.Bitrate, GetServerSourceKey(server.Id), track.AlbumId)))
                            .Select(track => ToCatalogTrackContentRow(track, server, registerRemoteMetadata: false)).ToList(),
                        albums.Select(album => ToCatalogAlbumContentRow(album, server)).ToList(),
                        artists.Select(artist => ToCatalogArtistContentRow(artist, server)).ToList()) };
                }, token, timeout: TimeSpan.FromSeconds(20));
            await Task.WhenAll(localTask, remoteTask);
            var result = LibraryLoadResult<LibrarySearchRows>.Combine([await localTask, await remoteTask]);
            if (!IsCurrent() || result.Status == LibraryLoadStatus.Cancelled)
                return;

            var trackRows = result.Rows.SelectMany(rows => rows.Tracks).ToList();
            var albumRows = MergeLogicalAlbumRows(result.Rows.SelectMany(rows => rows.Albums));
            var artistRows = onlyServer is null
                ? MergeUnifiedArtistRows(result.Rows.SelectMany(rows => rows.Artists))
                : result.Rows.SelectMany(rows => rows.Artists).ToList();
            var selectedRow = isRetry ? GetSelectedContentRow() : null;
            var verticalOffset = isRetry ? CaptureCurrentVerticalOffset() : null;
            _librarySearchLoadResult = new LibraryLoadResult<ContentRow>(
                [.. trackRows, .. albumRows, .. artistRows], result.Sources);
            foreach (var row in trackRows)
                if (row.OrynivoServer is not null && row.FilePath is { } path)
                    _orynivoTracksByUrl[path] = row;
            ApplySearchColumns();
            SearchTracksDataGrid.ItemsSource = trackRows;
            SearchAlbumsDataGrid.ItemsSource = albumRows;
            SearchArtistsDataGrid.ItemsSource = artistRows;
            RefreshLibrarySearchPresentation();
            if (isRetry)
                RestoreSearchSelection(selectedRow?.Id, selectedRow?.SourceKey, verticalOffset, IsCurrent, selectedRow?.EntityType);
        }
        finally
        {
            if (version == _librarySearchVersion)
            {
                _librarySearchRetryVersion = null;
                UpdateUnifiedLibraryLoadNotice();
            }
        }
    }

    /// <summary>Loads local Lucene results without accessing mutable filter selections.</summary>
    /// <param name="query">Original query preserving Lucene semantics.</param>
    /// <param name="filters">Captured facet selections.</param>
    /// <param name="token">Owning search cancellation.</param>
    /// <returns>Score-ordered local category rows.</returns>
    private LibrarySearchRows LoadLocalSearchRows(string query, TrackFacetFilterSnapshot filters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ids = TrackSearchIndex.SearchByCategory(query);
        token.ThrowIfCancellationRequested();
        using var db = AudioDatabase.OpenDefault();

        var trackIds = ids.Tracks.Ids.ToList();
        if (filters.HasTrackFilters)
        {
            var allowed = db.GetTrackFacets()
                .Where(facet => filters.Matches(facet))
                .Select(facet => facet.Id)
                .ToHashSet();
            trackIds = trackIds.Where(allowed.Contains).ToList();
        }

        var albumScores = BuildEntityScores(db.GetAlbumIdsByTrackIds(ids.Albums.Ids), ids.Albums.Scores);
        var artistScores = MergeSearchScores(
            BuildEntityScores(db.GetArtistIdsByTrackIds(ids.Artists.Ids), ids.Artists.Scores),
            BuildEntityScores(db.GetAlbumArtistIdsByTrackIds(ids.AlbumArtists.Ids), ids.AlbumArtists.Scores));
        return new LibrarySearchRows(
            Tracks: SortBySearchScore(
                db.GetTrackListByIds(trackIds).Select(ToTrackContentRow),
                ids.Tracks.Scores),
            Albums: SortBySearchScore(
                db.GetAlbumsByTrackIds(ids.Albums.Ids).Select(ToAlbumContentRow),
                albumScores),
            Artists: SortBySearchScore(
                db.GetArtistsByIds(artistScores.Keys).Select(ToArtistContentRow),
                artistScores));
    }

    /// <summary>Refreshes search counts, empty sections, and failure text after publication or language changes.</summary>
    private void RefreshLibrarySearchPresentation()
    {
        if (!SearchResultsScrollViewer.IsVisible || _librarySearchLoadResult is null)
            return;
        var query = _librarySearchQuery ?? string.Empty;
        var tracks = (SearchTracksDataGrid.ItemsSource as IReadOnlyCollection<ContentRow>)?.Count ?? 0;
        var albums = (SearchAlbumsDataGrid.ItemsSource as IReadOnlyCollection<ContentRow>)?.Count ?? 0;
        var artists = (SearchArtistsDataGrid.ItemsSource as IReadOnlyCollection<ContentRow>)?.Count ?? 0;
        var notice = GetLibrarySearchNoticeText();
        UpdateSearchEmptyState(SearchTracksEmptyTextBlock, tracks, LocalizationManager.Current.SearchTermNotFoundInTracks, query, notice);
        UpdateSearchEmptyState(SearchAlbumsEmptyTextBlock, albums, LocalizationManager.Current.SearchTermNotFoundInAlbums, query, notice);
        UpdateSearchEmptyState(SearchArtistsEmptyTextBlock, artists, LocalizationManager.Current.SearchTermNotFoundInArtists, query, notice);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        ContentCountTextBlock.Text = string.Format(LocalizationManager.Current.SearchResultSummary, tracks, albums, artists);
        UpdateUnifiedLibraryLoadNotice();
    }

    /// <summary>Formats incomplete search text without exposing raw errors or source identities.</summary>
    /// <returns>The localized incomplete-search notice, or an empty string.</returns>
    private string GetLibrarySearchNoticeText() => _librarySearchLoadResult is not { } result ? string.Empty :
        string.Format(result.Status switch
        {
            LibraryLoadStatus.Partial => LocalizationManager.Current.SearchLoadPartial,
            LibraryLoadStatus.Failed => LocalizationManager.Current.SearchLoadFailed,
            _ => string.Empty
        }, result.Sources.Count(source => source.Status is LibrarySourceLoadStatus.Failed or LibrarySourceLoadStatus.TimedOut));

    /// <summary>Distinguishes an empty category from an incomplete source search.</summary>
    /// <param name="textBlock">Category empty-state control.</param>
    /// <param name="count">Published category row count.</param>
    /// <param name="format">Successful no-match message format.</param>
    /// <param name="query">Original query.</param>
    /// <param name="incompleteNotice">Optional incomplete-search notice.</param>
    private static void UpdateSearchEmptyState(TextBlock textBlock, int count, string format, string query, string? incompleteNotice = null)
    {
        textBlock.Text = string.IsNullOrEmpty(incompleteNotice) ? string.Format(format, query) : incompleteNotice;
        textBlock.IsVisible = count == 0;
    }

    /// <summary>Uses the same strict outcome and cancellation path for a server-scoped search.</summary>
    /// <param name="query">Search text; empty restores the server track list.</param>
    /// <returns>A task completing after publication or abandonment.</returns>
    private Task ShowOrynivoSearchResultsAsync(string query) => _activeOrynivoServer is { } server
        ? ShowLibrarySearchResultsAsync(query, server)
        : Task.CompletedTask;

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
