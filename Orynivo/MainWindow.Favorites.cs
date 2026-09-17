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
/// Now-playing and entity favorite toggling for local and Orynivo Server
/// tracks, albums, and artists in <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// The remote Orynivo Server track currently playing, expressed as its server and
    /// track ID, or <see langword="null"/> when no remote track is the favourite target.
    /// </summary>
    private (OrynivoServerSettings Server, long Id)? CurrentOrynivoFavoriteTarget =>
        _currentTrackId is null && _currentOrynivoTrackRow is { OrynivoServer: { } server, Id: long id }
            ? (server, id)
            : null;

    private void UpdateNowPlayingFavoriteButton()
    {
        NowPlayingFavoriteButton.IsEnabled = _currentTrackId.HasValue || CurrentOrynivoFavoriteTarget is not null;
        NowPlayingFavoriteGlyph.Text = _currentTrackIsFavorite ? "❤" : "♡";
        NowPlayingFavoriteGlyph.FontSize = _currentTrackIsFavorite ? 18 : 15;
    }

    private void NowPlayingFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetCurrentTrackFavorite(!_currentTrackIsFavorite);
    }

    /// <summary>Applies a favorite state to the current local or remote library track.</summary>
    /// <param name="favorite">Requested favorite state.</param>
    /// <returns><see langword="true"/> when an eligible current track was updated.</returns>
    private bool SetCurrentTrackFavorite(bool favorite)
    {
        // Remote Orynivo Server track: persist the profile-scoped state locally and
        // mirror it to the server when the endpoint is available.
        if (CurrentOrynivoFavoriteTarget is { } target)
        {
            var (favServer, favId) = target;
            _currentTrackIsFavorite = favorite;
            InvalidateUnifiedLibraryViewCache();
            var key = GetOrynivoFavoriteKey(favServer.Id, "Track", favId);
            if (_currentTrackIsFavorite)
                ActiveUserProfile.OrynivoServerFavorites.Add(key);
            else
                ActiveUserProfile.OrynivoServerFavorites.Remove(key);
            _settingsStore.Save(_settings);

            if (_currentOrynivoTrackRow is not null)
                _currentOrynivoTrackRow.IsFavorite = _currentTrackIsFavorite;
            UpdateNowPlayingFavoriteButton();
            RefreshOrynivoFavoriteRows(favServer, favId, _currentTrackIsFavorite);
            _ = _orynivoClient.UpdateTrackFavoriteAsync(favServer, favId, _currentTrackIsFavorite);
            return true;
        }

        if (_currentTrackId is not long id)
            return false;

        _currentTrackIsFavorite = favorite;
        InvalidateUnifiedLibraryViewCache();
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SetTrackFavorite(id, _currentTrackIsFavorite);
        }
        catch { }

        UpdateNowPlayingFavoriteButton();

        if (ContentDataGrid.ItemsSource is IEnumerable<ContentRow> rows)
        {
            var row = rows.FirstOrDefault(r => r.Id == id);
            if (row is not null)
                row.IsFavorite = _currentTrackIsFavorite;
        }
        return true;
    }

    /// <summary>Reflects a remote track favourite change in any currently visible remote track rows.</summary>
    /// <param name="server">Server owning the toggled track.</param>
    /// <param name="trackId">Toggled remote track ID.</param>
    /// <param name="isFavorite">New favourite state.</param>
    private void RefreshOrynivoFavoriteRows(OrynivoServerSettings server, long trackId, bool isFavorite)
    {
        if (ContentDataGrid.ItemsSource is not IEnumerable<ContentRow> rows)
            return;

        var matches = rows.Where(r => r.EntityType == "OrynivoTrack" &&
                                      r.Id == trackId &&
                                      r.OrynivoServer?.Id == server.Id).ToList();
        if (matches.Count == 0)
            return;

        foreach (var row in matches)
            row.IsFavorite = isFavorite;
    }

    private async Task SetUnifiedArtistFavoriteAsync(string? artistName, bool isFavorite)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        var localArtists = await _localCatalogProvider.GetArtistsAsync();
        using (var db = AudioDatabase.OpenDefault())
        {
            foreach (var artist in localArtists.Where(candidate =>
                         ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                db.SetArtistFavorite(artist.Id, isFavorite);
        }

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            try
            {
                var artists = await CreateOrynivoCatalogProvider(server).GetArtistsAsync();
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var key = GetOrynivoFavoriteKey(server.Id, "Artist", artist.Id);
                    if (isFavorite)
                        ActiveUserProfile.OrynivoServerFavorites.Add(key);
                    else
                        ActiveUserProfile.OrynivoServerFavorites.Remove(key);
                    _ = _orynivoClient.UpdateArtistFavoriteAsync(server, artist.Id, isFavorite);
                }
            }
            catch
            {
                // Keep the available libraries in sync even if one server is offline.
            }
        }

        _settingsStore.Save(_settings);
        InvalidateUnifiedLibraryViewCache();
    }

    private void SetArtistInfoFavoriteState(bool isFavorite)
    {
        _artistInfoIsFavorite = isFavorite;
        ArtistInfoFavoriteButton.Content = isFavorite ? "❤" : "♡";
    }

    private async void ArtistInfoFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var artistName = ArtistInfoTitleButton.Content as string;
        if (string.IsNullOrWhiteSpace(artistName))
            return;

        var isFavorite = !_artistInfoIsFavorite;
        ArtistInfoFavoriteButton.IsEnabled = false;
        SetArtistInfoFavoriteState(isFavorite);
        try
        {
            await SetUnifiedArtistFavoriteAsync(artistName, isFavorite);
        }
        catch
        {
            SetArtistInfoFavoriteState(!isFavorite);
        }
        finally
        {
            ArtistInfoFavoriteButton.IsEnabled = true;
        }

        e.Handled = true;
    }

    private async void FavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row } || row.Id is not long id)
            return;

        row.IsFavorite = !row.IsFavorite;
        InvalidateUnifiedLibraryViewCache();
        if (row.EntityType == "UnifiedAlbum" && row.LogicalAlbumParts is { Count: > 0 })
        {
            SetLogicalAlbumFavorite(row.LogicalAlbumParts, row.IsFavorite);
            if (_albumFavoritesOnly && !row.IsFavorite)
                await ReloadEntityRowsAsync("Albums");
            e.Handled = true;
            return;
        }
        if (row.EntityType == "UnifiedArtist")
        {
            await SetUnifiedArtistFavoriteAsync(row.Title, row.IsFavorite);
            if (_artistFavoritesOnly && !row.IsFavorite)
                await ReloadEntityRowsAsync("Artists");
            e.Handled = true;
            return;
        }

        ActivateRowOrynivoServer(row);
        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal) &&
            _activeOrynivoServer is not null)
        {
            var entityType = row.EntityType["Orynivo".Length..];
            var entityIds = entityType == "Album" && row.LogicalAlbumIds is { Count: > 0 }
                ? row.LogicalAlbumIds
                : [id];
            foreach (var entityId in entityIds)
            {
                var key = GetOrynivoFavoriteKey(_activeOrynivoServer.Id, entityType, entityId);
                if (row.IsFavorite)
                    ActiveUserProfile.OrynivoServerFavorites.Add(key);
                else
                    ActiveUserProfile.OrynivoServerFavorites.Remove(key);
                if (entityType == "Artist")
                    _ = _orynivoClient.UpdateArtistFavoriteAsync(_activeOrynivoServer, entityId, row.IsFavorite);
                else if (entityType == "Album")
                    _ = _orynivoClient.UpdateAlbumFavoriteAsync(_activeOrynivoServer, entityId, row.IsFavorite);
                else if (entityType == "Track")
                    _ = _orynivoClient.UpdateTrackFavoriteAsync(_activeOrynivoServer, entityId, row.IsFavorite);
            }
            _settingsStore.Save(_settings);
            if (_trackFavoritesOnly && !row.IsFavorite)
                await LoadOrynivoViewAsync();
            e.Handled = true;
            return;
        }

        using (var db = AudioDatabase.OpenDefault())
        {
            if (row.EntityType == "Artist")
                db.SetArtistFavorite(id, row.IsFavorite);
            else if (row.EntityType == "Album")
            {
                var albumIds = row.LogicalAlbumIds is { Count: > 0 }
                    ? row.LogicalAlbumIds
                    : [id];
                foreach (var albumId in albumIds)
                    db.SetAlbumFavorite(albumId, row.IsFavorite);
            }
            else
                db.SetTrackFavorite(id, row.IsFavorite);
        }

        if (row.EntityType == "Artist" && _artistFavoritesOnly && !row.IsFavorite)
            await ReloadEntityRowsAsync("Artists");
        else if (row.EntityType == "Album" && _albumFavoritesOnly && !row.IsFavorite)
            await ReloadEntityRowsAsync("Albums");

        QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
        e.Handled = true;
    }

    private void SetLogicalAlbumFavorite(IReadOnlyList<LogicalAlbumPart> parts, bool isFavorite)
    {
        var localIds = parts
            .Where(part => part.Server is null)
            .Select(part => part.AlbumId)
            .Distinct()
            .ToList();
        if (localIds.Count > 0)
        {
            using var db = AudioDatabase.OpenDefault();
            foreach (var albumId in localIds)
                db.SetAlbumFavorite(albumId, isFavorite);
        }

        var hasRemote = false;
        foreach (var part in parts.Where(part => part.Server is not null))
        {
            var server = part.Server!;
            var key = GetOrynivoFavoriteKey(server.Id, "Album", part.AlbumId);
            if (isFavorite)
                ActiveUserProfile.OrynivoServerFavorites.Add(key);
            else
                ActiveUserProfile.OrynivoServerFavorites.Remove(key);
            _ = _orynivoClient.UpdateAlbumFavoriteAsync(server, part.AlbumId, isFavorite);
            hasRemote = true;
        }
        if (hasRemote)
            _settingsStore.Save(_settings);
    }
}
