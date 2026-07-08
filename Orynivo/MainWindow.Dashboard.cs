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

            var (recentlyPlayed, recentThumbs) = await LoadRecentlyPlayedAsync(12);
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

            DashboardBuildGreeting();

            if (recentlyPlayed.Count > 0)
            {
                DashboardPanel.Children.Add(DashboardCreateSectionHeader(
                    LocalizationManager.Current.RecentlyPlayed,
                    showAllAction: () => _ = ShowAllRecentlyPlayedAsync()));
                DashboardBuildRecentlyPlayed(recentlyPlayed, recentThumbs);
            }

            DashboardPanel.Children.Add(DashboardCreateSectionHeader(
                LocalizationManager.Current.RecentAlbums,
                showAllAction: () => _ = ShowAllRecentAlbumsAsync()));
            DashboardBuildRecentAlbums(recentAlbums);

            DashboardBuildStatsSection(calendarData, topGenres, topAlbums, topArtists);
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
    private static Task<(List<DailyHistoryEntry> Entries, Dictionary<long, string> Thumbs)> LoadRecentlyPlayedAsync(int count) =>
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
            return (deduped, thumbs);
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

        var albums = await LoadRecentAlbumsAsync(200);
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

        var (entries, thumbs) = await LoadRecentlyPlayedAsync(200);
        DashboardPanel.Children.Add(DashboardCreateSectionHeader(LocalizationManager.Current.RecentlyPlayed));
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in entries)
            wrap.Children.Add(BuildRecentlyPlayedCard(entry, thumbs, expandedSpacing: true));
        DashboardPanel.Children.Add(wrap);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(entries.Count);
    }

    /// <summary>Builds the personal greeting hero shown at the top of the dashboard.</summary>
    private void DashboardBuildGreeting()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour switch
        {
            >= 5 and < 12 => LocalizationManager.Current.GreetingMorning,
            >= 12 and < 18 => LocalizationManager.Current.GreetingAfternoon,
            _ => LocalizationManager.Current.GreetingEvening
        };

        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
        stack.Children.Add(new TextBlock
        {
            Text = greeting,
            FontSize = ResolveFontSize("FontSizeHeadline"),
            FontWeight = FontWeight.Bold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.DashboardTagline,
            FontSize = ResolveFontSize("FontSizeBody"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush")
        });
        DashboardPanel.Children.Add(stack);
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
    private async Task<List<DashboardAlbum>> LoadRecentAlbumsAsync(int perSource = 12)
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
        DashboardPanel.Children.Add(scroll);
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
        Dictionary<long, string> thumbs)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in entries)
            panel.Children.Add(BuildRecentlyPlayedCard(entry, thumbs));
        scroll.Content = panel;
        DashboardPanel.Children.Add(scroll);
    }

    /// <summary>Builds a compact recently played card for the dashboard strip or full-page history grid.</summary>
    /// <param name="entry">Playback-history entry to render.</param>
    /// <param name="thumbs">Local album thumbnail paths keyed by album identifier.</param>
    /// <param name="expandedSpacing">Whether to use the roomier spacing needed by the full-page grid.</param>
    /// <returns>The card control.</returns>
    private Control BuildRecentlyPlayedCard(
        DailyHistoryEntry entry,
        Dictionary<long, string> thumbs,
        bool expandedSpacing = false)
    {
        const double artSize = 116;
        var playable = IsPlayableHistoryEntry(entry);
        var normalBorderBrush = FindResource<IBrush>("AppGridLineBrush");
        var hoverBorderBrush = FindResource<IBrush>("AppAccentBrush");

        var card = new Border
        {
            Width           = artSize + 20,
            Margin          = expandedSpacing
                ? new Thickness(0, 0, 20, 24)
                : new Thickness(0, 0, 12, 0),
            Padding         = new Thickness(10),
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush     = normalBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(0, 18, 0, 18),
            Cursor          = new Cursor(playable ? StandardCursorType.Hand : StandardCursorType.Arrow)
        };
        card.Classes.Add("motionCard");

        var stack = new StackPanel { Spacing = 4 };

        var artHost = new Border
        {
            Width        = artSize,
            Height       = artSize,
            Background   = FindResource<IBrush>("AppArtworkPlaceholderBrush"),
            CornerRadius = new CornerRadius(0, 14, 0, 14),
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
            CornerRadius       = new CornerRadius(0, 14, 0, 14),
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

        card.Child = stack;

        if (TryGetOrynivoHistoryTarget(entry, out _, out _))
            _ = HydrateRecentlyPlayedRemoteCardAsync(entry, titleBlock, artistButton, artistBlock, initialsAvatar);

        card.PointerEntered += (_, _) =>
        {
            card.BorderBrush = hoverBorderBrush;
            if (playable)
                playOverlay.IsVisible = true;
        };
        card.PointerExited += (_, _) =>
        {
            card.BorderBrush = normalBorderBrush;
            if (playable)
                playOverlay.IsVisible = false;
        };

        if (playable)
        {
            card.PointerReleased += async (_, e) =>
            {
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
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(14),
            Padding         = new Thickness(14),
            Margin          = new Thickness(0, 0, 0, 4)
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
            MinHeight       = 72,
            Background      = isToday
                ? FindResource<IBrush>("AppAccentSoftBrush")
                : Brushes.Transparent,
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(6),
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
            HorizontalAlignment = HorizontalAlignment.Right
        });

        if (data is not null && data.TotalSeconds > 0)
        {
            var ts = TimeSpan.FromSeconds(data.TotalSeconds);
            stack.Children.Add(new TextBlock
            {
                Text       = FormatDashboardDuration(ts),
                FontSize   = 10,
                Foreground = FindResource<IBrush>("AppAccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            });

            foreach (var genre in data.TopGenres.Take(3))
            {
                var genreButton = new Button
                {
                    Content = genre,
                    FontSize = 9,
                    Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(2, 0, 2, 0),
                    Tag = genre
                };
                genreButton.Click += DashboardGenreButton_OnClick;
                stack.Children.Add(genreButton);
            }
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
        List<TopArtistStat> topArtists)
    {
        // Shared period selector header for all three "top" analytics cards.
        DashboardPanel.Children.Add(DashboardCreateStatsPeriodHeader());

        var calendarHeader = DashboardCreateSectionHeader(
            string.Format(
                LocalizationManager.Current.Calendar,
                new DateTime(_dashboardYear, _dashboardMonth, 1).ToString("MMMM yyyy")),
            calendarNav: true);
        var calendarCard = DashboardBuildCalendarCard(calendarData);

        var calendarColumn = new StackPanel();
        calendarColumn.Children.Add(calendarHeader);
        calendarColumn.Children.Add(calendarCard);

        var genresColumn = new StackPanel();
        genresColumn.Children.Add(DashboardCreateSectionHeader(LocalizationManager.Current.TopGenres));
        genresColumn.Children.Add(DashboardBuildTopGenresCard(topGenres));

        var albumsColumn = new StackPanel();
        albumsColumn.Children.Add(DashboardCreateSectionHeader(LocalizationManager.Current.TopAlbums));
        albumsColumn.Children.Add(DashboardBuildTopAlbumsCard(topAlbums));

        var artistsColumn = new StackPanel();
        artistsColumn.Children.Add(DashboardCreateSectionHeader(LocalizationManager.Current.TopArtists));
        artistsColumn.Children.Add(DashboardBuildTopArtistsCard(topArtists));

        if (_dashboardTwoColumnLayout == true)
        {
            // Left: calendar + top albums. Right: top genres + top artists.
            var leftColumn = new StackPanel();
            leftColumn.Children.Add(calendarColumn);
            leftColumn.Children.Add(albumsColumn);

            var rightColumn = new StackPanel();
            rightColumn.Children.Add(genresColumn);
            rightColumn.Children.Add(artistsColumn);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(leftColumn, 0);
            Grid.SetColumn(rightColumn, 2);
            grid.Children.Add(leftColumn);
            grid.Children.Add(rightColumn);
            DashboardPanel.Children.Add(grid);
        }
        else
        {
            DashboardPanel.Children.Add(calendarColumn);
            DashboardPanel.Children.Add(genresColumn);
            DashboardPanel.Children.Add(albumsColumn);
            DashboardPanel.Children.Add(artistsColumn);
        }
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

        double maxSecs = genres[0].Seconds;

        var panel = new Border
        {
            Background = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 16, 8),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var rows = new StackPanel { Spacing = 12 };

        for (int i = 0; i < genres.Count; i++)
        {
            var (genre, secs) = genres[i];
            var color = _genreColors[i % _genreColors.Length];
            var brush = new SolidColorBrush(color);

            var row = new StackPanel { Spacing = 6 };

            // Top line: rank chip + genre name (link) on the left, duration on the right.
            var topLine = new Grid();
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rankChip = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(7),
                Background = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.CurrentCulture),
                    FontSize = ResolveFontSize("FontSizeMeta"),
                    FontWeight = FontWeight.Bold,
                    Foreground = PickContrastBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(rankChip, 0);
            topLine.Children.Add(rankChip);

            var labelButton = new Button
            {
                Content = genre,
                FontSize = ResolveFontSize("FontSizeBody"),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = genre
            };
            labelButton.Click += DashboardGenreButton_OnClick;
            Grid.SetColumn(labelButton, 1);
            topLine.Children.Add(labelButton);

            var durationTb = new TextBlock
            {
                Text = FormatDashboardDuration(TimeSpan.FromSeconds(secs)),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(durationTb, 2);
            topLine.Children.Add(durationTb);
            row.Children.Add(topLine);

            // Thin proportional bar over a muted track.
            double fraction = maxSecs > 0 ? secs / maxSecs : 0;
            var barBg = new Border
            {
                Height       = 8,
                Background   = FindResource<IBrush>("AppGridLineBrush"),
                CornerRadius = new CornerRadius(4)
            };
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            var barFill = new Border
            {
                Height       = 8,
                Background   = brush,
                CornerRadius = new CornerRadius(4)
            };
            Grid.SetColumn(barFill, 0);
            barGrid.Children.Add(barFill);

            var barHost = new Grid();
            barHost.Children.Add(barBg);
            barHost.Children.Add(barGrid);
            row.Children.Add(barHost);

            rows.Children.Add(row);
        }

        panel.Child = rows;
        return panel;
    }

    /// <summary>Builds the most-listened-albums analytics card for the dashboard.</summary>
    /// <param name="albums">Ranked albums with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopAlbumsCard(List<TopAlbumStat> albums)
    {
        if (albums.Count == 0)
            return DashboardNoDataText();

        double maxSecs = albums[0].Seconds;
        var panel = DashboardSurfaceCard();
        var rows = new StackPanel { Spacing = 12 };

        for (int i = 0; i < albums.Count; i++)
        {
            var album = albums[i];
            var color = _genreColors[i % _genreColors.Length];
            var brush = new SolidColorBrush(color);

            var row = new StackPanel { Spacing = 6 };

            var topLine = new Grid();
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cover = DashboardBuildTopAlbumCover(album);
            Grid.SetColumn(cover, 0);
            topLine.Children.Add(cover);

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
            topLine.Children.Add(textStack);

            var durationTb = new TextBlock
            {
                Text = FormatDashboardDuration(TimeSpan.FromSeconds(album.Seconds)),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(durationTb, 2);
            topLine.Children.Add(durationTb);
            row.Children.Add(topLine);

            row.Children.Add(DashboardBuildStatBar(album.Seconds, maxSecs, brush));
            rows.Children.Add(row);
        }

        panel.Child = rows;
        return panel;
    }

    /// <summary>Builds the most-listened-artists analytics card for the dashboard.</summary>
    /// <param name="artists">Ranked artists with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopArtistsCard(List<TopArtistStat> artists)
    {
        if (artists.Count == 0)
            return DashboardNoDataText();

        double maxSecs = artists[0].Seconds;
        var panel = DashboardSurfaceCard();
        var rows = new StackPanel { Spacing = 12 };

        for (int i = 0; i < artists.Count; i++)
        {
            var artist = artists[i];
            var color = _genreColors[i % _genreColors.Length];
            var brush = new SolidColorBrush(color);

            var row = new StackPanel { Spacing = 6 };

            var topLine = new Grid();
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rankChip = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(7),
                Background = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.CurrentCulture),
                    FontSize = ResolveFontSize("FontSizeMeta"),
                    FontWeight = FontWeight.Bold,
                    Foreground = PickContrastBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(rankChip, 0);
            topLine.Children.Add(rankChip);

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
            topLine.Children.Add(labelButton);

            var durationTb = new TextBlock
            {
                Text = FormatDashboardDuration(TimeSpan.FromSeconds(artist.Seconds)),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(durationTb, 2);
            topLine.Children.Add(durationTb);
            row.Children.Add(topLine);

            row.Children.Add(DashboardBuildStatBar(artist.Seconds, maxSecs, brush));
            rows.Children.Add(row);
        }

        panel.Child = rows;
        return panel;
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
            CornerRadius = new CornerRadius(0, 10, 0, 10),
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
        OpenHistoryArtistAsync(MakeStatHistoryEntry(
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

        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackGenres.Add(genre);
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
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



