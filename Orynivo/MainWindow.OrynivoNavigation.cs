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
/// Remote Orynivo Server album and artist drill-down plus queue item helpers.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Opens every matching local and Orynivo Server album for a remote artist name.</summary>
    /// <param name="artistId">Remote server artist identifier.</param>
    /// <param name="title">Artist display name for the header.</param>
    private Task OpenOrynivoArtistAlbumsAsync(long artistId, string? title) =>
        ShowUnifiedArtistAlbumsAsync(title ?? LocalizationManager.Current.Unknown);

    /// <summary>Opens the tracks of a remote server album, pushing navigation state.</summary>
    /// <param name="albumId">Remote server album identifier.</param>
    /// <param name="title">Album title for the header.</param>
    /// <param name="artist">Album artist for the header, or <see langword="null"/>.</param>
    /// <param name="artistFilterId">Optional provider-local artist scope applied initially.</param>
    /// <param name="artistFilterName">Optional artist name shown for the active scope.</param>
    private async Task OpenOrynivoAlbumTracksAsync(
        long albumId,
        string? title,
        string? artist,
        long? artistFilterId = null,
        string? artistFilterName = null)
    {
        PushCurrentNavigationState();
        _orynivoNavigationStack.Push((_activeOrynivoView, null, null));
        BackButton.IsVisible = true;
        if (_activeOrynivoServer is null)
            return;

        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var album = await provider.GetAlbumAsync(albumId, includeArtwork: true)
                    ?? new LibraryCatalogAlbum(
                        LibraryCatalogSource.OrynivoServer,
                        albumId,
                        title ?? LocalizationManager.Current.Unknown,
                        artist,
                        null,
                        null,
                        null,
                        IsOrynivoFavorite(_activeOrynivoServer, "Album", albumId));
        await ShowProviderAlbumTracksAsync(
            provider,
            album,
            artistFilterId ?? _activeArtistFilterId,
            artistFilterName ?? _activeArtistFilterName);
    }

    private async Task OpenOrynivoAlbumTracksAsync(ContentRow row)
    {
        if (row.Id is not long albumId)
            return;

        var (artistFilterId, artistFilterName) = GetAlbumArtistScope(row);
        PushCurrentNavigationState();
        _orynivoNavigationStack.Push((_activeOrynivoView, null, null));
        BackButton.IsVisible = true;
        if (_activeOrynivoServer is null)
            return;

        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var album = await provider.GetAlbumAsync(albumId, includeArtwork: true)
                    ?? new LibraryCatalogAlbum(
                        LibraryCatalogSource.OrynivoServer,
                        albumId,
                        row.Title ?? LocalizationManager.Current.Unknown,
                        row.Artist,
                        int.TryParse(row.Year, NumberStyles.Integer, CultureInfo.CurrentCulture, out var year) ? year : null,
                        row.ArtworkPath,
                        row.ThumbnailPath,
                        row.IsFavorite,
                        row.ArtistId);
        await ShowProviderAlbumTracksAsync(
            provider,
            album,
            artistFilterId,
            artistFilterName,
            row.LogicalAlbumIds);
    }

    /// <summary>Deletes the cached remote album list after album artwork metadata changes.</summary>
    /// <param name="server">Server whose album list cache should be removed.</param>
    internal static void DeleteOrynivoAlbumListCache(OrynivoServerSettings server)
    {
        try
        {
            var path = GetOrynivoAlbumListCachePath(server);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Resolves the provider-local artist scope to retain when an album is opened
    /// from the unified artist album list.
    /// </summary>
    /// <param name="row">The selected album row.</param>
    /// <returns>
    /// The artist identifier and display name used to initially filter the album,
    /// or the ambient scope when the album was opened from another view.
    /// </returns>
    private (long? ArtistId, string? ArtistName) GetAlbumArtistScope(ContentRow row)
    {
        var isUnifiedArtistAlbumList =
            _activeAlbumFilterId is null &&
            _activeArtistFilterId is null &&
            !string.IsNullOrWhiteSpace(_activeArtistFilterName) &&
            (string.Equals(_currentTopLevelTag, "Albums", StringComparison.Ordinal) ||
             string.Equals(_currentTopLevelTag, "UnifiedArtistAlbums", StringComparison.Ordinal));

        return isUnifiedArtistAlbumList
            ? (row.ArtistId, _activeArtistFilterName)
            : (_activeArtistFilterId, _activeArtistFilterName);
    }

    private async Task RestoreOrynivoAlbumTracksAsync(
        string? navigationTag,
        long albumId,
        string? albumTitle,
        long? artistFilterId,
        string? artistFilterName,
        double? verticalOffset,
        IReadOnlyList<long>? logicalAlbumIds = null)
    {
        if (string.IsNullOrWhiteSpace(navigationTag))
            return;

        SelectNavigationItem(navigationTag);
        await ShowTopLevelViewAsync(navigationTag);
        if (_activeOrynivoServer is null)
            return;

        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var album = await provider.GetAlbumAsync(albumId, includeArtwork: true)
                    ?? new LibraryCatalogAlbum(
                        LibraryCatalogSource.OrynivoServer,
                        albumId,
                        albumTitle ?? LocalizationManager.Current.Unknown,
                        null,
                        null,
                        null,
                        null,
                        IsOrynivoFavorite(_activeOrynivoServer, "Album", albumId));
        await ShowProviderAlbumTracksAsync(
            provider,
            album,
            artistFilterId,
            artistFilterName,
            logicalAlbumIds);
        RestoreSelectionFromCurrentItems(null, verticalOffset);
    }

    /// <summary>Restores the filtered album list for a remote server artist.</summary>
    /// <param name="navigationTag">Remote server navigation tag to restore.</param>
    /// <param name="artistId">Remote server artist identifier.</param>
    /// <param name="artistName">Artist display name for the header.</param>
    /// <param name="selectedAlbumId">Album identifier to reselect.</param>
    /// <param name="verticalOffset">Optional vertical scroll offset to restore.</param>
    private async Task RestoreOrynivoArtistAlbumsAsync(
        string? navigationTag,
        long artistId,
        string? artistName,
        long? selectedAlbumId,
        double? verticalOffset)
    {
        if (string.IsNullOrWhiteSpace(navigationTag))
            return;

        SelectNavigationItem(navigationTag);
        await ShowTopLevelViewAsync(navigationTag);
        if (_activeOrynivoServer is null)
            return;

        _currentTopLevelTag = navigationTag;
        _activeOrynivoView = "Albums";
        _activeArtistFilterId = artistId;
        _activeArtistFilterName = artistName;
        ContentTitleTextBlock.Text = $"{_activeOrynivoServer.Name} · {artistName}";
        await LoadOrynivoViewAsync(filterArtistId: artistId);
        RestoreSelectionFromCurrentItems(selectedAlbumId, verticalOffset);
    }

    private async Task PlayTrackFromRowsAsync(ContentRow row, List<ContentRow> allRows)
    {
        if (string.IsNullOrEmpty(row.FilePath))
            return;

        StopInfiniteMix();
        _queue.Clear();
        foreach (var r in allRows.Where(r => !string.IsNullOrEmpty(r.FilePath)))
            _queue.Add(ToPlaylistItem(r));

        _queueIndex = _queue.IndexOf(_queue.FirstOrDefault(p => p.FilePath == row.FilePath) ?? _queue[0]);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(row.FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private static PlaylistItem ToPlaylistItem(ContentRow row) =>
        new(
            row.FilePath,
            string.IsNullOrWhiteSpace(row.Title) ? null : row.Title,
            string.IsNullOrWhiteSpace(row.Artist) ? null : row.Artist,
            string.IsNullOrWhiteSpace(row.Album) ? null : row.Album,
            string.IsNullOrWhiteSpace(row.Duration) ? null : row.Duration,
            string.IsNullOrWhiteSpace(row.Format) ? null : row.Format,
            row.KnownDuration);

    private PlaylistItem CreatePlaylistItem(string path)
    {
        if (_orynivoTracksByUrl.TryGetValue(path, out var orynivoRow))
            return ToPlaylistItem(orynivoRow);
        if (_plexTracksByUrl.TryGetValue(path, out var plexRow))
            return ToPlaylistItem(plexRow);
        // Never perform a synchronous provider/network lookup while building a
        // queue. Missing metadata is optional; playback resolves the source
        // asynchronously and the queue item can be enriched later.
        return new PlaylistItem(path);
    }

    private PlaylistItem? GetPlaylistMetadata(string path)
    {
        var queueItem = _queue.FirstOrDefault(item =>
            string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (queueItem is not null &&
            (!string.IsNullOrWhiteSpace(queueItem.Title) ||
             !string.IsNullOrWhiteSpace(queueItem.Artist) ||
             !string.IsNullOrWhiteSpace(queueItem.Album)))
        {
            return queueItem;
        }

        if (_orynivoTracksByUrl.TryGetValue(path, out var orynivoRow))
            return ToPlaylistItem(orynivoRow);
        if (TryResolveOrynivoPlaylistReferenceRow(path, out var referencedRow))
            return ToPlaylistItem(referencedRow);
        if (_plexTracksByUrl.TryGetValue(path, out var plexRow))
            return ToPlaylistItem(plexRow);
        return null;
    }

    private bool TryResolveOrynivoPlaylistReferenceRow(string path, out ContentRow row)
    {
        row = null!;
        if (!TryResolveOrynivoPlaylistReference(path, out var server, out var trackId))
            return false;

        try
        {
            var provider = CreateOrynivoCatalogProvider(server);
            var remoteTrack = provider.GetTracksByIdsAsync([trackId])
                .GetAwaiter()
                .GetResult()
                .FirstOrDefault();
            if (remoteTrack is null)
                return false;
            row = ToCatalogTrackContentRow(remoteTrack, server);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
