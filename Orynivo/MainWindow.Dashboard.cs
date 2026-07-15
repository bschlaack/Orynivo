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

            var recentAlbums = await LoadRecentAlbumsAsync();
            if (buildVersion != _dashboardBuildVersion)
                return;

            var librarySummary = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetDashboardLibrarySummary();
            });
            var remoteFavoriteTrackCount = await ResolveDashboardRemoteFavoriteTrackCountAsync();
            if (buildVersion != _dashboardBuildVersion)
                return;
            librarySummary = librarySummary with
            {
                FavoriteCount = librarySummary.FavoriteCount + remoteFavoriteTrackCount
            };
            if (buildVersion != _dashboardBuildVersion)
                return;

            var (recentlyPlayed, recentThumbs, recentFavorites) = await LoadRecentlyPlayedAsync(20);
            if (buildVersion != _dashboardBuildVersion)
                return;

            var calendarData = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetCalendarData(_dashboardYear, _dashboardMonth);
            });
            if (buildVersion != _dashboardBuildVersion)
                return;

            var since = StatsPeriodSinceUnix(_dashboardStatsPeriod);
            var listeningStats = await Task.Run(() =>
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
            });
            if (buildVersion != _dashboardBuildVersion)
                return;
            var topGenres = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTopGenres(10, since);
            });
            if (buildVersion != _dashboardBuildVersion)
                return;

            var topAlbums = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTopAlbums(10, since);
            });
            if (buildVersion != _dashboardBuildVersion)
                return;

            var topArtists = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTopArtists(10, since);
            });
            if (buildVersion != _dashboardBuildVersion)
                return;

            DashboardBuildGreeting(librarySummary);
            DashboardBuildMediaOverview(recentlyPlayed, recentThumbs, recentFavorites, recentAlbums);
            DashboardBuildStatsSection(
                calendarData, topGenres, topAlbums, topArtists,
                listeningStats.Total, listeningStats.Previous, listeningStats.Trend,
                librarySummary);
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
        }
        finally
        {
            if (ReferenceEquals(DashboardPanel, buildPanel))
                DashboardPanel = visiblePanel;
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
            ("♫", summary.AlbumCount, LocalizationManager.Current.Albums, Color.Parse("#C56CFF")),
            ("♪", summary.TrackCount, LocalizationManager.Current.Tracks, Color.Parse("#20D9E8")),
            ("●", summary.ArtistCount, LocalizationManager.Current.Artists, Color.Parse("#4FD58A")),
            ("♡", summary.FavoriteCount, LocalizationManager.Current.Favorites, Color.Parse("#FF806C"))
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

    private Border DashboardBuildHeroStatTile(string icon, int value, string label, Color accent)
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
            Child = new TextBlock
            {
                Text = icon,
                FontSize = ResolveFontSize("FontSizeSubtitle"),
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(accent),
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
    private sealed record DashboardAlbum(
        long Id,
        string Title,
        string Artist,
        long? ArtistId,
        long AddedAt,
        OrynivoServerSettings? Server,
        string? ArtworkPath,
        bool HasArtwork,
        bool IsFavorite);

    /// <summary>
    /// Loads the recently added albums for the dashboard, merging the local library with every
    /// configured remote Orynivo Server and keeping the globally most recent entries.
    /// </summary>
    /// <param name="perSource">Maximum entries to fetch per source and to return overall.</param>
    /// <returns>The merged, recency-sorted recently added albums.</returns>
    private async Task<List<DashboardAlbum>> LoadRecentAlbumsAsync(int perSource = 20)
    {
        var local = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetRecentAlbums(perSource);
        });

        var combined = local
            .Select(a => new DashboardAlbum(
                a.Id, a.Title, a.Artist, a.ArtistId, a.AddedAt,
                null, a.ArtworkPath ?? a.ThumbPath, !string.IsNullOrEmpty(a.ArtworkPath ?? a.ThumbPath),
                a.IsFavorite))
            .ToList();

        var servers = _settings.OrynivoServers ?? [];
        if (servers.Count > 0)
        {
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
        }

        return combined
            .OrderByDescending(a => a.AddedAt)
            .Take(perSource)
            .ToList();
    }

    private void DashboardBuildRecentAlbums(List<DashboardAlbum> albums)
        => DashboardPanel.Children.Add(DashboardCreateRecentAlbumsStrip(albums));

    private ScrollViewer DashboardCreateRecentAlbumsStrip(List<DashboardAlbum> albums)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var template = FindResource<IDataTemplate>("AlbumArtworkCardTemplate");
        if (template is not null)
            foreach (var album in albums)
                panel.Children.Add(BuildRecentAlbumCard(album, template));

        scroll.Content = panel;
        return scroll;
    }

    private void DashboardBuildMediaOverview(
        List<DailyHistoryEntry> recentlyPlayed,
        Dictionary<long, string> recentThumbs,
        HashSet<long> recentFavorites,
        List<DashboardAlbum> recentAlbums)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var playedContent = recentlyPlayed.Count == 0
            ? DashboardNoDataText()
            : DashboardCreateRecentlyPlayedStrip(recentlyPlayed, recentThumbs, recentFavorites);
        var playedCard = DashboardBuildMediaSectionCard(
            LocalizationManager.Current.RecentlyPlayed,
            () => _ = ShowAllRecentlyPlayedAsync(),
            playedContent,
            playedContent as ScrollViewer);
        Grid.SetColumn(playedCard, 0);
        grid.Children.Add(playedCard);

        var albumsContent = recentAlbums.Count == 0
            ? DashboardNoDataText()
            : DashboardCreateRecentAlbumsStrip(recentAlbums);
        var albumsCard = DashboardBuildMediaSectionCard(
            LocalizationManager.Current.RecentAlbums,
            () => _ = ShowAllRecentAlbumsAsync(),
            albumsContent,
            albumsContent as ScrollViewer);
        Grid.SetColumn(albumsCard, 2);
        grid.Children.Add(albumsCard);
        DashboardPanel.Children.Add(grid);
    }

    private Border DashboardBuildMediaSectionCard(
        string title,
        Action showAllAction,
        Control content,
        ScrollViewer? carousel = null)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(2, 0, 2, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = ResolveFontSize("FontSizeBodyStrong"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (carousel is not null)
        {
            var previous = DashboardCreateCarouselButton(forward: false);
            var next = DashboardCreateCarouselButton(forward: true);
            var isAnimating = false;
            previous.IsEnabled = false;
            next.IsEnabled = false;

            void UpdateButtons()
            {
                previous.IsEnabled = !isAnimating && carousel.Offset.X > 1;
                next.IsEnabled = !isAnimating &&
                                 carousel.Offset.X + carousel.Viewport.Width < carousel.Extent.Width - 1;
                previous.Opacity = previous.IsEnabled ? 0.92 : 0.35;
                next.Opacity = next.IsEnabled ? 0.92 : 0.35;
            }

            previous.Click += async (_, e) =>
            {
                e.Handled = true;
                isAnimating = true;
                UpdateButtons();
                await DashboardScrollCarouselAsync(carousel, -1);
                isAnimating = false;
                UpdateButtons();
            };
            next.Click += async (_, e) =>
            {
                e.Handled = true;
                isAnimating = true;
                UpdateButtons();
                await DashboardScrollCarouselAsync(carousel, 1);
                isAnimating = false;
                UpdateButtons();
            };
            carousel.ScrollChanged += (_, _) => UpdateButtons();
            carousel.SizeChanged += (_, _) => UpdateButtons();
            Grid.SetColumn(previous, 1);
            Grid.SetColumn(next, 2);
            header.Children.Add(previous);
            header.Children.Add(next);
        }

        var showAll = new Button
        {
            Content = LocalizationManager.Current.ShowAll,
            FontSize = ResolveFontSize("FontSizeCaption"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppAccentBrush"),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            RenderTransform = new TranslateTransform(0, 2)
        };
        showAll.Click += (_, e) => { e.Handled = true; showAllAction(); };
        Grid.SetColumn(showAll, 3);
        header.Children.Add(showAll);
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        return new Border
        {
            Padding = new Thickness(12, 12, 12, 10),
            Background = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = layout
        };
    }

    /// <summary>
    /// Builds one recently added album card from the shared album artwork template,
    /// so it looks and behaves exactly like the normal Albums artwork view
    /// (cover change, favorite toggle, and in-library navigation).
    /// </summary>
    /// <param name="album">The recently added album to render.</param>
    /// <param name="template">The shared album artwork card template.</param>
    /// <returns>The realised card control bound to a backing <see cref="ContentRow"/>.</returns>
    private Control BuildRecentAlbumCard(DashboardAlbum album, IDataTemplate template)
    {
        var row = BuildRecentAlbumRow(album);
        var control = template.Build(row) ?? new Border();
        control.DataContext = row;
        control.DoubleTapped += RecentAlbumCard_OnDoubleTapped;
        return control;
    }

    /// <summary>Maps a dashboard album to a <see cref="ContentRow"/> for the shared artwork card.</summary>
    /// <param name="album">The dashboard album.</param>
    /// <returns>A hydrated content row (local <c>Album</c> or remote <c>OrynivoAlbum</c>).</returns>
    private ContentRow BuildRecentAlbumRow(DashboardAlbum album)
    {
        var isRemote = album.Server is not null;
        var row = new ContentRow
        {
            Id = album.Id,
            AlbumId = album.Id,
            ArtistId = album.ArtistId,
            Title = string.IsNullOrWhiteSpace(album.Title) ? LocalizationManager.Current.Unknown : album.Title,
            Artist = string.IsNullOrWhiteSpace(album.Artist) ? null : album.Artist,
            ArtworkPath = album.ArtworkPath,
            IsFavorite = album.IsFavorite,
            EntityType = isRemote ? "OrynivoAlbum" : "Album",
            ExternalId = isRemote ? album.Id.ToString(CultureInfo.InvariantCulture) : null,
            OrynivoServer = album.Server,
            FilePath = ""
        };
        EnsureArtworkHydrated(row);
        return row;
    }

    /// <summary>Opens a recently added album card on double-click, staying in its own library.</summary>
    /// <param name="sender">The tapped card control.</param>
    /// <param name="e">The tap event data.</param>
    private async void RecentAlbumCard_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Buttons inside the card (title/artist links, favorite, cover) handle
        // their own clicks; ignore double-taps that land on them.
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (sender is not Control { DataContext: ContentRow { Id: long albumId } row })
            return;

        if (row.EntityType == "OrynivoAlbum")
        {
            _activeArtistFilterId = null;
            _activeArtistFilterName = null;
            _activeOrynivoServer = row.OrynivoServer;
            await OpenOrynivoAlbumTracksAsync(albumId, row.Title, row.Artist);
        }
        else
        {
            await ShowAlbumTracksAsync(albumId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    /// <summary>Builds the horizontal "recently played" strip of compact history cards.</summary>
    /// <param name="entries">Recent, de-duplicated playback-history entries.</param>
    /// <param name="thumbs">Local album thumbnail paths keyed by album identifier.</param>
    private void DashboardBuildRecentlyPlayed(
        List<DailyHistoryEntry> entries,
        Dictionary<long, string> thumbs,
        HashSet<long> favoriteTrackIds)
        => DashboardPanel.Children.Add(DashboardCreateRecentlyPlayedStrip(entries, thumbs, favoriteTrackIds));

    private ScrollViewer DashboardCreateRecentlyPlayedStrip(
        List<DailyHistoryEntry> entries,
        Dictionary<long, string> thumbs,
        HashSet<long> favoriteTrackIds)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in entries)
            panel.Children.Add(BuildRecentlyPlayedCard(entry, thumbs, favoriteTrackIds));
        scroll.Content = panel;
        return scroll;
    }

    private static async Task DashboardScrollCarouselAsync(ScrollViewer scroll, double direction)
    {
        var start = scroll.Offset.X;
        var step = Math.Max(180, scroll.Viewport.Width * 0.8);
        var maximum = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
        var target = Math.Clamp(start + direction * step, 0, maximum);
        const int frameCount = 12;
        for (var frame = 1; frame <= frameCount; frame++)
        {
            var progress = frame / (double)frameCount;
            var eased = 1 - Math.Pow(1 - progress, 3);
            scroll.Offset = new Vector(start + (target - start) * eased, scroll.Offset.Y);
            await Task.Delay(16);
        }
    }

    private async Task<int> ResolveDashboardRemoteFavoriteTrackCountAsync()
    {
        var tasks = (_settings.OrynivoServers ?? [])
            .Select(async server =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var facets = await _orynivoClient.GetTrackFacetsAsync(server, timeout.Token);
                    var favoriteIds = facets
                        .Where(facet => IsOrynivoFavorite(server, "Track", facet.Id))
                        .Select(facet => facet.Id)
                        .Distinct()
                        .ToList();
                    if (favoriteIds.Count == 0)
                        return 0;

                    var tracks = await CreateOrynivoCatalogProvider(server)
                        .GetTracksByIdsAsync(favoriteIds, timeout.Token);
                    return tracks.Select(track => track.Id).Distinct().Count();
                }
                catch
                {
                    // The favorite view also omits a server that cannot resolve its rows.
                    return 0;
                }
            });
        var counts = await Task.WhenAll(tasks);
        return counts.Sum();
    }

    private Button DashboardCreateCarouselButton(bool forward)
    {
        var button = CreateCalNavButton(string.Empty);
        button.Width = 26;
        button.Height = 26;
        button.Margin = new Thickness(2, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Opacity = 0.92;
        button.Content = new AvaloniaPath
        {
            Width = 7,
            Height = 12,
            Stretch = Stretch.Fill,
            Data = Geometry.Parse(forward ? "M 0 0 L 7 6 L 0 12" : "M 7 0 L 0 6 L 7 12"),
            Stroke = FindResource<IBrush>("AppPrimaryTextBrush"),
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        return button;
    }

    /// <summary>Builds a compact recently played card for the dashboard strip or full-page history grid.</summary>
    /// <param name="entry">Playback-history entry to render.</param>
    /// <param name="thumbs">Local album thumbnail paths keyed by album identifier.</param>
    /// <param name="expandedSpacing">Whether to use the roomier spacing needed by the full-page grid.</param>
    /// <returns>The card control.</returns>
    private Control BuildRecentlyPlayedCard(
        DailyHistoryEntry entry,
        Dictionary<long, string> thumbs,
        HashSet<long> favoriteTrackIds,
        bool expandedSpacing = false)
    {
        const double artSize = 160;
        var playable = IsPlayableHistoryEntry(entry);
        var isRemote = TryGetOrynivoHistoryTarget(entry, out var favoriteServer, out var favoriteRemoteTrackId);
        var isPlex = entry.ExternalId?.StartsWith("plex:", StringComparison.OrdinalIgnoreCase) == true;
        var sourceBadge = isRemote ? "OS" : isPlex ? "P" : "L";
        var isFavorite = isRemote
            ? IsOrynivoFavorite(favoriteServer, "Track", favoriteRemoteTrackId)
            : entry.TrackId is long localTrackId && favoriteTrackIds.Contains(localTrackId);
        var card = new Border
        {
            Width           = 180,
            MinHeight       = 276,
            Margin          = expandedSpacing
                ? new Thickness(8, 0, 12, 20)
                : new Thickness(8),
            Padding         = new Thickness(10),
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            CornerRadius    = new CornerRadius(12),
            Cursor          = new Cursor(playable ? StandardCursorType.Hand : StandardCursorType.Arrow)
        };
        card.Classes.Add("motionCard");

        var stack = new StackPanel { Spacing = 4 };

        var artHost = new Border
        {
            Width        = artSize,
            Height       = artSize,
            Background   = FindResource<IBrush>("AppArtworkPlaceholderBrush"),
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true
        };
        var artContent = new Grid();

        // Initials placeholder as the base layer; a thumbnail (decoded off the UI
        // thread) is layered on top when available so 200 cards build without a hitch.
        var initialsAvatar = new Orynivo.Controls.InitialsAvatar
        {
            DisplayName = string.IsNullOrWhiteSpace(entry.Title) ? entry.Artist : entry.Title,
            FontSize    = 30,
            IsHitTestVisible = false
        };
        artContent.Children.Add(initialsAvatar);

        string? thumbPath = entry.AlbumId is long albumId && thumbs.TryGetValue(albumId, out var path) ? path : null;
        if (!string.IsNullOrEmpty(thumbPath))
        {
            var thumbImage = new Image
            {
                Width   = artSize,
                Height  = artSize,
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false
            };
            artContent.Children.Add(thumbImage);
            _ = LoadDashboardLocalArtworkAsync(thumbImage, thumbPath);
        }
        else if (TryGetOrynivoHistoryTarget(entry, out var server, out var trackId))
        {
            var thumbImage = new Image
            {
                Width   = artSize,
                Height  = artSize,
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false
            };
            artContent.Children.Add(thumbImage);
            _ = LoadDashboardRemoteArtworkAsync(
                thumbImage,
                OrynivoServerClient.GetTrackArtworkUrl(server, trackId, 320));
        }

        // A play affordance that fades in on hover for playable history entries.
        // The overlay needs an explicit size and a high ZIndex: without a fixed-size
        // sibling (e.g. an avatar-only card with no cover) a stretch-only overlay was
        // arranged to just the glyph size, so the play symbol did not appear on hover.
        var playOverlay = new Border
        {
            Width              = artSize,
            Height             = artSize,
            ZIndex             = 10,
            Background         = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            CornerRadius       = new CornerRadius(9),
            IsHitTestVisible   = false,
            IsVisible          = false,
            Child = new TextBlock
            {
                Text = "▶",
                FontSize = 26,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        if (playable)
            artContent.Children.Add(playOverlay);

        artHost.Child = artContent;
        stack.Children.Add(artHost);

        var titleBlock = new TextBlock
        {
            Text              = entry.Title,
            FontSize          = ResolveFontSize("FontSizeCaption"),
            FontWeight        = FontWeight.SemiBold,
            Foreground        = FindResource<IBrush>("AppPrimaryTextBrush"),
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxLines          = 1,
            Margin            = new Thickness(2, 2, 2, 0)
        };
        stack.Children.Add(titleBlock);

        var artistButton = new Button
        {
            Content = entry.Artist,
            FontSize = ResolveFontSize("FontSizeMeta"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = artSize,
            Margin = new Thickness(2, 0, 2, 0),
            IsVisible = CanOpenHistoryArtist(entry)
        };
        artistButton.Click += async (_, e) =>
        {
            e.Handled = true;
            await OpenHistoryArtistAsync(entry);
        };
        var artistBlock = new TextBlock
        {
            Text         = entry.Artist,
            FontSize     = ResolveFontSize("FontSizeMeta"),
            Foreground   = FindResource<IBrush>("AppSecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines     = 1,
            Margin       = new Thickness(2, 0, 2, 0),
            IsVisible    = !string.IsNullOrWhiteSpace(entry.Artist) && !artistButton.IsVisible
        };
        stack.Children.Add(artistButton);
        stack.Children.Add(artistBlock);

        var footer = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var favoriteButton = new Button
        {
            Content = isFavorite ? "❤" : "♡",
            Width = 28,
            Height = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = FindResource<IBrush>("AppFavoriteBrush"),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = ResolveFontSize("FontSizeBodyStrong"),
            Cursor = new Cursor(StandardCursorType.Hand),
            IsEnabled = !isPlex
        };
        favoriteButton.Click += (_, e) =>
        {
            e.Handled = true;
            isFavorite = !isFavorite;
            favoriteButton.Content = isFavorite ? "❤" : "♡";
            if (isRemote)
            {
                var key = GetOrynivoFavoriteKey(favoriteServer.Id, "Track", favoriteRemoteTrackId);
                if (isFavorite)
                    _settings.OrynivoServerFavorites.Add(key);
                else
                    _settings.OrynivoServerFavorites.Remove(key);
                _settingsStore.Save(_settings);
                RefreshOrynivoFavoriteRows(favoriteServer, favoriteRemoteTrackId, isFavorite);
            }
            else if (entry.TrackId is long id)
            {
                using var db = AudioDatabase.OpenDefault();
                db.SetTrackFavorite(id, isFavorite);
            }
        };
        footer.Children.Add(favoriteButton);
        var badge = new Border
        {
            Height = 20,
            MinWidth = 27,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(10),
            Background = FindResource<IBrush>("AppAccentSoftBrush"),
            BorderBrush = FindResource<IBrush>("AppAccentBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = sourceBadge,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppAccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ToolTip.SetTip(badge, sourceBadge switch
        {
            "OS" => "Orynivo Server",
            "P" => "Plex",
            _ => LocalizationManager.Current.LocalLibrary
        });
        Grid.SetColumn(badge, 1);
        footer.Children.Add(badge);
        stack.Children.Add(footer);

        card.Child = stack;

        if (TryGetOrynivoHistoryTarget(entry, out _, out _))
            _ = HydrateRecentlyPlayedRemoteCardAsync(entry, titleBlock, artistButton, artistBlock, initialsAvatar);

        card.PointerEntered += (_, _) =>
        {
            if (playable)
                playOverlay.IsVisible = true;
        };
        card.PointerExited += (_, _) =>
        {
            if (playable)
                playOverlay.IsVisible = false;
        };

        if (playable)
        {
            card.PointerReleased += async (_, e) =>
            {
                if (FindAncestor<Button>(e.Source as Visual) is not null)
                    return;
                e.Handled = true;
                await PlayHistoryEntryInPlaceAsync(entry);
            };
        }

        return card;
    }

    /// <summary>Refreshes a remote recently played card with authoritative server metadata.</summary>
    /// <param name="entry">Playback-history entry to resolve.</param>
    /// <param name="titleBlock">Title text block to update.</param>
    /// <param name="artistButton">Artist link button to update.</param>
    /// <param name="artistBlock">Artist text block to update.</param>
    /// <param name="initialsAvatar">Artwork placeholder to retitle.</param>
    /// <returns>A task representing the asynchronous metadata refresh.</returns>
    private async Task HydrateRecentlyPlayedRemoteCardAsync(
        DailyHistoryEntry entry,
        TextBlock titleBlock,
        Button artistButton,
        TextBlock artistBlock,
        Orynivo.Controls.InitialsAvatar initialsAvatar)
    {
        try
        {
            var row = await ResolveOrynivoHistoryTrackRowAsync(entry);
            if (row is null)
                return;

            if (!string.IsNullOrWhiteSpace(row.Title))
                titleBlock.Text = row.Title;
            var canOpenArtist = row.ArtistId.HasValue && !string.IsNullOrWhiteSpace(row.Artist);
            artistButton.Content = row.Artist;
            artistButton.IsVisible = canOpenArtist;
            artistBlock.Text = row.Artist;
            artistBlock.IsVisible = !canOpenArtist && !string.IsNullOrWhiteSpace(row.Artist);
            initialsAvatar.DisplayName = string.IsNullOrWhiteSpace(row.Title) ? row.Artist : row.Title;
        }
        catch
        {
            // Stale or unreachable servers leave the persisted history text visible.
        }
    }

    /// <summary>
    /// Determines whether a recently played entry can be replayed in place: only
    /// music tracks (not radio/podcast, which have their own views) that are either
    /// a locally available file or a playable stream URL (remote server / Plex).
    /// </summary>
    /// <param name="entry">The history entry to test.</param>
    /// <returns><see langword="true"/> when the entry can be played from its card.</returns>
    /// <summary>Determines whether a playback-history artist can be opened from local or remote metadata.</summary>
    /// <param name="entry">The history entry to test.</param>
    /// <returns><see langword="true"/> when the artist has a local ID or a resolvable Orynivo Server track target.</returns>
    /// <summary>Opens the artist album list for a local or Orynivo Server playback-history entry.</summary>
    /// <param name="entry">The history entry whose artist should be opened.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    /// <summary>
    /// Plays a recently played entry without leaving the current view: it replaces
    /// the queue with just this track and starts playback, so the dashboard or the
    /// full "recently played" view stays open.
    /// </summary>
    /// <param name="entry">The history entry to play.</param>
    /// <returns>A task representing the asynchronous playback start.</returns>
    /// <summary>Loads and registers a full remote track row for a history entry before replay.</summary>
    /// <param name="entry">The playback-history entry.</param>
    /// <returns>The hydrated remote row, or <see langword="null"/> when the entry is not resolvable.</returns>
    /// <summary>Fills a card image with a local thumbnail, decoding off the UI thread.</summary>
    /// <param name="image">The card image control to populate.</param>
    /// <param name="thumbnailPath">The local thumbnail file path.</param>
    /// <returns>A task representing the asynchronous load.</returns>
    private static async Task LoadDashboardLocalArtworkAsync(Image image, string thumbnailPath)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                if (!File.Exists(thumbnailPath))
                    return null;
                using var stream = File.OpenRead(thumbnailPath);
                return new Bitmap(stream);
            });
            if (bitmap is not null)
                image.Source = bitmap;
        }
        catch { /* a missing or invalid thumbnail leaves the placeholder visible */ }
    }

    /// <summary>Fills a dashboard album card's image with remote server artwork (cached locally).</summary>
    /// <param name="image">The card image control to populate.</param>
    /// <param name="artUrl">The authenticated remote artwork URL.</param>
    /// <returns>A task representing the asynchronous load.</returns>
    private async Task LoadDashboardRemoteArtworkAsync(Image image, string artUrl)
    {
        try
        {
            var bitmap = await LoadRemoteArtworkImageAsync(artUrl, 320);
            if (bitmap is not null)
                image.Source = bitmap;
        }
        catch { }
    }

    /// <summary>Builds the calendar card control for the dashboard stats section.</summary>
    /// <param name="data">Per-day playback aggregates for the current month.</param>
    /// <returns>The bordered calendar card.</returns>
    private Control DashboardBuildCalendarCard(List<CalendarDayData> data)
    {
        var dayMap = data.ToDictionary(d => d.Day);
        int daysInMonth = DateTime.DaysInMonth(_dashboardYear, _dashboardMonth);
        var firstDay = new DateTime(_dashboardYear, _dashboardMonth, 1);
        int startDow = ((int)firstDay.DayOfWeek + 6) % 7; // Monday=0

        var outer = new Border
        {
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(0),
            Margin          = new Thickness(0)
        };

        var inner = new StackPanel();
        _calendarInner = inner;

        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (int i = 0; i < 7; i++)
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] dayNames = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];
        for (int i = 0; i < 7; i++)
        {
            var tb = new TextBlock
            {
                Text      = dayNames[i],
                FontSize  = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin    = new Thickness(0, 0, 0, 4)
            };
            Grid.SetColumn(tb, i);
            headerRow.Children.Add(tb);
        }
        inner.Children.Add(headerRow);

        DashboardRefreshCalendarContent(inner, dayMap, daysInMonth, startDow);

        outer.Child = inner;
        return outer;
    }

    private void DashboardRefreshCalendarContent(StackPanel inner, Dictionary<int, CalendarDayData> dayMap,
        int daysInMonth, int startDow)
    {
        // Remove all rows except the header (index 0)
        while (inner.Children.Count > 1)
            inner.Children.RemoveAt(1);

        int col = startDow;
        Grid? rowGrid = null;

        for (int day = 1; day <= daysInMonth; day++)
        {
            if (col == 0 || rowGrid is null)
            {
                rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                for (int i = 0; i < 7; i++)
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                inner.Children.Add(rowGrid);
            }

            dayMap.TryGetValue(day, out var dayData);
            var cell = BuildCalDayCell(day, dayData);
            Grid.SetColumn(cell, col);
            rowGrid.Children.Add(cell);

            col = (col + 1) % 7;
        }
    }

    private Control BuildCalDayCell(int day, CalendarDayData? data)
    {
        bool isToday = _dashboardYear == DateTime.Now.Year
                    && _dashboardMonth == DateTime.Now.Month
                    && day == DateTime.Now.Day;

        var border = new Border
        {
            Margin          = new Thickness(2),
            MinHeight       = 31,
            Background      = isToday
                ? FindResource<IBrush>("AppAccentSoftBrush")
                : Brushes.Transparent,
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(16),
            Padding         = new Thickness(3),
            Cursor          = data is not null && data.TotalSeconds > 0
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow)
        };
        if (data is not null && data.TotalSeconds > 0)
            ToolTip.SetTip(border, string.Format(
                LocalizationManager.Current.DailyHistoryTitle,
                new DateTime(_dashboardYear, _dashboardMonth, day)
                    .ToString("D", CultureInfo.CurrentCulture)));

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text       = day.ToString(),
            FontSize   = 11,
            FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (data is not null && data.TotalSeconds > 0)
        {
            stack.Children.Add(new Border
            {
                Width = 4,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = FindResource<IBrush>("AppAccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }

        if (data is not null && data.TotalSeconds > 0)
        {
            var date = new DateTime(_dashboardYear, _dashboardMonth, day);
            border.PointerReleased += async (_, e) =>
            {
                e.Handled = true;
                await ShowDailyHistoryAsync(date);
            };
        }

        border.Child = stack;
        return border;
    }

    private void ShowPodcastInfo(PodcastPlayback playback)
    {
        var episode = playback.Episode;
        PodcastInfoImage.Source = null;
        PodcastInfoImagePlaceholder.IsVisible = true;
        PodcastInfoPodcastName.Text = playback.Podcast.Name;
        PodcastInfoEpisodeTitle.Text = episode.Title;

        var metadata = new List<string>();
        if (episode.PublishedAt is { } publishedAt)
            metadata.Add(string.Format(
                LocalizationManager.Current.PodcastPublishedOn,
                publishedAt.ToLocalTime().ToString("d")));
        if (episode.FeedDuration is { } duration && duration > TimeSpan.Zero)
            metadata.Add(string.Format(
                LocalizationManager.Current.PodcastEpisodeDuration,
                FormatTime(duration)));
        if (!string.IsNullOrWhiteSpace(playback.Podcast.Author))
            metadata.Add(playback.Podcast.Author);
        if (!string.IsNullOrWhiteSpace(playback.Podcast.Genre))
            metadata.Add(playback.Podcast.Genre);
        PodcastInfoMetadata.Text = string.Join("  ·  ", metadata);

        var description = NormalizePodcastDescription(episode.Description);
        PodcastInfoDescription.Text = string.IsNullOrWhiteSpace(description)
            ? LocalizationManager.Current.PodcastDescriptionUnavailable
            : description;

        _ = LoadPodcastInfoArtworkAsync(
            playback.Podcast.ArtworkUrl,
            _playbackCts?.Token ?? CancellationToken.None);
    }

    private async Task LoadPodcastInfoArtworkAsync(
        string? artworkUrl,
        CancellationToken cancellationToken)
    {
        var image = await DownloadPodcastImageAsync(artworkUrl, 600, cancellationToken);
        if (image is null || cancellationToken.IsCancellationRequested)
            return;

        PodcastInfoImage.Source = image;
        PodcastInfoImagePlaceholder.IsVisible = false;
    }

    /// <summary>
    /// Arranges the listening-statistics blocks: a shared period selector governs the
    /// genre, album, and artist analytics cards, while the calendar keeps its own month
    /// navigation. Blocks sit side by side on wide dashboards and stack on narrow ones.
    /// </summary>
    /// <param name="calendarData">Per-day playback aggregates for the current month.</param>
    /// <param name="topGenres">Ranked genres by play time in the selected period.</param>
    /// <param name="topAlbums">Ranked albums by play time in the selected period.</param>
    /// <param name="topArtists">Ranked artists by play time in the selected period.</param>
    private void DashboardBuildStatsSection(
        List<CalendarDayData> calendarData,
        List<(string Genre, double Seconds)> topGenres,
        List<TopAlbumStat> topAlbums,
        List<TopArtistStat> topArtists,
        double totalListeningSeconds,
        double previousListeningSeconds,
        IReadOnlyList<double> listeningTrend,
        DashboardLibrarySummary librarySummary)
    {
        var overview = new Grid();
        var wide = _dashboardTwoColumnLayout == true;
        if (wide)
        {
            for (var column = 0; column < 7; column++)
                overview.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = column % 2 == 0
                        ? new GridLength(1, GridUnitType.Star)
                        : new GridLength(12)
                });
            overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var cards = new Control[]
        {
            DashboardBuildListeningSummaryCard(totalListeningSeconds, previousListeningSeconds, listeningTrend),
            DashboardWrapOverviewCard(
                LocalizationManager.Current.TopGenres,
                DashboardBuildTopGenresCard(topGenres.Take(5).ToList())),
            DashboardBuildCompactCalendarCard(calendarData),
            DashboardBuildQuickAccessCard(librarySummary)
        };
        for (var i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], wide ? i * 2 : (i % 2) * 2);
            Grid.SetRow(cards[i], wide ? 0 : (i / 2) * 2);
            overview.Children.Add(cards[i]);
        }
        DashboardPanel.Children.Add(overview);

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var albumsCard = DashboardWrapOverviewCard(
            LocalizationManager.Current.TopAlbums,
            DashboardBuildTopAlbumsCard(topAlbums));
        details.Children.Add(albumsCard);
        var artistsCard = DashboardWrapOverviewCard(
            LocalizationManager.Current.TopArtists,
            DashboardBuildTopArtistsCard(topArtists));
        Grid.SetColumn(artistsCard, 2);
        details.Children.Add(artistsCard);
        DashboardPanel.Children.Add(details);
    }

    private Border DashboardBuildListeningSummaryCard(
        double totalListeningSeconds,
        double previousListeningSeconds,
        IReadOnlyList<double> listeningTrend)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = Math.Round(totalListeningSeconds / 60).ToString("N0", CultureInfo.CurrentCulture),
            FontSize = ResolveFontSize("FontSizeHeadline"),
            FontWeight = FontWeight.Bold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.DashboardTotalMinutes,
            FontSize = ResolveFontSize("FontSizeCaption"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush")
        });

        if (previousListeningSeconds > 0)
        {
            var change = (totalListeningSeconds - previousListeningSeconds) / previousListeningSeconds * 100;
            content.Children.Add(new TextBlock
            {
                Text = $"{(change >= 0 ? "▲" : "▼")} {Math.Abs(change):0}%  ·  {LocalizationManager.Current.PeriodPrevious}",
                FontSize = ResolveFontSize("FontSizeMeta"),
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(change >= 0 ? "#4FD58A" : "#FF806C")),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        content.Children.Add(DashboardBuildListeningChart(listeningTrend));
        var periodBox = new ComboBox
        {
            MinWidth = 138,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[]
            {
                LocalizationManager.Current.PeriodAllTime,
                LocalizationManager.Current.PeriodThisYear,
                LocalizationManager.Current.PeriodThisMonth,
                LocalizationManager.Current.PeriodLast30Days,
                LocalizationManager.Current.PeriodLast7Days
            },
            SelectedIndex = (int)_dashboardStatsPeriod
        };
        periodBox.SelectionChanged += DashboardStatsPeriod_OnSelectionChanged;
        return DashboardWrapOverviewCard(LocalizationManager.Current.ListeningStats, content, periodBox);
    }

    private Control DashboardBuildListeningChart(IReadOnlyList<double> listeningTrend)
    {
        var values = listeningTrend.Count > 1
            ? listeningTrend.ToArray()
            : new double[] { 0, 0, 0, 0, 0, 0, 0 };
        var peakMinutes = Math.Max(1, values.Max() / 60);
        var maxMinutes = DashboardNiceAxisMaximum(peakMinutes);
        var maxSeconds = maxMinutes * 60;
        const double plotWidth = 300;
        const double plotHeight = 82;
        const double topPadding = 5;
        const double bottomY = 76;

        var outer = new Grid { Height = 124, Margin = new Thickness(0, 8, 0, 0) };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(94) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

        var yLegend = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        yLegend.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        yLegend.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        yLegend.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var yLabels = new[] { maxMinutes, maxMinutes / 2, 0d };
        for (var i = 0; i < yLabels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = yLabels[i].ToString("0", CultureInfo.CurrentCulture),
                FontSize = 9,
                Foreground = FindResource<IBrush>("AppMutedTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center
            };
            Grid.SetRow(label, i);
            yLegend.Children.Add(label);
        }
        outer.Children.Add(yLegend);

        var plot = new Grid { ClipToBounds = true };
        for (var i = 0; i < 3; i++)
            plot.Children.Add(new Border
            {
                Height = 1,
                Background = FindResource<IBrush>("AppGridLineBrush"),
                Opacity = 0.65,
                VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center
            });

        var points = values.Select((value, index) => new Point(
            index * plotWidth / Math.Max(1, values.Length - 1),
            bottomY - value / maxSeconds * (bottomY - topPadding))).ToList();
        var smoothLine = DashboardBuildSmoothCurve(points);
        // Anchor the geometry at the logical chart origin. Without this zero-length
        // subpath Avalonia's Stretch.Fill normalizes the curve's own highest point
        // to the top edge, which visually changes the Y scale whenever no value
        // reaches the configured axis maximum.
        var areaData = $"M 0,0 L 0,0 {smoothLine} L {plotWidth.ToString(CultureInfo.InvariantCulture)},{plotHeight.ToString(CultureInfo.InvariantCulture)} L 0,{plotHeight.ToString(CultureInfo.InvariantCulture)} Z";
        plot.Children.Add(new AvaloniaPath
        {
            Data = Geometry.Parse(areaData),
            Stretch = Stretch.Fill,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x88, 0x20, 0xD9, 0xE8), 0),
                    new GradientStop(Color.FromArgb(0x3D, 0x20, 0xD9, 0xE8), 0.55),
                    new GradientStop(Color.FromArgb(0x05, 0x20, 0xD9, 0xE8), 1)
                }
            },
            Stroke = FindResource<IBrush>("AppAccentBrush"),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        });

        var hoverZones = new Grid { Background = Brushes.Transparent };
        var pointDates = DashboardListeningDates(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            hoverZones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var hitZone = new Border { Background = Brushes.Transparent };
            ToolTip.SetTip(
                hitZone,
                $"{pointDates[index].ToString("d", CultureInfo.CurrentCulture)} · " +
                $"{values[index] / 60:N0} {LocalizationManager.Current.DashboardMinutesShort}");
            Grid.SetColumn(hitZone, index);
            hoverZones.Children.Add(hitZone);
        }
        plot.Children.Add(hoverZones);
        Grid.SetColumn(plot, 1);
        outer.Children.Add(plot);

        var unit = new TextBlock
        {
            Text = LocalizationManager.Current.DashboardMinutesShort,
            FontSize = 9,
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 6, 0)
        };
        Grid.SetRow(unit, 1);
        outer.Children.Add(unit);

        var xLegend = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        var xLabels = DashboardListeningLegendLabels(Math.Min(7, values.Length));
        for (var i = 0; i < xLabels.Count; i++)
        {
            xLegend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = xLabels[i],
                FontSize = 9,
                Foreground = FindResource<IBrush>("AppMutedTextBrush"),
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == xLabels.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center
            };
            Grid.SetColumn(label, i);
            xLegend.Children.Add(label);
        }
        Grid.SetColumn(xLegend, 1);
        Grid.SetRow(xLegend, 1);
        outer.Children.Add(xLegend);
        return outer;
    }

    private static string DashboardBuildSmoothCurve(IReadOnlyList<Point> points)
    {
        var path = new StringBuilder($"M {DashboardPoint(points[0])} ");
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            var minimumY = Math.Min(p1.Y, p2.Y);
            var maximumY = Math.Max(p1.Y, p2.Y);
            c1 = new Point(c1.X, Math.Clamp(c1.Y, minimumY, maximumY));
            c2 = new Point(c2.X, Math.Clamp(c2.Y, minimumY, maximumY));
            path.Append($"C {DashboardPoint(c1)} {DashboardPoint(c2)} {DashboardPoint(p2)} ");
        }
        return path.ToString();
    }

    private static string DashboardPoint(Point point) =>
        $"{point.X.ToString("0.###", CultureInfo.InvariantCulture)},{point.Y.ToString("0.###", CultureInfo.InvariantCulture)}";

    private static double DashboardNiceAxisMaximum(double peakMinutes)
    {
        var roughStep = Math.Max(1, peakMinutes / 4);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var normalized = roughStep / magnitude;
        var step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        var maximum = Math.Ceiling(peakMinutes / step) * step;
        if (maximum <= peakMinutes + 0.0001)
            maximum += step;
        return maximum;
    }

    private static int DashboardListeningBucketCount(StatsPeriod period) => period switch
    {
        StatsPeriod.Last7Days => 7,
        StatsPeriod.Last30Days => 30,
        StatsPeriod.ThisMonth => Math.Max(2, DateTime.Now.Day),
        StatsPeriod.ThisYear => 12,
        _ => 12
    };

    private IReadOnlyList<string> DashboardListeningLegendLabels(int count)
        => DashboardListeningDates(count)
            .Select(date => _dashboardStatsPeriod == StatsPeriod.ThisYear
                ? date.ToString("MMM", CultureInfo.CurrentCulture)
                : date.ToString("dd.MM", CultureInfo.CurrentCulture))
            .ToList();

    private IReadOnlyList<DateTime> DashboardListeningDates(int count)
    {
        var now = DateTime.Now.Date;
        DateTime start = _dashboardStatsPeriod switch
        {
            StatsPeriod.ThisYear => new DateTime(now.Year, 1, 1),
            StatsPeriod.ThisMonth => new DateTime(now.Year, now.Month, 1),
            StatsPeriod.Last7Days => now.AddDays(-7),
            StatsPeriod.Last30Days => now.AddDays(-30),
            _ => now.AddMonths(-Math.Max(1, count - 1))
        };
        return Enumerable.Range(0, count)
            .Select(i => start.AddTicks((now - start).Ticks * i / Math.Max(1, count - 1)))
            .ToList();
    }

    private Border DashboardBuildCompactCalendarCard(List<CalendarDayData> calendarData)
    {
        var content = DashboardBuildCalendarCard(calendarData);
        var title = string.Format(
            LocalizationManager.Current.Calendar,
            new DateTime(_dashboardYear, _dashboardMonth, 1).ToString("MMMM yyyy"));
        var card = DashboardWrapOverviewCard(title, content);
        if (card.Child is Grid layout && layout.Children.FirstOrDefault() is Grid header)
        {
            var nav = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var prev = CreateCalNavButton("‹");
            var next = CreateCalNavButton("›");
            prev.Click += CalendarPrev_OnClick;
            next.Click += CalendarNext_OnClick;
            nav.Children.Add(prev);
            nav.Children.Add(next);
            Grid.SetColumn(nav, 1);
            header.Children.Add(nav);
        }
        return card;
    }

    private Border DashboardBuildQuickAccessCard(DashboardLibrarySummary summary)
    {
        var rows = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Stretch };
        rows.Children.Add(DashboardQuickAccessButton("♡", LocalizationManager.Current.Favorites, async () =>
        {
            ClearTrackFacetFilters();
            _trackFavoritesOnly = true;
            await DashboardNavigateAsync("Tracks");
        }, "#FF5D7A", $"{summary.FavoriteCount:N0} {LocalizationManager.Current.Tracks}"));
        rows.Children.Add(DashboardQuickAccessButton("≡", LocalizationManager.Current.UpNext,
            () => DashboardNavigateAsync("Queue"), "#F59E42", $"{_queue.Count:N0} {LocalizationManager.Current.Tracks}"));
        rows.Children.Add(DashboardQuickAccessButton("◷", LocalizationManager.Current.RecentlyPlayed,
            ShowAllRecentlyPlayedAsync, "#43D7C8", LocalizationManager.Current.ShowAll));
        rows.Children.Add(DashboardQuickAccessButton("⤨", LocalizationManager.Current.DashboardRandomPlayback,
            DashboardPlayRandomAsync, "#20D9E8", LocalizationManager.Current.LocalLibrary));
        return DashboardWrapOverviewCard(LocalizationManager.Current.DashboardQuickAccess, rows);
    }

    private Button DashboardQuickAccessButton(string icon, string label, Func<Task> action, string accent, string subtitle)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(
                0x28, Color.Parse(accent).R, Color.Parse(accent).G, Color.Parse(accent).B)),
            Child = new TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush(Color.Parse(accent)),
                FontSize = ResolveFontSize("FontSizeBodyStrong"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var textStack = new StackPanel { Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = ResolveFontSize("FontSizeCaption"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = ResolveFontSize("FontSizeMeta"),
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textStack, 1);
        content.Children.Add(textStack);
        var arrow = new TextBlock
        {
            Text = "›",
            FontSize = ResolveFontSize("FontSizeSubtitle"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        content.Children.Add(arrow);
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(7, 5),
            Background = FindResource<IBrush>("AppSurfaceHoverBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.Click += async (_, e) => { e.Handled = true; await action(); };
        return button;
    }

    private Border DashboardWrapOverviewCard(string title, Control content, Control? headerAction = null)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = ResolveFontSize("FontSizeBodyStrong"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (headerAction is not null)
        {
            Grid.SetColumn(headerAction, 1);
            header.Children.Add(headerAction);
        }
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        return new Border
        {
            MinHeight = 250,
            Padding = new Thickness(14),
            Background = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = layout
        };
    }

    /// <summary>Builds the statistics section header carrying the shared period selector.</summary>
    /// <returns>The header control with an accent underline and a right-aligned period dropdown.</returns>
    private Control DashboardCreateStatsPeriodHeader()
    {
        var container = new StackPanel();

        var grid = new Grid { Margin = new Thickness(0, 24, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tb = new TextBlock
        {
            Text = LocalizationManager.Current.ListeningStats,
            FontSize = ResolveFontSize("FontSizeSubtitle"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 0);
        grid.Children.Add(tb);

        var periodBox = new ComboBox
        {
            MinWidth = 170,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[]
            {
                LocalizationManager.Current.PeriodAllTime,
                LocalizationManager.Current.PeriodThisYear,
                LocalizationManager.Current.PeriodThisMonth,
                LocalizationManager.Current.PeriodLast30Days,
                LocalizationManager.Current.PeriodLast7Days
            },
            SelectedIndex = (int)_dashboardStatsPeriod
        };
        periodBox.SelectionChanged += DashboardStatsPeriod_OnSelectionChanged;
        Grid.SetColumn(periodBox, 1);
        grid.Children.Add(periodBox);

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

    /// <summary>Rebuilds the dashboard when the statistics period changes.</summary>
    /// <param name="sender">The period selector.</param>
    /// <param name="e">The selection change event data.</param>
    private async void DashboardStatsPeriod_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 and var index })
            return;
        var period = (StatsPeriod)index;
        if (period == _dashboardStatsPeriod)
            return;
        _dashboardStatsPeriod = period;
        await BuildDashboardAsync();
    }

    /// <summary>Returns the inclusive Unix-second lower bound for a statistics period.</summary>
    /// <param name="period">The selected statistics period.</param>
    /// <returns>The lower bound, or <see langword="null"/> for all-time.</returns>
    private static long? StatsPeriodSinceUnix(StatsPeriod period)
    {
        var now = DateTime.Now;
        return period switch
        {
            StatsPeriod.ThisYear => new DateTimeOffset(new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Local))
                .ToUnixTimeSeconds(),
            StatsPeriod.ThisMonth => new DateTimeOffset(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Local))
                .ToUnixTimeSeconds(),
            StatsPeriod.Last30Days => new DateTimeOffset(now.Date.AddDays(-30), TimeZoneInfo.Local.GetUtcOffset(now.Date.AddDays(-30)))
                .ToUnixTimeSeconds(),
            StatsPeriod.Last7Days => new DateTimeOffset(now.Date.AddDays(-7), TimeZoneInfo.Local.GetUtcOffset(now.Date.AddDays(-7)))
                .ToUnixTimeSeconds(),
            _ => null
        };
    }

    /// <summary>Builds the modern top-genres analytics card for the dashboard.</summary>
    /// <param name="genres">Ranked genres with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopGenresCard(List<(string Genre, double Seconds)> genres)
    {
        if (genres.Count == 0)
            return new TextBlock
            {
                Text       = LocalizationManager.Current.NoData,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                Margin     = new Thickness(0, 4, 0, 0)
            };

        double maxSecs = genres.Max(item => item.Seconds);
        var rows = new StackPanel { Spacing = 8 };

        for (int i = 0; i < genres.Count; i++)
        {
            var (genre, secs) = genres[i];
            var row = new Grid { MinHeight = 31 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            var labelButton = new Button
            {
                Content = genre,
                FontSize = ResolveFontSize("FontSizeCaption"),
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = genre
            };
            labelButton.Click += DashboardGenreButton_OnClick;
            row.Children.Add(labelButton);

            var barHost = DashboardBuildStatBar(
                secs, maxSecs, FindResource<IBrush>("AppAccentBrush") ?? Brushes.Cyan);
            barHost.Margin = new Thickness(8, 0);
            barHost.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(barHost, 1);
            row.Children.Add(barHost);

            var durationTb = new TextBlock
            {
                Text = Math.Round(secs / 60).ToString("N0", CultureInfo.CurrentCulture),
                FontSize = ResolveFontSize("FontSizeCaption"),
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(durationTb, 2);
            row.Children.Add(durationTb);
            rows.Children.Add(row);
        }
        return rows;
    }

    /// <summary>Builds the most-listened-albums analytics card for the dashboard.</summary>
    /// <param name="albums">Ranked albums with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopAlbumsCard(List<TopAlbumStat> albums)
    {
        if (albums.Count == 0)
            return DashboardNoDataText();

        double maxSecs = albums[0].Seconds;
        var rows = new StackPanel { Spacing = 10 };

        for (int i = 0; i < albums.Count; i++)
        {
            var album = albums[i];
            var row = new Grid { MinHeight = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            var cover = DashboardBuildTopAlbumCover(album);
            row.Children.Add(cover);

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            var titleButton = new Button
            {
                Content = album.Title,
                FontSize = ResolveFontSize("FontSizeBody"),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            titleButton.Click += async (_, e) => { e.Handled = true; await OpenTopAlbumAsync(album); };
            textStack.Children.Add(titleButton);

            if (!string.IsNullOrWhiteSpace(album.Artist))
            {
                var artistButton = new Button
                {
                    Content = album.Artist,
                    FontSize = ResolveFontSize("FontSizeMeta"),
                    Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                artistButton.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await OpenTopArtistAsync(new TopArtistStat(
                        album.Artist, album.Seconds, album.LocalArtistId, album.ExternalId, album.Path));
                };
                textStack.Children.Add(artistButton);
            }
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);

            var bar = DashboardBuildStatBar(
                album.Seconds, maxSecs, FindResource<IBrush>("AppAccentBrush") ?? Brushes.Cyan);
            bar.Margin = new Thickness(12, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var durationTb = new TextBlock
            {
                Text = Math.Round(album.Seconds / 60).ToString("N0", CultureInfo.CurrentCulture),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(durationTb, 3);
            row.Children.Add(durationTb);
            rows.Children.Add(row);
        }

        return rows;
    }

    /// <summary>Builds the most-listened-artists analytics card for the dashboard.</summary>
    /// <param name="artists">Ranked artists with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopArtistsCard(List<TopArtistStat> artists)
    {
        if (artists.Count == 0)
            return DashboardNoDataText();

        double maxSecs = artists[0].Seconds;
        var rows = new StackPanel { Spacing = 10 };

        for (int i = 0; i < artists.Count; i++)
        {
            var artist = artists[i];
            var color = _genreColors[i % _genreColors.Length];
            var row = new Grid { MinHeight = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            var rankChip = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0x42, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.CurrentCulture),
                    FontSize = ResolveFontSize("FontSizeMeta"),
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(rankChip, 0);
            row.Children.Add(rankChip);

            var labelButton = new Button
            {
                Content = artist.Name,
                FontSize = ResolveFontSize("FontSizeBody"),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            labelButton.Click += async (_, e) => { e.Handled = true; await OpenTopArtistAsync(artist); };
            Grid.SetColumn(labelButton, 1);
            row.Children.Add(labelButton);

            var bar = DashboardBuildStatBar(
                artist.Seconds, maxSecs, FindResource<IBrush>("AppAccentBrush") ?? Brushes.Cyan);
            bar.Margin = new Thickness(12, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var durationTb = new TextBlock
            {
                Text = Math.Round(artist.Seconds / 60).ToString("N0", CultureInfo.CurrentCulture),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(durationTb, 3);
            row.Children.Add(durationTb);
            rows.Children.Add(row);
        }

        return rows;
    }

    /// <summary>Builds a small album cover for a top-albums row: local thumbnail, remote artwork, or initials.</summary>
    /// <param name="album">The ranked album.</param>
    /// <returns>A fixed-size cover control.</returns>
    private Control DashboardBuildTopAlbumCover(TopAlbumStat album)
    {
        const double size = 40;
        var host = new Border
        {
            Width = size,
            Height = size,
            Background = FindResource<IBrush>("AppArtworkPlaceholderBrush"),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid();
        content.Children.Add(new Orynivo.Controls.InitialsAvatar
        {
            DisplayName = string.IsNullOrWhiteSpace(album.Title) ? album.Artist : album.Title,
            FontSize = 14,
            IsHitTestVisible = false
        });

        if (!string.IsNullOrEmpty(album.ThumbPath))
        {
            var image = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            content.Children.Add(image);
            _ = LoadDashboardLocalArtworkAsync(image, album.ThumbPath!);
        }
        else if (TryGetTopAlbumRemoteArtUrl(album, out var artUrl))
        {
            var image = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            content.Children.Add(image);
            _ = LoadDashboardRemoteArtworkAsync(image, artUrl);
        }

        host.Child = content;
        return host;
    }

    /// <summary>Resolves a remote Orynivo Server track-artwork URL for a top-albums row, when applicable.</summary>
    /// <param name="album">The ranked album.</param>
    /// <param name="artUrl">The resolved authenticated artwork URL.</param>
    /// <returns><see langword="true"/> when the album maps to a configured Orynivo Server track.</returns>
    private bool TryGetTopAlbumRemoteArtUrl(TopAlbumStat album, out string artUrl)
    {
        artUrl = string.Empty;
        var pseudo = MakeStatHistoryEntry(album.ExternalId, album.Path, album.Title, album.Artist, album.Title, null, null);
        if (TryGetOrynivoHistoryTarget(pseudo, out var server, out var trackId))
        {
            artUrl = OrynivoServerClient.GetTrackArtworkUrl(server, trackId, 96);
            return true;
        }
        return false;
    }

    /// <summary>Builds the thin proportional stat bar shared by the album and artist cards.</summary>
    /// <param name="seconds">This row's play seconds.</param>
    /// <param name="maxSeconds">The largest row's play seconds, used to scale the fill.</param>
    /// <param name="fill">The fill brush.</param>
    /// <returns>The bar control.</returns>
    private Control DashboardBuildStatBar(double seconds, double maxSeconds, IBrush fill)
    {
        double fraction = maxSeconds > 0 ? seconds / maxSeconds : 0;
        var barBg = new Border
        {
            Height = 8,
            Background = FindResource<IBrush>("AppGridLineBrush"),
            CornerRadius = new CornerRadius(4)
        };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var barFill = new Border { Height = 8, Background = fill, CornerRadius = new CornerRadius(4) };
        Grid.SetColumn(barFill, 0);
        barGrid.Children.Add(barFill);

        var barHost = new Grid();
        barHost.Children.Add(barBg);
        barHost.Children.Add(barGrid);
        return barHost;
    }

    /// <summary>Builds the bordered surface container shared by the analytics cards.</summary>
    /// <returns>An empty surface border ready to host its content.</returns>
    private Border DashboardSurfaceCard() => new()
    {
        Background = FindResource<IBrush>("AppSurfaceBrush"),
        BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(16, 14, 16, 8),
        Margin = new Thickness(0, 0, 0, 4)
    };

    /// <summary>Builds the muted "no data" placeholder shown when an analytics card is empty.</summary>
    /// <returns>A muted text block.</returns>
    private Control DashboardNoDataText() => new TextBlock
    {
        Text = LocalizationManager.Current.NoData,
        Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
        Margin = new Thickness(0, 4, 0, 0)
    };

    /// <summary>Opens a ranked album, staying in whichever library (local, remote, Plex) it belongs to.</summary>
    /// <param name="album">The ranked album to open.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private Task OpenTopAlbumAsync(TopAlbumStat album) =>
        OpenHistoryAlbumAsync(MakeStatHistoryEntry(
            album.ExternalId, album.Path, album.Title, album.Artist, album.Title,
            album.LocalAlbumId, album.LocalArtistId));

    /// <summary>Opens a ranked artist, staying in whichever library (local, remote, Plex) it belongs to.</summary>
    /// <param name="artist">The ranked artist to open.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private Task OpenTopArtistAsync(TopArtistStat artist) =>
        artist.LocalArtistId.HasValue ||
        artist.ExternalId?.StartsWith("orynivo:", StringComparison.OrdinalIgnoreCase) == true
            ? ShowUnifiedArtistAlbumsAsync(artist.Name)
            : OpenHistoryArtistAsync(MakeStatHistoryEntry(
                artist.ExternalId, artist.Path, artist.Name, artist.Name, null, null, artist.LocalArtistId));

    /// <summary>Builds a synthetic playback-history entry used to route statistics-card clicks through the shared open logic.</summary>
    /// <param name="externalId">Representative external identifier.</param>
    /// <param name="path">Representative playback path.</param>
    /// <param name="title">Display title.</param>
    /// <param name="artist">Artist display name.</param>
    /// <param name="album">Album display title.</param>
    /// <param name="albumId">Local album identifier, or <see langword="null"/>.</param>
    /// <param name="artistId">Local artist identifier, or <see langword="null"/>.</param>
    /// <returns>A minimal <see cref="DailyHistoryEntry"/> carrying the identity fields.</returns>
    private static DailyHistoryEntry MakeStatHistoryEntry(
        string? externalId,
        string? path,
        string title,
        string? artist,
        string? album,
        long? albumId,
        long? artistId) =>
        new(0, null, path ?? string.Empty, DateTime.Now, 0, null, "track", title, artist, album, artistId, albumId, externalId);

    /// <summary>Chooses black or white text for readable contrast on a colored background.</summary>
    /// <param name="background">The background color.</param>
    /// <returns>A near-black or white brush, whichever contrasts better.</returns>
    private static IBrush PickContrastBrush(Color background)
    {
        // Relative luminance (sRGB approximation); bright backgrounds get dark text.
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            : Brushes.White;
    }

    private async void DashboardGenreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string genre } || string.IsNullOrWhiteSpace(genre))
            return;
        e.Handled = true;

        ClearTrackFacetFilters();
        _selectedTrackGenres.Add(genre);
        _expandedTrackFilterSections.Add(LocalizationManager.Current.Genre);
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = "Tracks";

        var tracksItem = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Tag as string, "Tracks", StringComparison.Ordinal));
        if (tracksItem is null)
            return;
        if (ReferenceEquals(NavListBox.SelectedItem, tracksItem))
            await ShowTopLevelViewAsync("Tracks");
        else
            NavListBox.SelectedItem = tracksItem;
    }

    private void ClearTrackFacetFilters()
    {
        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        _selectedTrackSources.Clear();
    }

    private async Task DashboardNavigateAsync(string tag)
    {
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = tag;
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (item is null || ReferenceEquals(NavListBox.SelectedItem, item))
            await ShowTopLevelViewAsync(tag);
        else
            NavListBox.SelectedItem = item;
    }

    private async void DashboardRandomPlayback_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await DashboardPlayRandomAsync();
    }

    private async Task DashboardPlayRandomAsync()
    {
        var path = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            var candidates = db.GetTracksLite()
                .Where(track => File.Exists(track.SourcePath))
                .ToList();
            return candidates.Count == 0
                ? null
                : candidates[Random.Shared.Next(candidates.Count)].Path;
        });
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusTextBlock.Text = LocalizationManager.Current.NoData;
            return;
        }

        _queue.Clear();
        _queue.Add(CreatePlaylistItem(path));
        _queueIndex = 0;
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(path); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private static string FormatDashboardDuration(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";

    /// <summary>Opens the album track list for a local or Orynivo Server playback-history entry.</summary>
    /// <param name="entry">The history entry whose album should be opened.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>

    private async void CalendarPrev_OnClick(object? sender, RoutedEventArgs e)
    {
        _dashboardMonth--;
        if (_dashboardMonth < 1) { _dashboardMonth = 12; _dashboardYear--; }
        await RefreshCalendarSectionAsync();
    }

    private async void CalendarNext_OnClick(object? sender, RoutedEventArgs e)
    {
        _dashboardMonth++;
        if (_dashboardMonth > 12) { _dashboardMonth = 1; _dashboardYear++; }
        await RefreshCalendarSectionAsync();
    }

    private async Task RefreshCalendarSectionAsync()
    {
        // Rebuild the whole dashboard — section header title contains the month/year
        await BuildDashboardAsync();
    }
}



