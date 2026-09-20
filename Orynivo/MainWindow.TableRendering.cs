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
/// Shared data-grid column factories, row loading and context menus, track
/// information, and now-playing row highlighting for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private DataGridColumn CreateFavoriteColumn() =>
        new DataGridTemplateColumn
        {
            Header = "",
            SortMemberPath = nameof(ContentRow.IsFavorite),
            Width = new DataGridLength(42),
            CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
            {
                var button = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Width = 28,
                    Height = 28,
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = FindResource<IBrush>("AppFavoriteBrush"),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 17
                };
                button.Bind(Button.ContentProperty, new Binding(nameof(ContentRow.FavoriteGlyph)));
                button.Bind(Button.TagProperty, new Binding("."));
                button.Click += FavoriteButton_OnClick;
                return button;
            })
        };

    private DataGridColumn CreateSourceBadgeColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = LocalizationManager.Current.SourceColumn,
            SortMemberPath = nameof(ContentRow.SourceName),
            Width = new DataGridLength(54),
            CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
            {
                var badge = new Border
                {
                    Height = 22,
                    MinWidth = 28,
                    Padding = new Thickness(7, 0),
                    CornerRadius = new CornerRadius(11),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = FindResource<IBrush>("AppSurfaceHoverBrush"),
                    BorderBrush = FindResource<IBrush>("AppAccentBrush"),
                    BorderThickness = new Thickness(1)
                };
                var text = new TextBlock
                {
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = FindResource<IBrush>("AppAccentBrush")
                };
                text.Bind(TextBlock.TextProperty, new Binding(nameof(ContentRow.SourceBadge)));
                // The default tooltip theme renders a string tip in a TextBlock that does not
                // inherit ToolTip.Foreground, which left the source name black on the dark
                // surface. Provide an explicit TextBlock with a theme foreground instead.
                var tipText = new TextBlock
                {
                    Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
                };
                tipText.Bind(TextBlock.TextProperty, new Binding(nameof(ContentRow.SourceName)));
                var tip = new ToolTip
                {
                    Background = FindResource<IBrush>("AppSurfaceBrush"),
                    Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                    BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4),
                    Content = tipText
                };
                ToolTip.SetTip(badge, tip);
                badge.Bind(IsVisibleProperty, new Binding(nameof(ContentRow.SourceBadge))
                {
                    Converter = StringNotEmptyConverter.Instance
                });
                badge.Child = text;
                return badge;
            })
        };
        column.Tag = "source";
        return column;
    }

    private DataGridTemplateColumn CreateEntityLinkColumn(
        string header,
        string property,
        double width,
        bool star,
        string entityType,
        double starWeight = 1)
    {
        EventHandler<RoutedEventArgs> clickHandler = entityType == "Artist"
            ? ArtistLinkButton_OnClick
            : AlbumLinkButton_OnClick;

        return new DataGridTemplateColumn
        {
            Header = header,
            SortMemberPath = GetContentRowSortMemberPath(property),
            Width = star
                ? new DataGridLength(starWeight, DataGridLengthUnitType.Star)
                : new DataGridLength(width),
            CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
            {
                var button = new Button
                {
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
                };
                button.Bind(Button.ContentProperty, new Binding(property));
                button.Bind(Button.TagProperty, new Binding("."));
                button.Click += clickHandler;
                return button;
            })
        };
    }

    /// <summary>Resolves a displayed content-row property to its typed sort property.</summary>
    /// <param name="property">Displayed <see cref="ContentRow"/> property name.</param>
    /// <returns>The property name used by the DataGrid collection view for sorting.</returns>
    private static string GetContentRowSortMemberPath(string property) => property switch
    {
        nameof(ContentRow.Nr) => nameof(ContentRow.NrSort),
        nameof(ContentRow.Year) => nameof(ContentRow.YearSort),
        nameof(ContentRow.TrackNumber) => nameof(ContentRow.TrackNumberSort),
        nameof(ContentRow.DiscNumber) => nameof(ContentRow.DiscNumberSort),
        nameof(ContentRow.Duration) => nameof(ContentRow.DurationSort),
        nameof(ContentRow.Bitrate) => nameof(ContentRow.BitrateSort),
        nameof(ContentRow.SampleRate) => nameof(ContentRow.SampleRateSort),
        nameof(ContentRow.BitDepth) => nameof(ContentRow.BitDepthSort),
        nameof(ContentRow.Channels) => nameof(ContentRow.ChannelsSort),
        nameof(ContentRow.Bpm) => nameof(ContentRow.BpmSort),
        nameof(ContentRow.FileSize) => nameof(ContentRow.FileSizeSort),
        nameof(ContentRow.AddedAt) => nameof(ContentRow.AddedAtSort),
        nameof(ContentRow.ReplayGainTrack) => nameof(ContentRow.ReplayGainTrackSort),
        nameof(ContentRow.ReplayGainAlbum) => nameof(ContentRow.ReplayGainAlbumSort),
        _ => property
    };

    private static string FormatSeconds(double? seconds)
    {
        if (seconds is null) return "";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private async void AlbumViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode)
            return;

        var artworkMode = AlbumArtworkViewRadioButton.IsChecked == true;
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;

        if (tag == "Albums" ||
            (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) && _activeOrynivoView == "Albums"))
        {
            _showAlbumArtworkView = artworkMode;
            _settings.AlbumArtworkView = artworkMode;
            if (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            {
                await LoadOrynivoViewAsync();
                return;
            }
        }
        else if (tag == "Artists" ||
                 (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) && _activeOrynivoView == "Artists"))
        {
            _showArtistArtworkView = artworkMode;
            _settings.ArtistArtworkView = artworkMode;
            if (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            {
                await LoadOrynivoViewAsync();
                return;
            }
        }
        else
        {
            return;
        }

        await ReloadEntityRowsAsync(tag);
    }

    private void SetViewModeButtons(bool artworkMode)
    {
        _updatingViewMode = true;
        AlbumArtworkViewRadioButton.IsChecked = artworkMode;
        AlbumTableViewRadioButton.IsChecked = !artworkMode;
        _updatingViewMode = false;
    }

    private void ContentDataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var loadingRowCount = Interlocked.Increment(ref _diagnosticLoadingRowCount);
        if (loadingRowCount <= 5 || loadingRowCount % 250 == 0)
            LogUiDiagnostics($"ContentDataGrid_OnLoadingRow count={loadingRowCount} tag={_currentTopLevelTag ?? "<null>"}");
        ApplyNowPlayingClass(e.Row);
        SetPlaylistContextFlyout(e.Row);
        if (e.Row.DataContext is not ContentRow row)
            return;
        if (ContentDataGrid.Columns.Any(column =>
                column.IsVisible &&
                string.Equals(column.Tag as string, "thumbnail", StringComparison.Ordinal)))
        {
            EnsureThumbnailHydrated(row);
        }
        if (row.EntityType == "Artist")
            _ = EnsureArtistProfileAsync(row);
    }

    private void TrackDataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        ApplyNowPlayingClass(e.Row);
        if (e.Row.DataContext is PodcastEpisodeViewModel)
            SetPodcastEpisodeContextFlyout(e.Row);
        else
            SetPlaylistContextFlyout(e.Row);
    }

    private void PlaylistDataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        SetPlaylistContextFlyout(e.Row);

    private void SetPlaylistContextFlyout(DataGridRow row)
    {
        row.RemoveHandler(
            PointerPressedEvent,
            PlaylistContextItem_OnPreviewMouseRightButtonDown);
        row.ContextFlyout = null;
        if (row.DataContext is not ContentRow contentRow)
        {
            return;
        }

        var isTrack = !string.IsNullOrEmpty(contentRow.FilePath);
        var isAlbum = contentRow.EntityType == "Album";
        var isRemoteArtworkEntity = contentRow.EntityType is "OrynivoArtist" or "OrynivoAlbum";
        if (isRemoteArtworkEntity)
        {
            row.ContextFlyout = BuildOrynivoArtworkContextFlyout(contentRow);
            row.AddHandler(
                PointerPressedEvent,
                PlaylistContextItem_OnPreviewMouseRightButtonDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            return;
        }

        if (!isTrack && !isAlbum)
            return;

        var menu = _activeOrynivoPlaylistServer is not null &&
                   isTrack &&
                   contentRow.PlaylistEntryId.HasValue
            ? BuildRemoveFromOrynivoPlaylistContextFlyout(
                _activeOrynivoPlaylistServer,
                contentRow.PlaylistEntryId.Value,
                contentRow.FilePath)
            : contentRow.EntityType.StartsWith("Plex", StringComparison.Ordinal) ||
              (!CanPersistQueuePath(contentRow.FilePath) &&
               contentRow.EntityType != "OrynivoTrack")
            ? BuildQueueContextFlyout([contentRow.FilePath])
            : _activePlaylistId.HasValue &&
              isTrack &&
              contentRow.PlaylistEntryId.HasValue
            ? BuildRemoveFromPlaylistContextFlyout(
                contentRow.PlaylistEntryId.Value,
                contentRow.FilePath)
            : BuildPlaylistContextFlyout(GetPathsForRow(contentRow));
        if (isTrack)
        {
            menu.Items.Add(new Separator());
            var infoItem = CreateFlyoutMenuItem(LocalizationManager.Current.ShowTrackInfo);
            infoItem.Tag = contentRow;
            infoItem.Click += TrackInfoMenuItem_OnClick;
            menu.Items.Add(infoItem);
        }
        row.ContextFlyout = menu;
        row.AddHandler(
            PointerPressedEvent,
            PlaylistContextItem_OnPreviewMouseRightButtonDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private async void TrackInfoMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row })
            return;

        e.Handled = true;
        var title = $"{LocalizationManager.Current.TrackInfo}: {DisplayTrackInfoValue(row.Title)}";
        var dialog = new TrackInfoDialog(title, BuildTrackInfoEntries(row));
        await dialog.ShowDialog(this);
    }

    private static IReadOnlyList<TrackInfoEntry> BuildTrackInfoEntries(ContentRow row)
    {
        var physicalPath = row.SourcePath;
        if (string.IsNullOrWhiteSpace(physicalPath) ||
            IsHttpUrl(physicalPath) ||
            physicalPath.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase))
        {
            physicalPath = !IsHttpUrl(row.FilePath) &&
                           !row.FilePath.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase)
                ? row.FilePath
                : null;
        }

        var path = row.OrynivoServer is { } server && !string.IsNullOrWhiteSpace(physicalPath)
            ? $"{server.Name}: {physicalPath}"
            : row.PlexServerId is null
                ? physicalPath
                : null;

        return
        [
            new(LocalizationManager.Current.PhysicalPath, DisplayTrackInfoValue(path)),
            new(LocalizationManager.Current.Title, DisplayTrackInfoValue(row.Title)),
            new(LocalizationManager.Current.Artist, DisplayTrackInfoValue(row.Artist)),
            new(LocalizationManager.Current.Album, DisplayTrackInfoValue(row.Album)),
            new(LocalizationManager.Current.AlbumArtist, DisplayTrackInfoValue(row.AlbumArtist)),
            new(LocalizationManager.Current.Genre, DisplayTrackInfoValue(row.Genre)),
            new(LocalizationManager.Current.Year, DisplayTrackInfoValue(row.Year)),
            new(LocalizationManager.Current.TrackNumber, DisplayTrackInfoValue(row.TrackNumber)),
            new(LocalizationManager.Current.DiscNumber, DisplayTrackInfoValue(row.DiscNumber)),
            new(LocalizationManager.Current.Duration, DisplayTrackInfoValue(row.Duration)),
            new(LocalizationManager.Current.Format, DisplayTrackInfoValue(row.Format)),
            new(LocalizationManager.Current.Bitrate, DisplayTrackInfoValue(row.Bitrate)),
            new(LocalizationManager.Current.SampleRate, DisplayTrackInfoValue(row.SampleRate)),
            new(LocalizationManager.Current.BitDepth, DisplayTrackInfoValue(row.BitDepth)),
            new(LocalizationManager.Current.Channels, DisplayTrackInfoValue(row.Channels)),
            new(LocalizationManager.Current.Composer, DisplayTrackInfoValue(row.Composer)),
            new(LocalizationManager.Current.Bpm, DisplayTrackInfoValue(row.Bpm)),
            new(LocalizationManager.Current.MusicalKey, DisplayTrackInfoValue(row.CamelotKey)),
            new(LocalizationManager.Current.FileName, DisplayTrackInfoValue(row.FileName)),
            new(LocalizationManager.Current.FileSize, DisplayTrackInfoValue(row.FileSize)),
            new(LocalizationManager.Current.AddedAt, DisplayTrackInfoValue(row.AddedAt)),
            new(LocalizationManager.Current.ReplayGainTrackColumn, DisplayTrackInfoValue(row.ReplayGainTrack)),
            new(LocalizationManager.Current.ReplayGainAlbumColumn, DisplayTrackInfoValue(row.ReplayGainAlbum)),
            new(LocalizationManager.Current.Favorites, row.FavoriteGlyph),
            new(LocalizationManager.Current.SourceColumn, DisplayTrackInfoValue(row.SourceName)),
            new(LocalizationManager.Current.PersonalRating, row.UserRatingGlyph),
            new(LocalizationManager.Current.MusicBrainzRating, row.MusicBrainzRatingDisplay)
        ];
    }

    private static string DisplayTrackInfoValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? LocalizationManager.Current.Unknown : value;

    private MenuFlyout BuildOrynivoArtworkContextFlyout(ContentRow row)
    {
        var menu = CreateSidebarMenuFlyout();
        if (row.EntityType == "OrynivoArtist")
        {
            var refreshItem = CreateFlyoutMenuItem(LocalizationManager.Current.RefreshArtistInfo);
            refreshItem.Tag = row;
            refreshItem.Click += OrynivoArtistInfoRefreshMenuItem_OnClick;
            menu.Items.Add(refreshItem);
            menu.Items.Add(new Separator());
        }

        var label = row.EntityType == "OrynivoArtist"
            ? LocalizationManager.Current.SearchArtistImage
            : LocalizationManager.Current.SearchCover;
        var item = CreateFlyoutMenuItem(label);
        item.Tag = row;
        item.Click += OrynivoArtworkMenuItem_OnClick;
        menu.Items.Add(item);
        if (row.EntityType == "OrynivoAlbum")
        {
            menu.Items.Add(new Separator());
            // Resolving a remote album's track list requires a server round-trip.
            // This flyout is built while the row is being realized, so fetching the
            // paths synchronously here blocks the UI thread inside the DataGrid layout
            // pass and freezes the whole album table. Populate the playlist targets
            // asynchronously the first time the flyout is opened instead.
            var populated = false;
            menu.Opened += async (_, _) =>
            {
                if (populated)
                    return;
                populated = true;

                List<string> paths;
                try { paths = await Task.Run(() => GetPathsForRow(row)); }
                catch { paths = []; }
                if (paths.Count > 0)
                    AppendPlaylistItems(menu, paths);
            };
        }
        return menu;
    }

    private void ApplyNowPlayingClass(DataGridRow row)
    {
        var rowPath = GetPlaybackPath(row.DataContext);
        row.Classes.Set(
            "nowPlaying",
            _player is not null &&
            !string.IsNullOrWhiteSpace(rowPath) &&
            string.Equals(rowPath, _currentFilePath, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyNowPlayingClass(TreeViewItem item)
    {
        var audiblePath = (_player as IGaplessAudioPlayer)?.CurrentFilePath ??
                          _currentFilePath;
        var itemPath = item.Tag switch
        {
            FolderTag { IsFile: true } folder => folder.FilePath,
            PlexFolderTag { IsTrack: true, Track: not null } plex => plex.Track.FilePath,
            _ => null
        };
        var isNowPlaying =
            _player is not null &&
            !string.IsNullOrWhiteSpace(itemPath) &&
            string.Equals(itemPath, audiblePath, StringComparison.OrdinalIgnoreCase);
        item.Classes.Set("nowPlaying", isNowPlaying);

        if (item.Tag is FolderTag { IsFile: true })
        {
            item.ClearValue(TemplatedControl.BackgroundProperty);
            if (itemPath is not null &&
                _localFolderTrackHeaders.TryGetValue(itemPath, out var header))
            {
                header.Background = isNowPlaying
                    ? FindResource<IBrush>("AppNowPlayingRowBrush")
                    : Brushes.Transparent;
            }
        }
    }

    private static string? GetPlaybackPath(object? row) => row switch
    {
        ContentRow content => content.FilePath,
        RadioStationViewModel radio => radio.StreamUrl,
        PodcastEpisodeViewModel podcast => podcast.Episode.AudioUrl,
        _ => null
    };

    private void UpdateNowPlayingRowHighlights()
    {
        foreach (var row in this.GetVisualDescendants().OfType<DataGridRow>())
            ApplyNowPlayingClass(row);
        foreach (var item in _localFolderTrackItems.Values)
            ApplyNowPlayingClass(item);
        foreach (var item in FolderTreeView.Items.OfType<TreeViewItem>())
            UpdateNowPlayingTreeHighlights(item);
    }

    private void UpdateNowPlayingTreeHighlights(TreeViewItem item)
    {
        ApplyNowPlayingClass(item);
        foreach (var child in item.Items.OfType<TreeViewItem>())
            UpdateNowPlayingTreeHighlights(child);
    }

    private void ApplyColumns(
        string view,
        DataGrid? targetGrid = null,
        bool captureCurrentWidths = true,
        string? tableKeyOverride = null)
    {
        var grid = targetGrid ?? ContentDataGrid;
        if (captureCurrentWidths && ReferenceEquals(grid, ContentDataGrid))
        {
            CaptureContentDataGridColumnWidths();
            _contentColumnWidthKey = GetContentColumnWidthKey(view);
        }
        var widthKey = tableKeyOverride ?? GetContentColumnWidthKey(view);
        grid.Columns.Clear();
        switch (view)
        {
            case "PlexArtists":
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, "artist", star: true);
                break;
            case "PlexAlbums":
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, "album", star: true);
                Add(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist");
                Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 70, "year", right: true);
                break;
            case "PlexTracks":
                Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, "title", star: true);
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180, "artist");
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Album), 180, "album");
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, "duration", right: true);
                Add(LocalizationManager.Current.Format, nameof(ContentRow.Format), 80, "format");
                break;
            case "Artists":
                AddFavorite();
                AddSourceBadge();
                AddThumbnail();
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, "artist", true, "Artist");
                break;
            case "Albums":
                AddFavorite();
                AddSourceBadge();
                AddThumbnail();
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, "album", true, "Album");
                AddEntityLink(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist", false, "Artist");
                Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 60, "year", right: true);
                break;
            case string playlistTag when playlistTag.StartsWith("Playlist:", StringComparison.Ordinal) ||
                                         playlistTag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal):
                AddFavorite();
                AddSourceBadge();
                Add("#", nameof(ContentRow.Nr), 38, "position", right: true);
                AddTrackColumns(includeFavorite: false, includeSource: false, includeGenreByDefault: false);
                break;
            case "Queue":
                Add("#", nameof(ContentRow.Nr), 38, "position", right: true);
                // Queue uses the same selectable track columns as Tracks. The
                // queue number and actions remain fixed utility columns.
                AddTrackColumns(includeFavorite: true, includeSource: true, includeGenreByDefault: true);
                AddQueueActions();
                break;
            default: // Tracks
                AddTrackColumns(includeFavorite: true, includeSource: true, includeGenreByDefault: true);
                break;
        }

        DataGridColumnChooser.Apply(grid, widthKey, _settings);
        RestoreColumnWidths(widthKey, grid);

        void AddEntityLink(
            string header,
            string prop,
            double width,
            string key,
            bool star,
            string entityType,
            double starWeight = 1,
            bool defaultVisible = true)
        {
            var column = CreateEntityLinkColumn(header, prop, width, star, entityType, starWeight);
            column.Tag = key;
            column.IsVisible = defaultVisible;
            grid.Columns.Add(column);
        }

        void Add(
            string header,
            string prop,
            double width,
            string key,
            bool star = false,
            bool right = false,
            double starWeight = 1,
            bool defaultVisible = true)
        {
            DataGridColumn column;
            if (right)
            {
                column = new DataGridTemplateColumn
                {
                    Header = header,
                    SortMemberPath = GetContentRowSortMemberPath(prop),
                    Width = star ? new DataGridLength(starWeight, DataGridLengthUnitType.Star) : new DataGridLength(width),
                    CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
                    {
                        var tb = new TextBlock
                        {
                            HorizontalAlignment = HorizontalAlignment.Right,
                            FontSize = 12,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        tb.Bind(TextBlock.TextProperty, new Binding(prop));
                        return tb;
                    })
                };
            }
            else
            {
                column = new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding(prop),
                    SortMemberPath = GetContentRowSortMemberPath(prop),
                    Width = star ? new DataGridLength(starWeight, DataGridLengthUnitType.Star) : new DataGridLength(width)
                };
            }
            column.Tag = key;
            column.IsVisible = defaultVisible;
            grid.Columns.Add(column);
        }

        void AddTrackColumns(bool includeFavorite, bool includeSource, bool includeGenreByDefault)
        {
            if (includeFavorite)
                AddFavorite();
            if (includeSource)
                AddSourceBadge();
            Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, "title", star: true, starWeight: 2.3);
            AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 0, "artist", true, "Artist", 1.05);
            AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Album), 0, "album", true, "Album", 1.05);
            Add(LocalizationManager.Current.Genre, nameof(ContentRow.Genre), 120, "genre", defaultVisible: includeGenreByDefault);
            Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 80, "duration", right: true);
            Add(LocalizationManager.Current.Format, nameof(ContentRow.Format), 80, "format");
            Add(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.AlbumArtist), 180, "albumArtist", defaultVisible: false);
            Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 80, "year", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.TrackNumber, nameof(ContentRow.TrackNumber), 90, "trackNumber", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.DiscNumber, nameof(ContentRow.DiscNumber), 90, "discNumber", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.Bitrate, nameof(ContentRow.Bitrate), 100, "bitrate", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.SampleRate, nameof(ContentRow.SampleRate), 110, "sampleRate", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.BitDepth, nameof(ContentRow.BitDepth), 90, "bitDepth", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.Channels, nameof(ContentRow.Channels), 80, "channels", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.Composer, nameof(ContentRow.Composer), 180, "composer", defaultVisible: false);
            Add(LocalizationManager.Current.Bpm, nameof(ContentRow.Bpm), 70, "bpm", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.MusicalKey, nameof(ContentRow.CamelotKey), 70, "camelotKey", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.FileName, nameof(ContentRow.FileName), 220, "fileName", defaultVisible: false);
            Add(LocalizationManager.Current.FileSize, nameof(ContentRow.FileSize), 100, "fileSize", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.AddedAt, nameof(ContentRow.AddedAt), 110, "addedAt", defaultVisible: false);
            Add(LocalizationManager.Current.ReplayGainTrackColumn, nameof(ContentRow.ReplayGainTrack), 120, "replayGainTrack", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.ReplayGainAlbumColumn, nameof(ContentRow.ReplayGainAlbum), 120, "replayGainAlbum", right: true, defaultVisible: false);
            grid.Columns.Add(CreatePersonalRatingColumn());
            var musicBrainzRatingColumn = CreateMusicBrainzRatingColumn();
            musicBrainzRatingColumn.IsVisible = false;
            grid.Columns.Add(musicBrainzRatingColumn);
        }

        void AddSourceBadge()
        {
            grid.Columns.Add(CreateSourceBadgeColumn());
        }

        void AddFavorite()
        {
            grid.Columns.Add(CreateFavoriteColumn());
        }

        void AddThumbnail(bool defaultVisible = true)
        {
            var column = new DataGridTemplateColumn
            {
                Header = "",
                SortMemberPath = nameof(ContentRow.Title),
                Width = new DataGridLength(64),
                CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
                {
                    var image = new Image
                    {
                        Width = 32,
                        Height = 32,
                        Stretch = Stretch.UniformToFill,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    image.Bind(Image.SourceProperty, new Binding(nameof(ContentRow.Thumbnail)));
                    return image;
                })
            };
            column.Tag = "thumbnail";
            column.IsVisible = defaultVisible;
            grid.Columns.Add(column);
        }

        void AddQueueActions()
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                CanUserSort = false,
                Width = new DataGridLength(244),
                CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
                {
                    var panel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 4
                    };
                    panel.Children.Add(CreateQueueActionButton(
                        "↑",
                        LocalizationManager.Current.MoveUp,
                        row,
                        QueueMoveUpButton_OnClick));
                    panel.Children.Add(CreateQueueActionButton(
                        "↓",
                        LocalizationManager.Current.MoveDown,
                        row,
                        QueueMoveDownButton_OnClick));
                    panel.Children.Add(CreateQueueActionButton(
                        "×",
                        LocalizationManager.Current.RemoveFromQueue,
                        row,
                        QueueRemoveButton_OnClick));
                    var mixActionsVisible = _infiniteMixEnabled && row.QueueItem is not null &&
                        _infiniteMixIdentitiesByPath.ContainsKey(row.QueueItem.FilePath);
                    var more = CreateQueueActionButton("+", LocalizationManager.Current.InfiniteMixMoreLikeThis, row, InfiniteMixMoreButton_OnClick);
                    var less = CreateQueueActionButton("−", LocalizationManager.Current.InfiniteMixLessLikeThis, row, InfiniteMixLessButton_OnClick);
                    var exclude = CreateQueueActionButton("⊘", LocalizationManager.Current.InfiniteMixExcludeTrack, row, InfiniteMixExcludeButton_OnClick);
                    more.IsVisible = less.IsVisible = exclude.IsVisible = mixActionsVisible;
                    panel.Children.Add(more);
                    panel.Children.Add(less);
                    panel.Children.Add(exclude);
                    return panel;
                })
            });
        }
    }
}
