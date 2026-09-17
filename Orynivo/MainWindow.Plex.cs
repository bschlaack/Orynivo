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
/// Plex Media Server library browsing, paging, drill-down, and lazy folder tree
/// for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private async Task ShowPlexLibraryAsync(string tag)
    {
        var parts = tag.Split(':', 3);
        if (parts.Length != 3)
            return;

        _activePlexServer = (_settings.PlexServers ?? [])
            .FirstOrDefault(server => string.Equals(server.Id, parts[1], StringComparison.Ordinal));
        if (_activePlexServer is null)
            return;

        _activePlexSectionKey = parts[2];
        _activePlexSectionTitle = NavListBox.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            ?.Content is TextBlock text
                ? (text.Text ?? string.Empty).Trim()
                : _activePlexServer.Name;
        try
        {
            _activePlexToken = new WindowsPlexCredentialStore()
                .LoadAll()
                .GetValueOrDefault(_activePlexServer.Id);
        }
        catch
        {
            _activePlexToken = null;
        }

        _activePlexView = "Artists";
        _plexNavigationStack.Clear();
        _updatingViewMode = true;
        PlexArtistsViewRadioButton.IsChecked = true;
        _updatingViewMode = false;
        ContentTitleTextBlock.Text = _activePlexSectionTitle;
        await LoadPlexViewAsync(reset: true);
    }

    private async void PlexViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode ||
            sender is not RadioButton { IsChecked: true, Tag: string view } ||
            _activePlexServer is null)
        {
            return;
        }

        _plexNavigationStack.Clear();
        _activePlexView = view;
        ContentTitleTextBlock.Text = _activePlexSectionTitle;
        BackButton.IsVisible = _navigationStack.Count > 0;
        await LoadPlexViewAsync(reset: true);
    }

    private async void PlexLoadMoreButton_OnClick(object? sender, RoutedEventArgs e)
        => await LoadPlexViewAsync(reset: false);

    private async Task LoadPlexViewAsync(bool reset)
    {
        if (_activePlexServer is null || string.IsNullOrWhiteSpace(_activePlexSectionKey))
            return;

        CancelAndDispose(ref _plexViewCts);
        _plexViewCts = new CancellationTokenSource();
        var cancellationToken = _plexViewCts.Token;
        var loadVersion = ++_plexViewLoadVersion;
        var server = _activePlexServer;
        var token = _activePlexToken;
        var sectionKey = _activePlexSectionKey;
        var view = _activePlexView;
        if (reset)
        {
            _plexLoadedCount = 0;
            _plexTotalCount = 0;
        }

        ContentDataGrid.IsVisible = view != "Folders";
        FolderTreeView.IsVisible = view == "Folders";
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        PlexLoadMoreButton.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.PlexLoading;
        if (view == "Folders")
            UpdateAlphabetIndex(null, false);

        try
        {
            if (view == "Folders")
            {
                await BuildPlexFolderTreeAsync(
                    server,
                    token,
                    sectionKey,
                    cancellationToken);
                if (loadVersion != _plexViewLoadVersion ||
                    !string.Equals(view, _activePlexView, StringComparison.Ordinal))
                {
                    return;
                }
                UpdatePlexFolderAlphabetIndex();
                ContentCountTextBlock.Text = string.Empty;
                StatusTextBlock.Text = string.Empty;
                return;
            }

            var mediaType = view switch
            {
                "Artists" => 8,
                "Albums" => 9,
                _ => 10
            };
            var page = await _plexClient.GetLibraryItemsAsync(
                server,
                token,
                sectionKey,
                mediaType,
                _plexLoadedCount,
                PlexPageSize,
                cancellationToken);
            if (loadVersion != _plexViewLoadVersion ||
                !string.Equals(view, _activePlexView, StringComparison.Ordinal))
            {
                return;
            }

            var newRows = page.Items
                .Select(item => ToPlexContentRow(item, view, server, token))
                .ToList();
            var rows = reset
                ? newRows
                : ((ContentDataGrid.ItemsSource as IEnumerable<ContentRow>) ?? [])
                    .Concat(newRows)
                    .ToList();
            _plexLoadedCount = rows.Count;
            _plexTotalCount = page.TotalSize;
            ApplyColumns("Plex" + view);
            ContentDataGrid.ItemsSource = rows;
            UpdateAlphabetIndex(rows, view is "Artists" or "Albums" or "Tracks");
            ContentCountTextBlock.Text = $"{_plexLoadedCount:N0} / {_plexTotalCount:N0}";
            PlexLoadMoreButton.IsVisible = _plexLoadedCount < _plexTotalCount;
            StatusTextBlock.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.PlexConnectionFailed,
                ex.Message);
        }
    }

    private ContentRow ToPlexContentRow(
        PlexMediaItem item,
        string view,
        PlexServerSettings server,
        string? token)
    {
        var entityType = view switch
        {
            "Artists" => "PlexArtist",
            "Albums" => "PlexAlbum",
            _ => "PlexTrack"
        };
        var row = new ContentRow
        {
            ExternalId = item.RatingKey,
            Title = item.Title,
            Artist = item.Artist,
            Album = item.Album,
            Year = item.Year?.ToString(),
            Duration = item.DurationMilliseconds is long duration
                ? FormatSeconds(duration / 1000d)
                : string.Empty,
            Format = item.Format?.ToUpperInvariant(),
            FilePath = item.PartKeys.Count > 0
                ? PlexServerClient.CreateStreamUrl(
                    server,
                    item.PartKeys[0],
                    token)
                : string.Empty,
            PlexPartUrls = item.PartKeys
                .Select(partKey => PlexServerClient.CreateStreamUrl(server, partKey, token))
                .ToArray(),
            KnownDuration = item.DurationMilliseconds is long durationMilliseconds
                ? TimeSpan.FromMilliseconds(durationMilliseconds)
                : null,
            EntityType = entityType,
            PlexServerId = server.Id,
            PlexAlbumRatingKey = item.ParentRatingKey,
            PlexArtistRatingKey = item.GrandparentRatingKey
        };
        if (entityType == "PlexTrack" && row.FilePath.Length > 0)
            _plexTracksByUrl[row.FilePath] = row;
        return row;
    }

    private async Task ShowPlexChildrenAsync(ContentRow parent)
    {
        if (_activePlexServer is null || string.IsNullOrWhiteSpace(parent.ExternalId))
            return;

        StatusTextBlock.Text = LocalizationManager.Current.PlexLoading;
        try
        {
            var page = await _plexClient.GetChildrenAsync(
                _activePlexServer,
                _activePlexToken,
                parent.ExternalId);
            _plexNavigationStack.Push(new PlexNavigationState(
                ContentTitleTextBlock.Text ?? string.Empty,
                _activePlexView,
                (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? []));
            BackButton.IsVisible = true;
            _activePlexView = parent.EntityType == "PlexArtist" ? "Albums" : "Tracks";
            var rows = page.Items
                .Select(item => ToPlexContentRow(
                    item,
                    _activePlexView,
                    _activePlexServer,
                    _activePlexToken))
                .ToList();
            ApplyColumns("Plex" + _activePlexView);
            ContentDataGrid.ItemsSource = rows;
            ContentTitleTextBlock.Text = parent.Title ?? _activePlexSectionTitle;
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
            PlexViewModeBorder.IsVisible = false;
            StatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.PlexConnectionFailed,
                ex.Message);
        }
    }

    private async Task BuildPlexFolderTreeAsync(
        PlexServerSettings server,
        string? token,
        string sectionKey,
        CancellationToken cancellationToken)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        FolderTreeView.Items.Clear();
        var page = await _plexClient.GetFoldersAsync(
            server,
            token,
            sectionKey,
            null,
            cancellationToken);
        foreach (var folder in page.Items)
            FolderTreeView.Items.Add(CreatePlexFolderItem(folder, server, token, sectionKey));
    }

    private TreeViewItem CreatePlexFolderItem(
        PlexMediaItem item,
        PlexServerSettings server,
        string? token,
        string sectionKey)
    {
        var row = item.IsFolder || item.PartKeys.Count == 0
            ? null
            : ToPlexContentRow(item, "Tracks", server, token);
        var treeItem = new TreeViewItem
        {
            Header = item.Title,
            Tag = new PlexFolderTag(item.Key, row is not null, row)
        };
        ApplyNowPlayingClass(treeItem);
        if (row is not null)
        {
            treeItem.ContextFlyout = BuildQueueContextFlyout([row.FilePath]);
            AttachFolderPlaylistContextHandler(treeItem);
            return treeItem;
        }

        var placeholder = new TreeViewItem();
        treeItem.Items.Add(placeholder);
        var isLoaded = false;
        Task? loadingTask = null;

        async Task LoadChildrenAsync()
        {
            if (isLoaded)
                return;
            if (loadingTask is not null)
            {
                await loadingTask;
                return;
            }

            loadingTask = LoadCoreAsync();
            await loadingTask;
            loadingTask = null;

            async Task LoadCoreAsync()
            {
                try
                {
                    var page = await _plexClient.GetFoldersAsync(
                        server,
                        token,
                        sectionKey,
                        item.Key);
                    treeItem.Items.Clear();
                    foreach (var child in page.Items)
                        treeItem.Items.Add(CreatePlexFolderItem(child, server, token, sectionKey));
                    isLoaded = true;
                    treeItem.InvalidateMeasure();
                }
                catch (Exception ex)
                {
                    treeItem.Items.Clear();
                    treeItem.Items.Add(placeholder);
                    StatusTextBlock.Text = string.Format(
                        LocalizationManager.Current.PlexConnectionFailed,
                        ex.Message);
                }
            }
        }

        treeItem.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(async (_, e) =>
            {
                if (!e.GetCurrentPoint(treeItem).Properties.IsLeftButtonPressed ||
                    !ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), treeItem))
                {
                    return;
                }

                var isChevronPress = FindAncestor<ToggleButton>(e.Source as Visual) is not null;
                if (isChevronPress)
                {
                    if (isLoaded)
                        return;
                    e.Handled = true;
                    await LoadChildrenAsync();
                    if (isLoaded)
                        treeItem.IsExpanded = true;
                    return;
                }

                if (e.ClickCount < 2)
                    return;

                e.Handled = true;
                if (treeItem.IsExpanded)
                {
                    treeItem.IsExpanded = false;
                    return;
                }

                await LoadChildrenAsync();
                if (isLoaded)
                    treeItem.IsExpanded = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        treeItem.AddHandler(
            InputElement.DoubleTappedEvent,
            new EventHandler<TappedEventArgs>((_, e) =>
            {
                if (ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), treeItem) &&
                    FindAncestor<ToggleButton>(e.Source as Visual) is null)
                    e.Handled = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        treeItem.Expanded += async (_, _) =>
        {
            if (isLoaded)
                return;

            treeItem.IsExpanded = false;
            await LoadChildrenAsync();
            if (isLoaded)
                treeItem.IsExpanded = true;
        };
        return treeItem;
    }
}
