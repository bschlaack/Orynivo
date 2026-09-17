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
/// Double-click navigation, entity links, and now-playing cover actions.
/// </summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Content-Doppelklick → Wiedergabe
    // ------------------------------------------------------------------

    private async void ContentDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (sender is not DataGrid grid ||
            !TryGetDoubleTappedRow<ContentRow>(grid, e, out var row))
            return;

        if (row.EntityType is "Track" or "OrynivoTrack" &&
            grid.ItemsSource is IEnumerable<ContentRow> rows)
        {
            // Build the queue from the grid that was actually double-clicked so
            // an album directory group ("CD1"/"CD2") queues exactly its own,
            // correctly ordered rows. Remote ("OrynivoTrack") rows must take
            // this path too; otherwise the queue would be rebuilt from the
            // hidden ContentDataGrid, whose source is the raw, ungrouped album
            // track list (interleaved across directories when disc numbers are
            // missing).
            var contextRows = IsUnfilteredTopLevelTracksView() ? [row] : rows.ToList();
            await PlayTrackFromRowsAsync(row, contextRows);
            return;
        }

        await HandleContentRowDoubleClickAsync(row);
    }

    private bool IsUnfilteredTopLevelTracksView() =>
        _currentTopLevelTag == "Tracks" &&
        _activePlaylistId is null &&
        _activeAlbumFilterId is null &&
        _activeArtistFilterId is null &&
        !HasActiveFilters;

    private async void AlbumArtworkListBox_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (AlbumArtworkListBox.SelectedItem is not ContentRow row)
            return;
        await HandleContentRowDoubleClickAsync(row);
    }

    private async void ArtistArtworkListBox_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (ArtistArtworkListBox.SelectedItem is ContentRow row)
            await HandleContentRowDoubleClickAsync(row);
    }

    private async void SearchTracksDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (!TryGetDoubleTappedRow<ContentRow>(SearchTracksDataGrid, e, out var row))
            return;

        var allRows = (SearchTracksDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    private async void SearchAlbumsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (!TryGetDoubleTappedRow<ContentRow>(SearchAlbumsDataGrid, e, out var row) ||
            row.Id is not long albumId)
            return;

        // Remote albums must open within the remote library; their IDs can collide
        // with local album IDs, so never route them through the local album view.
        if (row.EntityType == "OrynivoAlbum")
        {
            // Search has no artist context; clear any stale artist filter so the
            // album shows all of its tracks.
            _activeArtistFilterId = null;
            _activeArtistFilterName = null;
            ActivateRowOrynivoServer(row);
            await OpenOrynivoAlbumTracksAsync(row);
            return;
        }

        await ShowAlbumTracksAsync(albumId, row.Title ?? LocalizationManager.Current.Unknown);
    }

    private async void SearchArtistsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (!TryGetDoubleTappedRow<ContentRow>(SearchArtistsDataGrid, e, out var row) ||
            row.Id is not long artistId)
            return;

        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        // Remote artists must open within the remote library (IDs can collide).
        if (row.EntityType == "OrynivoArtist")
        {
            ActivateRowOrynivoServer(row);
            await OpenOrynivoArtistAlbumsAsync(artistId, row.Title);
            return;
        }

        await ShowArtistAlbumsAsync(artistId, row.Title ?? LocalizationManager.Current.Unknown);
    }

    private static bool TryGetDoubleTappedRow<T>(DataGrid grid, Avalonia.Input.TappedEventArgs e, out T row)
        where T : class
    {
        row = null!;
        if (FindAncestor<DataGridRow>(e.Source as Visual) is { DataContext: T sourceRow })
        {
            grid.SelectedItem = sourceRow;
            row = sourceRow;
            return true;
        }

        if (grid.SelectedItem is T selectedRow)
        {
            row = selectedRow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Activates the remote server carried by a self-describing row so the shared
    /// album/artist/favorite/cover handlers operate on the row's own server even
    /// when opened outside a server view (e.g. the dashboard's mixed recent list).
    /// Rows without a server (local, or normal server-view rows) leave the ambient
    /// server unchanged.
    /// </summary>
    /// <param name="row">The row whose server context should be activated.</param>
    private void ActivateRowOrynivoServer(ContentRow row)
    {
        if (row.OrynivoServer is { } server)
            _activeOrynivoServer = server;
    }

    /// <summary>Resolves the Orynivo Server context for a remote row and activates it.</summary>
    /// <param name="row">Remote row carrying the server context when it came from a mixed view.</param>
    /// <returns>The row server, or the current ambient server as a fallback.</returns>
    private OrynivoServerSettings? ResolveRowOrynivoServer(ContentRow row)
    {
        if (row.OrynivoServer is { } server)
        {
            _activeOrynivoServer = server;
            return server;
        }

        return _activeOrynivoServer;
    }

    private async void ArtistLinkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        e.Handled = true;

        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal))
        {
            ActivateRowOrynivoServer(row);
            var remoteArtistId = row.EntityType == "OrynivoArtist" ? row.Id : row.ArtistId;
            if (remoteArtistId is long remoteId)
                await OpenOrynivoArtistAlbumsAsync(remoteId, row.EntityType == "OrynivoArtist" ? row.Title : row.Artist);
            return;
        }

        var artistId = row.ArtistId;
        if (artistId is null)
        {
            using var db = AudioDatabase.OpenDefault();
            if (row.EntityType == "Artist")
                artistId = row.Id;
            else if ((row.AlbumId ?? (row.EntityType == "Album" ? row.Id : null)) is long albumId)
                artistId = db.GetAlbumArtistId(albumId);
            else if (!string.IsNullOrWhiteSpace(row.FilePath))
                artistId = db.GetTrackNavigationIds(row.FilePath).ArtistId;
            row.ArtistId = artistId;
        }

        if (artistId is not long id)
            return;

        var artistName = row.EntityType == "Artist" ? row.Title : row.Artist;
        await ShowArtistAlbumsAsync(id, artistName ?? LocalizationManager.Current.Unknown);
    }

    private async void AlbumLinkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        e.Handled = true;

        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal))
        {
            ActivateRowOrynivoServer(row);
            var remoteAlbumId = row.EntityType == "OrynivoAlbum" ? row.Id : row.AlbumId;
            if (remoteAlbumId is long remoteId)
            {
                if (row.EntityType == "OrynivoAlbum")
                    await OpenOrynivoAlbumTracksAsync(row);
                else
                    await OpenOrynivoAlbumTracksAsync(remoteId, row.Album, row.Artist);
            }
            return;
        }

        if (row.EntityType == "UnifiedAlbum")
        {
            await OpenLogicalAlbumTracksAsync(row);
            return;
        }

        var albumId = row.AlbumId ?? (row.EntityType == "Album" ? row.Id : null);
        if (albumId is null && !string.IsNullOrWhiteSpace(row.FilePath))
        {
            using var db = AudioDatabase.OpenDefault();
            albumId = db.GetTrackNavigationIds(row.FilePath).AlbumId;
            row.AlbumId = albumId;
        }

        if (albumId is not long id)
            return;

        var albumTitle = row.EntityType == "Album" ? row.Title : row.Album;
        var (artistFilterId, artistFilterName) = GetAlbumArtistScope(row);
        await ShowAlbumTracksAsync(
            id,
            albumTitle ?? LocalizationManager.Current.Unknown,
            artistFilterId,
            artistFilterName,
            row.LogicalAlbumIds);
    }

    private async void NowPlayingArtistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await OpenNowPlayingArtistAsync();
    }

    private async void NowPlayingAlbumButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        await OpenNowPlayingAlbumAsync();
    }

    private async void NowPlayingCoverOpenAlbumMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await OpenNowPlayingAlbumAsync();
    }

    private async void NowPlayingCoverOpenArtistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await OpenNowPlayingArtistAsync();
    }

    private async void NowPlayingCoverSearchMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SearchNowPlayingCoverAsync();
    }

    private void NowPlayingCoverFavoriteMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        NowPlayingFavoriteButton_OnClick(sender, e);
    }

    private async Task OpenNowPlayingAlbumAsync()
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, AlbumId: long remoteAlbumId })
        {
            _activeOrynivoServer = server;
            await OpenOrynivoAlbumTracksAsync(
                remoteAlbumId,
                _currentOrynivoTrackRow.Album ?? _currentAlbumTitle,
                _currentOrynivoTrackRow.Artist);
            return;
        }

        if (_currentAlbumId is long albumId)
            await ShowAlbumTracksAsync(
                albumId,
                _currentAlbumTitle ?? LocalizationManager.Current.Unknown);
    }

    private async Task OpenNowPlayingArtistAsync()
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, ArtistId: long remoteArtistId })
        {
            _activeOrynivoServer = server;
            await OpenOrynivoArtistAlbumsAsync(remoteArtistId, _currentArtistName);
            return;
        }

        if (_currentArtistId is long artistId)
            await ShowArtistAlbumsAsync(
                artistId,
                _currentArtistName ?? LocalizationManager.Current.Unknown);
    }

    private async Task SearchNowPlayingCoverAsync()
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, AlbumId: long remoteAlbumId })
        {
            var row = new ContentRow
            {
                Id = remoteAlbumId,
                Title = _currentOrynivoTrackRow.Album ?? _currentAlbumTitle ?? LocalizationManager.Current.Unknown,
                Artist = _currentOrynivoTrackRow.Artist,
                AlbumArtist = _currentOrynivoTrackRow.AlbumArtist,
                EntityType = "OrynivoAlbum",
                ExternalId = remoteAlbumId.ToString(CultureInfo.InvariantCulture),
                OrynivoServer = server
            };
            await OpenOrynivoAlbumCoverSearchAsync(server, remoteAlbumId, row);
            NowPlayingArtworkImage.Source = row.Thumbnail ?? row.Artwork ?? NowPlayingArtworkImage.Source;
            LyricsBackgroundImage.Source = row.Artwork ?? row.Thumbnail ?? LyricsBackgroundImage.Source;
            return;
        }

        if (_currentAlbumId is not long albumId)
            return;

        var localRow = new ContentRow
        {
            Id = albumId,
            Title = _currentAlbumTitle ?? LocalizationManager.Current.Unknown,
            Artist = _currentArtistName,
            EntityType = "Album"
        };
        await OpenCoverSearchAsync(localRow);
        NowPlayingArtworkImage.Source = localRow.Thumbnail ?? localRow.Artwork ?? NowPlayingArtworkImage.Source;
        LyricsBackgroundImage.Source = localRow.Artwork ?? localRow.Thumbnail ?? LyricsBackgroundImage.Source;
    }

    private void SetNowPlayingAlbum(string? albumTitle, long? albumId, bool canNavigate)
    {
        _currentAlbumId = albumId;
        _currentAlbumTitle = string.IsNullOrWhiteSpace(albumTitle) ? null : albumTitle;
        NowPlayingAlbumBlock.Text = _currentAlbumTitle ?? string.Empty;
        NowPlayingAlbumButton.IsVisible = !string.IsNullOrWhiteSpace(_currentAlbumTitle);
        NowPlayingAlbumButton.IsEnabled = canNavigate && albumId is not null;
    }

    private void ClearNowPlayingAlbum() => SetNowPlayingAlbum(null, null, false);

    private async Task HandleContentRowDoubleClickAsync(ContentRow row)
    {
        if (row.QueueItem is not null && _currentTopLevelTag == "Queue")
        {
            var queueIndex = IndexOfQueueItem(row.QueueItem);
            if (queueIndex < 0)
                return;
            _queueIndex = queueIndex;
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueNavigationButtons();
            try { await StartPlaybackAsync(row.FilePath); }
            catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
            catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
            return;
        }

        if (row.EntityType is "PlexArtist" or "PlexAlbum")
        {
            await ShowPlexChildrenAsync(row);
            return;
        }

        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        if (row.EntityType == "OrynivoArtist" && row.Id is long orynivoArtistId)
        {
            // In the unified Artists view no server is "active"; navigate within the
            // row's own server (IDs can collide between local and remote libraries).
            ActivateRowOrynivoServer(row);
            await OpenOrynivoArtistAlbumsAsync(orynivoArtistId, row.Title);
            return;
        }

        if (row.EntityType == "OrynivoAlbum" && row.Id is long orynivoAlbumId)
        {
            ActivateRowOrynivoServer(row);
            await OpenOrynivoAlbumTracksAsync(row);
            return;
        }

        if (row.EntityType == "Artist" && row.Id is long artistId)
        {
            await ShowArtistAlbumsAsync(artistId, row.Title ?? "(Unbekannt)");
            return;
        }

        if ((AlbumViewModeBorder.IsVisible ||
             string.Equals(_currentTopLevelTag, "GenreCloud", StringComparison.Ordinal)) &&
            row.EntityType is "Album" or null &&
            (row.AlbumId ?? row.Id) is long albumId)
        {
            var (artistFilterId, artistFilterName) = GetAlbumArtistScope(row);
            await ShowAlbumTracksAsync(
                albumId,
                row.Title ?? "(Unbekannt)",
                artistFilterId,
                artistFilterName,
                row.LogicalAlbumIds);
            return;
        }

        if (row.EntityType == "UnifiedAlbum")
        {
            await OpenLogicalAlbumTracksAsync(row);
            return;
        }

        if (string.IsNullOrEmpty(row.FilePath))
            return;

        var allRows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }
}
