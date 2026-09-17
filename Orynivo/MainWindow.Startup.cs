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
/// Startup view selection, window placement, settings loading, and background
/// library activity monitoring for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void SelectInitialView()
    {
        var tag = _settings.LastMainView;
        ApplySidebarNavigationSettings();
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));
        if (item is null && tag.StartsWith("PlexLibrary:", StringComparison.Ordinal))
            _pendingInitialNavigationTag = tag;
        NavListBox.SelectedItem = item
            ?? NavListBox.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, "Tracks", StringComparison.Ordinal));
    }

    private void PersistViewState()
    {
        if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag } &&
            IsPersistableMainViewTag(tag))
        {
            _settings.LastMainView = tag;
        }
        _settings.AlbumArtworkView = _showAlbumArtworkView;
        _settings.ArtistArtworkView = _showArtistArtworkView;
        _settings.Volume = VolumeSlider.Value;
        _settings.LastTrackPath = IsAvailableLocalTrack(_currentFilePath) ? _currentFilePath : null;
        CapturePlaybackQueueState();
        _settingsStore.Save(_settings);
    }

    /// <summary>Restores the configured startup state and the last usable normal window bounds.</summary>
    private void RestoreWindowPlacement()
    {
        var width = Math.Clamp(_settings.MainWindowWidth, MinWidth, 4096);
        var height = Math.Clamp(_settings.MainWindowHeight, MinHeight, 2160);
        Width = width;
        Height = height;

        if (_settings.StartMaximized)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        WindowState = WindowState.Normal;
        if (_settings.MainWindowX is int x &&
            _settings.MainWindowY is int y &&
            IsWindowPlacementVisible(x, y, width, height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>Captures the current bounds while the window is in its normal state.</summary>
    private void CaptureNormalWindowPlacement()
    {
        if (WindowState != WindowState.Normal || ClientSize.Width < MinWidth || ClientSize.Height < MinHeight)
            return;

        _settings.MainWindowWidth = ClientSize.Width;
        _settings.MainWindowHeight = ClientSize.Height;
        _settings.MainWindowX = Position.X;
        _settings.MainWindowY = Position.Y;
    }

    /// <summary>Checks whether a saved window rectangle still has a useful intersection with an attached screen.</summary>
    /// <param name="x">Saved horizontal screen coordinate.</param>
    /// <param name="y">Saved vertical screen coordinate.</param>
    /// <param name="width">Saved logical width.</param>
    /// <param name="height">Saved logical height.</param>
    /// <returns><see langword="true"/> when at least 100 physical pixels remain visible in both dimensions.</returns>
    private bool IsWindowPlacementVisible(int x, int y, double width, double height)
    {
        foreach (var screen in Screens.All)
        {
            var bounds = screen.WorkingArea;
            var physicalWidth = width * screen.Scaling;
            var physicalHeight = height * screen.Scaling;
            var intersectionWidth = Math.Min(x + physicalWidth, bounds.Right) - Math.Max(x, bounds.X);
            var intersectionHeight = Math.Min(y + physicalHeight, bounds.Bottom) - Math.Max(y, bounds.Y);
            if (intersectionWidth >= 100 && intersectionHeight >= 100)
                return true;
        }

        return false;
    }

    private void RestoreFixedDataGridColumnWidths()
    {
        RestoreColumnWidths("RadioStations", RadioStationsDataGrid);
        RestoreColumnWidths("Podcasts", PodcastsDataGrid);
        RestoreColumnWidths("PodcastEpisodes", PodcastEpisodesDataGrid);
    }

    private void AttachDataGridColumnChoosers()
    {
        DataGridColumnChooser.Attach(
            ContentDataGrid,
            () => _contentColumnWidthKey ?? "Content.Tracks",
            _settings);
        DataGridColumnChooser.Attach(SearchTracksDataGrid, "SearchTracks", _settings);
        DataGridColumnChooser.Attach(SearchAlbumsDataGrid, "SearchAlbums", _settings);
        DataGridColumnChooser.Attach(SearchArtistsDataGrid, "SearchArtists", _settings);
        DataGridColumnChooser.Attach(ArtistInfoTracksDataGrid, "ArtistInfoTracks", _settings);
        DataGridColumnChooser.Attach(RadioStationsDataGrid, "RadioStations", _settings);
        DataGridColumnChooser.Attach(PodcastsDataGrid, "Podcasts", _settings);
        DataGridColumnChooser.Attach(PodcastEpisodesDataGrid, "PodcastEpisodes", _settings);
    }

    private void CaptureAllDataGridColumnWidths()
    {
        CaptureContentDataGridColumnWidths();
        CaptureColumnWidths("RadioStations", RadioStationsDataGrid);
        CaptureColumnWidths("Podcasts", PodcastsDataGrid);
        CaptureColumnWidths("PodcastEpisodes", PodcastEpisodesDataGrid);
        CaptureColumnWidths("SearchTracks", SearchTracksDataGrid);
        CaptureColumnWidths("SearchAlbums", SearchAlbumsDataGrid);
        CaptureColumnWidths("SearchArtists", SearchArtistsDataGrid);
        CaptureColumnWidths("ArtistInfoTracks", ArtistInfoTracksDataGrid);
    }

    private void CaptureContentDataGridColumnWidths()
    {
        if (!string.IsNullOrWhiteSpace(_contentColumnWidthKey))
            CaptureColumnWidths(_contentColumnWidthKey, ContentDataGrid);
    }

    private void CaptureColumnWidths(string key, DataGrid grid) =>
        DataGridColumnWidthStore.Capture(_settings.DataGridColumnWidths, key, grid);

    private void RestoreColumnWidths(string key, DataGrid grid) =>
        DataGridColumnWidthStore.Restore(_settings.DataGridColumnWidths, key, grid);

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        _profileManager = new UserProfileManager(_settings);
        AudioDatabase.SetActiveProfile(_profileManager.ActiveProfile.Id);
        ApplyServerProfileContext();
        _ = RefreshServerProfileMappingsAsync();
        _settings.DataGridColumnWidths ??= new Dictionary<string, List<double>>(StringComparer.Ordinal);
        _settings.VisibleDataGridColumns ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _settings.DataGridColumnOrders ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _settings.PlaybackQueuePaths ??= [];
        _settings.CollapsedOrynivoServerLibraryGroups ??= new HashSet<string>(StringComparer.Ordinal);
        _settings.CollapsedOrynivoServerPlaylistGroups ??= new HashSet<string>(StringComparer.Ordinal);
        if (_settings.OutputBackend == OutputBackend.Asio && !SteinbergAsioStream.IsAvailable)
        {
            _settings.OutputBackend = SteinbergAsioStream.IsCwAsioAvailable
                ? OutputBackend.CwAsio
                : OutputBackend.Wasapi;
            _settings.SelectedDriverName = null;
            _settingsStore.Save(_settings);
        }
        else if (_settings.OutputBackend == OutputBackend.CwAsio && !SteinbergAsioStream.IsCwAsioAvailable)
        {
            _settings.OutputBackend = SteinbergAsioStream.IsAvailable
                ? OutputBackend.Asio
                : OutputBackend.Wasapi;
            _settings.SelectedDriverName = null;
            _settingsStore.Save(_settings);
        }
        RefreshSelectedDriverText();
        ApplyArtistInfoSettings();
    }

    private void ApplyArtistInfoSettings()
    {
        ArtistProfileService.Source = _settings.ArtistInfoSource;
        ArtistProfileService.LastFmApiKey = _settings.LastFmApiKey;
        ArtistProfileService.FanartTvApiKey =
            string.IsNullOrWhiteSpace(_settings.FanartTvApiKey)
                ? Environment.GetEnvironmentVariable("FANART_TV_API_KEY")
                : _settings.FanartTvApiKey;
    }

    private void OnWatchedLibraryChanged()
    {
        InvalidateDashboardCatalogCache();
        InvalidateGenreCloudViewCache();
        InvalidateUnifiedLibraryViewCache();

        // Coalesce bursts of change signals into a single UI-thread pass.
        if (Interlocked.Exchange(ref _libraryWatcherRefreshPending, 1) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _libraryWatcherRefreshPending, 0);
            try
            {
                // The sidebar playlist list is lightweight metadata, not a content
                // reload, so keep it in sync immediately.
                LoadNavPlaylists();

                // Do NOT auto-reload the visible content view. Instead offer a
                // controlled "new library data available" refresh action on views
                // that can safely reload in place. No automatic navigation.
                if (CanReloadCurrentViewAfterLibraryChange())
                    SetLibraryRefreshAvailable(true);
            }
            catch
            {
                // Background library refreshes must not affect playback or input handling.
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Updates the subtle sidebar activity indicator while the background scanner or
    /// indexer is running. Throttled so a fast per-file scan does not flood the UI thread.
    /// </summary>
    /// <param name="activity">The reported scan-activity snapshot.</param>
    private void OnLibraryScanActivity(LibraryScanActivity activity)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Always render start/stop transitions; throttle intermediate progress.
            var stateChanged = activity.Active != _libraryScanActive;
            if (!stateChanged && activity.Active &&
                (DateTime.UtcNow - _lastLibraryActivityUiUpdate).TotalMilliseconds < 200)
            {
                return;
            }

            _libraryScanActive = activity.Active;
            _lastLibraryActivityUiUpdate = DateTime.UtcNow;
            _localScanText = activity.Active
                ? (activity.Total > 0 && activity.Current > 0
                    ? string.Format(
                        LocalizationManager.Current.LibraryUpdatingWithCount,
                        activity.Current,
                        activity.Total)
                    : LocalizationManager.Current.LibraryUpdating)
                : null;
            UpdateLibraryActivityIndicator();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Shows the subtle sidebar activity line. The local scan takes priority; when no local
    /// scan is running the most recent remote server scan status (if any) is shown instead.
    /// </summary>
    private void UpdateLibraryActivityIndicator()
    {
        var text = _localScanText ?? _remoteScanText;
        var visible = !string.IsNullOrWhiteSpace(text);
        LibraryActivityPanel.IsVisible = visible;
        LibraryActivityTextBlock.Text = visible ? text! : string.Empty;
    }

    /// <summary>
    /// Polls every configured remote Orynivo Server for an in-progress scan and surfaces its
    /// progress in the sidebar activity line, without reloading or blocking the current view.
    /// </summary>
    /// <returns>A task representing the asynchronous poll.</returns>
    private async Task PollRemoteServerScansAsync()
    {
        if (_remoteScanPollInProgress)
            return;
        var servers = _settings.OrynivoServers;
        if (servers is null || servers.Count == 0)
        {
            if (_remoteScanText is not null)
            {
                _remoteScanText = null;
                UpdateLibraryActivityIndicator();
            }
            return;
        }

        _remoteScanPollInProgress = true;
        try
        {
            string? scanning = null;
            foreach (var server in servers)
            {
                try
                {
                    var status = await _orynivoClient.GetScanStatusAsync(server, CancellationToken.None);
                    if (status?.LibraryChangedAt is long libraryVersion)
                    {
                        if (_dashboardRemoteLibraryVersions.TryGetValue(server.Id, out var previousVersion) &&
                            previousVersion != libraryVersion)
                        {
                            InvalidateDashboardCatalogCache();
                            InvalidateGenreCloudViewCache();
                            InvalidateUnifiedLibraryViewCache();
                        }
                        _dashboardRemoteLibraryVersions[server.Id] = libraryVersion;
                    }
                    if (status is { IsRunning: true })
                    {
                        scanning = status is { Total: > 0, Current: > 0 }
                            ? string.Format(
                                LocalizationManager.Current.RemoteScanningWithCount,
                                server.Name, status.Current, status.Total)
                            : string.Format(LocalizationManager.Current.RemoteScanning, server.Name);
                        break;
                    }
                }
                catch
                {
                    // Unreachable or older servers are skipped silently.
                }
            }

            _remoteScanText = scanning;
            UpdateLibraryActivityIndicator();
        }
        finally
        {
            _remoteScanPollInProgress = false;
        }
    }

    /// <summary>Shows or hides the per-view "new library data available" refresh action.</summary>
    /// <param name="available">Whether fresh library data is available for the current view.</param>
    private void SetLibraryRefreshAvailable(bool available)
    {
        _libraryRefreshAvailable = available;
        LibraryRefreshButton.IsVisible = available;
    }

    /// <summary>Reloads the current view on demand when the user accepts the refresh prompt.</summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The click event data.</param>
    private async void LibraryRefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetLibraryRefreshAvailable(false);
        if (_currentTopLevelTag is { } tag && CanReloadCurrentViewAfterLibraryChange())
            await ShowTopLevelViewAsync(tag);
    }

    private bool CanReloadCurrentViewAfterLibraryChange()
    {
        if (_currentTopLevelTag is not ("Artists" or "Albums" or "Tracks" or "Folders"))
            return false;
        if (_activeAlbumFilterId is not null ||
            _activeArtistFilterId is not null ||
            _activePlaylistId is not null ||
            _activeOrynivoPlaylistId is not null ||
            _activeAlbumCatalogProvider is not null ||
            _activeCatalogAlbum is not null ||
            LyricsView.IsVisible ||
            ArtistInfoView.IsVisible ||
            PodcastInfoView.IsVisible ||
            SearchResultsScrollViewer.IsVisible)
        {
            return false;
        }

        return true;
    }

    private void RestoreLastTrackState()
    {
        var path = _settings.LastTrackPath;
        if (string.IsNullOrWhiteSpace(path) || !IsAvailableLocalTrack(path))
            return;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            if (track is null)
                return;

            _currentFilePath = path;
            NowPlayingTitleBlock.Text = track.Title ?? Path.GetFileNameWithoutExtension(path);
            NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
            var navigationIds = db.GetTrackNavigationIds(path);
            SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
            var artworkPaths = db.GetArtworkPathsByTrackPath(path);
            NowPlayingArtworkImage.Source = CreateArtworkImage(artworkPaths?.Thumb96Path, 96);
            LyricsBackgroundImage.Source = CreateArtworkImage(
                artworkPaths?.Thumb320Path ?? artworkPaths?.OriginalPath,
                900);
            var trackInfo = db.GetTrackIdAndFavorite(path);
            var artist = db.GetArtistByTrackPath(path);
            _currentTrackId = trackInfo?.Id;
            _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
            _currentArtistId = artist?.Id;
            _currentArtistName = artist?.Artist;
            _currentAlbumId = navigationIds.AlbumId;
            NowPlayingArtistButton.IsEnabled = artist is not null;
            ArtistInfoButton.IsEnabled = artist is not null;
            UpdateNowPlayingFavoriteButton();
            LyricsButton.IsEnabled = true;
            _ = LoadLyricsForTrackAsync(path, forceRefresh: false);
            PlayButton.IsEnabled = true;
            SetPlayPauseIcon(isPlaying: false);
        }
        catch
        {
            _currentFilePath = string.Empty;
            NowPlayingTitleBlock.Text = string.Empty;
            NowPlayingArtistBlock.Text = string.Empty;
            ClearNowPlayingAlbum();
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            _currentTrackIsFavorite = false;
            UpdateNowPlayingFavoriteButton();
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
    }

    private void RefreshSelectedDriverText()
    {
        SelectedDriverTextBlock.Text = _settings.OutputBackend switch
        {
            OutputBackend.Asio when !string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                $"{_settings.SelectedDriverName}  ·  Steinberg ASIO",
            OutputBackend.CwAsio when !string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                $"{_settings.SelectedDriverName}  ·  cwASIO",
            OutputBackend.Wasapi when !string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceName) =>
                $"{_settings.SelectedWasapiDeviceName}  ·  {(!OperatingSystem.IsWindows()
                    ? _settings.SelectedWasapiDeviceId?.StartsWith("alsa:", StringComparison.Ordinal) == true
                        ? LocalizationManager.Current.DirectAlsa
                        : LocalizationManager.Current.OpenAl
                    : "WASAPI")}",
            OutputBackend.AirPlay when !string.IsNullOrWhiteSpace(_settings.SelectedAirPlayDeviceName) =>
                $"{_settings.SelectedAirPlayDeviceName}  ·  {LocalizationManager.Current.AirPlay}",
            OutputBackend.KernelStreaming => "KernelStreaming",
            _ => LocalizationManager.Current.NoDeviceSelected
        };
    }
}
