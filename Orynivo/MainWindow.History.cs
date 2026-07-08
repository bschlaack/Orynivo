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
    private async Task ShowDailyHistoryAsync(DateTime date)
    {
        var entries = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetHistoryForDay(date);
        });

        var dialog = new DailyHistoryDialog(date, entries, _settings);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedEntry is not { } entry)
            return;

        switch (dialog.SelectedAction)
        {
            case DailyHistoryAction.Track:
                await OpenHistoryTrackAsync(entry);
                break;
            case DailyHistoryAction.Album:
                await OpenHistoryAlbumAsync(entry);
                break;
            case DailyHistoryAction.Artist when entry.ArtistId is long artistId:
                await ShowArtistAlbumsAsync(
                    artistId,
                    entry.Artist ?? LocalizationManager.Current.Unknown);
                break;
            case DailyHistoryAction.Artist:
                await OpenHistoryArtistAsync(entry);
                break;
        }
    }

    private async Task OpenHistoryAlbumAsync(DailyHistoryEntry entry)
    {
        if (entry.AlbumId is long localAlbumId)
        {
            await ShowAlbumTracksAsync(
                localAlbumId,
                entry.Album ?? LocalizationManager.Current.Unknown);
            return;
        }

        if (TryGetPlexHistoryTarget(entry, out var plexServer, out var plexToken, out _, out var plexAlbumKey, out _)
            && !string.IsNullOrWhiteSpace(plexAlbumKey))
        {
            await OpenPlexAlbumFromHistoryAsync(plexServer, plexToken, plexAlbumKey, entry.Album, entry.Artist);
            return;
        }

        var remoteRow = await ResolveOrynivoHistoryTrackRowAsync(entry);
        if (remoteRow is not { OrynivoServer: { } server, AlbumId: long remoteAlbumId })
            return;

        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
        _activeOrynivoServer = server;
        await OpenOrynivoAlbumTracksAsync(
            remoteAlbumId,
            remoteRow.Album ?? entry.Album ?? LocalizationManager.Current.Unknown,
            remoteRow.AlbumArtist ?? remoteRow.Artist ?? entry.Artist);
    }

    private async Task OpenHistoryTrackAsync(DailyHistoryEntry entry)
    {
        if (entry.TrackId is null || !IsAvailableLocalTrack(entry.Path))
            return;

        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = "Tracks";

        var tracksItem = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Tag as string, "Tracks", StringComparison.Ordinal));
        if (tracksItem is not null && !ReferenceEquals(NavListBox.SelectedItem, tracksItem))
        {
            _suppressNavSelectionChanged = true;
            try
            {
                NavListBox.SelectedItem = tracksItem;
            }
            finally
            {
                _suppressNavSelectionChanged = false;
            }
        }

        await ShowTopLevelViewAsync("Tracks");
        var rows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;

        ContentDataGrid.SelectedItem = row;
        ContentDataGrid.ScrollIntoView(row, null);
        await PlayTrackFromRowsAsync(row, rows);
    }

    private static bool IsPlayableHistoryEntry(DailyHistoryEntry entry) =>
        string.Equals(entry.MediaType, "track", StringComparison.OrdinalIgnoreCase) &&
        (IsAvailableLocalTrack(entry.Path) || IsHttpUrl(entry.Path));

    private bool CanOpenHistoryArtist(DailyHistoryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Artist) &&
        (entry.ArtistId.HasValue ||
         TryGetOrynivoHistoryTarget(entry, out _, out _) ||
         (TryGetPlexHistoryTarget(entry, out _, out _, out _, out _, out var plexArtistKey) &&
          !string.IsNullOrWhiteSpace(plexArtistKey)));

    private async Task OpenHistoryArtistAsync(DailyHistoryEntry entry)
    {
        if (entry.ArtistId is long localArtistId)
        {
            await ShowArtistAlbumsAsync(
                localArtistId,
                entry.Artist ?? LocalizationManager.Current.Unknown);
            return;
        }

        if (TryGetPlexHistoryTarget(entry, out var plexServer, out var plexToken, out _, out _, out var plexArtistKey)
            && !string.IsNullOrWhiteSpace(plexArtistKey))
        {
            await OpenPlexArtistFromHistoryAsync(plexServer, plexToken, plexArtistKey, entry.Artist);
            return;
        }

        var remoteRow = await ResolveOrynivoHistoryTrackRowAsync(entry);
        if (remoteRow is not { OrynivoServer: { } server, ArtistId: long remoteArtistId })
            return;

        _activeOrynivoServer = server;
        await OpenOrynivoArtistAlbumsAsync(
            remoteArtistId,
            remoteRow.Artist ?? entry.Artist ?? LocalizationManager.Current.Unknown);
    }

    private async Task PlayHistoryEntryInPlaceAsync(DailyHistoryEntry entry)
    {
        if (!IsPlayableHistoryEntry(entry))
            return;

        var remoteRow = await ResolveOrynivoHistoryTrackRowAsync(entry);
        var queueItem = remoteRow is not null
            ? ToPlaylistItem(remoteRow)
            : new PlaylistItem(
                entry.Path,
                string.IsNullOrWhiteSpace(entry.Title) ? null : entry.Title,
                string.IsNullOrWhiteSpace(entry.Artist) ? null : entry.Artist,
                string.IsNullOrWhiteSpace(entry.Album) ? null : entry.Album,
                null,
                null,
                entry.DurationSeconds is > 0 ? TimeSpan.FromSeconds(entry.DurationSeconds.Value) : null);

        _queue.Clear();
        _queue.Add(queueItem);
        _queueIndex = 0;
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(queueItem.FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
        UpdateNowPlayingRowHighlights();
    }

    private async Task<ContentRow?> ResolveOrynivoHistoryTrackRowAsync(DailyHistoryEntry entry)
    {
        if (!TryGetOrynivoHistoryTarget(entry, out var server, out var trackId))
            return null;

        var streamUrl = OrynivoServerClient.GetStreamUrl(server, trackId);
        if (_orynivoTracksByUrl.TryGetValue(streamUrl, out var cached))
            return cached;

        var tracks = await _orynivoClient.GetTracksByIdsAsync(server, [trackId], CancellationToken.None);
        var track = tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null)
            return null;

        var row = ToOrynivoTrackContentRow(server, track);
        EnsureArtworkHydrated(row);
        return row;
    }

    /// <summary>
    /// Resolves a configured Plex server, access token, and the stored track/album/artist
    /// rating keys from a playback-history entry that carries a stable Plex context.
    /// </summary>
    /// <param name="entry">The playback-history entry to inspect.</param>
    /// <param name="server">The resolved Plex server settings.</param>
    /// <param name="token">The resolved access token, or <see langword="null"/> when unavailable.</param>
    /// <param name="trackKey">The Plex track rating key.</param>
    /// <param name="albumKey">The Plex album (parent) rating key, or an empty string.</param>
    /// <param name="artistKey">The Plex artist (grandparent) rating key, or an empty string.</param>
    /// <returns><see langword="true"/> when the entry maps to a configured Plex server.</returns>
    private bool TryGetPlexHistoryTarget(
        DailyHistoryEntry entry,
        out PlexServerSettings server,
        out string? token,
        out string trackKey,
        out string albumKey,
        out string artistKey)
    {
        server = null!;
        token = null;
        if (!TryParsePlexHistoryExternalId(entry.ExternalId, out var serverId, out trackKey, out albumKey, out artistKey))
            return false;

        var match = (_settings.PlexServers ?? [])
            .FirstOrDefault(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
        if (match is null)
            return false;

        server = match;
        try { token = new WindowsPlexCredentialStore().LoadAll().GetValueOrDefault(match.Id); }
        catch { token = null; }
        return true;
    }

    /// <summary>Parses a Plex track identifier stored in playback history.</summary>
    /// <param name="externalId">Stored history external identifier.</param>
    /// <param name="serverId">Parsed Plex server identifier.</param>
    /// <param name="trackKey">Parsed Plex track rating key.</param>
    /// <param name="albumKey">Parsed Plex album (parent) rating key, or an empty string.</param>
    /// <param name="artistKey">Parsed Plex artist (grandparent) rating key, or an empty string.</param>
    /// <returns><see langword="true"/> when the identifier is a valid Plex context.</returns>
    private static bool TryParsePlexHistoryExternalId(
        string? externalId,
        out string serverId,
        out string trackKey,
        out string albumKey,
        out string artistKey)
    {
        serverId = trackKey = albumKey = artistKey = string.Empty;
        if (string.IsNullOrWhiteSpace(externalId))
            return false;

        // plex:{serverId}:{trackKey}:{albumKey}:{artistKey}; album/artist keys may be empty.
        var parts = externalId.Split(':');
        if (parts.Length < 3 || !string.Equals(parts[0], "plex", StringComparison.OrdinalIgnoreCase))
            return false;

        serverId = parts[1];
        trackKey = parts[2];
        albumKey = parts.Length > 3 ? parts[3] : string.Empty;
        artistKey = parts.Length > 4 ? parts[4] : string.Empty;
        return !string.IsNullOrWhiteSpace(serverId) && !string.IsNullOrWhiteSpace(trackKey);
    }

    /// <summary>Opens a Plex album's tracks from a playback-history entry, staying in the Plex library.</summary>
    /// <param name="server">The Plex server owning the album.</param>
    /// <param name="token">The Plex access token, or <see langword="null"/>.</param>
    /// <param name="albumKey">The Plex album rating key.</param>
    /// <param name="title">The album title used as a fallback header.</param>
    /// <param name="artist">The album artist used as a fallback subtitle.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task OpenPlexAlbumFromHistoryAsync(
        PlexServerSettings server,
        string? token,
        string albumKey,
        string? title,
        string? artist)
    {
        var parent = new ContentRow
        {
            EntityType = "PlexAlbum",
            ExternalId = albumKey,
            Title = title,
            Artist = artist
        };
        await OpenPlexChildrenFromHistoryAsync(server, token, parent);
    }

    /// <summary>Opens a Plex artist's albums from a playback-history entry, staying in the Plex library.</summary>
    /// <param name="server">The Plex server owning the artist.</param>
    /// <param name="token">The Plex access token, or <see langword="null"/>.</param>
    /// <param name="artistKey">The Plex artist rating key.</param>
    /// <param name="title">The artist name used as a fallback header.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task OpenPlexArtistFromHistoryAsync(
        PlexServerSettings server,
        string? token,
        string artistKey,
        string? title)
    {
        var parent = new ContentRow
        {
            EntityType = "PlexArtist",
            ExternalId = artistKey,
            Title = title
        };
        await OpenPlexChildrenFromHistoryAsync(server, token, parent);
    }

    /// <summary>
    /// Switches the main content area to the Plex table and loads the children of a
    /// synthesized Plex album/artist parent row, pushing the current view onto the global
    /// navigation stack so Back returns to it.
    /// </summary>
    /// <param name="server">The Plex server to activate.</param>
    /// <param name="token">The Plex access token, or <see langword="null"/>.</param>
    /// <param name="parent">The synthesized parent row (album or artist) to expand.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task OpenPlexChildrenFromHistoryAsync(
        PlexServerSettings server,
        string? token,
        ContentRow parent)
    {
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);

        _activePlexServer = server;
        _activePlexToken = token;
        _activePlexSectionTitle = server.Name;
        _plexNavigationStack.Clear();

        // Reveal the Plex table and hide every other full-area view, mirroring the
        // remote-library entry path so the previous view does not cover Plex content.
        ContentDataGrid.IsVisible = true;
        ContentDataGrid.ItemsSource = null;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        PlexLoadMoreButton.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        SaveSmartPlaylistButton.IsVisible = false;
        HideAlbumDetailHeader();
        UpdateLibraryIntroCard(null);

        await ShowPlexChildrenAsync(parent);

        // Base level: further intra-Plex drill-downs push their own state, but Back from
        // this first level should return to the originating view via the global stack.
        _plexNavigationStack.Clear();
    }
}



