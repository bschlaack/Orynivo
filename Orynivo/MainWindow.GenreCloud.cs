using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;

namespace Orynivo;

public partial class MainWindow
{
    private sealed record GenreCloudSource(
        OrynivoServerSettings? Server,
        GenreCloudSnapshot Snapshot);

    private sealed record GenreCloudCandidate(
        OrynivoServerSettings? Server,
        GenreCloudTrackCandidate Track,
        double Score);

    private sealed record GenreCloudBackgroundImageSource(
        bool IsRemote,
        string Location);

    private static readonly TimeSpan GenreCloudBackgroundLifetime = TimeSpan.FromHours(24);
    private const int GenreCloudBackgroundMaximumImages = 32;
    private const int GenreCloudBackgroundHeight = 400;

    private CancellationTokenSource? _genreCloudCts;
    private string? _genreCloudSelectedKey;
    private List<GenreCloudNode> _genreCloudNodes = [];
    private List<ContentRow> _genreCloudTrackRows = [];
    private List<ContentRow> _genreCloudAlbumRows = [];

    /// <summary>Starts Infinite Mix with every leaf genre represented by the current cloud level.</summary>
    private async void GenreCloudInfiniteMixButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if (_infiniteMixLoading)
            return;

        var representedKeys = _genreCloudNodes.Count > 0
            ? _genreCloudNodes.Select(node => node.Key)
            : string.IsNullOrWhiteSpace(_genreCloudSelectedKey)
                ? Enumerable.Empty<string>()
                : [_genreCloudSelectedKey];
        var leafKeys = GenreCloudService.ResolveLeafGenreKeys(representedKeys);
        if (leafKeys.Count == 0)
            return;

