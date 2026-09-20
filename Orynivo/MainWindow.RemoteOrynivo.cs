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

public partial class MainWindow : Window
{
    private async void LoadOrynivoServerNavigation()
    {
        var loadVersion = ++_orynivoNavigationLoadVersion;
        _orynivoPlaylistsByTag.Clear();
        foreach (var item in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) ||
                                     tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal) ||
                                     tag.StartsWith("LibraryGroup:OrynivoServer", StringComparison.Ordinal)))
                     .ToList())
            NavListBox.Items.Remove(item);

        // Orynivo Server libraries are shown in the shared Artists, Albums, and
        // Tracks views. Keep the server playlist cache populated for existing
        // server-owned playlist actions, but do not render separate server
        // library branches in the sidebar.
        foreach (var server in _settings.OrynivoServers ?? [])
        {
            if (loadVersion != _orynivoNavigationLoadVersion)
                return;
            try
            {
                var playlists = await _orynivoClient.GetPlaylistsAsync(server);
                if (loadVersion != _orynivoNavigationLoadVersion)
                    return;
                foreach (var playlist in playlists)
                {
                    var tag = $"OrynivoServerPlaylist:{server.Id}:{playlist.Id}";
                    _orynivoPlaylistsByTag[tag] = playlist;
                }
            }
            catch { }
        }

        ApplySidebarNavigationSettings();
    }

    private int GetOrynivoServerInsertIndex()
    {
        var insertIndex = NavListBox.Items.IndexOf(FoldersNavItem);
        if (insertIndex < 0)
            return -1;

        for (var index = insertIndex + 1; index < NavListBox.Items.Count; index++)
        {
            if (NavListBox.Items[index] is ListBoxItem { Tag: string tag } &&
                (tag == "LibraryGroup:LocalPlaylists" ||
                 tag.StartsWith("Playlist:", StringComparison.Ordinal)))
            {
                insertIndex = index;
            }
        }

        return insertIndex + 1;
    }

    private StackPanel CreateSmartPlaylistSidebarContent(string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new AvaloniaPath
        {
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Data = FindResource<Geometry>("IconSmartPlaylist"),
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x2A)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x45)),
            StrokeThickness = 0.7,
            StrokeJoin = PenLineJoin.Round
        });
        sp.Children.Add(CreateSidebarEntryText(text));
        return sp;
    }

    private int InsertOrynivoServerNavItem(int index, string serverId, string view, string title, bool isEnabled = true)
    {
        var icon = view switch
        {
            "Artists" => "IconArtist",
            "Albums" => "IconAlbum",
            "Tracks" => "IconTrack",
            "Folders" => "IconFolder",
            _ => "IconServer"
        };
        var content = CreateSidebarEntryContent(icon, title);
        content.Margin = new Thickness(16, 0, 0, 0);
        NavListBox.Items.Insert(index, new ListBoxItem
        {
            Content = content,
            Tag = $"OrynivoServer:{serverId}:{view}",
            IsEnabled = isEnabled,
            Theme = FindResource<ControlTheme>("NavItemTheme")
        });
        return index + 1;
    }

    // ------------------------------------------------------------------
    // Orynivo Server browsing
    // ------------------------------------------------------------------

    private async Task ShowOrynivoServerAsync(string tag)
    {
        if (!TryParseOrynivoServerTag(tag, out var serverId, out var view))
            return;
        _activeOrynivoServer = (_settings.OrynivoServers ?? [])
            .FirstOrDefault(s => string.Equals(s.Id, serverId, StringComparison.Ordinal));
        if (_activeOrynivoServer is null)
            return;

        _orynivoNavigationStack.Clear();
        // Start each server visit with a clean facet slate; genres/formats from another
        // library would otherwise filter the remote Tracks view to nothing.
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        _activeOrynivoView = view == "Root" ? "Artists" : view;
        _updatingViewMode = true;
        OrynivoArtistsViewRadioButton.IsChecked = _activeOrynivoView == "Artists";
        _updatingViewMode = false;
        ContentTitleTextBlock.Text = $"{_activeOrynivoServer.Name} · {GetOrynivoViewTitle(_activeOrynivoView)}";
        await LoadOrynivoViewAsync();
    }

    private static bool TryParseOrynivoServerTag(string tag, out string serverId, out string view)
    {
        serverId = string.Empty;
        view = string.Empty;
        if (!tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            return false;
        var parts = tag.Split(':', 3);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            return false;
        serverId = parts[1];
        view = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : "Root";
        return true;
    }

    private static string GetOrynivoFavoriteKey(string serverId, string entityType, long id)
        => $"{serverId}:{entityType}:{id}";

    internal static string GetServerSourceKey(string serverId) => $"server:{serverId}";

    private bool IsOrynivoFavorite(OrynivoServerSettings server, string entityType, long id)
        => ActiveUserProfile.OrynivoServerFavorites.Contains(GetOrynivoFavoriteKey(server.Id, entityType, id));

    /// <summary>Returns the server-side track IDs the client currently marks as favourites for a remote server.</summary>
    /// <param name="server">Remote server whose client-side track favourites are collected.</param>
    /// <returns>The favourite track IDs.</returns>
    private List<long> GetOrynivoFavoriteTrackIds(OrynivoServerSettings server)
    {
        var prefix = $"{server.Id}:Track:";
        var ids = new List<long>();
        foreach (var key in ActiveUserProfile.OrynivoServerFavorites)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(key.AsSpan(prefix.Length), out var id))
                ids.Add(id);
        }
        return ids;
    }

    private string GetOrynivoViewTitle(string view) => view switch
    {
        "Artists" => LocalizationManager.Current.Artists,
        "Albums" => LocalizationManager.Current.Albums,
        "Tracks" => LocalizationManager.Current.Tracks,
        "Folders" => LocalizationManager.Current.FolderStructure,
        _ => LocalizationManager.Current.OrynivoServers
    };

    private async void OrynivoServerViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode ||
            sender is not RadioButton { IsChecked: true, Tag: string view } ||
            _activeOrynivoServer is null)
            return;

        _orynivoNavigationStack.Clear();
        _activeOrynivoView = view;
        ContentTitleTextBlock.Text = _activeOrynivoServer.Name;
        BackButton.IsVisible = _navigationStack.Count > 0;
        await LoadOrynivoViewAsync();
    }

    private async Task LoadOrynivoViewAsync(long? filterArtistId = null, long? filterAlbumId = null)
    {
        if (_activeOrynivoServer is null)
            return;

        ShowContentLoadingSkeleton();
        CancelAndDispose(ref _orynivoViewCts);
        _orynivoViewCts = new CancellationTokenSource();
        var ct = _orynivoViewCts.Token;
        var server = _activeOrynivoServer;
        var provider = CreateOrynivoCatalogProvider(server);
        var view = filterAlbumId.HasValue ? "AlbumTracks"
            : filterArtistId.HasValue ? "ArtistAlbums"
            : _activeOrynivoView;

        ContentDataGrid.IsVisible = true;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        // Hide the other full-area views so navigating into a remote view from the
        // dashboard, radio, podcast, lyrics, or artist-info view (e.g. a dashboard
        // recent-album card) does not leave that view covering the remote content.
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoLoading;
        ContentDataGrid.ItemsSource = null;
        if (view != "AlbumTracks")
            HideAlbumDetailHeader();
        UpdateLibraryIntroCard(view is "Artists" or "Albums" or "Tracks" or "Folders" ? view : null);
        UpdateEntityFavoritesFilterToggle(_currentTopLevelTag);
        TrackFilterButton.IsVisible = view == "Tracks";
        SaveSmartPlaylistButton.IsVisible = view == "Tracks";
        if (view == "Tracks")
            UpdateSaveSmartPlaylistButtonState();
        _orynivoTrackFacets = null;

        try
        {
            switch (view)
            {
                case "Artists":
                {
                    ApplyColumns("Artists");
                    var artists = await provider.GetArtistsAsync(ct);
                    if (ct.IsCancellationRequested) return;
                    var rows = artists.Select(artist => ToCatalogArtistContentRow(artist, server))
                    .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                    .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showArtistArtworkView);
                    ContentDataGrid.IsVisible = !_showArtistArtworkView;
                    AlbumArtworkListBox.IsVisible = false;
                    ArtistArtworkListBox.IsVisible = _showArtistArtworkView;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Artists", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "ArtistAlbums":
                {
                    ApplyColumns("Albums");
                    var albums = await provider.GetAlbumsByArtistAsync(
                        filterArtistId!.Value,
                        includeArtwork: true,
                        cancellationToken: ct);
                    if (ct.IsCancellationRequested) return;
                    var rows = MergeLogicalAlbumRows(albums.Select(album => ToCatalogAlbumContentRow(album, server)))
                        .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                        .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showAlbumArtworkView);
                    ContentDataGrid.IsVisible = !_showAlbumArtworkView;
                    AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                    ArtistArtworkListBox.IsVisible = false;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Albums", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "Albums":
                {
                    ApplyColumns("Albums");
                    var albums = await provider.GetAlbumsAsync(includeArtwork: true, cancellationToken: ct);
                    if (ct.IsCancellationRequested) return;
                    var rows = MergeLogicalAlbumRows(albums.Select(album => ToCatalogAlbumContentRow(album, server)))
                        .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                        .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showAlbumArtworkView);
                    ContentDataGrid.IsVisible = !_showAlbumArtworkView;
                    AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                    ArtistArtworkListBox.IsVisible = false;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Albums", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "AlbumTracks":
                case "Tracks":
                {
                    AlbumViewModeBorder.IsVisible = false;
                    IReadOnlyList<LibraryCatalogTrack> catalogTracks;
                    if (filterAlbumId.HasValue)
                    {
                        catalogTracks = await provider.GetTracksByAlbumAsync(filterAlbumId.Value, filterArtistId, ct);
                    }
                    else
                    {
                        _orynivoTrackFacets = (await _orynivoClient.GetTrackFacetsAsync(server, ct))
                            .Select(f => f with
                            {
                                IsFavorite = IsOrynivoFavorite(server, "Track", f.Id),
                                SourceKey = GetServerSourceKey(server.Id)
                            })
                            .ToList();
                        if (ct.IsCancellationRequested) return;
                        catalogTracks = await ResolveOrynivoTrackRowsAsync(server, provider, ct);
                    }
                    if (ct.IsCancellationRequested) return;
                    // Build the columns immediately before binding, mirroring the local
                    // Tracks view. Applying them before the long-running async load lets
                    // the DataGrid run layout passes while still empty; Avalonia then
                    // fails to realize the rows that are bound in the async continuation,
                    // leaving only the column headers visible (most noticeable with the
                    // large unfiltered track set, while the small favourites set still
                    // happened to render).
                    ApplyColumns("Tracks");
                    var rows = catalogTracks.Select(track => ToCatalogTrackContentRow(track, server)).ToList();
                    ContentDataGrid.ItemsSource = rows;
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
                    break;
                }

                case "Folders":
                {
                    ContentDataGrid.IsVisible = false;
                    FolderTreeView.IsVisible = true;
                    ShowOrynivoFolderLoadingState();
                    var tracks = await LoadOrynivoFolderTracksAsync(server, ct);
                    if (ct.IsCancellationRequested) return;
                    var metadata = await LoadOrynivoFolderTrackMetadataAsync(server, tracks, ct);
                    if (ct.IsCancellationRequested) return;
                    BuildOrynivoFolderTree(server, tracks, metadata);
                    ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(tracks.Count);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }

        StatusTextBlock.Text = string.Empty;
    }

    private OrynivoServerLibraryCatalogProvider CreateOrynivoCatalogProvider(OrynivoServerSettings server)
        => new(server, _orynivoClient, (entityType, id) => IsOrynivoFavorite(server, entityType, id));

    private OrynivoServerNowPlayingMetadataProvider CreateOrynivoNowPlayingProvider(OrynivoServerSettings server)
        => new(server, _orynivoClient);

    private async Task<List<LibraryCatalogTrack>> LoadAllOrynivoTracksAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        // The server reports when its library index last changed; reuse the
        // locally cached track list while that timestamp is unchanged so the
        // full list does not have to be downloaded on every visit.
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogTrack>? tracks = null;
        if (libraryChangedAt.HasValue)
        {
            // Reading and deserializing the (potentially large) cache file runs on
            // a background thread so the UI thread is never blocked on disk I/O.
            tracks = await Task.Run(
                () => TryLoadOrynivoTrackListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (tracks is null)
        {
            // Match the server's maximum page size. Requesting more makes the
            // capped first page look like the final page and truncates the cache.
            const int pageSize = 5000;
            tracks = [];
            for (var page = 0; ; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await provider.GetTracksAsync(page, pageSize, cancellationToken);
                tracks.AddRange(batch);
                if (batch.Count < pageSize)
                    break;
            }

            if (libraryChangedAt.HasValue)
            {
                var toCache = tracks;
                await Task.Run(
                    () => SaveOrynivoTrackListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        // Favourites are stored client-side and can change without the server's
        // library timestamp changing, so re-apply them after loading (including
        // from the cache) instead of trusting the cached favourite flags.
        return tracks
            .Select(track => track with { IsFavorite = IsOrynivoFavorite(server, "Track", track.Id) })
            .ToList();
    }

    private static bool TryLoadOrynivoTrackListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogTrack> tracks)
    {
        tracks = [];
        try
        {
            var path = GetOrynivoTrackListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoTrackListCache>(File.ReadAllText(path));
            if (cache?.Tracks is null || cache.SchemaVersion != 1 || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            tracks = cache.Tracks
                .Select(track => track with
                {
                    PlaybackPath = OrynivoServerClient.GetStreamUrl(server, track.Id)
                })
                .ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveOrynivoTrackListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogTrack> tracks)
    {
        try
        {
            var path = GetOrynivoTrackListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoTrackListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                tracks.Select(track => track with
                {
                    PlaybackPath = $"orynivo://{server.Id}/track/{track.Id}"
                }).ToList(), SchemaVersion: 1);
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // Cache failures must never prevent browsing a remote server.
        }
    }

    private static string GetOrynivoTrackListCachePath(OrynivoServerSettings server)
        => RemoteServerCache.TrackListCachePath(server);

    private async Task<List<LibraryCatalogArtist>> LoadAllOrynivoArtistsAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogArtist>? artists = null;
        if (libraryChangedAt.HasValue)
        {
            artists = await Task.Run(
                () => TryLoadOrynivoArtistListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (artists is null)
        {
            artists = (await provider.GetArtistsAsync(cancellationToken)).ToList();
            if (libraryChangedAt.HasValue)
            {
                var toCache = artists;
                await Task.Run(
                    () => SaveOrynivoArtistListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        return artists
            .Select(artist => artist with { IsFavorite = IsOrynivoFavorite(server, "Artist", artist.Id) })
            .ToList();
    }

    private async Task<List<LibraryCatalogAlbum>> LoadAllOrynivoAlbumsAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogAlbum>? albums = null;
        if (libraryChangedAt.HasValue)
        {
            albums = await Task.Run(
                () => TryLoadOrynivoAlbumListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (albums is null)
        {
            albums = (await provider.GetAlbumsAsync(includeArtwork: true, cancellationToken)).ToList();
            if (libraryChangedAt.HasValue)
            {
                var toCache = albums;
                await Task.Run(
                    () => SaveOrynivoAlbumListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        return albums
            .Select(album => album with { IsFavorite = IsOrynivoFavorite(server, "Album", album.Id) })
            .ToList();
    }

    private static bool TryLoadOrynivoArtistListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogArtist> artists)
    {
        artists = [];
        try
        {
            var path = GetOrynivoArtistListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoArtistListCache>(File.ReadAllText(path));
            if (cache?.Artists is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            artists = cache.Artists;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadOrynivoAlbumListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogAlbum> albums)
    {
        albums = [];
        try
        {
            var path = GetOrynivoAlbumListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoAlbumListCache>(File.ReadAllText(path));
            if (cache?.Albums is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            albums = cache.Albums;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveOrynivoArtistListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogArtist> artists)
    {
        try
        {
            var path = GetOrynivoArtistListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoArtistListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                artists.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    /// <summary>Deletes the cached remote artist list for a server after profile or image metadata changes.</summary>
    /// <param name="server">Server whose artist list cache should be removed.</param>
    internal static void DeleteOrynivoArtistListCache(OrynivoServerSettings server)
    {
        try
        {
            var path = GetOrynivoArtistListCachePath(server);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void SaveOrynivoAlbumListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogAlbum> albums)
    {
        try
        {
            var path = GetOrynivoAlbumListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoAlbumListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                albums.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    private static string GetOrynivoArtistListCachePath(OrynivoServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{server.Id}|{server.BaseUrl}|{server.ApiKey}")));
        return AppPaths.GetDataPath("remote-artist-cache", $"{key}.json");
    }

    private static string GetOrynivoAlbumListCachePath(OrynivoServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{server.Id}|{server.BaseUrl}|{server.ApiKey}|albums-with-tracks-v1")));
        return AppPaths.GetDataPath("remote-album-cache", $"{key}.json");
    }

    /// <summary>
    /// Resolves the remote Tracks row set: facet-filtered when filters are active,
    /// otherwise the server search results, otherwise all tracks. Uses the same
    /// <see cref="MatchesTrackFilters"/> logic as the local Tracks view.
    /// </summary>
    /// <param name="provider">Active remote catalog provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The track rows to display.</returns>
    private async Task<IReadOnlyList<LibraryCatalogTrack>> ResolveOrynivoTrackRowsAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        if (HasActiveFilters && _orynivoTrackFacets is { } facets)
        {
            var ids = facets.Where(f => MatchesTrackFilters(f)).Select(f => f.Id).ToList();
            return await provider.GetTracksByIdsAsync(ids, cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
            return await provider.SearchTracksAsync(SearchTextBox.Text.Trim(), 500, cancellationToken);
        return await LoadAllOrynivoTracksAsync(server, provider, cancellationToken);
    }

    /// <summary>Re-resolves and rebinds remote Tracks rows after a facet filter change.</summary>
    private async Task ApplyOrynivoTrackFiltersAsync()
    {
        if (_activeOrynivoServer is null)
            return;
        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var catalogTracks = await ResolveOrynivoTrackRowsAsync(_activeOrynivoServer, provider, CancellationToken.None);
        var rows = catalogTracks.Select(track => ToCatalogTrackContentRow(track, _activeOrynivoServer)).ToList();
        BindRemoteTrackRows(rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        UpdateSaveSmartPlaylistButtonState();
    }

    private void BindRemoteTrackRows(IReadOnlyList<ContentRow> rows)
    {
        ContentDataGrid.ItemsSource = rows;
    }

    private static async Task LoadOrynivoArtworkAsync(ContentRow row, string artUrl)
    {
        try
        {
            var artwork = await LoadRemoteArtworkImageAsync(artUrl, 320);
            var thumbnail = await LoadRemoteArtworkImageAsync(artUrl, 96);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                row.Artwork = artwork;
                row.Thumbnail = thumbnail;
                row.ArtworkLoadQueued = false;
                row.ArtworkLoadCompleted = true;
                row.ThumbnailLoadQueued = false;
                row.ThumbnailLoadCompleted = true;
            }, DispatcherPriority.Background);
        }
        catch { }
    }
}



