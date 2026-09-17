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
/// Top-level view switching, the shared content loading skeleton, and the
/// library intro card for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private async Task ShowTopLevelViewAsync(string tag)
    {
        var diagnosticStopwatch = Stopwatch.StartNew();
        LogUiDiagnostics($"ShowTopLevelViewAsync start tag={tag}");
        var showLoadingSkeleton = !tag.StartsWith("Radio:", StringComparison.Ordinal);
        if (showLoadingSkeleton)
            ShowContentLoadingSkeleton();
        _currentTopLevelTag = tag;
        _orynivoTrackFacets = null;
        // A fresh load reflects current library data, so any pending refresh prompt is stale.
        SetLibraryRefreshAvailable(false);
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        _activePlaylistId = tag.StartsWith("Playlist:") &&
                            long.TryParse(tag.AsSpan("Playlist:".Length), out long parsedPid)
            ? parsedPid : null;
        _activeOrynivoPlaylistServer = null;
        _activeOrynivoPlaylistId = null;
        if (TryParseOrynivoPlaylistTag(tag, out var playlistServer, out var playlistId))
        {
            _activeOrynivoPlaylistServer = playlistServer;
            _activeOrynivoPlaylistId = playlistId;
        }

        ContentTitleTextBlock.Text = tag switch
        {
            "Artists" => LocalizationManager.Current.Artists,
            "Albums"  => LocalizationManager.Current.Albums,
            "Tracks"  => LocalizationManager.Current.Tracks,
            "GenreCloud" => LocalizationManager.Current.GenreExplorer,
            "Folders" => LocalizationManager.Current.FolderStructure,
            "Queue" => LocalizationManager.Current.UpNext,
            "AiChat" => LocalizationManager.Current.AiChat,
            "InternetRadio" => LocalizationManager.Current.InternetRadio,
            "Podcasts" => LocalizationManager.Current.Podcasts,
            "RecentAlbumsAll" => LocalizationManager.Current.RecentAlbums,
            "RecentlyPlayedAll" => LocalizationManager.Current.RecentlyPlayed,
            _ when tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) =>
                _activePlexSectionTitle ?? LocalizationManager.Current.PlexServers,
            _ when tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) =>
                _activeOrynivoServer?.Name ?? LocalizationManager.Current.OrynivoServers,
            _ when tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal) => GetOrynivoPlaylistName(tag),
            _ when tag.StartsWith("Radio:", StringComparison.Ordinal) => GetRadioName(tag),
            _ when tag.StartsWith("Podcast:", StringComparison.Ordinal) => GetPodcastName(tag),
            _         => tag.StartsWith("Playlist:") ? GetPlaylistName(tag) : tag
        };

        // Stop any in-flight unified folder-tree load so a late completion cannot mutate
        // the tree or the count of the view the user is switching to.
        CancelAndDispose(ref _folderViewCts);
        CancelAndDispose(ref _genreCloudCts);
        ContentDataGrid.ItemsSource = null;
        AlbumArtworkListBox.ItemsSource = null;
        ArtistArtworkListBox.ItemsSource = null;
        _albumArtworkRows = [];
        _artistArtworkRows = [];
        _visibleAlbumArtworkRows.Clear();
        _visibleArtistArtworkRows.Clear();
        UpdateAlphabetIndex(null, false);
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        GenreCloudPanel.IsVisible = false;
        GenreCloudSurface.IsVisible = false;
        ContentDataGrid.Margin = new Thickness(0);
        AlbumArtworkListBox.Margin = new Thickness(0);
        AiChatViewControl.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        PodcastEpisodesView.IsVisible = false;
        HideAlbumDetailHeader();
        ContentCountTextBlock.Text  = "";
        UpdateLibraryIntroCard(tag);
        var isOrynivoServerTag = tag.StartsWith("OrynivoServer:", StringComparison.Ordinal);
        var isOrynivoTracksTag = TryParseOrynivoServerTag(tag, out _, out var orynivoViewForHeader) &&
                                 orynivoViewForHeader == "Tracks";
        SearchTextBox.IsVisible = isOrynivoTracksTag ||
                                   !(tag is "InternetRadio" or "Podcasts" or "Queue" or "AiChat" or "GenreCloud"
                                        or "RecentAlbumsAll" or "RecentlyPlayedAll" ||
                                    tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                    tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                    tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) ||
                                    tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal) ||
                                    isOrynivoServerTag);
        UpdateEntityFavoritesFilterToggle(tag);
        AlbumViewModeBorder.IsVisible = tag is "Albums" or "Artists";
        PlexViewModeBorder.IsVisible = tag.StartsWith("PlexLibrary:", StringComparison.Ordinal);
        OrynivoServerViewModeBorder.IsVisible = false;
        if (tag is "Albums" or "Artists")
            SetViewModeButtons(tag == "Albums" ? _showAlbumArtworkView : _showArtistArtworkView);
        TrackFilterButton.IsVisible = tag == "Tracks" ? true : false;
        SaveSmartPlaylistButton.IsVisible = tag == "Tracks" ? true : false;
        ClearQueueButton.IsVisible = tag == "Queue";
        ClearQueueButton.IsEnabled = _queue.Count > 0;
        InfiniteMixButton.IsVisible = tag == "Queue";
        InfiniteMixActionsPanel.IsVisible = tag == "Queue" && _infiniteMixEnabled;
        InfiniteMixStatusTextBlock.IsVisible = tag == "Queue" && _infiniteMixEnabled;
        SaveQueueAsPlaylistButton.IsVisible = tag == "Queue";
        SaveQueueAsPlaylistButton.IsEnabled =
            _queue.Any(item => CanPersistQueuePath(item.FilePath));
        UpdateRestoreQueueButtonState(tag);
        if (tag == "Queue" && _infiniteMixEnabled)
            RestoreQueueButton.IsVisible = false;
        if (tag == "Tracks") UpdateSaveSmartPlaylistButtonState();
        TrackFilterPopup.IsOpen = false;
        try
        {
            if (tag == "Dashboard")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await ShowDashboardAsync();
            }
            else if (tag == "RecentAlbumsAll")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await BuildAllRecentAlbumsViewAsync();
            }
            else if (tag == "RecentlyPlayedAll")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await BuildAllRecentlyPlayedViewAsync();
            }
            else if (tag == "AiChat")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                AiChatViewControl.IsVisible = true;
            }
            else if (tag == "InternetRadio")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                InternetRadioView.IsVisible = true;
                await EnsureRadioFilterCatalogAsync();
                if (RadioStationsDataGrid.ItemsSource is null)
                {
                    RadioStatusTextBlock.Text = LocalizationManager.Current.RadioEmptyState;
                    RadioStatusTextBlock.IsVisible = true;
                }
            }
            else if (tag == "Podcasts")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                PodcastView.IsVisible = true;
                PodcastEpisodesView.IsVisible = false;
                await EnsurePodcastFilterCatalogAsync();
                if (PodcastsDataGrid.ItemsSource is null)
                {
                    PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastEmptyState;
                    PodcastStatusTextBlock.IsVisible = true;
                }
            }
            else if (tag == "Queue")
            {
                ContentDataGrid.IsVisible = true;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                ApplyColumns("Queue");
                RefreshQueueRows();
            }
            else if (tag == "GenreCloud")
            {
                ContentDataGrid.IsVisible = true;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                GenreCloudPanel.IsVisible = true;
                GenreCloudSurface.IsVisible = true;
                ApplyColumns("Tracks");
                if (!_restoringNavigationHistory)
                    _genreCloudSelectedKey = null;
                await ShowGenreCloudAsync(_genreCloudSelectedKey);
            }
            else if (tag.StartsWith("Podcast:", StringComparison.Ordinal) &&
                     long.TryParse(tag.AsSpan("Podcast:".Length), out var podcastId))
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                PodcastView.IsVisible = true;
                await ShowSavedPodcastAsync(podcastId);
            }
            else if (tag.StartsWith("Radio:", StringComparison.Ordinal) &&
                     long.TryParse(tag.AsSpan("Radio:".Length), out var radioId))
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                InternetRadioView.IsVisible = true;
                _ = PlaySavedRadioAsync(radioId);
            }
            else if (tag.StartsWith("PlexLibrary:", StringComparison.Ordinal))
            {
                await ShowPlexLibraryAsync(tag);
            }
            else if (isOrynivoServerTag)
            {
                await ShowOrynivoServerAsync(tag);
            }
            else if (tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal))
            {
                await ShowOrynivoPlaylistAsync(tag);
            }
            else if (tag == "Folders")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = true;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                await ShowUnifiedFolderTreeAsync();
            }
            else
            {
                var showArtwork = tag == "Albums"
                    ? _showAlbumArtworkView
                    : tag == "Artists" && _showArtistArtworkView;
                ContentDataGrid.IsVisible = !(showArtwork);
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = tag == "Albums" && _showAlbumArtworkView
                    ? true : false;
                ArtistArtworkListBox.IsVisible = tag == "Artists" && _showArtistArtworkView
                    ? true : false;

                if (tag is "Artists" or "Albums" or "Tracks")
                {
                    await BindLocalRowsAndStartRemoteAppendAsync(tag);
                }
                else
                {
                    var rows = await Task.Run(() => QueryRows(tag));
                    ApplyColumns(tag);
                    ContentDataGrid.ItemsSource = rows;
                    UpdateAlphabetIndex(rows, false);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                }
            }
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
            LogUiDiagnostics(
                $"ShowTopLevelViewAsync finish tag={tag} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms gridVisible={ContentDataGrid.IsVisible} gridItems={GetDiagnosticItemCount(ContentDataGrid.ItemsSource)}");
        }
    }

    private void ShowContentLoadingSkeleton()
    {
        _contentLoadingDepth++;
        var generation = ++_contentLoadingGeneration;
        if (_contentLoadingDepth > 1)
            return;

        ContentLoadingOverlay.Opacity = 0;
        ContentLoadingOverlay.IsVisible = true;
        ContentLoadingOverlay.IsHitTestVisible = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_contentLoadingGeneration == generation && _contentLoadingDepth > 0)
                    ContentLoadingOverlay.Opacity = 1;
            },
            DispatcherPriority.Render);
    }

    private void HideContentLoadingSkeleton()
    {
        if (_contentLoadingDepth > 0)
            _contentLoadingDepth--;
        var generation = ++_contentLoadingGeneration;
        if (_contentLoadingDepth > 0)
            return;

        ContentLoadingOverlay.Opacity = 0;
        ContentLoadingOverlay.IsHitTestVisible = false;
        _ = HideContentLoadingSkeletonAsync(generation);
    }

    private async Task HideContentLoadingSkeletonAsync(int generation)
    {
        await Task.Delay(170);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_contentLoadingGeneration == generation &&
                _contentLoadingDepth == 0)
            {
                ContentLoadingOverlay.Opacity = 0;
                ContentLoadingOverlay.IsHitTestVisible = false;
                ContentLoadingOverlay.IsVisible = false;
            }
        });
    }

    private void FadeInVisibleContentSurface()
    {
        foreach (var surface in _animatedViewSurfaces)
        {
            if (surface.IsVisible)
                surface.Opacity = 0;
            else
                surface.Opacity = 1;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var surface in _animatedViewSurfaces)
            {
                if (surface.IsVisible)
                    surface.Opacity = 1;
            }
        }, DispatcherPriority.Render);
    }

    private void UpdateLibraryIntroCard(string? tag)
    {
        var strings = LocalizationManager.Current;
        var intro = tag switch
        {
            "Artists" => (strings.ArtistsIntroTitle, strings.ArtistsIntroHint, "IconArtist"),
            "Albums" => (strings.AlbumsIntroTitle, strings.AlbumsIntroHint, "IconAlbum"),
            "Tracks" => (strings.TracksIntroTitle, strings.TracksIntroHint, "IconTrack"),
            "Folders" => (strings.FoldersIntroTitle, strings.FoldersIntroHint, "IconFolder"),
            _ => default
        };

        var visible = !string.IsNullOrWhiteSpace(intro.Item1);
        LibraryIntroCard.IsVisible = visible;
        if (!visible)
            return;

        LibraryIntroTitleTextBlock.Text = intro.Item1;
        LibraryIntroHintTextBlock.Text = intro.Item2;
        LibraryIntroIconPath.Data = FindResource<Geometry>(intro.Item3);
    }
}
