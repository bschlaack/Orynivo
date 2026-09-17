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
/// Unified local/remote library view loading, row merging, and track, album,
/// and artist row conversion for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private ContentRow ToOrynivoTrackContentRow(OrynivoServerSettings server, OrynivoTrackInfo track)
    {
        var streamUrl = OrynivoServerClient.GetStreamUrl(server, track.Id);
        var row = new ContentRow
        {
            Title = track.Title?.Trim() ?? track.FileName.Trim(),
            AlphabetIndexText = track.SortTitle?.Trim() ?? track.Title?.Trim() ?? track.FileName.Trim(),
            Id = track.Id,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            Year = track.Year?.ToString(CultureInfo.CurrentCulture),
            TrackNumber = FormatPartNumber(track.TrackNumber, track.TrackTotal),
            DiscNumber = FormatPartNumber(track.DiscNumber, track.DiscTotal),
            Duration = FormatSeconds(track.Duration),
            Genre = track.Genre,
            Format = track.Format?.ToUpperInvariant(),
            Bitrate = track.Bitrate is > 0 ? $"{track.Bitrate:N0} kbps" : null,
            SampleRate = track.SampleRate is > 0 ? $"{track.SampleRate:N0} Hz" : null,
            SampleRateHz = track.SampleRate,
            BitDepth = track.BitDepth is > 0 ? $"{track.BitDepth:N0} Bit" : null,
            Channels = track.Channels?.ToString(CultureInfo.CurrentCulture),
            ChannelCount = track.Channels,
            Composer = track.Composer,
            Bpm = track.Bpm?.ToString(CultureInfo.CurrentCulture),
            FileName = track.FileName,
            FileSize = FormatFileSize(track.FileSize),
            AddedAt = track.AddedAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(track.AddedAt.Value)
                    .ToLocalTime()
                    .ToString("d", CultureInfo.CurrentCulture)
                : null,
            ReplayGainTrack = FormatReplayGainDisplay(track.ReplayGainTrack),
            ReplayGainAlbum = FormatReplayGainDisplay(track.ReplayGainAlbum),
            UserRating = track.UserRating,
            MusicBrainzRating = track.MusicBrainzRating,
            MusicBrainzRatingVotes = track.MusicBrainzRatingVotes,
            MusicBrainzTrackId = track.MusicBrainzTrackId,
            MusicBrainzRatingFetchedAt = track.MusicBrainzRatingFetchedAt,
            FilePath = streamUrl,
            SourcePath = track.SourcePath ?? track.Path,
            ArtworkPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 320),
            ThumbnailPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 96),
            IsFavorite = IsOrynivoFavorite(server, "Track", track.Id),
            ArtistId = track.ArtistId,
            AlbumId = track.AlbumId,
            EntityType = "OrynivoTrack",
            ExternalId = track.Id.ToString(CultureInfo.InvariantCulture),
            KnownDuration = track.Duration.HasValue ? TimeSpan.FromSeconds(track.Duration.Value) : null,
            OrynivoServer = server
        };
        _orynivoTracksByUrl[streamUrl] = row;
        return row;
    }

    // ------------------------------------------------------------------
    // DB-Abfragen
    // ------------------------------------------------------------------

    private List<ContentRow> QueryRows(string view, string? searchQuery = null, bool registerRemoteMetadata = true)
    {
        try
        {
            using var db = AudioDatabase.OpenDefault();

            if (view.StartsWith("Playlist:") && long.TryParse(view.AsSpan("Playlist:".Length), out long pid))
            {
                var playlist = db.GetPlaylistById(pid);
                if (playlist is null) return [];

                if (playlist.IsSmartPlaylist && playlist.FilterCriteria is not null)
                {
                    SmartPlaylistCriteria criteria;
                    try { criteria = JsonSerializer.Deserialize<SmartPlaylistCriteria>(playlist.FilterCriteria)!; }
                    catch { return []; }

                    return ResolveUnifiedSmartPlaylistRows(criteria, registerRemoteMetadata);
                }

                var ptracks = db.GetPlaylistTracks(pid).ToList();
                return ptracks.Select((pt, i) =>
                {
                    var t = db.GetByPath(pt.Path);
                    if (t is null)
                    {
                        if (TryResolveOrynivoPlaylistReference(pt.Path, out var server, out var trackId))
                        {
                            try
                            {
                                var provider = CreateOrynivoCatalogProvider(server);
                                var remoteTrack = provider.GetTracksByIdsAsync([trackId])
                                    .GetAwaiter()
                                    .GetResult()
                                    .FirstOrDefault();
                                if (remoteTrack is not null)
                                {
                                    var remoteRow = ToCatalogTrackContentRow(remoteTrack, server, registerRemoteMetadata);
                                    remoteRow.Nr = (i + 1).ToString();
                                    remoteRow.PlaylistEntryId = pt.Id;
                                    return remoteRow;
                                }
                            }
                            catch
                            {
                                // Fall through to a plain placeholder row.
                            }
                        }

                        return new ContentRow
                        {
                            Nr = (i + 1).ToString(),
                            PlaylistEntryId = pt.Id,
                            Title = Path.GetFileName(pt.Path),
                            FileName = Path.GetFileName(pt.Path),
                            FilePath = pt.Path
                        };
                    }

                    var row = ToTrackContentRow(ToTrackListInfo(t));
                    row.Nr = (i + 1).ToString();
                    row.PlaylistEntryId = pt.Id;
                    return row;
                }).ToList();
            }

            return view switch
            {
                "Search" => db.GetTrackListByIds(TrackSearchIndex.SearchByCategory(searchQuery ?? string.Empty).Tracks.Ids)
                    .Select(ToTrackContentRow)
                    .ToList(),

                "Artists" => db.GetArtistsLite()
                    .Select(LocalLibraryCatalogProvider.ToCatalogArtist)
                    .Where(a => !_artistFavoritesOnly || a.IsFavorite)
                    .Select(artist => ToCatalogArtistContentRow(artist))
                    .ToList(),

                "Albums" => (_activeArtistFilterId is long artistId
                        ? db.GetAlbumsByArtist(artistId, _showAlbumArtworkView)
                        : db.GetAlbumsLite(_showAlbumArtworkView))
                    .Select(LocalLibraryCatalogProvider.ToCatalogAlbum)
                    .Where(a => !_albumFavoritesOnly || a.IsFavorite)
                    .Select(album => ToCatalogAlbumContentRow(album))
                    .ToList(),

                _ => (_activeAlbumFilterId is long albumId
                        ? db.GetTrackListByAlbum(albumId)
                        : db.GetTrackList()) // "Tracks" and fallback
                    .Select(LocalLibraryCatalogProvider.ToCatalogTrack)
                    .Select(track => ToCatalogTrackContentRow(track))
                    .ToList()
            };
        }
        catch { return []; }
    }

    private List<ContentRow> GetFilteredTrackRows()
    {
        using var db = AudioDatabase.OpenDefault();
        if (!HasActiveFilters)
        {
            return db.GetTrackList()
                .Select(ToTrackContentRow)
                .ToList();
        }

        var facets = db.GetTrackFacets();
        var ids = facets
            .Where(f => MatchesTrackFilters(f))
            .Select(f => f.Id)
            .ToList();
        return db.GetTrackListFiltered(ids)
            .Select(ToTrackContentRow)
            .ToList();
    }

    private sealed class StringNotEmptyConverter : IValueConverter
    {
        public static readonly StringNotEmptyConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string text && !string.IsNullOrWhiteSpace(text);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private async Task BindLocalRowsAndStartRemoteAppendAsync(string tag)
    {
        var diagnosticStopwatch = Stopwatch.StartNew();
        LogUiDiagnostics($"BindLocalRowsAndStartRemoteAppendAsync start tag={tag}");
        CancelAndDispose(ref _unifiedLibraryAppendCts);
        _unifiedLibraryAppendCts = new CancellationTokenSource();
        var cancellationToken = _unifiedLibraryAppendCts.Token;
        var version = ++_unifiedLibraryLoadVersion;
        if (TryGetUnifiedLibraryViewCache(tag, out var cachedRows))
        {
            ApplyColumns(tag);
            ContentDataGrid.ItemsSource = cachedRows;
            BindUnifiedArtworkRowsIfVisible(tag, cachedRows);
            UpdateAlphabetIndex(cachedRows, true);
            UpdateUnifiedContentCount(tag, cachedRows.Count);
            LogUiDiagnostics(
                $"BindLocalRowsAndStartRemoteAppendAsync cache hit tag={tag} count={cachedRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
            return;
        }
        var rows = tag == "Tracks"
            ? await Task.Run(GetFilteredTrackRows)
            : await Task.Run(() => QueryRows(tag));
        LogUiDiagnostics(
            $"BindLocalRowsAndStartRemoteAppendAsync local rows loaded tag={tag} count={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
        if (cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
        {
            LogUiDiagnostics($"BindLocalRowsAndStartRemoteAppendAsync canceled before bind tag={tag} version={version}");
            return;
        }

        if ((_settings.OrynivoServers?.Count ?? 0) > 0)
        {
            var remoteRows = await LoadRemoteUnifiedRowsAsync(tag, version, cancellationToken, diagnosticStopwatch);
            if (remoteRows.Count > 0)
            {
                rows.AddRange(remoteRows);
                LogUiDiagnostics(
                    $"BindLocalRowsAndStartRemoteAppendAsync remote rows merged before bind tag={tag} remote={remoteRows.Count} total={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
            }
        }

        if (cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
        {
            LogUiDiagnostics($"BindLocalRowsAndStartRemoteAppendAsync canceled before combined bind tag={tag} version={version}");
            return;
        }

        var sortedRows = SortUnifiedRows(rows);
        if (tag == "Artists")
            sortedRows = MergeUnifiedArtistRows(sortedRows);
        else if (tag == "Albums")
            sortedRows = MergeLogicalAlbumRows(sortedRows);
        StoreUnifiedLibraryViewCache(tag, sortedRows);
        LogUiDiagnostics(
            $"BindLocalRowsAndStartRemoteAppendAsync combined rows sorted tag={tag} count={sortedRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
        ApplyColumns(tag);
        ContentDataGrid.ItemsSource = sortedRows;
        BindUnifiedArtworkRowsIfVisible(tag, sortedRows);
        UpdateAlphabetIndex(sortedRows, true);
        UpdateUnifiedContentCount(tag, sortedRows.Count);
        LogUiDiagnostics(
            $"BindLocalRowsAndStartRemoteAppendAsync combined bind completed tag={tag} count={sortedRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
    }

    private async Task<List<ContentRow>> LoadRemoteUnifiedRowsAsync(
        string tag,
        int version,
        CancellationToken cancellationToken,
        Stopwatch diagnosticStopwatch)
    {
        LogUiDiagnostics(
            $"LoadRemoteUnifiedRowsAsync start tag={tag} version={version} servers={_settings.OrynivoServers?.Count ?? 0}");
        var tasks = (_settings.OrynivoServers ?? [])
            .Select(server => LoadRemoteUnifiedServerRowsAsync(
                tag,
                server,
                version,
                cancellationToken,
                diagnosticStopwatch))
            .ToArray();
        var rows = (await Task.WhenAll(tasks)).SelectMany(serverRows => serverRows).ToList();
        LogUiDiagnostics(
            $"LoadRemoteUnifiedRowsAsync finish tag={tag} rows={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
        return rows;
    }

    /// <summary>Loads one remote server's shared library rows with an independent timeout.</summary>
    /// <param name="tag">Shared Artists, Albums, or Tracks view tag.</param>
    /// <param name="server">Owning Orynivo Server.</param>
    /// <param name="version">Navigation load version.</param>
    /// <param name="cancellationToken">Navigation cancellation token.</param>
    /// <param name="diagnosticStopwatch">Shared diagnostic stopwatch.</param>
    /// <returns>Mapped rows from this server, or an empty list when unavailable.</returns>
    private async Task<List<ContentRow>> LoadRemoteUnifiedServerRowsAsync(
        string tag,
        OrynivoServerSettings server,
        int version,
        CancellationToken cancellationToken,
        Stopwatch diagnosticStopwatch)
    {
        if (cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
            return [];
        try
        {
            LogUiDiagnostics(
                $"LoadRemoteUnifiedRowsAsync server start tag={tag} server={server.Name} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var provider = CreateOrynivoCatalogProvider(server);
            var serverRows = tag switch
            {
                "Artists" => (await LoadAllOrynivoArtistsAsync(server, provider, linkedCts.Token))
                    .Where(artist => !_artistFavoritesOnly || artist.IsFavorite)
                    .Select(artist => ToCatalogArtistContentRow(artist, server))
                    .ToList(),
                "Albums" => (await LoadAllOrynivoAlbumsAsync(server, provider, linkedCts.Token))
                    .Where(album => !_albumFavoritesOnly || album.IsFavorite)
                    .Select(album => ToCatalogAlbumContentRow(album, server))
                    .ToList(),
                "Tracks" => (await LoadRemoteTracksForUnifiedViewAsync(server, provider, linkedCts.Token))
                    .Select(track => ToCatalogTrackContentRow(track, server))
                    .ToList(),
                _ => []
            };
            LogUiDiagnostics(
                $"LoadRemoteUnifiedRowsAsync server rows loaded tag={tag} server={server.Name} count={serverRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
            return cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion
                ? []
                : serverRows;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            LogUiDiagnostics(
                $"LoadRemoteUnifiedRowsAsync server failed tag={tag} server={server.Name} error={ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Returns cached plain or synchronized lyrics for the current track.</summary>
    /// <returns>Cached lyrics, or <see langword="null"/> when no eligible track or lyrics exist.</returns>
    private async Task<string?> GetCurrentCachedLyricsForToolAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return null;
        try
        {
            var provider = _currentNowPlayingProvider ?? _localNowPlayingProvider;
            var lyrics = await provider.GetCachedLyricsAsync(BuildNowPlayingTrackContext(_currentFilePath));
            return !string.IsNullOrWhiteSpace(lyrics?.Plain)
                ? lyrics.Plain
                : lyrics?.Synced;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Starts a normal library scan on the configured server with the supplied name.</summary>
    /// <param name="serverName">Exact server display name.</param>
    /// <returns><see langword="true"/> when the server exists and accepted the scan request.</returns>
    private async Task<bool> TriggerOrynivoServerScanByNameAsync(string serverName)
    {
        var server = (_settings.OrynivoServers ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, serverName?.Trim(), StringComparison.OrdinalIgnoreCase));
        return server is not null && await _orynivoClient.TriggerScanAsync(server);
    }

    private async Task<IReadOnlyList<LibraryCatalogTrack>> LoadRemoteTracksForUnifiedViewAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        if (HasActiveFilters)
        {
            var facets = await _orynivoClient.GetTrackFacetsAsync(server, cancellationToken);
            var ids = facets
                .Select(facet => facet with
                {
                    IsFavorite = IsOrynivoFavorite(server, "Track", facet.Id),
                    SourceKey = GetServerSourceKey(server.Id)
                })
                .Where(facet => MatchesTrackFilters(facet))
                .Select(facet => facet.Id)
                .ToList();
            return ids.Count == 0
                ? []
                : await provider.GetTracksByIdsAsync(ids, cancellationToken);
        }

        return await ResolveOrynivoTrackRowsAsync(server, provider, cancellationToken);
    }

    private void BindUnifiedRows(string tag, List<ContentRow> rows)
    {
        rows = SortUnifiedRows(rows);
        if (tag == "Albums")
            rows = MergeLogicalAlbumRows(rows);
        ApplyColumns(tag);
        ContentDataGrid.ItemsSource = rows;
        BindUnifiedArtworkRowsIfVisible(tag, rows);
        UpdateAlphabetIndex(rows, true);
        UpdateUnifiedContentCount(tag, rows.Count);
    }

    private void BindUnifiedArtworkRowsIfVisible(string tag, IReadOnlyList<ContentRow> rows)
    {
        if (tag == "Albums" && _showAlbumArtworkView)
            BindArtworkRows(tag, rows);
        else if (tag == "Artists" && _showArtistArtworkView)
            BindArtworkRows(tag, rows);
    }

    private void UpdateUnifiedContentCount(string tag, int count)
    {
        ContentCountTextBlock.Text = tag == "Tracks"
            ? LocalizationManager.FormatTrackCount(count)
            : LocalizationManager.FormatEntryCount(count);
    }

    private static List<ContentRow> SortUnifiedRows(IEnumerable<ContentRow> rows) =>
        rows
            .OrderBy(row => row.AlphabetIndexText ?? row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.SourceName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static List<ContentRow> MergeUnifiedArtistRows(IEnumerable<ContentRow> rows) =>
        rows
            .GroupBy(row => ArtistNameNormalizer.CreateComparisonKey(row.Title), StringComparer.Ordinal)
            .Select(group =>
            {
                var candidates = group.ToList();
                var row = candidates.FirstOrDefault(candidate => candidate.EntityType == "Artist")
                          ?? candidates[0];
                if (candidates.Count > 1)
                {
                    var imageSource = candidates
                        .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ArtworkPath))
                        .OrderByDescending(candidate => candidate.ImageIsManual)
                        .ThenByDescending(candidate => candidate.ProfileFetchedAt ?? 0)
                        .FirstOrDefault();
                    var profileSource = candidates
                        .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Biography))
                        .OrderByDescending(candidate => candidate.ProfileFetchedAt ?? 0)
                        .FirstOrDefault();
                    if (imageSource is not null)
                    {
                        row.ArtworkPath = imageSource.ArtworkPath;
                        row.ThumbnailPath = imageSource.ThumbnailPath;
                        row.ImageIsManual = imageSource.ImageIsManual;
                    }
                    if (profileSource is not null)
                    {
                        row.Biography = profileSource.Biography;
                        row.SourceUrl = profileSource.SourceUrl;
                        row.ProfileLanguage = profileSource.ProfileLanguage;
                        row.ProfileFetchedAt = profileSource.ProfileFetchedAt;
                    }
                    row.EntityType = "UnifiedArtist";
                    row.IsFavorite = candidates.Any(candidate => candidate.IsFavorite);
                }
                return row;
            })
            .OrderBy(row => row.AlphabetIndexText ?? row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static List<ContentRow> MergeLogicalAlbumRows(IEnumerable<ContentRow> rows) =>
        rows
            .Where(row => IsKnownAlbumTitle(row.Title))
            .GroupBy(row =>
            {
                var title = row.Title?.Trim() ?? string.Empty;
                var artistKey = ArtistNameNormalizer.CreateComparisonKey(row.Artist);
                return string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artistKey)
                    ? $"{row.SourceKey}\u001f{row.Id?.ToString(CultureInfo.InvariantCulture)}"
                    : $"{title.ToUpperInvariant()}\u001f{artistKey}";
            }, StringComparer.Ordinal)
            .Select(group =>
            {
                var candidates = group.ToList();
                var row = candidates
                    .OrderByDescending(candidate => candidate.OrynivoServer is null)
                    .ThenByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.ArtworkPath))
                    .ThenBy(candidate => candidate.Id)
                    .First();
                var artworkSource = candidates.FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.ArtworkPath));
                if (artworkSource is not null)
                {
                    row.ArtworkPath = artworkSource.ArtworkPath;
                    row.ThumbnailPath = artworkSource.ThumbnailPath;
                }
                row.LogicalAlbumIds = candidates
                    .Select(candidate => candidate.AlbumId ?? candidate.Id)
                    .OfType<long>()
                    .Distinct()
                    .ToList();
                row.LogicalAlbumParts = candidates
                    .Select(candidate => new LogicalAlbumPart(
                        (candidate.AlbumId ?? candidate.Id)!.Value,
                        candidate.ArtistId,
                        candidate.OrynivoServer))
                    .DistinctBy(part => $"{part.Server?.Id ?? LocalSourceKey}:{part.AlbumId}")
                    .ToList();
                if (candidates.Select(candidate => candidate.SourceKey).Distinct(StringComparer.Ordinal).Skip(1).Any())
                    row.EntityType = "UnifiedAlbum";
                row.IsFavorite = candidates.Any(candidate => candidate.IsFavorite);
                return row;
            })
            .OrderBy(row => row.AlphabetIndexText ?? row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.SourceName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static bool IsKnownAlbumTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var normalized = title.Trim();
        if (string.Equals(normalized, LocalizationManager.Current.Unknown, StringComparison.CurrentCultureIgnoreCase))
            return false;

        return normalized.ToUpperInvariant() is not
            ("UNKNOWN" or "(UNKNOWN)" or
             "UNBEKANNT" or "(UNBEKANNT)" or
             "INCONNU" or "(INCONNU)" or
             "DESCONOCIDO" or "(DESCONOCIDO)");
    }

    private static ContentRow ToTrackContentRow(TrackListInfo t) => new()
    {
        Title = t.Title?.Trim() ?? t.FileName.Trim(),
        AlphabetIndexText = t.SortTitle?.Trim() ?? t.Title?.Trim() ?? t.FileName.Trim(),
        Id = t.Id,
        Artist = t.Artist,
        Album = t.Album,
        AlbumArtist = t.AlbumArtist,
        Year = t.Year?.ToString(CultureInfo.CurrentCulture),
        TrackNumber = FormatPartNumber(t.TrackNumber, t.TrackTotal),
        DiscNumber = FormatPartNumber(t.DiscNumber, t.DiscTotal),
        Duration = FormatSeconds(t.Duration),
        Genre = t.Genre,
        Format = t.Format?.ToUpperInvariant(),
        Bitrate = t.Bitrate is > 0 ? $"{t.Bitrate:N0} kbps" : null,
        SampleRate = t.SampleRate is > 0 ? $"{t.SampleRate:N0} Hz" : null,
        BitDepth = t.BitDepth is > 0 ? $"{t.BitDepth:N0} Bit" : null,
        Channels = t.Channels?.ToString(CultureInfo.CurrentCulture),
        Composer = t.Composer,
        Bpm = t.Bpm?.ToString(CultureInfo.CurrentCulture),
        FileName = t.FileName,
        FileSize = FormatFileSize(t.FileSize),
        AddedAt = DateTimeOffset.FromUnixTimeSeconds(t.AddedAt)
            .ToLocalTime()
            .ToString("d", CultureInfo.CurrentCulture),
        ReplayGainTrack = FormatReplayGainDisplay(t.ReplayGainTrack),
        ReplayGainAlbum = FormatReplayGainDisplay(t.ReplayGainAlbum),
        UserRating = t.UserRating,
        MusicBrainzRating = t.MusicBrainzRating,
        MusicBrainzRatingVotes = t.MusicBrainzRatingVotes,
        MusicBrainzTrackId = t.MusicBrainzTrackId,
        MusicBrainzRatingFetchedAt = t.MusicBrainzRatingFetchedAt,
        FilePath = t.Path,
        SourcePath = t.Path,
        IsFavorite = t.IsFavorite,
        ArtistId = t.ArtistId,
        AlbumId = t.AlbumId,
        KnownDuration = t.Duration.HasValue ? TimeSpan.FromSeconds(t.Duration.Value) : null,
        EntityType = "Track"
    };

    private ContentRow ToCatalogTrackContentRow(LibraryCatalogTrack track, OrynivoServerSettings? server = null, bool registerRemoteMetadata = true)
    {
        server ??= track.Source == LibraryCatalogSource.OrynivoServer ? _activeOrynivoServer : null;
        var row = new ContentRow
        {
            Title = track.Title?.Trim() ?? track.FileName.Trim(),
            AlphabetIndexText = track.SortTitle?.Trim() ?? track.Title?.Trim() ?? track.FileName.Trim(),
            Id = track.Id,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            Year = track.Year?.ToString(CultureInfo.CurrentCulture),
            TrackNumber = FormatPartNumber(track.TrackNumber, track.TrackTotal),
            DiscNumber = FormatPartNumber(track.DiscNumber, track.DiscTotal),
            Duration = FormatSeconds(track.Duration),
            Genre = track.Genre,
            Format = track.Format?.ToUpperInvariant(),
            Bitrate = track.Bitrate is > 0 ? $"{track.Bitrate:N0} kbps" : null,
            SampleRate = track.SampleRate is > 0 ? $"{track.SampleRate:N0} Hz" : null,
            SampleRateHz = track.SampleRate,
            BitDepth = track.BitDepth is > 0 ? $"{track.BitDepth:N0} Bit" : null,
            Channels = track.Channels?.ToString(CultureInfo.CurrentCulture),
            ChannelCount = track.Channels,
            Composer = track.Composer,
            Bpm = track.Bpm?.ToString(CultureInfo.CurrentCulture),
            FileName = track.FileName,
            FileSize = FormatFileSize(track.FileSize),
            AddedAt = track.AddedAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(track.AddedAt.Value)
                    .ToLocalTime()
                    .ToString("d", CultureInfo.CurrentCulture)
                : null,
            ReplayGainTrack = FormatReplayGainDisplay(track.ReplayGainTrack),
            ReplayGainAlbum = FormatReplayGainDisplay(track.ReplayGainAlbum),
            UserRating = track.UserRating,
            MusicBrainzRating = track.MusicBrainzRating,
            MusicBrainzRatingVotes = track.MusicBrainzRatingVotes,
            MusicBrainzTrackId = track.MusicBrainzTrackId,
            MusicBrainzRatingFetchedAt = track.MusicBrainzRatingFetchedAt,
            FilePath = track.PlaybackPath,
            SourcePath = track.SourcePath,
            IsFavorite = track.IsFavorite,
            ArtistId = track.ArtistId,
            AlbumId = track.AlbumId,
            EntityType = track.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoTrack" : "Track",
            ExternalId = track.Source == LibraryCatalogSource.OrynivoServer
                ? track.Id.ToString(CultureInfo.InvariantCulture)
                : null,
            KnownDuration = track.KnownDuration
        };
        if (track.Source == LibraryCatalogSource.OrynivoServer && server is not null)
        {
            row.OrynivoServer = server;
            if (registerRemoteMetadata) _orynivoTracksByUrl[track.PlaybackPath] = row;
        }
        return row;
    }

    private static ContentRow ToCatalogAlbumContentRow(LibraryCatalogAlbum album, OrynivoServerSettings? server = null) => new()
    {
        Id = album.Id,
        AlbumId = album.Id,
        ArtistId = album.ArtistId,
        Title = string.IsNullOrEmpty(album.Title) ? LocalizationManager.Current.Unknown : album.Title,
        AlphabetIndexText = string.IsNullOrEmpty(album.Title) ? LocalizationManager.Current.Unknown : album.Title,
        Artist = string.IsNullOrEmpty(album.DisplayArtist) ? null : album.DisplayArtist,
        Year = album.Year?.ToString(CultureInfo.CurrentCulture),
        ArtworkPath = album.ArtworkPath,
        ThumbnailPath = album.ThumbnailPath,
        IsFavorite = album.IsFavorite,
        EntityType = album.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoAlbum" : "Album",
        ExternalId = album.Source == LibraryCatalogSource.OrynivoServer
            ? album.Id.ToString(CultureInfo.InvariantCulture)
            : null,
        OrynivoServer = album.Source == LibraryCatalogSource.OrynivoServer ? server : null
    };

    private static ContentRow ToCatalogArtistContentRow(LibraryCatalogArtist artist, OrynivoServerSettings? server = null) => new()
    {
        Id = artist.Id,
        ArtistId = artist.Id,
        Title = string.IsNullOrEmpty(artist.Name) ? LocalizationManager.Current.Unknown : artist.Name,
        AlphabetIndexText = string.IsNullOrEmpty(artist.Name) ? LocalizationManager.Current.Unknown : artist.Name,
        IsFavorite = artist.IsFavorite,
        ArtworkPath = artist.ArtworkPath,
        ThumbnailPath = artist.ThumbnailPath,
        Biography = artist.Biography,
        SourceUrl = artist.SourceUrl,
        ProfileLanguage = artist.ProfileLanguage,
        ProfileFetchedAt = artist.ProfileFetchedAt,
        ImageIsManual = artist.ImageIsManual,
        EntityType = artist.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoArtist" : "Artist",
        ExternalId = artist.Source == LibraryCatalogSource.OrynivoServer
            ? artist.Id.ToString(CultureInfo.InvariantCulture)
            : null,
        OrynivoServer = artist.Source == LibraryCatalogSource.OrynivoServer ? server : null,
        FilePath = ""
    };

    private static TrackListInfo ToTrackListInfo(TrackRecord track) => new(
        track.Path, track.FileName, track.Title, track.Artist, track.Album, track.AlbumArtist,
        track.Genre, track.Format, track.Bitrate, track.Duration, track.SortTitle, track.Id,
        false, track.Year, track.TrackNumber, track.TrackTotal, track.DiscNumber,
        track.DiscTotal, track.SampleRate, track.BitDepth, track.Channels, track.Composer,
        track.Bpm, track.FileSize, track.AddedAt, track.ReplayGainTrack, track.ReplayGainAlbum,
        UserRating: track.UserRating,
        MusicBrainzRating: track.MusicBrainzRating,
        MusicBrainzRatingVotes: track.MusicBrainzRatingVotes,
        MusicBrainzTrackId: track.MusicBrainzTrackId,
        MusicBrainzRatingFetchedAt: track.MusicBrainzRatingFetchedAt);

    private static string? FormatPartNumber(int? number, int? total) =>
        number is null ? null : total is > 0 ? $"{number}/{total}" : number.Value.ToString(CultureInfo.CurrentCulture);

    private static string? FormatReplayGainDisplay(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{value} dB";

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes is null || bytes < 0)
            return null;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
