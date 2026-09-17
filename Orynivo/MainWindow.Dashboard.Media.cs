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
/// Dashboard recently added and recently played strips, media cards, calendar, and podcast info.
/// </summary>
public partial class MainWindow : Window
{
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
        Action? showAllAction,
        Control content,
        ScrollViewer? carousel = null,
        Control? headerContent = null)
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

        if (headerContent is not null)
        {
            Grid.SetColumn(headerContent, 3);
            header.Children.Add(headerContent);
        }
        else if (showAllAction is not null)
        {
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
        }
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
        var parts = album.LogicalAlbumParts;
        var isUnified = parts?
            .Select(part => part.Server?.Id ?? LocalSourceKey)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any() == true;
        var row = new ContentRow
        {
            Id = album.Id,
            AlbumId = album.Id,
            LogicalAlbumIds = album.LogicalAlbumIds,
            LogicalAlbumParts = parts,
            ArtistId = album.ArtistId,
            Title = string.IsNullOrWhiteSpace(album.Title) ? LocalizationManager.Current.Unknown : album.Title,
            Artist = string.IsNullOrWhiteSpace(album.Artist) ? null : album.Artist,
            ArtworkPath = album.ArtworkPath,
            IsFavorite = album.IsFavorite,
            EntityType = isUnified ? "UnifiedAlbum" : isRemote ? "OrynivoAlbum" : "Album",
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

        if (row.EntityType == "UnifiedAlbum")
        {
            await OpenLogicalAlbumTracksAsync(row);
        }
        else if (row.EntityType == "OrynivoAlbum")
        {
            _activeArtistFilterId = null;
            _activeArtistFilterName = null;
            _activeOrynivoServer = row.OrynivoServer;
            await OpenOrynivoAlbumTracksAsync(row);
        }
        else
        {
            await ShowAlbumTracksAsync(
                albumId,
                row.Title ?? LocalizationManager.Current.Unknown,
                logicalAlbumIds: row.LogicalAlbumIds);
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

    /// <summary>
    /// Resolves aggregate counts from every reachable Orynivo Server. New servers provide
    /// track and album totals through a compact summary endpoint; older servers fall back to
    /// their lightweight facet and album lists. Artist names are retained so local and remote
    /// identities can be unified with the same comparison key as the Artists view.
    /// </summary>
    /// <returns>Combined remote counts and normalized artist identity keys.</returns>
    private async Task<(int AlbumCount, int TrackCount, int FavoriteCount, HashSet<string> ArtistKeys)>
        ResolveDashboardRemoteLibrarySummaryAsync(DashboardBuildMetrics? metrics = null)
    {
        var tasks = (_settings.OrynivoServers ?? [])
            .Select(async server =>
            {
                var serverTimer = Stopwatch.StartNew();
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var summaryTask = _orynivoClient.GetLibrarySummaryAsync(server, timeout.Token);
                    var artistsTask = _orynivoClient.GetArtistsAsync(server, timeout.Token);
                    var facetsTask = _orynivoClient.GetTrackFacetsAsync(server, timeout.Token);
                    await Task.WhenAll(summaryTask, artistsTask, facetsTask);

                    var summary = await summaryTask;
                    var artists = await artistsTask;
                    var facets = await facetsTask;
                    var albumCount = summary?.AlbumCount;
                    if (!albumCount.HasValue)
                        albumCount = (await _orynivoClient.GetAlbumsAsync(server, timeout.Token)).Count;

                    var favoriteIds = facets
                        .Where(facet => IsOrynivoFavorite(server, "Track", facet.Id))
                        .Select(facet => facet.Id)
                        .Distinct()
                        .ToList();
                    var favoriteCount = 0;
                    if (favoriteIds.Count > 0)
                    {
                        var tracks = await CreateOrynivoCatalogProvider(server)
                            .GetTracksByIdsAsync(favoriteIds, timeout.Token);
                        favoriteCount = tracks.Select(track => track.Id).Distinct().Count();
                    }

                    var result = (
                        AlbumCount: albumCount.GetValueOrDefault(),
                        TrackCount: summary?.TrackCount ?? facets.Count,
                        FavoriteCount: favoriteCount,
                        ArtistKeys: artists
                            .Select(artist => ArtistNameNormalizer.CreateComparisonKey(artist.Name))
                            .ToHashSet(StringComparer.Ordinal));
                    metrics?.Record("library-remote-server", serverTimer.ElapsedMilliseconds);
                    return result;
                }
                catch
                {
                    // Unified library views also omit a server that cannot resolve its rows.
                    metrics?.Record("library-remote-server", serverTimer.ElapsedMilliseconds);
                    return (
                        AlbumCount: 0,
                        TrackCount: 0,
                        FavoriteCount: 0,
                        ArtistKeys: new HashSet<string>(StringComparer.Ordinal));
                }
            });
        var summaries = await Task.WhenAll(tasks);
        var artistKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var summary in summaries)
            artistKeys.UnionWith(summary.ArtistKeys);
        return (
            summaries.Sum(summary => summary.AlbumCount),
            summaries.Sum(summary => summary.TrackCount),
            summaries.Sum(summary => summary.FavoriteCount),
            artistKeys);
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
            MinHeight       = 296,
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

        var albumButton = new Button
        {
            Content = entry.Album,
            FontSize = ResolveFontSize("FontSizeMeta"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = artSize,
            Margin = new Thickness(2, 0, 2, 0),
            IsVisible = CanOpenHistoryAlbum(entry)
        };
        ToolTip.SetTip(albumButton, entry.Album);
        albumButton.Click += async (_, e) =>
        {
            e.Handled = true;
            await OpenHistoryAlbumAsync(entry);
        };
        var albumBlock = new TextBlock
        {
            Text = entry.Album,
            FontSize = ResolveFontSize("FontSizeMeta"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 0, 2, 0),
            IsVisible = !string.IsNullOrWhiteSpace(entry.Album) && !albumButton.IsVisible
        };
        ToolTip.SetTip(albumBlock, entry.Album);
        stack.Children.Add(albumButton);
        stack.Children.Add(albumBlock);

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
                    ActiveUserProfile.OrynivoServerFavorites.Add(key);
                else
                    ActiveUserProfile.OrynivoServerFavorites.Remove(key);
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
            _ = HydrateRecentlyPlayedRemoteCardAsync(
                entry,
                titleBlock,
                artistButton,
                artistBlock,
                albumButton,
                albumBlock,
                initialsAvatar);

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
    /// <param name="albumButton">Album link button to update.</param>
    /// <param name="albumBlock">Album text block to update.</param>
    /// <param name="initialsAvatar">Artwork placeholder to retitle.</param>
    /// <returns>A task representing the asynchronous metadata refresh.</returns>
    private async Task HydrateRecentlyPlayedRemoteCardAsync(
        DailyHistoryEntry entry,
        TextBlock titleBlock,
        Button artistButton,
        TextBlock artistBlock,
        Button albumButton,
        TextBlock albumBlock,
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
            var canOpenAlbum = row.AlbumId.HasValue && !string.IsNullOrWhiteSpace(row.Album);
            albumButton.Content = row.Album;
            albumButton.IsVisible = canOpenAlbum;
            ToolTip.SetTip(albumButton, row.Album);
            albumBlock.Text = row.Album;
            albumBlock.IsVisible = !canOpenAlbum && !string.IsNullOrWhiteSpace(row.Album);
            ToolTip.SetTip(albumBlock, row.Album);
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
}
