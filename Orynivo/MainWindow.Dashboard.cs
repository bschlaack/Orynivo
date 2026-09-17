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
    // ------------------------------------------------------------------
    // Dashboard
    // ------------------------------------------------------------------

    /// <summary>Selectable time window for the dashboard listening-statistics cards.</summary>
    private enum StatsPeriod
    {
        /// <summary>All recorded playback history.</summary>
        AllTime,
        /// <summary>Playback since the start of the current calendar year.</summary>
        ThisYear,
        /// <summary>Playback since the start of the current calendar month.</summary>
        ThisMonth,
        /// <summary>Playback within the last 30 days.</summary>
        Last30Days,
        /// <summary>Playback within the last 7 days.</summary>
        Last7Days
    }

    /// <summary>Selectable history window for album recommendations.</summary>
    private enum RecommendationPeriod
    {
        /// <summary>Playback within the last seven days.</summary>
        LastWeek,
        /// <summary>Playback within the last thirty days.</summary>
        LastMonth,
        /// <summary>All recorded playback history.</summary>
        AllTime
    }

    /// <summary>Optional mood bias applied to recommendation candidates.</summary>
    private enum RecommendationMood
    {
        /// <summary>No mood bias.</summary>
        All,
        /// <summary>Prefer calm, lower-tempo music.</summary>
        Relaxed,
        /// <summary>Prefer driving, higher-tempo music.</summary>
        Energetic,
        /// <summary>Prefer bright and upbeat genres.</summary>
        Happy,
        /// <summary>Prefer reflective and melancholic genres.</summary>
        Melancholic
    }

    private RecommendationPeriod _dashboardRecommendationPeriod = RecommendationPeriod.LastMonth;
    private RecommendationMood _dashboardRecommendationMood;
    private int _dashboardRecommendationStageIndex;

    private sealed record DashboardRecommendationCandidate(
        DashboardAlbum Album,
        string? Genres,
        double? AverageBpm);

    /// <summary>Immutable expensive catalog portion shared by repeated Dashboard builds.</summary>
    /// <param name="RecentAlbums">Merged recently added albums.</param>
    /// <param name="Recommendations">Ranked album recommendations for the selected profile.</param>
    /// <param name="LocalSummary">Local library totals and normalized artist identities.</param>
    /// <param name="RemoteSummary">Remote library totals and normalized artist identities.</param>
    private sealed record DashboardCatalogSnapshot(
        List<DashboardAlbum> RecentAlbums,
        List<DashboardAlbum> Recommendations,
        (DashboardLibrarySummary Summary, HashSet<string> ArtistKeys) LocalSummary,
        (int AlbumCount, int TrackCount, int FavoriteCount, HashSet<string> ArtistKeys) RemoteSummary);

    /// <summary>One versioned in-memory Dashboard catalog cache entry.</summary>
    /// <param name="Key">Non-persisted cache identity derived from view options and configured server identities.</param>
    /// <param name="ExpiresAtUtc">UTC expiry used to bound playback-history and remote-library staleness.</param>
    /// <param name="Snapshot">Cached catalog snapshot.</param>
    private sealed record DashboardCatalogCacheEntry(
        string Key,
        DateTimeOffset ExpiresAtUtc,
        DashboardCatalogSnapshot Snapshot);

    private static readonly TimeSpan DashboardCatalogCacheLifetime = TimeSpan.FromHours(24);
    private readonly object _dashboardCatalogCacheSync = new();
    private DashboardCatalogCacheEntry? _dashboardCatalogCache;
    private Task<DashboardCatalogSnapshot>? _dashboardCatalogLoadTask;
    private string? _dashboardCatalogLoadKey;
    private int _dashboardCatalogGeneration;

    /// <summary>Collects one Dashboard build's sanitized phase durations.</summary>
    /// <param name="buildVersion">Monotonic build identifier used to recognize superseded loads.</param>
    private sealed class DashboardBuildMetrics(int buildVersion)
    {
        private readonly object _sync = new();
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly Dictionary<string, long> _phases = new(StringComparer.Ordinal);

        /// <summary>Measures an asynchronous Dashboard phase.</summary>
        /// <typeparam name="T">Phase result type.</typeparam>
        /// <param name="name">Stable non-sensitive phase name.</param>
        /// <param name="action">Asynchronous work to execute.</param>
        /// <returns>The phase result.</returns>
        public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action)
        {
            var timer = Stopwatch.StartNew();
            try { return await action(); }
            finally { Set(name, timer.ElapsedMilliseconds); }
        }

        /// <summary>Measures a synchronous Dashboard phase.</summary>
        /// <param name="name">Stable non-sensitive phase name.</param>
        /// <param name="action">Work to execute.</param>
        public void Measure(string name, Action action)
        {
            var timer = Stopwatch.StartNew();
            try { action(); }
            finally { Set(name, timer.ElapsedMilliseconds); }
        }

        /// <summary>Adds an independently measured duration to a phase.</summary>
        /// <param name="name">Stable non-sensitive phase name.</param>
        /// <param name="elapsedMilliseconds">Elapsed whole milliseconds.</param>
        public void Record(string name, long elapsedMilliseconds)
        {
            lock (_sync)
                _phases[name] = _phases.GetValueOrDefault(name) + elapsedMilliseconds;
        }

        private void Set(string name, long elapsedMilliseconds)
        {
            lock (_sync)
                _phases[name] = elapsedMilliseconds;
        }

        /// <summary>Writes the completed timing summary.</summary>
        /// <param name="outcome">Completed, superseded, or incomplete state.</param>
        /// <param name="serverCount">Number of configured remote libraries.</param>
        public void Complete(string outcome, int serverCount)
        {
            _total.Stop();
            string phases;
            lock (_sync)
            {
                phases = string.Join(' ', _phases
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}ms"));
            }
            DashboardPerformanceLog.Write(
                $"build={buildVersion} outcome={outcome} total={_total.ElapsedMilliseconds}ms " +
                $"servers={serverCount} {phases}".TrimEnd());
        }
    }

    private async Task ShowDashboardAsync()
    {
        ContentTitleTextBlock.Text = LocalizationManager.Current.Dashboard;
        var now = DateTime.Now;
        if (_dashboardYear == 0)
        {
            _dashboardYear  = now.Year;
            _dashboardMonth = now.Month;
        }

        // Re-flow the calendar/genre columns when the window crosses the
        // two-column width threshold; only rebuilds on an actual layout change.
        if (!_dashboardResizeHooked)
        {
            _dashboardResizeHooked = true;
            DashboardScrollViewer.SizeChanged += DashboardScrollViewer_OnSizeChanged;
        }

        await BuildDashboardAsync();
    }

    private async void DashboardScrollViewer_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Only the main dashboard re-flows its two-column stats layout; the
        // full-page "show all" views share this surface but must not be replaced.
        if (!DashboardScrollViewer.IsVisible || _currentTopLevelTag != "Dashboard")
            return;
        if (ComputeDashboardTwoColumn() == _dashboardTwoColumnLayout)
            return;
        await BuildDashboardAsync();
    }

    /// <summary>Decides whether the calendar and top-genre blocks fit side by side.</summary>
    /// <returns><see langword="true"/> when the dashboard is wide enough for two columns.</returns>
    private bool ComputeDashboardTwoColumn()
    {
        var width = DashboardScrollViewer.Bounds.Width;
        if (width < 1)
            width = Bounds.Width - 260; // sidebar + margins fallback before first layout
        return width >= 980;
    }

    private async Task BuildDashboardAsync()
    {
        var buildVersion = ++_dashboardBuildVersion;
        var metrics = new DashboardBuildMetrics(buildVersion);
        var committed = false;
        var visiblePanel = _dashboardRootPanel ?? DashboardPanel;
        _dashboardRootPanel = visiblePanel;
        var buildPanel = new StackPanel
        {
            Spacing = visiblePanel.Spacing,
            Margin = visiblePanel.Margin,
            Orientation = visiblePanel.Orientation,
            HorizontalAlignment = visiblePanel.HorizontalAlignment,
            VerticalAlignment = visiblePanel.VerticalAlignment
        };

        DashboardPanel = buildPanel;
        try
        {
            _calendarInner = null;
            _dashboardTwoColumnLayout = ComputeDashboardTwoColumn();

            // The expensive catalog portion is reused briefly between navigations.
            // Its loader still overlaps all independent local and remote requests.
            var catalogTask = GetDashboardCatalogSnapshotAsync(metrics);
            var recentlyPlayedTask = metrics.MeasureAsync(
                "recently-played",
                () => LoadRecentlyPlayedAsync(20));
            var calendarDataTask = metrics.MeasureAsync("calendar", () => Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetCalendarData(_dashboardYear, _dashboardMonth);
            }));
            if (buildVersion != _dashboardBuildVersion)
                return;
            var since = StatsPeriodSinceUnix(_dashboardStatsPeriod);
            var listeningStatsTask = metrics.MeasureAsync("listening", () => Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var total = db.GetTotalListeningSeconds(since);
                var trend = db.GetListeningTrend(since, DashboardListeningBucketCount(_dashboardStatsPeriod));
                double previous = 0;
                if (since is long currentStart)
                {
                    var nowUnix = DateTimeOffset.Now.ToUnixTimeSeconds();
                    var span = Math.Max(1, nowUnix - currentStart);
                    previous = db.GetTotalListeningSeconds(currentStart - span, currentStart);
                }
                return (Total: total, Previous: previous, Trend: trend);
            }));
            var topGenresTask = metrics.MeasureAsync("top-genres", () => Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTopGenres(10, since);
            }));
            var topAlbumsTask = metrics.MeasureAsync("top-albums", () => Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTopAlbums(10, since);
            }));
            var topArtistsTask = metrics.MeasureAsync("top-artists", () => Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTopArtists(10, since);
            }));

            await Task.WhenAll(
                catalogTask,
                recentlyPlayedTask,
                calendarDataTask,
                listeningStatsTask,
                topGenresTask,
                topAlbumsTask,
                topArtistsTask);
            if (buildVersion != _dashboardBuildVersion)
                return;

            var catalog = await catalogTask;
            var recentAlbums = catalog.RecentAlbums;
            var recommendations = catalog.Recommendations;
            var localLibrary = catalog.LocalSummary;
            var remoteLibrary = catalog.RemoteSummary;
            var (recentlyPlayed, recentThumbs, recentFavorites) = await recentlyPlayedTask;
            var calendarData = await calendarDataTask;
            var listeningStats = await listeningStatsTask;
            var topGenres = await topGenresTask;
            var topAlbums = await topAlbumsTask;
            var topArtists = await topArtistsTask;

            var unifiedArtistKeys = new HashSet<string>(localLibrary.ArtistKeys, StringComparer.Ordinal);
            unifiedArtistKeys.UnionWith(remoteLibrary.ArtistKeys);
            var librarySummary = localLibrary.Summary with
            {
                AlbumCount = localLibrary.Summary.AlbumCount + remoteLibrary.AlbumCount,
                TrackCount = localLibrary.Summary.TrackCount + remoteLibrary.TrackCount,
                ArtistCount = unifiedArtistKeys.Count,
                FavoriteCount = localLibrary.Summary.FavoriteCount + remoteLibrary.FavoriteCount
            };

            metrics.Measure("ui-build", () =>
            {
                DashboardBuildGreeting(librarySummary);
                DashboardBuildMediaOverview(recentlyPlayed, recentThumbs, recentFavorites, recentAlbums);
                DashboardBuildRecommendations(recommendations);
                DashboardBuildStatsSection(
                    calendarData, topGenres, topAlbums, topArtists,
                    listeningStats.Total, listeningStats.Previous, listeningStats.Trend,
                    librarySummary);
            });
            if (buildVersion != _dashboardBuildVersion)
                return;

            DashboardPanel = visiblePanel;
            visiblePanel.Children.Clear();
            while (buildPanel.Children.Count > 0)
            {
                var child = buildPanel.Children[0];
                buildPanel.Children.RemoveAt(0);
                visiblePanel.Children.Add(child);
            }
            committed = true;
        }
        finally
        {
            if (ReferenceEquals(DashboardPanel, buildPanel))
                DashboardPanel = visiblePanel;
            metrics.Complete(
                committed ? "completed" : buildVersion != _dashboardBuildVersion ? "superseded" : "incomplete",
                (_settings.OrynivoServers ?? []).Count);
        }
    }

    /// <summary>
    /// Loads the most recent, path-de-duplicated playback-history entries together
    /// with local album thumbnail paths, shared by the dashboard strip and the
    /// full "recently played" view.
    /// </summary>
    /// <param name="count">Maximum number of distinct entries to return.</param>
    /// <returns>The de-duplicated entries and their album thumbnail paths.</returns>
    private static Task<(List<DailyHistoryEntry> Entries, Dictionary<long, string> Thumbs, HashSet<long> FavoriteTrackIds)> LoadRecentlyPlayedAsync(int count) =>
        Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            // Over-fetch so de-duplication by path can still fill the requested count.
            var history = db.GetRecentHistory(count * 4);
            var deduped = new List<DailyHistoryEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in history)
            {
                if (seenPaths.Add(entry.Path))
                    deduped.Add(entry);
                if (deduped.Count >= count)
                    break;
            }

            // Older history rows can predate the stored track/artist/album IDs.
            // Resolve those local paths in one database query so every card in
            // the full history view receives the same navigation targets as the
            // most recent entries.
            var localRows = db.GetTrackListByPaths(deduped.Select(entry => entry.Path))
                .ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < deduped.Count; index++)
            {
                var entry = deduped[index];
                if (!localRows.TryGetValue(entry.Path, out var track))
                    continue;
                deduped[index] = entry with
                {
                    TrackId = entry.TrackId ?? track.Id,
                    ArtistId = entry.ArtistId ?? track.ArtistId,
                    AlbumId = entry.AlbumId ?? track.AlbumId
                };
            }

            var localTrackIds = deduped
                .Where(entry => entry.TrackId is long)
                .Select(entry => entry.TrackId!.Value)
                .Distinct()
                .ToList();
            var thumbs = db.GetAlbumsByTrackIds(localTrackIds)
                .Where(album => !string.IsNullOrEmpty(album.ThumbnailPath))
                .GroupBy(album => album.Id)
                .ToDictionary(group => group.Key, group => group.First().ThumbnailPath!);
            var favoriteTrackIds = db.GetTrackListByIds(localTrackIds)
                .Where(track => track.IsFavorite)
                .Select(track => track.Id)
                .ToHashSet();
            return (deduped, thumbs, favoriteTrackIds);
        });

    /// <summary>Opens the full-page "recently added" view, preserving Back navigation.</summary>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task ShowAllRecentAlbumsAsync()
    {
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        await ShowTopLevelViewAsync("RecentAlbumsAll");
    }

    /// <summary>Opens the full-page "recently played" view, preserving Back navigation.</summary>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task ShowAllRecentlyPlayedAsync()
    {
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        await ShowTopLevelViewAsync("RecentlyPlayedAll");
    }

    /// <summary>Builds the full-page grid of up to 200 recently added albums.</summary>
    /// <returns>A task representing the asynchronous build.</returns>
    private async Task BuildAllRecentAlbumsViewAsync()
    {
        DashboardPanel.Children.Clear();
        _calendarInner = null;
        SearchTextBox.IsVisible = false;

        var albums = await LoadRecentAlbumsAsync(100);
        DashboardPanel.Children.Add(DashboardCreateSectionHeader(LocalizationManager.Current.RecentAlbums));
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var template = FindResource<IDataTemplate>("AlbumArtworkCardTemplate");
        if (template is not null)
            foreach (var album in albums)
                wrap.Children.Add(BuildRecentAlbumCard(album, template));
        DashboardPanel.Children.Add(wrap);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(albums.Count);
    }

    /// <summary>Builds the full-page grid of up to 200 recently played entries.</summary>
    /// <returns>A task representing the asynchronous build.</returns>
    private async Task BuildAllRecentlyPlayedViewAsync()
    {
        DashboardPanel.Children.Clear();
        _calendarInner = null;
        SearchTextBox.IsVisible = false;

        var (entries, thumbs, favorites) = await LoadRecentlyPlayedAsync(100);
        DashboardPanel.Children.Add(DashboardCreateSectionHeader(LocalizationManager.Current.RecentlyPlayed));
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in entries)
            wrap.Children.Add(BuildRecentlyPlayedCard(entry, thumbs, favorites, expandedSpacing: true));
        DashboardPanel.Children.Add(wrap);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(entries.Count);
    }

    /// <summary>Builds the personal greeting hero shown at the top of the dashboard.</summary>
    private void DashboardBuildGreeting(DashboardLibrarySummary summary)
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour switch
        {
            >= 5 and < 12 => LocalizationManager.Current.GreetingMorning,
            >= 12 and < 18 => LocalizationManager.Current.GreetingAfternoon,
            _ => LocalizationManager.Current.GreetingEvening
        };

        var hero = new Border
        {
            Height = 210,
            Background = FindResource<IBrush>("DashboardHeroBackgroundBrush"),
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
        };
        var heroLayers = new Grid();
        heroLayers.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(16),
            IsHitTestVisible = false
        });
        var heroContent = new Border
        {
            Margin = new Thickness(3),
            Background = FindResource<IBrush>("DashboardHeroBackgroundBrush"),
            CornerRadius = new CornerRadius(13),
            ClipToBounds = true,
            Padding = new Thickness(27, 21)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });

        var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.DashboardWelcomeBack,
            FontSize = ResolveFontSize("FontSizeMeta"),
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.8,
            Foreground = new SolidColorBrush(Color.Parse("#78A8EA"))
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{greeting}  👋",
            FontSize = ResolveFontSize("FontSizeHeadline"),
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.DashboardHeroHint,
            FontSize = ResolveFontSize("FontSizeBody"),
            Foreground = new SolidColorBrush(Color.Parse("#C3D1E7")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 430,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(0, 2, 0, 10)
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var randomButton = DashboardHeroButton(LocalizationManager.Current.DashboardRandomPlayback, primary: true);
        randomButton.Click += DashboardRandomPlayback_OnClick;
        actions.Children.Add(randomButton);
        var infiniteMixButton = DashboardHeroButton(LocalizationManager.Current.InfiniteMixStart, primary: false);
        infiniteMixButton.Click += InfiniteMixButton_OnClick;
        actions.Children.Add(infiniteMixButton);
        var queueButton = DashboardHeroButton(LocalizationManager.Current.UpNext, primary: false);
        queueButton.Click += async (_, e) =>
        {
            e.Handled = true;
            await DashboardNavigateAsync("Queue");
        };
        actions.Children.Add(queueButton);
        stack.Children.Add(actions);
        Grid.SetColumn(stack, 0);
        layout.Children.Add(stack);

        var stats = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var values = new[]
        {
            (FindResource<Geometry>("IconAlbum")!, summary.AlbumCount, LocalizationManager.Current.Albums, Color.Parse("#C56CFF")),
            (FindResource<Geometry>("IconTrack")!, summary.TrackCount, LocalizationManager.Current.Tracks, Color.Parse("#20D9E8")),
            (FindResource<Geometry>("IconArtist")!, summary.ArtistCount, LocalizationManager.Current.Artists, Color.Parse("#4FD58A")),
            (FindResource<Geometry>("IconFavorite")!, summary.FavoriteCount, LocalizationManager.Current.Favorites, Color.Parse("#FF806C"))
        };
        for (var i = 0; i < values.Length; i++)
        {
            var tile = DashboardBuildHeroStatTile(values[i].Item1, values[i].Item2, values[i].Item3, values[i].Item4);
            stats.Children.Add(tile);
        }
        Grid.SetColumn(stats, 2);
        layout.Children.Add(stats);
        heroContent.Child = layout;
        heroLayers.Children.Add(heroContent);
        hero.Child = heroLayers;
        DashboardPanel.Children.Add(hero);
    }

    private Button DashboardHeroButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 36,
            Padding = new Thickness(18, 8),
            FontSize = ResolveFontSize("FontSizeCaption"),
            FontWeight = FontWeight.SemiBold,
            Foreground = primary ? FindResource<IBrush>("AppAccentTextBrush") : Brushes.White,
            Background = primary ? FindResource<IBrush>("AppAccentBrush") : new SolidColorBrush(Color.FromArgb(0x35, 0x08, 0x17, 0x2A)),
            BorderBrush = primary ? FindResource<IBrush>("AppAccentBrush") : new SolidColorBrush(Color.Parse("#53658D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        return button;
    }

    private Border DashboardBuildHeroStatTile(Geometry icon, int value, string label, Color accent)
    {
        var stack = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            Child = new AvaloniaPath
            {
                Data = icon,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        stack.Children.Add(new TextBlock
        {
            Text = value.ToString("N0", CultureInfo.CurrentCulture),
            FontSize = ResolveFontSize("FontSizeTitle"),
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 5, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = ResolveFontSize("FontSizeCaption"),
            Foreground = new SolidColorBrush(Color.Parse("#AFC0D8"))
        });
        return new Border
        {
            Width = 148,
            MinHeight = 126,
            Padding = new Thickness(14, 12),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xB5, 0x0B, 0x1A, 0x2B)),
            BorderBrush = new SolidColorBrush(Color.Parse("#29415D")),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    private void DashboardAddSectionHeader(string title, bool calendarNav = false) =>
        DashboardPanel.Children.Add(DashboardCreateSectionHeader(title, calendarNav));

    /// <summary>Builds a dashboard section header with an accent underline and optional month navigation.</summary>
    /// <param name="title">Section title text.</param>
    /// <param name="calendarNav">Whether to include the previous/next month buttons.</param>
    /// <param name="showAllAction">Optional "show all" action rendered as a right-aligned link.</param>
    /// <returns>The header control, ready to be inserted into a dashboard column.</returns>
    private Control DashboardCreateSectionHeader(
        string title,
        bool calendarNav = false,
        Action? showAllAction = null)
    {
        var container = new StackPanel();

        var grid = new Grid { Margin = new Thickness(0, 24, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (calendarNav)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var tb = new TextBlock
        {
            Text       = title,
            FontSize   = ResolveFontSize("FontSizeSubtitle"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 0);
        grid.Children.Add(tb);

        if (showAllAction is not null)
        {
            var showAll = new Button
            {
                Content = $"{LocalizationManager.Current.ShowAll} →",
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppAccentBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            showAll.Click += (_, e) =>
            {
                e.Handled = true;
                showAllAction();
            };
            Grid.SetColumn(showAll, 1);
            grid.Children.Add(showAll);
        }

        if (calendarNav)
        {
            var prev = CreateCalNavButton("◀");
            var next = CreateCalNavButton("▶");
            prev.Click += CalendarPrev_OnClick;
            next.Click += CalendarNext_OnClick;
            Grid.SetColumn(prev, 2);
            Grid.SetColumn(next, 3);
            grid.Children.Add(prev);
            grid.Children.Add(next);
        }

        container.Children.Add(grid);
        container.Children.Add(new Border
        {
            Height = 3,
            Width = 34,
            Background = FindResource<IBrush>("AppAccentBrush"),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 14)
        });
        return container;
    }

    private Button CreateCalNavButton(string symbol)
    {
        return new Button
        {
            Content    = symbol,
            FontSize   = 13,
            Padding    = new Thickness(8, 3, 8, 3),
            Margin     = new Thickness(4, 0, 0, 0),
            Background = FindResource<IBrush>("AppButtonBrush"),
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            Cursor     = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}



