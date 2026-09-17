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
/// Dashboard catalog snapshots, recommendation ranking, and the recommendation stage.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>A recently added album for the dashboard, from the local library or a remote server.</summary>
    /// <param name="Id">Album identifier within its source library.</param>
    /// <param name="Title">Album title.</param>
    /// <param name="Artist">Display artist name.</param>
    /// <param name="ArtistId">Album-artist identifier, or <see langword="null"/>.</param>
    /// <param name="AddedAt">Unix timestamp of the most recently added track in the album.</param>
    /// <param name="Server">The owning remote server, or <see langword="null"/> for the local library.</param>
    /// <param name="ArtworkPath">Local artwork file path or authenticated remote artwork URL, or <see langword="null"/>.</param>
    /// <param name="HasArtwork">Whether artwork is available for the album.</param>
    /// <param name="IsFavorite">Whether the album is flagged as a favorite (local flag or client-side remote favorite).</param>
    /// <param name="LogicalAlbumIds">All provider-local album identifiers represented by this logical album.</param>
    /// <param name="LogicalAlbumParts">Source-aware album identities represented by this logical album.</param>
    private sealed record DashboardAlbum(
        long Id,
        string Title,
        string Artist,
        long? ArtistId,
        long AddedAt,
        OrynivoServerSettings? Server,
        string? ArtworkPath,
        bool HasArtwork,
        bool IsFavorite,
        IReadOnlyList<long>? LogicalAlbumIds = null,
        IReadOnlyList<LogicalAlbumPart>? LogicalAlbumParts = null);

    /// <summary>
    /// Loads the recently added albums for the dashboard, merging the local library with every
    /// configured remote Orynivo Server and keeping the globally most recent entries.
    /// </summary>
    /// <param name="perSource">Maximum entries to fetch per source and to return overall.</param>
    /// <returns>The merged, recency-sorted recently added albums.</returns>
    private async Task<List<DashboardAlbum>> LoadRecentAlbumsAsync(
        int perSource = 20,
        DashboardBuildMetrics? metrics = null)
    {
        var localTimer = Stopwatch.StartNew();
        var local = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetRecentAlbums(perSource);
        });
        metrics?.Record("recent-albums-local", localTimer.ElapsedMilliseconds);

        var combined = local
            .Select(a => new DashboardAlbum(
                a.Id, a.Title, a.Artist, a.ArtistId, a.AddedAt,
                null, a.ArtworkPath ?? a.ThumbPath, !string.IsNullOrEmpty(a.ArtworkPath ?? a.ThumbPath),
                a.IsFavorite))
            .ToList();

        var servers = _settings.OrynivoServers ?? [];
        if (servers.Count > 0)
        {
            var remoteTimer = Stopwatch.StartNew();
            var remoteTasks = servers
                .Select(server => (Server: server, Task: _orynivoClient.GetRecentAlbumsAsync(server, perSource)))
                .ToList();
            try { await Task.WhenAll(remoteTasks.Select(t => t.Task)); }
            catch { /* Individual failures already yield empty lists. */ }

            foreach (var (server, task) in remoteTasks)
            {
                if (!task.IsCompletedSuccessfully)
                    continue;
                combined.AddRange(task.Result.Select(a => new DashboardAlbum(
                    a.Id, a.Title, a.Artist, a.ArtistId, a.AddedAt,
                    server,
                    a.HasArtwork ? OrynivoServerClient.GetAlbumArtworkUrl(server, a.Id, 320) : null,
                    a.HasArtwork,
                    IsOrynivoFavorite(server, "Album", a.Id))));
            }
            metrics?.Record("recent-albums-remote", remoteTimer.ElapsedMilliseconds);
        }

        return MergeDashboardAlbums(combined)
            .OrderByDescending(a => a.AddedAt)
            .Take(perSource)
            .ToList();
    }

    /// <summary>
    /// Returns the current expensive Dashboard catalog snapshot, coalescing concurrent
    /// builds and reusing a completed result for a bounded period.
    /// </summary>
    /// <param name="metrics">Optional build metrics receiving cache and source timings.</param>
    /// <returns>A consistent local-plus-remote catalog snapshot.</returns>
    private async Task<DashboardCatalogSnapshot> GetDashboardCatalogSnapshotAsync(
        DashboardBuildMetrics metrics)
    {
        var key = CreateDashboardCatalogCacheKey();
        Task<DashboardCatalogSnapshot> loadTask;
        lock (_dashboardCatalogCacheSync)
        {
            if (_dashboardCatalogCache is { } cached &&
                cached.Key == key &&
                cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                metrics.Record("catalog-cache-hit", 0);
                return cached.Snapshot;
            }

            if (_dashboardCatalogLoadTask is { IsCompleted: false } active &&
                _dashboardCatalogLoadKey == key)
            {
                metrics.Record("catalog-cache-wait", 0);
                loadTask = active;
            }
            else
            {
                loadTask = LoadDashboardCatalogSnapshotAsync(metrics);
                _dashboardCatalogLoadTask = loadTask;
                _dashboardCatalogLoadKey = key;
            }
        }

        var snapshot = await loadTask;
        lock (_dashboardCatalogCacheSync)
        {
            if (key == CreateDashboardCatalogCacheKey())
            {
                _dashboardCatalogCache = new DashboardCatalogCacheEntry(
                    key,
                    DateTimeOffset.UtcNow + DashboardCatalogCacheLifetime,
                    snapshot);
            }
            if (ReferenceEquals(_dashboardCatalogLoadTask, loadTask))
            {
                _dashboardCatalogLoadTask = null;
                _dashboardCatalogLoadKey = null;
            }
        }
        return snapshot;
    }

    /// <summary>Loads all expensive Dashboard catalog sources concurrently.</summary>
    /// <param name="metrics">Build metrics receiving individual source timings.</param>
    /// <returns>The newly loaded catalog snapshot.</returns>
    private async Task<DashboardCatalogSnapshot> LoadDashboardCatalogSnapshotAsync(
        DashboardBuildMetrics metrics)
    {
        var recentAlbumsTask = metrics.MeasureAsync(
            "recent-albums",
            () => LoadRecentAlbumsAsync(metrics: metrics));
        var recommendationsTask = metrics.MeasureAsync(
            "recommendations",
            () => LoadDashboardRecommendationsAsync(metrics));
        var localLibraryTask = metrics.MeasureAsync("library-local", () => Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return (
                Summary: db.GetDashboardLibrarySummary(),
                ArtistKeys: db.GetArtistsLite()
                    .Select(artist => ArtistNameNormalizer.CreateComparisonKey(artist.Artist))
                    .ToHashSet(StringComparer.Ordinal));
        }));
        var remoteLibraryTask = metrics.MeasureAsync(
            "library-remote",
            () => ResolveDashboardRemoteLibrarySummaryAsync(metrics));

        await Task.WhenAll(
            recentAlbumsTask,
            recommendationsTask,
            localLibraryTask,
            remoteLibraryTask);
        return new DashboardCatalogSnapshot(
            await recentAlbumsTask,
            await recommendationsTask,
            await localLibraryTask,
            await remoteLibraryTask);
    }

    /// <summary>Builds a non-persisted cache identity without including server credentials.</summary>
    /// <returns>The current Dashboard catalog cache identity.</returns>
    private string CreateDashboardCatalogCacheKey()
    {
        var servers = string.Join('|', (_settings.OrynivoServers ?? [])
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .Select(server => $"{server.Id}:{server.BaseUrl}"));
        return $"{Volatile.Read(ref _dashboardCatalogGeneration)};" +
               $"{(int)_dashboardRecommendationPeriod};{(int)_dashboardRecommendationMood};{servers}";
    }

    /// <summary>Invalidates cached Dashboard catalog aggregates after a local library change.</summary>
    private void InvalidateDashboardCatalogCache()
    {
        Interlocked.Increment(ref _dashboardCatalogGeneration);
        lock (_dashboardCatalogCacheSync)
            _dashboardCatalogCache = null;
    }

    private async Task<List<DashboardAlbum>> LoadDashboardRecommendationsAsync(
        DashboardBuildMetrics? metrics = null)
    {
        var since = _dashboardRecommendationPeriod switch
        {
            RecommendationPeriod.LastWeek => DateTimeOffset.Now.AddDays(-7).ToUnixTimeSeconds(),
            RecommendationPeriod.LastMonth => DateTimeOffset.Now.AddDays(-30).ToUnixTimeSeconds(),
            _ => (long?)null
        };

        var localTimer = Stopwatch.StartNew();
        var localProfile = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return (
                Genres: db.GetTopGenres(50, since),
                Played: db.GetPlayedAlbumIdentities(since),
                Albums: db.GetRecommendationAlbums());
        });
        metrics?.Record("recommendations-local", localTimer.ElapsedMilliseconds);

        var candidates = localProfile.Albums.Select(album =>
            new DashboardRecommendationCandidate(
                new DashboardAlbum(
                    album.Id,
                    album.Title,
                    album.Artist,
                    album.ArtistId,
                    0,
                    null,
                    album.ArtworkPath,
                    !string.IsNullOrWhiteSpace(album.ArtworkPath),
                    album.IsFavorite),
                album.Genres,
                album.AverageBpm)).ToList();

        var remoteTimer = Stopwatch.StartNew();
        var remoteTasks = (_settings.OrynivoServers ?? [])
            .Select(server => (Server: server, Task: _orynivoClient.GetRecommendationAlbumsAsync(server)))
            .ToList();
        try
        {
            await Task.WhenAll(remoteTasks.Select(item => item.Task));
        }
        catch
        {
            // Individual client calls return empty results when a server is unavailable.
        }

        foreach (var (server, task) in remoteTasks)
        {
            if (!task.IsCompletedSuccessfully)
                continue;
            candidates.AddRange(task.Result.Select(album =>
                new DashboardRecommendationCandidate(
                    new DashboardAlbum(
                        album.Id,
                        album.Title,
                        album.Artist,
                        album.ArtistId,
                        0,
                        server,
                        !string.IsNullOrWhiteSpace(album.ArtworkPath)
                            ? OrynivoServerClient.GetAlbumArtworkUrl(server, album.Id, 320)
                            : null,
                        !string.IsNullOrWhiteSpace(album.ArtworkPath),
                        IsOrynivoFavorite(server, "Album", album.Id)),
                    album.Genres,
                    album.AverageBpm)));
        }
        metrics?.Record("recommendations-remote", remoteTimer.ElapsedMilliseconds);

        candidates = MergeDashboardRecommendationCandidates(candidates);

        if (localProfile.Genres.Count == 0)
            return [];

        var maximumGenreSeconds = Math.Max(1, localProfile.Genres.Max(item => item.Seconds));
        var genreWeights = localProfile.Genres
            .GroupBy(item => NormalizeRecommendationToken(item.Genre), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.Seconds) / maximumGenreSeconds,
                StringComparer.Ordinal);
        var played = localProfile.Played
            .Select(item => RecommendationAlbumKey(item.Album, item.Artist))
            .ToHashSet(StringComparer.Ordinal);

        return candidates
            .Select(candidate =>
            {
                var genreScore = SplitRecommendationGenres(candidate.Genres)
                    .Select(genre => genreWeights.GetValueOrDefault(genre))
                    .DefaultIfEmpty(0)
                    .Max();
                if (genreScore <= 0)
                    return (Candidate: candidate, Score: 0d);

                var moodFactor = RecommendationMoodFactor(
                    _dashboardRecommendationMood,
                    candidate.Genres,
                    candidate.AverageBpm);
                var heardFactor = played.Contains(RecommendationAlbumKey(
                    candidate.Album.Title,
                    candidate.Album.Artist))
                    ? 0.35
                    : 1.0;
                var favoriteBonus = candidate.Album.IsFavorite ? 0.05 : 0;
                return (Candidate: candidate, Score: genreScore * moodFactor * heardFactor + favoriteBonus);
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Album.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Candidate.Album)
            .Take(20)
            .ToList();
    }

    private static IEnumerable<string> SplitRecommendationGenres(string? genres) =>
        (genres ?? string.Empty)
        .Split(new[] { ';', ',', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeRecommendationToken)
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal);

    private static string NormalizeRecommendationToken(string? value) =>
        ArtistNameNormalizer.CreateComparisonKey(value);

    private static string RecommendationAlbumKey(string? album, string? artist) =>
        $"{NormalizeRecommendationToken(album)}\u001f{NormalizeRecommendationToken(artist)}";

    private static List<DashboardAlbum> MergeDashboardAlbums(IEnumerable<DashboardAlbum> albums) =>
        albums
            .Where(album => IsKnownAlbumTitle(album.Title))
            .GroupBy(
                album => RecommendationAlbumKey(album.Title, album.Artist),
                StringComparer.Ordinal)
            .Select(group => MergeDashboardAlbumGroup(group.ToList()))
            .ToList();

    private static DashboardAlbum MergeDashboardAlbumGroup(IReadOnlyList<DashboardAlbum> albums)
    {
        var representative = albums
            .OrderByDescending(album => album.Server is null)
            .ThenByDescending(album => album.HasArtwork)
            .ThenByDescending(album => album.AddedAt)
            .ThenBy(album => album.Id)
            .First();
        var artworkSource = albums.FirstOrDefault(album => album.HasArtwork);
        return representative with
        {
            AddedAt = albums.Max(album => album.AddedAt),
            IsFavorite = albums.Any(album => album.IsFavorite),
            ArtworkPath = artworkSource?.ArtworkPath ?? representative.ArtworkPath,
            HasArtwork = artworkSource is not null || representative.HasArtwork,
            LogicalAlbumIds = albums
                .SelectMany(album => album.LogicalAlbumIds ?? [album.Id])
                .Distinct()
                .ToList(),
            LogicalAlbumParts = albums
                .SelectMany(album => album.LogicalAlbumParts ??
                    [new LogicalAlbumPart(album.Id, album.ArtistId, album.Server)])
                .DistinctBy(part => $"{part.Server?.Id ?? LocalSourceKey}:{part.AlbumId}")
                .ToList()
        };
    }

    private static List<DashboardRecommendationCandidate> MergeDashboardRecommendationCandidates(
        IEnumerable<DashboardRecommendationCandidate> candidates) =>
        candidates
            .Where(candidate => IsKnownAlbumTitle(candidate.Album.Title))
            .GroupBy(
                candidate => RecommendationAlbumKey(candidate.Album.Title, candidate.Album.Artist),
                StringComparer.Ordinal)
            .Select(group =>
            {
                var values = group.ToList();
                var bpms = values
                    .Select(value => value.AverageBpm)
                    .OfType<double>()
                    .Where(value => value > 0)
                    .ToList();
                return new DashboardRecommendationCandidate(
                    MergeDashboardAlbumGroup(values.Select(value => value.Album).ToList()),
                    string.Join(';', values
                        .SelectMany(value => SplitRecommendationGenres(value.Genres))
                        .Distinct(StringComparer.Ordinal)),
                    bpms.Count == 0 ? null : bpms.Average());
            })
            .ToList();

    private static double RecommendationMoodFactor(
        RecommendationMood mood,
        string? genres,
        double? averageBpm)
    {
        if (mood == RecommendationMood.All)
            return 1;

        var genreText = string.Join(' ', SplitRecommendationGenres(genres));
        var matches = mood switch
        {
            RecommendationMood.Relaxed =>
                averageBpm is > 0 and <= 105 ||
                ContainsAny(genreText, "ambient", "chill", "classical", "jazz", "acoustic", "soul"),
            RecommendationMood.Energetic =>
                averageBpm >= 120 ||
                ContainsAny(genreText, "rock", "metal", "dance", "electronic", "punk", "techno"),
            RecommendationMood.Happy =>
                ContainsAny(genreText, "pop", "disco", "funk", "reggae", "dance", "ska"),
            RecommendationMood.Melancholic =>
                ContainsAny(genreText, "blues", "ambient", "classical", "gothic", "sad", "doom"),
            _ => true
        };
        return matches ? 1.4 : 0.55;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.Ordinal));

    private void DashboardBuildRecommendations(List<DashboardAlbum> albums)
    {
        var period = new ComboBox
        {
            MinWidth = 150,
            Height = 30,
            ItemsSource = new[]
            {
                LocalizationManager.Current.PeriodLast7Days,
                LocalizationManager.Current.PeriodLast30Days,
                LocalizationManager.Current.PeriodAllTime
            },
            SelectedIndex = (int)_dashboardRecommendationPeriod
        };
        period.SelectionChanged += DashboardRecommendationPeriod_OnSelectionChanged;

        var mood = new ComboBox
        {
            MinWidth = 150,
            Height = 30,
            ItemsSource = new[]
            {
                LocalizationManager.Current.RecommendationMoodAll,
                LocalizationManager.Current.RecommendationMoodRelaxed,
                LocalizationManager.Current.RecommendationMoodEnergetic,
                LocalizationManager.Current.RecommendationMoodHappy,
                LocalizationManager.Current.RecommendationMoodMelancholic
            },
            SelectedIndex = (int)_dashboardRecommendationMood
        };
        mood.SelectionChanged += DashboardRecommendationMood_OnSelectionChanged;

        var listMode = new RadioButton
        {
            GroupName = "DashboardRecommendationViewMode",
            Content = LocalizationManager.Current.RecommendationListView,
            Theme = FindResource<ControlTheme>("ViewModeRadioTheme"),
            MinWidth = 76,
            Padding = new Thickness(14, 7),
            IsChecked = !_settings.DashboardRecommendationStageView
        };
        var stageMode = new RadioButton
        {
            GroupName = "DashboardRecommendationViewMode",
            Content = LocalizationManager.Current.RecommendationStageView,
            Theme = FindResource<ControlTheme>("ViewModeRadioTheme"),
            MinWidth = 76,
            Padding = new Thickness(14, 7),
            IsChecked = _settings.DashboardRecommendationStageView
        };
        var modeSwitch = new Border
        {
            Padding = new Thickness(3),
            Background = FindResource<IBrush>("AppButtonBrush"),
            BorderBrush = FindResource<IBrush>("AppButtonBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { listMode, stageMode }
            }
        };
        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { period, mood, modeSwitch }
        };

        var contentHost = new Border();
        void ApplyViewMode()
        {
            contentHost.Child = albums.Count == 0
                ? DashboardNoDataText(LocalizationManager.Current.RecommendationNoMatches)
                : _settings.DashboardRecommendationStageView
                    ? DashboardCreateRecommendationStage(albums)
                    : DashboardCreateRecentAlbumsStrip(albums);
        }

        listMode.IsCheckedChanged += (_, _) =>
        {
            if (listMode.IsChecked != true || !_settings.DashboardRecommendationStageView)
                return;
            _settings.DashboardRecommendationStageView = false;
            ApplyViewMode();
            _ = Task.Run(() => _settingsStore.Save(_settings));
        };
        stageMode.IsCheckedChanged += (_, _) =>
        {
            if (stageMode.IsChecked != true || _settings.DashboardRecommendationStageView)
                return;
            _settings.DashboardRecommendationStageView = true;
            ApplyViewMode();
            _ = Task.Run(() => _settingsStore.Save(_settings));
        };
        ApplyViewMode();
        DashboardPanel.Children.Add(DashboardBuildMediaSectionCard(
            LocalizationManager.Current.AlbumRecommendations,
            null,
            contentHost,
            null,
            filters));
    }

    /// <summary>Builds the tall cover-flow stage used by Dashboard album recommendations.</summary>
    /// <param name="albums">Ranked recommendation albums.</param>
    /// <returns>A stage with circular previous/next navigation.</returns>
    private Control DashboardCreateRecommendationStage(IReadOnlyList<DashboardAlbum> albums)
    {
        _dashboardRecommendationStageIndex = Math.Clamp(
            _dashboardRecommendationStageIndex,
            0,
            Math.Max(0, albums.Count - 1));
        var stage = new Grid
        {
            Height = 430,
            ClipToBounds = true,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1, GridUnitType.Star))
            }
        };
        var previous = DashboardCreateCarouselButton(forward: false);
        var next = DashboardCreateCarouselButton(forward: true);
        previous.Width = next.Width = 32;
        previous.Height = next.Height = 32;
        previous.IsEnabled = next.IsEnabled = albums.Count > 1;
        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 2, 0, 8),
            Children = { previous, next }
        };
        stage.Children.Add(navigation);

        var viewport = new Grid { ClipToBounds = true };
        Grid.SetRow(viewport, 1);
        stage.Children.Add(viewport);
        var moving = false;
        Grid? deck = null;
        deck = DashboardBuildRecommendationStageDeck(
            albums,
            _dashboardRecommendationStageIndex,
            direction => _ = MoveAsync(direction));
        viewport.Children.Add(deck);

        async Task MoveAsync(int direction)
        {
            if (moving || albums.Count < 2)
                return;
            moving = true;
            previous.IsEnabled = next.IsEnabled = false;
            var oldDeck = deck!;
            _dashboardRecommendationStageIndex =
                (_dashboardRecommendationStageIndex + direction + albums.Count) % albums.Count;
            var newDeck = DashboardBuildRecommendationStageDeck(
                albums,
                _dashboardRecommendationStageIndex,
                nextDirection => _ = MoveAsync(nextDirection));
            newDeck.Opacity = 0;
            var oldMotion = new TransformGroup();
            var oldRotate = new RotateTransform();
            var oldTranslate = new TranslateTransform();
            oldMotion.Children.Add(oldRotate);
            oldMotion.Children.Add(oldTranslate);
            oldDeck.RenderTransform = oldMotion;
            oldDeck.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            var newMotion = new TransformGroup();
            var newRotate = new RotateTransform { Angle = direction * 2.5 };
            var newTranslate = new TranslateTransform { X = direction * 110 };
            newMotion.Children.Add(newRotate);
            newMotion.Children.Add(newTranslate);
            newDeck.RenderTransform = newMotion;
            newDeck.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            viewport.Children.Add(newDeck);

            const int frameCount = 15;
            for (var frame = 1; frame <= frameCount; frame++)
            {
                var progress = frame / (double)frameCount;
                var eased = 1 - Math.Pow(1 - progress, 3);
                oldDeck.Opacity = 1 - eased;
                oldTranslate.X = -direction * 110 * eased;
                oldRotate.Angle = -direction * 2.5 * eased;
                newDeck.Opacity = eased;
                newTranslate.X = direction * 110 * (1 - eased);
                newRotate.Angle = direction * 2.5 * (1 - eased);
                await Task.Delay(16);
            }

            viewport.Children.Remove(oldDeck);
            deck = newDeck;
            moving = false;
            previous.IsEnabled = next.IsEnabled = true;
        }

        previous.Click += (_, e) =>
        {
            e.Handled = true;
            _ = MoveAsync(-1);
        };
        next.Click += (_, e) =>
        {
            e.Handled = true;
            _ = MoveAsync(1);
        };
        return stage;
    }

    /// <summary>Builds one static five-position frame of the recommendation stage.</summary>
    /// <param name="albums">Ranked recommendation albums.</param>
    /// <param name="centerIndex">Index shown in the center.</param>
    /// <param name="move">Callback receiving minus one or plus one when a side album is selected.</param>
    /// <returns>The arranged cover deck.</returns>
    private Grid DashboardBuildRecommendationStageDeck(
        IReadOnlyList<DashboardAlbum> albums,
        int centerIndex,
        Action<int> move)
    {
        var deck = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(0.7, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.25, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(0.7, GridUnitType.Star))
            }
        };
        var template = FindResource<IDataTemplate>("AlbumArtworkCardTemplate");
        if (template is null)
            return deck;

        var usedIndices = new HashSet<int>();
        for (var offset = -2; offset <= 2; offset++)
        {
            var albumIndex = (centerIndex + offset + albums.Count) % albums.Count;
            if (!usedIndices.Add(albumIndex))
                continue;

            var isCenter = offset == 0;
            var distance = Math.Abs(offset);
            var card = BuildRecentAlbumCard(albums[albumIndex], template);
            card.IsHitTestVisible = isCenter;
            var scale = distance switch
            {
                0 => 1.18,
                1 => 0.88,
                _ => 0.68
            };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform
            {
                ScaleX = scale * (isCenter ? 1 : 0.78),
                ScaleY = scale
            });
            transforms.Children.Add(new SkewTransform
            {
                AngleY = offset < 0 ? -7 : offset > 0 ? 7 : 0
            });
            transforms.Children.Add(new RotateTransform
            {
                Angle = offset < 0 ? 3.5 : offset > 0 ? -3.5 : 0
            });
            card.RenderTransform = transforms;
            card.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

            var host = new Border
            {
                MinWidth = isCenter ? 230 : 150,
                Height = isCenter ? 350 : distance == 1 ? 310 : 270,
                Opacity = isCenter ? 1 : distance == 1 ? 0.78 : 0.42,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(isCenter ? StandardCursorType.Arrow : StandardCursorType.Hand),
                Child = card,
                ZIndex = 10 - distance
            };
            if (!isCenter)
            {
                var direction = Math.Sign(offset);
                host.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(host).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                        return;
                    e.Handled = true;
                    move(direction);
                };
            }
            Grid.SetColumn(host, offset + 2);
            deck.Children.Add(host);
        }
        return deck;
    }

    private async void DashboardRecommendationPeriod_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 } box ||
            (RecommendationPeriod)box.SelectedIndex == _dashboardRecommendationPeriod)
        {
            return;
        }
        _dashboardRecommendationPeriod = (RecommendationPeriod)box.SelectedIndex;
        await BuildDashboardAsync();
    }

    private async void DashboardRecommendationMood_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 } box ||
            (RecommendationMood)box.SelectedIndex == _dashboardRecommendationMood)
        {
            return;
        }
        _dashboardRecommendationMood = (RecommendationMood)box.SelectedIndex;
        await BuildDashboardAsync();
    }
}