        StopInfiniteMix();
        _settings.InfiniteMix.IncludedGenres = leafKeys.ToList();
        await Task.Run(() => new SettingsStore().Save(_settings));
        await StartInfiniteMixAsync(editSettings: false);
    }

    /// <summary>Loads and renders one level of the unified local and remote genre cloud.</summary>
    /// <param name="parentKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
    private async Task ShowGenreCloudAsync(string? parentKey)
    {
        _genreCloudSelectedKey = parentKey;
        CancelAndDispose(ref _genreCloudCts);
        _genreCloudCts = new CancellationTokenSource();
        var cancellationToken = _genreCloudCts.Token;
        try
        {
            if (GenreCloudNodesCanvas.Children.Count > 0)
            {
                GenreCloudNodesCanvas.Opacity = 0;
                await Task.Delay(140, cancellationToken);
            }
            GenreCloudNodesCanvas.Children.Clear();
            GenreCloudBreadcrumbPanel.Children.Clear();
            GenreCloudEmptyTextBlock.IsVisible = false;
            GenreCloudLeafTextBlock.IsVisible = false;
            GenreCloudBackgroundImage.Opacity = 0;
            GenreCloudBackgroundShade.Opacity = 0;
            ContentDataGrid.ItemsSource = null;
            AlbumArtworkListBox.ItemsSource = null;
            ContentCountTextBlock.Text = string.Empty;

            var localTask = Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return new GenreCloudSource(
                    null,
                    GenreCloudService.BuildSnapshot(db.GetTrackFacets(), parentKey, 500));
            }, cancellationToken);
            var remoteTasks = _settings.OrynivoServers
                .Select(server => LoadRemoteGenreCloudAsync(server, parentKey, cancellationToken))
                .ToArray();
            var sources = new List<GenreCloudSource> { await localTask };
            sources.AddRange(await Task.WhenAll(remoteTasks));
            cancellationToken.ThrowIfCancellationRequested();

            var nodes = sources
                .SelectMany(source => source.Snapshot.Nodes)
                .GroupBy(node => node.Key, StringComparer.Ordinal)
                .Select(group => new GenreCloudNode(
                    group.Key,
                    group.First().DisplayName,
                    group.Sum(node => node.TrackCount),
                    group.Sum(node => node.AlbumCount),
                    group.Any(node => node.HasChildren)))
                .OrderByDescending(node => node.TrackCount)
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _genreCloudNodes = nodes;
            BuildGenreCloudBreadcrumb(parentKey);
            BuildGenreCloudNodes(nodes, GenreAlbumRecommendationsRadioButton.IsChecked == true);
            GenreCloudNodesCanvas.Opacity = 1;
            GenreCloudEmptyTextBlock.IsVisible = nodes.Count == 0 && string.IsNullOrWhiteSpace(parentKey);
            GenreCloudLeafTextBlock.IsVisible = nodes.Count == 0 && !string.IsNullOrWhiteSpace(parentKey);
            GenreCloudLeafTextBlock.Text = GenreCloudLeafTextBlock.IsVisible
                ? GetGenreCloudDisplayName(parentKey!)
                : string.Empty;

            var listeningWeights = await Task.Run(LoadGenreListeningWeights, cancellationToken);
            var candidates = sources
                .SelectMany(source => source.Snapshot.Candidates.Select(candidate =>
                    new GenreCloudCandidate(
                        source.Server,
                        candidate,
                        CalculateGenreCandidateScore(source.Server, candidate, listeningWeights))))
                .OrderByDescending(candidate => candidate.Score)
                .Take(100)
                .ToList();
            var rows = await ResolveGenreCandidateRowsAsync(candidates, cancellationToken);
            var albumRows = await ResolveGenreCandidateAlbumRowsAsync(rows, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _genreCloudTrackRows = rows;
            _genreCloudAlbumRows = albumRows;
            ApplyGenreRecommendationMode();
            var backgroundMode = _settings.GenreCloudBackground;
            var backgroundWidth = (int)Math.Clamp(
                Math.Ceiling(Math.Max(GenreCloudSurface.Bounds.Width, 1000) / 250d) * 250,
                1000,
                4000);
            var backgroundColumns = Math.Clamp(backgroundWidth / 250, 4, 16);
            var backgroundImageCount = Math.Min(
                backgroundColumns * 2,
                GenreCloudBackgroundMaximumImages);
            _ = UpdateGenreCloudBackgroundAsync(
                parentKey,
                backgroundMode == GenreCloudBackgroundMode.Albums ? albumRows : rows,
                backgroundMode,
                backgroundWidth,
                backgroundColumns,
                backgroundImageCount,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Navigation or a newer drill-down superseded this request.
        }
    }

    /// <summary>Loads or creates the muted artist collage for the selected genre level.</summary>
    /// <param name="genreKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
    /// <param name="rows">Ranked track recommendations from which artist identities are selected.</param>
    /// <param name="mode">Configured background artwork source.</param>
    /// <param name="renderWidth">Width of the cached mosaic in pixels.</param>
    /// <param name="columnCount">Maximum tile columns appropriate for the current surface width.</param>
    /// <param name="imageCount">Maximum number of unique images to render.</param>
    /// <param name="cancellationToken">Token that cancels stale genre navigation.</param>
    private async Task UpdateGenreCloudBackgroundAsync(
        string? genreKey,
        IReadOnlyList<ContentRow> rows,
        GenreCloudBackgroundMode mode,
        int renderWidth,
        int columnCount,
        int imageCount,
        CancellationToken cancellationToken)
    {
        try
        {
            await UpdateGenreCloudBackgroundCoreAsync(
                genreKey,
                rows,
                mode,
                renderWidth,
                columnCount,
                imageCount,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Navigation to another genre invalidated this background.
        }
        catch
        {
            // Decorative artwork must never prevent genre navigation or recommendations.
        }
    }

    /// <summary>Performs cache lookup, image resolution, rendering, and guarded UI assignment.</summary>
    /// <param name="genreKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
    /// <param name="rows">Ranked track recommendations from which artist identities are selected.</param>
    /// <param name="mode">Configured background artwork source.</param>
    /// <param name="renderWidth">Width of the cached mosaic in pixels.</param>
    /// <param name="columnCount">Maximum tile columns appropriate for the current surface width.</param>
    /// <param name="imageCount">Maximum number of unique images to render.</param>
    /// <param name="cancellationToken">Token that cancels stale genre navigation.</param>
    private async Task UpdateGenreCloudBackgroundCoreAsync(
        string? genreKey,
        IReadOnlyList<ContentRow> rows,
        GenreCloudBackgroundMode mode,
        int renderWidth,
        int columnCount,
        int imageCount,
        CancellationToken cancellationToken)
    {
        if (mode == GenreCloudBackgroundMode.None)
            return;

        var cachePath = GetGenreCloudBackgroundCachePath(genreKey, mode, columnCount);
        if (!IsFreshGenreCloudBackground(cachePath))
        {
            var imageSources = mode == GenreCloudBackgroundMode.Albums
                ? ResolveGenreCloudAlbumImages(rows, imageCount * 3)
                : await ResolveGenreCloudArtistImagesAsync(rows, imageCount * 3, cancellationToken);
            var imageData = await LoadGenreCloudImageDataAsync(
                imageSources,
                imageCount,
                cancellationToken);
            if (imageData.Count > 0)
            {
                await Task.Run(
                    () => RenderGenreCloudBackground(
                        cachePath,
                        imageData,
                        renderWidth,
                        columnCount),
                    cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(cachePath) || !string.Equals(_genreCloudSelectedKey, genreKey, StringComparison.Ordinal))
            return;

        var image = await Task.Run(() => CreateArtworkImage(cachePath, renderWidth), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (image is null || !string.Equals(_genreCloudSelectedKey, genreKey, StringComparison.Ordinal))
            return;

        GenreCloudBackgroundImage.Source = image;
        GenreCloudBackgroundImage.Opacity = _settings.GenreCloudBackgroundOpacity;
        GenreCloudBackgroundShade.Opacity = 0.52;
    }

    /// <summary>Resolves cached local and remote artist images in recommendation order.</summary>
    /// <param name="rows">Ranked genre recommendation rows.</param>
    /// <param name="sourceLimit">Maximum number of candidate image locations to return.</param>
    /// <param name="cancellationToken">Token that cancels remote catalog requests.</param>
    /// <returns>At most sixteen distinct artist image locations.</returns>
    private async Task<List<GenreCloudBackgroundImageSource>> ResolveGenreCloudArtistImagesAsync(
        IReadOnlyList<ContentRow> rows,
        int sourceLimit,
        CancellationToken cancellationToken)
    {
        var localIds = rows
            .Where(row => row.OrynivoServer is null && row.ArtistId is not null)
            .Select(row => row.ArtistId!.Value)
            .Distinct()
            .ToArray();
        var localImages = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return localIds
                .Select(id => (Id: id, Path: db.GetArtistById(id)?.ImagePath))
                .Where(item => !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
                .ToDictionary(item => item.Id, item => item.Path!);
        }, cancellationToken);

        var remoteImages = new Dictionary<string, Dictionary<long, string>>(StringComparer.Ordinal);
        foreach (var server in rows
                     .Where(row => row.OrynivoServer is not null && row.ArtistId is not null)
                     .Select(row => row.OrynivoServer!)
                     .DistinctBy(server => server.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var artists = await _orynivoClient.GetArtistsAsync(server, cancellationToken);
                remoteImages[server.Id] = artists
                    .Where(artist => artist.HasImage)
                    .ToDictionary(
                        artist => artist.Id,
                        artist => OrynivoServerClient.GetArtistArtworkUrl(server, artist.Id));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                remoteImages[server.Id] = [];
            }
        }

        var result = new List<GenreCloudBackgroundImageSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.ArtistId is not long artistId)
                continue;

            var identity = row.OrynivoServer is null
                ? $"local:{artistId}"
                : $"server:{row.OrynivoServer.Id}:{artistId}";
            if (!seen.Add(identity))
                continue;

            string? location = null;
            if (row.OrynivoServer is null)
                localImages.TryGetValue(artistId, out location);
            else if (remoteImages.TryGetValue(row.OrynivoServer.Id, out var serverImages))
                serverImages.TryGetValue(artistId, out location);

            if (!string.IsNullOrWhiteSpace(location))
                result.Add(new GenreCloudBackgroundImageSource(row.OrynivoServer is not null, location));
            if (result.Count >= sourceLimit)
                break;
        }

        return result;
    }

    /// <summary>Resolves distinct recommendation album covers in rank order.</summary>
    /// <param name="rows">Ranked genre recommendation rows.</param>
    /// <param name="sourceLimit">Maximum number of candidate image locations to return.</param>
    /// <returns>At most sixteen local or remote cover image locations.</returns>
    private static List<GenreCloudBackgroundImageSource> ResolveGenreCloudAlbumImages(
        IReadOnlyList<ContentRow> rows,
        int sourceLimit)
    {
        var result = new List<GenreCloudBackgroundImageSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var location = !string.IsNullOrWhiteSpace(row.ArtworkPath)
                ? row.ArtworkPath
                : row.ThumbnailPath;
            if (string.IsNullOrWhiteSpace(location))
                continue;

            var identity = row.OrynivoServer is null
                ? $"local:{row.AlbumId}:{location}"
                : $"server:{row.OrynivoServer.Id}:{row.AlbumId}";
            if (!seen.Add(identity))
                continue;
            if (!IsHttpUrl(location) && !File.Exists(location))
                continue;

            result.Add(new GenreCloudBackgroundImageSource(IsHttpUrl(location), location));
            if (result.Count >= sourceLimit)
                break;
        }

        return result;
    }

    /// <summary>Reads artist image bytes without persisting authenticated remote URLs.</summary>
    /// <param name="images">Resolved local paths or in-memory remote artwork URLs.</param>
    /// <param name="imageLimit">Maximum number of content-distinct image payloads to return.</param>
    /// <param name="cancellationToken">Token that cancels file and network reads.</param>
    /// <returns>Decodable image payloads in recommendation order.</returns>
    private static async Task<List<byte[]>> LoadGenreCloudImageDataAsync(
        IReadOnlyList<GenreCloudBackgroundImageSource> images,
        int imageLimit,
        CancellationToken cancellationToken)
    {
        var result = new List<byte[]>();
        var contentFingerprints = new HashSet<ulong>();
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                byte[] data;
                if (!image.IsRemote)
                {
                    data = await File.ReadAllBytesAsync(image.Location, cancellationToken);
                }
                else
                {
                    var cachedPath = GetRemoteArtworkCachePath(image.Location, 320);
                    if (File.Exists(cachedPath))
                        data = await File.ReadAllBytesAsync(cachedPath, cancellationToken);
                    else
                    {
                        data = await RemoteArtworkHttpClient.GetByteArrayAsync(image.Location, cancellationToken);
                        WriteRemoteArtworkCache(image.Location, data);
                    }
                }

                if (data.Length is > 0 and <= 16 * 1024 * 1024 &&
                    TryCreateGenreCloudImageFingerprint(data, out var fingerprint) &&
                    contentFingerprints.Add(fingerprint))
                {
                    result.Add(data);
                    if (result.Count >= imageLimit)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A missing artist image must not prevent the remaining collage from loading.
            }
        }

        return result;
    }

    /// <summary>Creates a compact perceptual fingerprint so differently encoded copies are not tiled twice.</summary>
    /// <param name="imageData">Encoded source image.</param>
    /// <param name="fingerprint">Receives the 8×8 average-luminance hash.</param>
    /// <returns><see langword="true"/> when the image could be decoded.</returns>
    private static bool TryCreateGenreCloudImageFingerprint(
        byte[] imageData,
        out ulong fingerprint)
    {
        fingerprint = 0;
        using var source = SKBitmap.Decode(imageData);
        if (source is null || source.Width <= 0 || source.Height <= 0)
            return false;
        using var thumbnail = source.Resize(
            new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.Medium);
        if (thumbnail is null)
            return false;

        Span<float> luminance = stackalloc float[64];
        float total = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var color = thumbnail.GetPixel(x, y);
                var value = 0.2126f * color.Red + 0.7152f * color.Green + 0.0722f * color.Blue;
                luminance[y * 8 + x] = value;
                total += value;
            }
        }

        var average = total / luminance.Length;
        for (var index = 0; index < luminance.Length; index++)
        {
            if (luminance[index] >= average)
                fingerprint |= 1UL << index;
        }

        return true;
    }

    /// <summary>Renders a grayscale, proportionally fitted tile collage into the genre cache.</summary>
    /// <param name="cachePath">Destination JPEG path.</param>
    /// <param name="imageData">Source image payloads.</param>
    /// <param name="renderWidth">Output mosaic width in pixels.</param>
    /// <param name="maximumColumns">Maximum number of columns for the current surface width.</param>
    private static void RenderGenreCloudBackground(
        string cachePath,
        IReadOnlyList<byte[]> imageData,
        int renderWidth,
        int maximumColumns)
    {
        var bitmaps = imageData
            .Select(SKBitmap.Decode)
            .Where(bitmap => bitmap is not null && bitmap.Width > 0 && bitmap.Height > 0)
            .Cast<SKBitmap>()
            .ToList();
        if (bitmaps.Count == 0)
            return;

        var temporaryPath = cachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var surface = new SKBitmap(
                renderWidth,
                GenreCloudBackgroundHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);
            using var canvas = new SKCanvas(surface);
            canvas.Clear(new SKColor(10, 20, 34));
            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Medium,
                ColorFilter = SKColorFilter.CreateColorMatrix(
                [
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0,       0,       0,       1, 0
                ])
            };

            var rowCount = bitmaps.Count > maximumColumns ? 2 : 1;
            var columnCount = (int)Math.Ceiling(bitmaps.Count / (double)rowCount);
            var tileWidth = renderWidth / (float)columnCount;
            var tileHeight = GenreCloudBackgroundHeight / (float)rowCount;
            var verticalOffset = (GenreCloudBackgroundHeight - rowCount * tileHeight) / 2f;
            for (var index = 0; index < bitmaps.Count; index++)
            {
                var bitmap = bitmaps[index];
                var row = index / columnCount;
                var firstIndexInRow = row * columnCount;
                var itemsInRow = Math.Min(columnCount, bitmaps.Count - firstIndexInRow);
                var column = index - firstIndexInRow;
                var horizontalOffset = (renderWidth - itemsInRow * tileWidth) / 2f;
                var destination = new SKRect(
                    horizontalOffset + column * tileWidth,
                    verticalOffset + row * tileHeight,
                    horizontalOffset + (column + 1) * tileWidth,
                    verticalOffset + (row + 1) * tileHeight);
                var scale = Math.Min(tileWidth / bitmap.Width, tileHeight / bitmap.Height);
                var fittedWidth = bitmap.Width * scale;
                var fittedHeight = bitmap.Height * scale;
                var fittedDestination = new SKRect(
                    destination.MidX - fittedWidth / 2f,
                    destination.MidY - fittedHeight / 2f,
                    destination.MidX + fittedWidth / 2f,
                    destination.MidY + fittedHeight / 2f);

                canvas.DrawBitmap(
                    bitmap,
                    new SKRect(0, 0, bitmap.Width, bitmap.Height),
                    fittedDestination,
                    paint);
            }

            using var image = SKImage.FromBitmap(surface);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 82);
            using (var stream = File.Create(temporaryPath))
                encoded.SaveTo(stream);
            File.Move(temporaryPath, cachePath, true);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
            try { File.Delete(temporaryPath); }
            catch { }
        }
    }

    /// <summary>Returns the non-sensitive cache path for one genre taxonomy level.</summary>
    /// <param name="genreKey">Taxonomy key, or <see langword="null"/> for the root.</param>
    /// <param name="mode">Artwork source represented by the cached mosaic.</param>
    /// <param name="columnCount">Width-derived tile column count represented by the cache.</param>
    /// <returns>Absolute JPEG cache path.</returns>
    private static string GetGenreCloudBackgroundCachePath(
        string? genreKey,
        GenreCloudBackgroundMode mode,
        int columnCount)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v6|{mode}|{columnCount}|{genreKey ?? "root"}")));
        return AppPaths.GetDataPath("genre-cloud-backgrounds", $"{key}.jpg");
    }

    /// <summary>Checks whether a cached collage can be reused.</summary>
    /// <param name="cachePath">Absolute collage path.</param>
    /// <returns><see langword="true"/> when the collage is no older than one day.</returns>
    private static bool IsFreshGenreCloudBackground(string cachePath)
        => File.Exists(cachePath) &&
           DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) <= GenreCloudBackgroundLifetime;

    /// <summary>Collapses the Genre Cloud surfaces before an album detail view is displayed.</summary>
    private void HideGenreCloudForDetailView()
    {
        GenreCloudPanel.IsVisible = false;
        GenreCloudSurface.IsVisible = false;
    }

    /// <summary>Loads a compact genre snapshot from one server, with a facet fallback for older servers.</summary>
    private async Task<GenreCloudSource> LoadRemoteGenreCloudAsync(
        OrynivoServerSettings server,
        string? parentKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await _orynivoClient.GetGenreCloudAsync(server, parentKey, 500, cancellationToken);
        var returnedWrongLevel = !string.Equals(
            snapshot.ParentKey,
            parentKey,
            StringComparison.Ordinal);
        if (returnedWrongLevel || snapshot.Nodes.Count == 0 && snapshot.Candidates.Count == 0)
        {
            var facets = await _orynivoClient.GetTrackFacetsAsync(server, cancellationToken);
            snapshot = GenreCloudService.BuildSnapshot(facets, parentKey, 500);
        }
        return new GenreCloudSource(server, snapshot);
    }

    /// <summary>Returns taxonomy weights derived from listening time across all recorded playback sources.</summary>
    private static IReadOnlyDictionary<string, double> LoadGenreListeningWeights()
    {
        using var db = AudioDatabase.OpenDefault();
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (genre, seconds) in db.GetTopGenres(100))
        {
            foreach (var key in GenreCloudService.ResolveGenreKeys(genre))
                result[key] = result.GetValueOrDefault(key) + seconds;
        }
        return result;
    }

    /// <summary>Combines listening affinity, favorite state, and stable variation into a recommendation score.</summary>
    private double CalculateGenreCandidateScore(
        OrynivoServerSettings? server,
        GenreCloudTrackCandidate candidate,
        IReadOnlyDictionary<string, double> listeningWeights)
    {
        var isFavorite = server is null
            ? candidate.IsFavorite
            : IsOrynivoFavorite(server, "Track", candidate.TrackId);
        var affinity = listeningWeights
            .Where(pair => GenreCloudService.IsDescendantOrSelf(candidate.GenreKey, pair.Key) ||
                           GenreCloudService.IsDescendantOrSelf(pair.Key, candidate.GenreKey))
            .Sum(pair => pair.Value);
        var stableVariation = Math.Abs(HashCode.Combine(server?.Id, candidate.TrackId)) % 1000 / 1000d;
        return Math.Log10(1 + affinity) * 100 + (isFavorite ? 25 : 0) + stableVariation;
    }

    /// <summary>Resolves compact candidate identifiers to shared playable table rows.</summary>
    private async Task<List<ContentRow>> ResolveGenreCandidateRowsAsync(
        IReadOnlyList<GenreCloudCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<(string Source, long Id), ContentRow>();
        var localIds = candidates.Where(candidate => candidate.Server is null)
            .Select(candidate => candidate.Track.TrackId).Distinct().ToList();
        if (localIds.Count > 0)
        {
            var localTracks = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackListByIds(localIds);
            }, cancellationToken);
            foreach (var track in localTracks)
                resolved[("local", track.Id)] = ToTrackContentRow(track);
        }

        var remoteGroups = candidates.Where(candidate => candidate.Server is not null)
            .GroupBy(candidate => candidate.Server!.Id, StringComparer.Ordinal);
        foreach (var group in remoteGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = group.First().Server!;
            var ids = group.Select(candidate => candidate.Track.TrackId).Distinct().ToList();
            var tracks = await _orynivoClient.GetTracksByIdsAsync(server, ids, cancellationToken);
            foreach (var track in tracks)
                resolved[($"server:{server.Id}", track.Id)] = ToOrynivoTrackContentRow(server, track);
        }

        return candidates
            .Select(candidate => resolved.GetValueOrDefault((
                candidate.Server is null ? "local" : $"server:{candidate.Server.Id}",
                candidate.Track.TrackId)))
            .Where(row => row is not null)
            .Cast<ContentRow>()
            .DistinctBy(row => (row.OrynivoServer?.Id ?? "local", row.Id))
            .ToList();
    }

    /// <summary>Resolves the distinct albums represented by the ranked track recommendations.</summary>
    private async Task<List<ContentRow>> ResolveGenreCandidateAlbumRowsAsync(
        IReadOnlyList<ContentRow> trackRows,
        CancellationToken cancellationToken)
    {
        var result = new List<ContentRow>();
        var localAlbumIds = trackRows
            .Where(row => row.OrynivoServer is null && row.AlbumId.HasValue)
            .Select(row => row.AlbumId!.Value)
            .Distinct()
            .ToHashSet();
        if (localAlbumIds.Count > 0)
        {
            var localAlbums = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetAlbumsLite(includeArtwork: true)
                    .Where(album => localAlbumIds.Contains(album.Id))
                    .Select(album => new LibraryCatalogAlbum(
                        LibraryCatalogSource.Local,
                        album.Id,
                        album.Album,
                        album.DisplayArtist,
                        album.Year,
                        album.ArtworkPath,
                        album.ThumbnailPath,
                        album.IsFavorite,
                        album.ArtistId))
                    .ToList();
            }, cancellationToken);
            result.AddRange(localAlbums.Select(album => ToCatalogAlbumContentRow(album)));
        }

        foreach (var serverGroup in trackRows
                     .Where(row => row.OrynivoServer is not null && row.AlbumId.HasValue)
                     .GroupBy(row => row.OrynivoServer!.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = serverGroup.First().OrynivoServer!;
            var albumIds = serverGroup.Select(row => row.AlbumId!.Value).Distinct().ToHashSet();
            var provider = CreateOrynivoCatalogProvider(server);
            var albums = await LoadAllOrynivoAlbumsAsync(server, provider, cancellationToken);
            result.AddRange(albums
                .Where(album => albumIds.Contains(album.Id))
                .Select(album => ToCatalogAlbumContentRow(album, server)));
        }

        var rank = trackRows
            .Where(row => row.AlbumId.HasValue)
            .Select((row, index) => new
            {
                Key = (row.OrynivoServer?.Id ?? "local", row.AlbumId!.Value),
                Index = index
            })
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Index));
        return result
            .Where(row => IsKnownAlbumTitle(row.Title))
            .DistinctBy(row => (row.OrynivoServer?.Id ?? "local", row.AlbumId ?? row.Id ?? 0))
            .OrderBy(row => rank.GetValueOrDefault((
                row.OrynivoServer?.Id ?? "local",
                row.AlbumId ?? row.Id ?? 0), int.MaxValue))
            .Take(40)
            .ToList();
    }

    /// <summary>Switches the recommendation result between playable tracks and album artwork cards.</summary>
    private void ApplyGenreRecommendationMode()
    {
        if (_currentTopLevelTag != "GenreCloud")
            return;
        var showAlbums = GenreAlbumRecommendationsRadioButton.IsChecked == true;
        ContentDataGrid.IsVisible = !showAlbums;
        AlbumArtworkListBox.IsVisible = showAlbums;
        ArtistArtworkListBox.IsVisible = false;
        if (showAlbums)
        {
            _albumArtworkRows = _genreCloudAlbumRows;
            _visibleAlbumArtworkRows.Clear();
            foreach (var album in _genreCloudAlbumRows)
                _visibleAlbumArtworkRows.Add(album);
            AlbumArtworkListBox.ItemsSource = _visibleAlbumArtworkRows;
            QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(_genreCloudAlbumRows.Count);
        }
        else
        {
            ContentDataGrid.ItemsSource = _genreCloudTrackRows;
            AlbumArtworkListBox.ItemsSource = null;
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(_genreCloudTrackRows.Count);
        }
    }

    /// <summary>Handles an explicit click on the Track/Album recommendation-mode selector.</summary>
    private void GenreRecommendationMode_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
            return;
        var showAlbums = ReferenceEquals(radioButton, GenreAlbumRecommendationsRadioButton);
        GenreAlbumRecommendationsRadioButton.IsChecked = showAlbums;
        GenreTrackRecommendationsRadioButton.IsChecked = !showAlbums;
        BuildGenreCloudNodes(_genreCloudNodes, showAlbums);
        ApplyGenreRecommendationMode();
    }

    /// <summary>Builds clickable breadcrumbs from the selected taxonomy path.</summary>
    private void BuildGenreCloudBreadcrumb(string? parentKey)
    {
        AddGenreBreadcrumbButton(LocalizationManager.Current.AllGenres, null);
        if (string.IsNullOrWhiteSpace(parentKey))
            return;
        var path = GenreCloudService.BuildSnapshot([], parentKey).BreadcrumbKeys;
        foreach (var key in path)
            AddGenreBreadcrumbButton(GetGenreCloudDisplayName(key), key);
    }

    /// <summary>Adds one clickable genre breadcrumb.</summary>
    private void AddGenreBreadcrumbButton(string label, string? key)
    {
        var button = new Button
        {
            Content = label,
            Tag = key,
            Margin = new Thickness(0, 0, 7, 4),
            Padding = new Thickness(10, 5),
            FontSize = GetTypographySize("FontSizeCaption", 12)
        };
        button.Click += GenreCloudButton_OnClick;
        GenreCloudBreadcrumbPanel.Children.Add(button);
    }

    /// <summary>Builds count-scaled buttons for the visible genre nodes.</summary>
    private void BuildGenreCloudNodes(IReadOnlyList<GenreCloudNode> nodes, bool useAlbumCounts)
    {
        GenreCloudNodesCanvas.Children.Clear();
        var maximum = Math.Max(1, nodes.Select(node => useAlbumCounts ? node.AlbumCount : node.TrackCount)
            .DefaultIfEmpty(1).Max());
        var minimumSize = GetTypographySize("FontSizeBody", 13);
        var maximumSize = GetTypographySize("FontSizeHeadline", 28);
        foreach (var node in nodes)
        {
            var count = useAlbumCounts ? node.AlbumCount : node.TrackCount;
            var scale = Math.Log(1 + count) / Math.Log(1 + maximum);
            var button = new Button
            {
                Content = $"{GetGenreCloudDisplayName(node.Key)}  ·  {count:N0}",
                Tag = node.Key,
                Margin = new Thickness(0, 0, 9, 9),
                Padding = new Thickness(12, 7),
                FontSize = minimumSize + (maximumSize - minimumSize) * scale,
                FontWeight = count == maximum ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Opacity = 0;
            button.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }
            };
            var scaleTransform = new ScaleTransform(0.82, 0.82);
            scaleTransform.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(260) },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(260) }
            };
            button.RenderTransform = scaleTransform;
            button.RenderTransformOrigin = RelativePoint.Center;
            button.Click += GenreCloudButton_OnClick;
            GenreCloudNodesCanvas.Children.Add(button);
        }

        Dispatcher.UIThread.Post(ArrangeGenreCloudNodes, DispatcherPriority.Loaded);
    }

    /// <summary>Places genre buttons in centered, collision-free cloud rows and starts their staggered transition.</summary>
    private void ArrangeGenreCloudNodes()
    {
        const double horizontalGap = 14;
        const double verticalGap = 12;
        var width = Math.Max(320, GenreCloudSurface.Bounds.Width - 40);
        var maximumRowWidth = Math.Max(280, width - 24);
        var entries = new List<(Button Button, double Width, double Height, int Index)>();
        for (var index = 0; index < GenreCloudNodesCanvas.Children.Count; index++)
        {
            if (GenreCloudNodesCanvas.Children[index] is not Button button)
                continue;
            button.Measure(new Size(maximumRowWidth, double.PositiveInfinity));
            entries.Add((
                button,
                Math.Min(maximumRowWidth, Math.Max(90, button.DesiredSize.Width)),
                Math.Max(34, button.DesiredSize.Height),
                index));
        }

        var rows = new List<List<(Button Button, double Width, double Height, int Index)>>();
        var rowWidths = new List<double>();
        foreach (var entry in entries)
        {
            if (rows.Count == 0 || rowWidths[^1] + horizontalGap + entry.Width > maximumRowWidth)
            {
                rows.Add([]);
                rowWidths.Add(0);
            }
            var rowIndex = rows.Count - 1;
            rows[rowIndex].Add(entry);
            rowWidths[rowIndex] += (rows[rowIndex].Count > 1 ? horizontalGap : 0) + entry.Width;
        }

        var rowHeights = rows.Select(row => row.Max(entry => entry.Height)).ToList();
        var contentHeight = rowHeights.Sum() + Math.Max(0, rows.Count - 1) * verticalGap + 16;
        GenreCloudNodesCanvas.Height = Math.Max(330, contentHeight);
        var top = Math.Max(8, (GenreCloudNodesCanvas.Height - contentHeight) / 2 + 8);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var left = Math.Max(8, (width - rowWidths[rowIndex]) / 2);
            foreach (var entry in rows[rowIndex])
            {
                Canvas.SetLeft(entry.Button, left);
                Canvas.SetTop(entry.Button, top + (rowHeights[rowIndex] - entry.Height) / 2);
                left += entry.Width + horizontalGap;
                var delay = TimeSpan.FromMilliseconds(Math.Min(entry.Index, 14) * 28);
                DispatcherTimer.RunOnce(() =>
                {
                    entry.Button.Opacity = 1;
                    if (entry.Button.RenderTransform is ScaleTransform transform)
                    {
                        transform.ScaleX = 1;
                        transform.ScaleY = 1;
                    }
                }, delay);
            }
            top += rowHeights[rowIndex] + verticalGap;
        }
    }

    /// <summary>Returns a numeric typography token for dynamic count-based scaling.</summary>
    private double GetTypographySize(string resourceKey, double fallback) =>
        this.TryFindResource(resourceKey, out var value) && value is double size ? size : fallback;

    /// <summary>Returns a localized UI name for virtual nodes and the taxonomy fallback for actual genres.</summary>
    private static string GetGenreCloudDisplayName(string key) => key == "more-genres"
        ? LocalizationManager.Current.MoreGenres
        : GenreCloudService.GetDisplayName(key);

    /// <summary>Handles cloud and breadcrumb drill-down actions.</summary>
    private async void GenreCloudButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button)
            await ShowGenreCloudAsync(button.Tag as string);
    }
}
