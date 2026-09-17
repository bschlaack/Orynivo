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
/// Album detail rendering (headers, folder groups, and ratings) plus logical
/// and provider album track loading for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private async Task OpenLogicalAlbumTracksAsync(ContentRow row)
    {
        if (row.Id is not long albumId || row.LogicalAlbumParts is not { Count: > 0 } parts)
            return;

        PushCurrentNavigationState();
        var (_, artistFilterName) = GetAlbumArtistScope(row);
        _activeAlbumFilterId = albumId;
        _activeAlbumFilterTitle = row.Title;
        _activeArtistFilterId = artistFilterName is null ? null : row.ArtistId;
        _activeArtistFilterName = artistFilterName;
        _activeAlbumCatalogProvider = null;
        _activeCatalogAlbum = null;
        _activeLogicalAlbumIds = null;
        _activeLogicalAlbumParts = parts;
        _activeLogicalAlbumRow = row;
        _showAllAlbumTracks = false;
        HideGenreCloudForDetailView();
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle(null);
        _updatingAlbumTrackScope = true;
        ShowAllAlbumTracksCheckBox.IsChecked = false;
        ShowAllAlbumTracksCheckBox.IsVisible = artistFilterName is not null;
        _updatingAlbumTrackScope = false;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Tracks} · {row.Title ?? LocalizationManager.Current.Unknown}";
        AlbumViewModeBorder.IsVisible = false;
        ContentDataGrid.IsVisible = true;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        UpdateAlphabetIndex(null, false);

        using (var artworkSyncCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            try
            {
                await SynchronizeLogicalAlbumArtworkAsync(row, parts, artworkSyncCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Artwork synchronization is optional and must not block album navigation.
            }
        }
        await ReloadVisibleLogicalAlbumTracksAsync();
        BackButton.IsVisible = true;
    }

    private async Task ReloadVisibleLogicalAlbumTracksAsync()
    {
        if (_activeLogicalAlbumRow is not { } albumRow ||
            _activeLogicalAlbumParts is not { Count: > 0 } parts)
        {
            return;
        }

        ShowContentLoadingSkeleton();
        try
        {
            var rows = new List<ContentRow>();
            foreach (var part in parts)
            {
                ILibraryCatalogProvider provider = part.Server is null
                    ? _localCatalogProvider
                    : CreateOrynivoCatalogProvider(part.Server);
                var artistId = _showAllAlbumTracks || _activeArtistFilterName is null
                    ? null
                    : part.ArtistId;
                var tracks = await provider.GetTracksByAlbumAsync(part.AlbumId, artistId);
                rows.AddRange(tracks.Select(track => ToCatalogTrackContentRow(track, part.Server)));
            }

            var groupedRows = rows
                .GroupBy(row => (
                    Directory: NormalizeAlbumGroupValue(Path.GetDirectoryName(row.SourcePath ?? row.FilePath)),
                    Album: NormalizeAlbumGroupValue(row.Album)))
                .Select(group =>
                {
                    var first = group.First();
                    var albumArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.AlbumArtist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    var primaryArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.Artist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    return new AlbumTrackGroup(
                        Path.GetDirectoryName(first.SourcePath ?? first.FilePath) ?? string.Empty,
                        string.IsNullOrWhiteSpace(first.Album)
                            ? albumRow.Title ?? LocalizationManager.Current.Unknown
                            : first.Album.Trim(),
                        albumArtists.Count == 1
                            ? albumArtists[0]
                            : albumArtists.Count == 0 && primaryArtists.Count == 1
                                ? primaryArtists[0]
                                : null,
                        first.Year,
                        group.ToList());
                })
                .OrderBy(group => group.Directory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Album, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ContentDataGrid.ItemsSource = rows;
            StartAlbumMusicBrainzRatingRefresh(rows);
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
            UpdateAlphabetIndex(null, false);
            EnsureArtworkHydrated(albumRow);
            AlbumDetailHeader.DataContext = albumRow;
            AlbumDetailHeader.IsVisible = true;
            AlbumSaveAsPlaylistButton.IsVisible = true;
            HideAlbumFolderGroups();
            if (groupedRows.Count > 1)
                ShowAlbumFolderGroups(groupedRows);
            else
            {
                ContentDataGrid.IsVisible = true;
                ApplyColumns("Tracks");
            }
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }
    }

    private async Task ShowAlbumTracksAsync(
        long albumId,
        string albumTitle,
        long? artistFilterId = null,
        string? artistFilterName = null,
        IReadOnlyList<long>? logicalAlbumIds = null)
    {
        PushCurrentNavigationState();
        _activeAlbumFilterId = albumId;
        _activeAlbumFilterTitle = albumTitle;
        _activeArtistFilterId = artistFilterId;
        _activeArtistFilterName = artistFilterName;
        _activeAlbumCatalogProvider = null;
        _activeCatalogAlbum = null;
        _activeLogicalAlbumIds = logicalAlbumIds is { Count: > 0 }
            ? logicalAlbumIds
            : [albumId];
        _activeLogicalAlbumParts = null;
        _activeLogicalAlbumRow = null;
        _showAllAlbumTracks = false;
        HideGenreCloudForDetailView();
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle(null);
        _updatingAlbumTrackScope = true;
        ShowAllAlbumTracksCheckBox.IsChecked = false;
        ShowAllAlbumTracksCheckBox.IsVisible = _activeArtistFilterId.HasValue;
        _updatingAlbumTrackScope = false;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Tracks} · {albumTitle}";
        AlbumViewModeBorder.IsVisible = false;
        ContentDataGrid.IsVisible = true;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        UpdateAlphabetIndex(null, false);

        await ReloadVisibleAlbumTracksAsync();
        BackButton.IsVisible = true;
    }

    private async Task ReloadVisibleAlbumTracksAsync()
    {
        if (_activeAlbumFilterId is not long albumId)
            return;

        ShowContentLoadingSkeleton();
        try
        {
            var artistId = _showAllAlbumTracks ? null : _activeArtistFilterId;
            var result = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var albumIds = _activeLogicalAlbumIds is { Count: > 0 }
                    ? _activeLogicalAlbumIds
                    : [albumId];
                var tracks = albumIds
                    .SelectMany(id => db.GetTrackListByAlbum(id, artistId))
                    .ToList();
                var directories = new Dictionary<long, string>();
                foreach (var id in albumIds)
                {
                    foreach (var pair in db.GetAlbumTrackDirectories(id, artistId))
                        directories[pair.Key] = pair.Value;
                }
                return (
                    Album: db.GetAlbumById(albumId),
                    Tracks: tracks,
                    Directories: directories);
            });
            var groupedRows = result.Tracks
                .GroupBy(
                    track => (
                        Directory: NormalizeAlbumGroupValue(
                            result.Directories.TryGetValue(track.Id, out var directory)
                                ? directory
                                : Path.GetDirectoryName(track.Path)),
                        Album: NormalizeAlbumGroupValue(track.Album)))
                .Select(group =>
                {
                    var first = group.First();
                    var albumArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(
                            track.AlbumArtist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    var primaryArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.Artist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    return new AlbumTrackGroup(
                        result.Directories.TryGetValue(first.Id, out var directory)
                            ? directory
                            : Path.GetDirectoryName(first.Path) ?? string.Empty,
                        string.IsNullOrWhiteSpace(first.Album)
                            ? LocalizationManager.Current.Unknown
                            : first.Album.Trim(),
                        albumArtists.Count == 1
                            ? albumArtists[0]
                            : albumArtists.Count == 0 && primaryArtists.Count == 1
                                ? primaryArtists[0]
                                : null,
                        first.Year?.ToString(CultureInfo.CurrentCulture),
                        group.Select(ToTrackContentRow).ToList());
                })
                .OrderBy(group => group.Directory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var rows = groupedRows.SelectMany(group => group.Rows).ToList();
            HideAlbumFolderGroups();
            ContentDataGrid.IsVisible = true;
            ApplyColumns("Tracks");
            ContentDataGrid.ItemsSource = rows;
            StartAlbumMusicBrainzRatingRefresh(rows);
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
            UpdateAlphabetIndex(null, false);
            ApplyAlbumDetailHeader(result.Album);
            if (result.Album is not null && groupedRows.Count > 1)
                ShowAlbumFolderGroups(groupedRows);
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }
    }

    private async Task ShowProviderAlbumTracksAsync(
        ILibraryCatalogProvider provider,
        LibraryCatalogAlbum album,
        long? artistFilterId = null,
        string? artistFilterName = null,
        IReadOnlyList<long>? logicalAlbumIds = null)
    {
        _activeAlbumFilterId = album.Id;
        _activeAlbumFilterTitle = album.Title;
        _activeArtistFilterId = artistFilterId;
        _activeArtistFilterName = artistFilterName;
        _activeAlbumCatalogProvider = provider;
        _activeCatalogAlbum = album;
        _activeLogicalAlbumIds = logicalAlbumIds is { Count: > 0 }
            ? logicalAlbumIds
            : [album.Id];
        _activeLogicalAlbumParts = null;
        _activeLogicalAlbumRow = null;
        _showAllAlbumTracks = false;
        HideGenreCloudForDetailView();
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle(null);
        _updatingAlbumTrackScope = true;
        ShowAllAlbumTracksCheckBox.IsChecked = false;
        ShowAllAlbumTracksCheckBox.IsVisible = artistFilterId.HasValue;
        _updatingAlbumTrackScope = false;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Tracks} · {album.Title}";
        AlbumViewModeBorder.IsVisible = false;
        ContentDataGrid.IsVisible = true;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        UpdateAlphabetIndex(null, false);

        await ReloadVisibleProviderAlbumTracksAsync();
    }

    private async Task ReloadVisibleProviderAlbumTracksAsync()
    {
        if (_activeAlbumCatalogProvider is not { } provider ||
            _activeCatalogAlbum is not { } album)
        {
            return;
        }

        ShowContentLoadingSkeleton();
        try
        {
            var artistId = _showAllAlbumTracks ? null : _activeArtistFilterId;
            var albumIds = _activeLogicalAlbumIds is { Count: > 0 }
                ? _activeLogicalAlbumIds
                : [album.Id];
            var catalogTracks = new List<LibraryCatalogTrack>();
            foreach (var albumId in albumIds)
                catalogTracks.AddRange(await provider.GetTracksByAlbumAsync(albumId, artistId));
            var rows = catalogTracks
                .Select(track => ToCatalogTrackContentRow(track, album.Source == LibraryCatalogSource.OrynivoServer ? _activeOrynivoServer : null))
                .ToList();
            var groupedRows = rows
                .GroupBy(row => (
                    Directory: NormalizeAlbumGroupValue(
                        Path.GetDirectoryName(row.SourcePath ?? row.FilePath)),
                    Album: NormalizeAlbumGroupValue(row.Album)))
                .Select(group =>
                {
                    var first = group.First();
                    var albumArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.AlbumArtist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    var primaryArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.Artist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    return new AlbumTrackGroup(
                        Path.GetDirectoryName(first.SourcePath ?? first.FilePath) ?? string.Empty,
                        string.IsNullOrWhiteSpace(first.Album)
                            ? album.Title
                            : first.Album.Trim(),
                        albumArtists.Count == 1
                            ? albumArtists[0]
                            : albumArtists.Count == 0 && primaryArtists.Count == 1
                                ? primaryArtists[0]
                                : null,
                        first.Year,
                        group.ToList());
                })
                .OrderBy(group => group.Directory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            HideAlbumFolderGroups();
            ContentDataGrid.IsVisible = true;
            ApplyColumns("Tracks");
            ContentDataGrid.ItemsSource = rows;
            StartAlbumMusicBrainzRatingRefresh(rows);
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
            UpdateAlphabetIndex(null, false);
            ApplyAlbumDetailHeader(album);
            if (groupedRows.Count > 1)
                ShowAlbumFolderGroups(groupedRows);
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }
    }

    private static string NormalizeAlbumGroupValue(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private async void ShowAllAlbumTracksCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingAlbumTrackScope || _activeAlbumFilterId is null)
            return;

        _showAllAlbumTracks = ShowAllAlbumTracksCheckBox.IsChecked == true;
        if (_activeLogicalAlbumParts is { Count: > 0 })
            await ReloadVisibleLogicalAlbumTracksAsync();
        else if (_activeAlbumCatalogProvider is not null)
            await ReloadVisibleProviderAlbumTracksAsync();
        else
            await ReloadVisibleAlbumTracksAsync();
    }

    private void ApplyAlbumDetailHeader(AlbumInfo? album)
    {
        if (album is null)
        {
            HideAlbumDetailHeader();
            return;
        }

        var row = CreateAlbumDetailRow(album);
        row.LogicalAlbumIds = _activeLogicalAlbumIds;
        EnsureArtworkHydrated(row);
        AlbumDetailHeader.DataContext = row;
        AlbumDetailHeader.IsVisible = true;
        AlbumSaveAsPlaylistButton.IsVisible = true;
    }

    private void ApplyAlbumDetailHeader(LibraryCatalogAlbum album)
    {
        var row = CreateAlbumDetailRow(album);
        row.LogicalAlbumIds = _activeLogicalAlbumIds;
        EnsureArtworkHydrated(row);
        AlbumDetailHeader.DataContext = row;
        AlbumDetailHeader.IsVisible = true;
        AlbumSaveAsPlaylistButton.IsVisible = album.Source == LibraryCatalogSource.Local;
    }

    private static ContentRow CreateAlbumDetailRow(AlbumInfo album) =>
        new()
        {
            Id = album.Id,
            AlbumId = album.Id,
            Title = string.IsNullOrWhiteSpace(album.Album)
                ? LocalizationManager.Current.Unknown
                : album.Album,
            ArtistId = album.ArtistId,
            Artist = string.IsNullOrWhiteSpace(album.DisplayArtist)
                ? null
                : album.DisplayArtist,
            Year = album.Year?.ToString(),
            ArtworkPath = album.ArtworkPath,
            ThumbnailPath = album.ThumbnailPath,
            IsFavorite = album.IsFavorite,
            EntityType = "Album",
            FilePath = ""
        };

    private static ContentRow CreateAlbumDetailRow(LibraryCatalogAlbum album) =>
        new()
        {
            Id = album.Id,
            AlbumId = album.Id,
            ArtistId = album.ArtistId,
            Title = string.IsNullOrWhiteSpace(album.Title)
                ? LocalizationManager.Current.Unknown
                : album.Title,
            Artist = string.IsNullOrWhiteSpace(album.DisplayArtist)
                ? null
                : album.DisplayArtist,
            Year = album.Year?.ToString(CultureInfo.CurrentCulture),
            ArtworkPath = album.ArtworkPath,
            ThumbnailPath = album.ThumbnailPath,
            IsFavorite = album.IsFavorite,
            EntityType = album.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoAlbum" : "Album",
            ExternalId = album.Source == LibraryCatalogSource.OrynivoServer
                ? album.Id.ToString(CultureInfo.InvariantCulture)
                : null,
            FilePath = ""
        };

    private void HideAlbumDetailHeader()
    {
        AlbumDetailHeader.IsVisible = false;
        AlbumDetailHeader.DataContext = null;
        ShowAllAlbumTracksCheckBox.IsVisible = false;
        HideAlbumFolderGroups();
    }

    private void ShowAlbumFolderGroups(IReadOnlyList<AlbumTrackGroup> groups)
    {
        AlbumFolderGroupsPanel.Children.Clear();

        foreach (var group in groups)
        {
            var groupPanel = new StackPanel { Spacing = 10 };
            groupPanel.Children.Add(CreateAlbumFolderGroupHeader(
                group));

            var grid = new DataGrid
            {
                ItemsSource = group.Rows,
                // The horizontal grid line adds 1px to each row's pitch (40 -> 41), so the
                // table must be sized with the real pitch plus a small buffer; otherwise the
                // grid is a few pixels too short, shows an internal scrollbar, and the mouse
                // wheel scrolls the table content instead of the whole album page.
                Height = 46 + (group.Rows.Count * 41),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = FindResource<IBrush>("AppContentBrush"),
                BorderThickness = new Thickness(0),
                RowHeight = 40,
                ColumnHeaderHeight = 44,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = FindResource<IBrush>("AppGridLineBrush"),
                AutoGenerateColumns = false,
                CanUserResizeColumns = true,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                FontSize = 12
            };
            ScrollViewer.SetBringIntoViewOnFocusChange(grid, false);
            grid.LoadingRow += ContentDataGrid_OnLoadingRow;
            grid.DoubleTapped += ContentDataGrid_OnMouseDoubleClick;
            ApplyColumns("Tracks", grid, captureCurrentWidths: false);
            DataGridColumnChooser.Attach(
                grid,
                GetContentColumnWidthKey("Tracks"),
                _settings);
            grid.AddHandler(
                PointerReleasedEvent,
                AlbumFolderDataGrid_OnPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _albumFolderGroupGrids.Add(grid);
            groupPanel.Children.Add(grid);
            AlbumFolderGroupsPanel.Children.Add(groupPanel);
        }

        ContentDataGrid.IsVisible = false;
        AlbumFolderGroupsScrollViewer.IsVisible = true;
    }

    private Border CreateAlbumFolderGroupHeader(AlbumTrackGroup group)
    {
        var representativeTrack = group.Rows[0];
        var albumRow = new ContentRow
        {
            Id = representativeTrack.Id,
            AlbumId = representativeTrack.AlbumId,
            ArtistId = representativeTrack.ArtistId,
            Title = group.Album,
            Artist = group.Artist,
            Album = group.Album,
            Year = group.Year,
            EntityType = "AlbumGroup",
            FilePath = representativeTrack.FilePath
        };
        var titleButton = new Button
        {
            Content = albumRow.Title,
            Tag = albumRow,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        titleButton.Click += AlbumLinkButton_OnClick;

        var artistButton = new Button
        {
            Content = albumRow.Artist,
            Tag = albumRow,
            FontSize = 14,
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        artistButton.Click += ArtistLinkButton_OnClick;

        var metadataPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        if (!string.IsNullOrWhiteSpace(group.Artist))
            metadataPanel.Children.Add(artistButton);
        if (!string.IsNullOrWhiteSpace(albumRow.Year))
        {
            metadataPanel.Children.Add(new TextBlock
            {
                Text = albumRow.Year,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = FindResource<IBrush>("AppMutedTextBrush")
            });
        }
        metadataPanel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.FormatTrackCount(group.Rows.Count),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindResource<IBrush>("AppMutedTextBrush")
        });

        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(titleButton);
        content.Children.Add(metadataPanel);
        content.Children.Add(new TextBlock
        {
            Text = $"{LocalizationManager.Current.AlbumPath}: {GetAlbumGroupDirectoryLabel(group.Directory)}",
            FontSize = 12,
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            Padding = new Thickness(16, 12),
            Background = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 16, 0, 16),
            Child = content
        };
    }

    /// <summary>Persists a personal rating and opportunistically refreshes its MusicBrainz community value.</summary>
    /// <param name="row">Track row being rated.</param>
    /// <param name="rating">New zero-to-five-star rating.</param>
    /// <returns>A task representing persistence and optional metadata refresh.</returns>
    private async Task SetPersonalTrackRatingAsync(ContentRow row, int rating)
    {
        if (row.Id is not long trackId || row.EntityType is not ("Track" or "OrynivoTrack"))
            return;
        var previous = row.UserRating;
        row.UserRating = rating;
        var saved = row.OrynivoServer is { } server
            ? await _orynivoClient.UpdateTrackRatingAsync(
                server,
                trackId,
                new OrynivoTrackRatingUpdate(UserRating: rating))
            : await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.SetTrackUserRating(trackId, rating);
                return true;
            });
        if (!saved)
        {
            row.UserRating = previous;
            StatusTextBlock.Text = LocalizationManager.Current.RatingUpdateFailed;
            return;
        }

        _ = RefreshMusicBrainzTrackRatingAsync(row);
    }

    /// <summary>
    /// Starts a cancellable, sequential MusicBrainz refresh for stale tracks in the
    /// currently displayed album without delaying album rendering.
    /// </summary>
    /// <param name="rows">Displayed album-track rows.</param>
    private void StartAlbumMusicBrainzRatingRefresh(IReadOnlyList<ContentRow> rows)
    {
        _albumMusicBrainzRatingCts?.Cancel();
        _albumMusicBrainzRatingCts = null;

        var freshAfter = DateTimeOffset.UtcNow
            .Subtract(MusicBrainzRatingCacheLifetime)
            .ToUnixTimeSeconds();
        var staleRows = rows
            .Where(row => row.Id.HasValue &&
                          row.EntityType is "Track" or "OrynivoTrack" &&
                          (!row.MusicBrainzRatingFetchedAt.HasValue ||
                           row.MusicBrainzRatingFetchedAt.Value < freshAfter))
            .ToList();
        if (staleRows.Count == 0)
            return;

        var cts = new CancellationTokenSource();
        _albumMusicBrainzRatingCts = cts;
        _ = RefreshAlbumMusicBrainzRatingsAsync(staleRows, cts);
    }

    /// <summary>Refreshes album-track ratings sequentially to respect MusicBrainz request limits.</summary>
    /// <param name="rows">Stale album-track rows.</param>
    /// <param name="cts">Cancellation source owned by the current album view.</param>
    /// <returns>A task representing the background refresh.</returns>
    private async Task RefreshAlbumMusicBrainzRatingsAsync(
        IReadOnlyList<ContentRow> rows,
        CancellationTokenSource cts)
    {
        Interlocked.Increment(ref _musicBrainzForegroundRequests);
        try
        {
            await Task.Yield();
            var knownGroups = rows
                .Where(row => Guid.TryParse(row.MusicBrainzTrackId, out _))
                .GroupBy(row => row.MusicBrainzTrackId!, StringComparer.OrdinalIgnoreCase);
            foreach (var group in knownGroups)
            {
                cts.Token.ThrowIfCancellationRequested();
                var groupRows = group.ToList();
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (await RefreshMusicBrainzTrackRatingGroupAsync(groupRows, cts.Token))
                        break;
                    await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cts.Token);
                }
            }

            foreach (var row in rows.Where(row => !Guid.TryParse(row.MusicBrainzTrackId, out _)))
            {
                cts.Token.ThrowIfCancellationRequested();
                await RefreshMusicBrainzTrackRatingAsync(row, cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Decrement(ref _musicBrainzForegroundRequests);
            if (ReferenceEquals(_albumMusicBrainzRatingCts, cts))
                _albumMusicBrainzRatingCts = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// Uses one reliable direct Recording lookup for rows sharing an MBID, then
    /// persists the result into every represented local or remote library row.
    /// </summary>
    /// <param name="rows">Rows sharing one known recording MBID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing lookup and persistence.</returns>
    private async Task<bool> RefreshMusicBrainzTrackRatingGroupAsync(
        IReadOnlyList<ContentRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return true;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var row in rows)
            {
                row.IsMusicBrainzRatingLoading = true;
                row.MusicBrainzRatingTemporaryFailure = false;
            }
        });
        try
        {
            var first = rows[0];
            var service = new MusicBrainzRatingService(MusicBrainzRatingHttpClient);
            var result = await service.GetRatingAsync(
                first.MusicBrainzTrackId,
                first.Artist,
                first.Title,
                first.KnownDuration?.TotalSeconds,
                cancellationToken);
            if (result is null)
            {
                await SetMusicBrainzTemporaryFailureAsync(rows);
                return false;
            }
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PersistMusicBrainzTrackRatingAsync(row, result, cancellationToken);
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await SetMusicBrainzTemporaryFailureAsync(rows);
            return false;
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var row in rows)
                    row.IsMusicBrainzRatingLoading = false;
            });
        }
    }

    /// <summary>Resolves and caches the community rating for one track without blocking rating interaction.</summary>
    /// <param name="row">Track row whose metadata should be resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the MusicBrainz lookup and cache update.</returns>
    private async Task RefreshMusicBrainzTrackRatingAsync(
        ContentRow row,
        CancellationToken cancellationToken = default)
    {
        if (row.Id is not long trackId)
            return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.IsMusicBrainzRatingLoading = true;
            row.MusicBrainzRatingTemporaryFailure = false;
        });
        try
        {
            var service = new MusicBrainzRatingService(MusicBrainzRatingHttpClient);
            var result = await service.GetRatingAsync(
                row.MusicBrainzTrackId,
                row.Artist,
                row.Title,
                row.KnownDuration?.TotalSeconds,
                cancellationToken);
            if (result is null)
            {
                await PersistMusicBrainzLookupAttemptAsync(row, cancellationToken);
                return;
            }
            await PersistMusicBrainzTrackRatingAsync(row, result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await SetMusicBrainzTemporaryFailureAsync([row]);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => row.IsMusicBrainzRatingLoading = false);
        }
    }

    /// <summary>Marks rows for a visible manual retry after a temporary MusicBrainz failure.</summary>
    /// <param name="rows">Rows whose lookup did not complete.</param>
    /// <returns>A task representing the UI update.</returns>
    private static async Task SetMusicBrainzTemporaryFailureAsync(IEnumerable<ContentRow> rows)
    {
        var snapshot = rows.ToList();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var row in snapshot)
                row.MusicBrainzRatingTemporaryFailure = true;
        });
    }

    /// <summary>Runs one user-requested MusicBrainz lookup ahead of background enrichment.</summary>
    /// <param name="row">Track row requested by the user.</param>
    /// <returns>A task representing lookup and persistence.</returns>
    private async Task RefreshMusicBrainzTrackRatingForegroundAsync(ContentRow row)
    {
        Interlocked.Increment(ref _musicBrainzForegroundRequests);
        try
        {
            await RefreshMusicBrainzTrackRatingAsync(row);
        }
        finally
        {
            Interlocked.Decrement(ref _musicBrainzForegroundRequests);
        }
    }

    /// <summary>Persists one client-resolved MusicBrainz result in its owning local or remote library.</summary>
    /// <param name="row">Track row whose rating was resolved.</param>
    /// <param name="result">Resolved recording identity and rating.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing persistence.</returns>
    private async Task PersistMusicBrainzTrackRatingAsync(
        ContentRow row,
        MusicBrainzTrackRating result,
        CancellationToken cancellationToken)
    {
        if (row.Id is not long trackId)
            return;
        var fetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var saved = row.OrynivoServer is { } server
            ? await _orynivoClient.UpdateTrackRatingAsync(
                server,
                trackId,
                new OrynivoTrackRatingUpdate(
                    MusicBrainzTrackId: result.RecordingMbid,
                    MusicBrainzRating: result.Rating,
                    MusicBrainzRatingVotes: result.Votes,
                    MusicBrainzRatingFetchedAt: fetchedAt,
                    MusicBrainzGenres: MusicBrainzGenreMetadata.Serialize(result.Genres),
                    MusicBrainzTags: MusicBrainzGenreMetadata.Serialize(result.Tags)),
                cancellationToken)
            : await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.SetTrackMusicBrainzRating(
                    trackId,
                    result.RecordingMbid,
                    result.Rating,
                    result.Votes,
                    fetchedAt,
                    MusicBrainzGenreMetadata.Serialize(result.Genres),
                    MusicBrainzGenreMetadata.Serialize(result.Tags));
                if (db.GetTrackById(trackId) is { } updatedTrack)
                    TrackSearchIndex.UpdateMany([updatedTrack]);
                return true;
            }, cancellationToken);
        if (!saved)
            return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.MusicBrainzTrackId = result.RecordingMbid;
            row.MusicBrainzRatingVotes = result.Votes;
            row.MusicBrainzRating = result.Rating;
            row.MusicBrainzRatingFetchedAt = fetchedAt;
            row.MusicBrainzRatingTemporaryFailure = false;
        });
    }

    private static string GetAlbumGroupDirectoryLabel(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return LocalizationManager.Current.Unknown;

        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        var leaf = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(leaf))
            return directory;

        if (!Regex.IsMatch(
                leaf,
                @"^(?:cd|disc|disk|dvd)[\s._-]*0*[1-9]\d?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return leaf;
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(trimmed));
        return string.IsNullOrWhiteSpace(parent) ? leaf : $"{parent} / {leaf}";
    }

    private void HideAlbumFolderGroups()
    {
        AlbumFolderGroupsScrollViewer.IsVisible = false;
        AlbumFolderGroupsPanel.Children.Clear();
        _albumFolderGroupGrids.Clear();
    }

    private void AlbumFolderDataGrid_OnPointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        var widthKey = GetContentColumnWidthKey("Tracks");
        CaptureColumnWidths(widthKey, grid);
        foreach (var otherGrid in _albumFolderGroupGrids.Where(other => !ReferenceEquals(other, grid)))
            RestoreColumnWidths(widthKey, otherGrid);
    }

    private async Task ReloadAlbumDetailHeaderAsync(long albumId)
    {
        var album = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetAlbumById(albumId);
        });
        ApplyAlbumDetailHeader(album);
    }

    private void AlbumDetailFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { Id: long albumId } row })
            return;

        row.IsFavorite = !row.IsFavorite;
        InvalidateUnifiedLibraryViewCache();
        if (row.EntityType == "UnifiedAlbum" && row.LogicalAlbumParts is { Count: > 0 })
        {
            SetLogicalAlbumFavorite(row.LogicalAlbumParts, row.IsFavorite);
            e.Handled = true;
            return;
        }
        var albumIds = row.LogicalAlbumIds is { Count: > 0 }
            ? row.LogicalAlbumIds
            : [albumId];
        if (row.EntityType == "OrynivoAlbum" &&
            _activeOrynivoServer is not null)
        {
            foreach (var id in albumIds)
            {
                var key = GetOrynivoFavoriteKey(_activeOrynivoServer.Id, "Album", id);
                if (row.IsFavorite)
                    ActiveUserProfile.OrynivoServerFavorites.Add(key);
                else
                    ActiveUserProfile.OrynivoServerFavorites.Remove(key);
            }
            _settingsStore.Save(_settings);
            e.Handled = true;
            return;
        }

        using var db = AudioDatabase.OpenDefault();
        foreach (var id in albumIds)
            db.SetAlbumFavorite(id, row.IsFavorite);
        e.Handled = true;
    }

    private void AlbumSaveAsPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var paths = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)
            ?.Where(row => !string.IsNullOrWhiteSpace(row.FilePath))
            .Select(GetPersistablePlaylistPath)
            .ToList() ?? [];
        if (paths.Count == 0)
            return;

        button.ContextFlyout = BuildPlaylistContextFlyout(paths);
        e.Handled = true;
        button.ContextFlyout.ShowAt(button);
    }

    private sealed record AlbumTrackGroup(
        string Directory,
        string Album,
        string? Artist,
        string? Year,
        List<ContentRow> Rows);
}
