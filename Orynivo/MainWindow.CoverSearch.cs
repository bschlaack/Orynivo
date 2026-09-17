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
/// Album cover and artist image search, upload, reassignment, deletion, and
/// unified artwork synchronization for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private async void SearchCoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        ActivateRowOrynivoServer(row);
        if (row.EntityType == "OrynivoAlbum" &&
            _activeOrynivoServer is not null &&
            long.TryParse(row.ExternalId, out var remoteAlbumId))
        {
            await OpenOrynivoAlbumCoverSearchAsync(_activeOrynivoServer, remoteAlbumId, row);
            return;
        }

        await OpenCoverSearchAsync(row);
    }

    private async void UploadCoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { Id: long albumId } row } ||
            await PickImageAsync() is not { } image)
        {
            return;
        }

        ILibraryCatalogProvider provider = row.EntityType == "OrynivoAlbum" &&
                                           ResolveRowOrynivoServer(row) is { } server
            ? CreateOrynivoCatalogProvider(server)
            : _localCatalogProvider;
        if (!await provider.SetAlbumArtworkAsync(albumId, image.Data, image.MimeType))
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        if (row.EntityType == "OrynivoAlbum" && ResolveRowOrynivoServer(row) is { } remoteServer)
        {
            row.ArtworkPath = OrynivoServerClient.GetAlbumArtworkUrl(remoteServer, albumId, 320);
            row.ThumbnailPath = OrynivoServerClient.GetAlbumArtworkUrl(remoteServer, albumId, 96);
            ApplyRemoteArtwork(row, image.Data);
            DeleteOrynivoAlbumListCache(remoteServer);
        }
        else
        {
            UpdateRowArtworkFromBytes(row, image.Data);
        }

        InvalidateUnifiedLibraryViewCache();

        if (_activeAlbumFilterId == albumId && row.EntityType != "OrynivoAlbum")
            await ReloadAlbumDetailHeaderAsync(albumId);
        StatusTextBlock.Text = string.Empty;
    }

    private async void DeleteCoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { Id: long albumId } row })
            return;

        ILibraryCatalogProvider provider = row.EntityType == "OrynivoAlbum" &&
                                           ResolveRowOrynivoServer(row) is { } server
            ? CreateOrynivoCatalogProvider(server)
            : _localCatalogProvider;
        if (!await provider.DeleteAlbumArtworkAsync(albumId))
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        InvalidateRemoteArtworkCache(row.ArtworkPath);
        InvalidateRemoteArtworkCache(row.ThumbnailPath);
        row.ArtworkPath = null;
        row.ThumbnailPath = null;
        UpdateRowArtworkFromBytes(row, null);
        if (row.EntityType == "OrynivoAlbum" && ResolveRowOrynivoServer(row) is { } remoteServer)
            DeleteOrynivoAlbumListCache(remoteServer);
        InvalidateUnifiedLibraryViewCache();
        if (_activeAlbumFilterId == albumId && row.EntityType != "OrynivoAlbum")
            await ReloadAlbumDetailHeaderAsync(albumId);
        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Lets the user select and validates a bounded local image file.</summary>
    /// <returns>The selected image bytes and MIME type, or <see langword="null"/> when cancelled or invalid.</returns>
    private async Task<(byte[] Data, string MimeType)?> PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.ImageFileType,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Current.ImageFileType)
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"]
                }
            ]
        });
        if (files.Count == 0)
            return null;

        const int maximumImageBytes = 20 * 1024 * 1024;
        await using var input = await files[0].OpenReadAsync();
        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + read > maximumImageBytes)
                return null;
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        if (output.Length == 0)
            return null;

        var data = output.ToArray();
        try
        {
            using var validationStream = new MemoryStream(data);
            using var bitmap = new Bitmap(validationStream);
        }
        catch
        {
            return null;
        }

        return (data, GuessImageMimeType(files[0].Name));
    }

    private async void DeleteCoverMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow { Id: long albumId } row })
            return;

        if (row.EntityType == "OrynivoAlbum")
            return;

        var verticalOffset = CaptureCurrentVerticalOffset();
        using (var db = AudioDatabase.OpenDefault())
            db.ClearArtworkFromAlbum(albumId);

        // On the dashboard, clear the bound card in place instead of rebuilding
        // the hidden Albums list.
        if (DashboardScrollViewer.IsVisible)
        {
            row.ArtworkPath = null;
            row.ThumbnailPath = null;
            UpdateRowArtworkFromBytes(row, null);
            return;
        }
        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync(albumId, verticalOffset);
    }

    private async void ReassignCoverMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row })
            return;

        ActivateRowOrynivoServer(row);
        if (row.EntityType == "OrynivoAlbum" &&
            _activeOrynivoServer is not null &&
            long.TryParse(row.ExternalId, out var remoteAlbumId))
        {
            await OpenOrynivoAlbumCoverSearchAsync(_activeOrynivoServer, remoteAlbumId, row);
            return;
        }

        await OpenCoverSearchAsync(row);
    }

    private async void OrynivoArtworkMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row } ||
            ResolveRowOrynivoServer(row) is not { } server ||
            !long.TryParse(row.ExternalId, out var entityId))
        {
            return;
        }

        if (row.EntityType == "OrynivoAlbum")
        {
            await OpenOrynivoAlbumCoverSearchAsync(server, entityId, row);
            return;
        }

        if (row.EntityType == "OrynivoArtist")
            await OpenOrynivoArtistImageSearchAsync(server, entityId, row);
    }

    private async void OrynivoArtistInfoRefreshMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row } ||
            row.EntityType != "OrynivoArtist" ||
            ResolveRowOrynivoServer(row) is not { } server ||
            !long.TryParse(row.ExternalId, out var artistId))
        {
            return;
        }

        StatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
        var language = GetProfileLanguageCode();
        var profile = await ArtistProfileService.DownloadAsync(
            artistId,
            row.Title ?? string.Empty,
            language,
            downloadImage: !row.ImageIsManual);
        byte[]? imageData = null;
        string? imageMimeType = null;
        if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
        {
            imageData = await File.ReadAllBytesAsync(profile.ImagePath);
            imageMimeType = GuessImageMimeType(profile.ImagePath);
        }

        var refreshed = await _orynivoClient.UpdateArtistProfileAsync(
            server,
            artistId,
            profile?.Biography,
            profile?.SourceUrl,
            profile?.Language ?? language,
            imageData,
            imageMimeType);
        if (refreshed is null)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        row.Biography = refreshed.Biography;
        row.SourceUrl = refreshed.SourceUrl;
        row.ProfileLanguage = refreshed.ProfileLanguage;
        row.ProfileFetchedAt = refreshed.ProfileFetchedAt;
        row.ImageIsManual = refreshed.ImageIsManual;
        if (imageData is not null)
        {
            EnsureOrynivoArtistArtworkPaths(server, artistId, row);
            ApplyRemoteArtwork(row, imageData);
        }
        else
        {
            InvalidateRemoteArtworkCache(row.ArtworkPath);
            _ = LoadOrynivoArtworkAsync(
                row,
                OrynivoServerClient.GetArtistArtworkUrl(server, artistId));
        }
        DeleteOrynivoArtistListCache(server);
        StatusTextBlock.Text = string.IsNullOrWhiteSpace(refreshed.Biography)
            ? LocalizationManager.Current.ArtistInfoNotFound
            : string.Empty;
    }

    private static string GuessImageMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private async Task OpenCoverSearchAsync(ContentRow row)
    {
        if (row.Id is not long albumId)
            return;

        var dialog = new CoverSearchWindow(row.Title ?? string.Empty, GetCoverSearchArtist(row));
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        await _localCatalogProvider.SetAlbumArtworkAsync(albumId, selected.ImageData, selected.MimeType);
        InvalidateUnifiedLibraryViewCache();

        // Refresh the bound row directly so a card outside the Albums list (e.g. a
        // dashboard recent-album card) updates immediately.
        UpdateRowArtworkFromBytes(row, selected.ImageData);

        // Dashboard and its Show all pages share this viewer. The bound card is
        // already refreshed; rebuilding Dashboard would replace the current page
        // and lose its scroll position.
        if (DashboardScrollViewer.IsVisible)
            return;
        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
    }

    /// <summary>Decodes image bytes and assigns them to a row's artwork/thumbnail in place.</summary>
    /// <param name="row">The row whose artwork should be updated.</param>
    /// <param name="imageData">The new image bytes, or <see langword="null"/> to clear.</param>
    private static void UpdateRowArtworkFromBytes(ContentRow row, byte[]? imageData)
    {
        if (imageData is not { Length: > 0 })
        {
            row.Artwork = null;
            row.Thumbnail = null;
            return;
        }

        try
        {
            using var stream = new MemoryStream(imageData);
            var bitmap = new Bitmap(stream);
            row.Artwork = bitmap;
            row.Thumbnail = bitmap;
            row.ArtworkLoadQueued = false;
            row.ArtworkLoadCompleted = true;
            row.ThumbnailLoadQueued = false;
            row.ThumbnailLoadCompleted = true;
        }
        catch { /* leave the existing artwork on decode failure */ }
    }

    private async Task OpenOrynivoAlbumCoverSearchAsync(
        OrynivoServerSettings server,
        long albumId,
        ContentRow row)
    {
        var dialog = new CoverSearchWindow(row.Title ?? string.Empty, GetCoverSearchArtist(row));
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        var provider = CreateOrynivoCatalogProvider(server);
        var uploaded = await provider.SetAlbumArtworkAsync(albumId, selected.ImageData, selected.MimeType);
        if (!uploaded)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        row.ArtworkPath ??= OrynivoServerClient.GetAlbumArtworkUrl(server, albumId, 320);
        row.ThumbnailPath ??= OrynivoServerClient.GetAlbumArtworkUrl(server, albumId, 96);
        ApplyRemoteArtwork(row, selected.ImageData);
        DeleteOrynivoAlbumListCache(server);
        InvalidateUnifiedLibraryViewCache();
        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Returns the best available artist name for narrowing manual cover searches.</summary>
    /// <param name="row">Album row or dashboard album card row.</param>
    /// <returns>The artist query to prefill, or <see langword="null"/>.</returns>
    private static string? GetCoverSearchArtist(ContentRow row) =>
        !string.IsNullOrWhiteSpace(row.AlbumArtist)
            ? row.AlbumArtist
            : !string.IsNullOrWhiteSpace(row.Artist)
                ? row.Artist
                : null;

    private async Task OpenOrynivoArtistImageSearchAsync(
        OrynivoServerSettings server,
        long artistId,
        ContentRow row)
    {
        var dialog = new ArtistImageSearchWindow(
            row.Title ?? string.Empty,
            _settings.FanartTvApiKey);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        var uploaded = await _orynivoClient.UploadArtistImageAsync(
            server,
            artistId,
            selected.ImageData,
            selected.MimeType);
        if (!uploaded)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        EnsureOrynivoArtistArtworkPaths(server, artistId, row);
        ApplyRemoteArtwork(row, selected.ImageData);
        DeleteOrynivoArtistListCache(server);
        await SynchronizeUnifiedArtistImageAsync(
            row.Title,
            selected.ImageData,
            selected.MimeType);
        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Ensures a remote artist row has stable artwork URLs after an upload.</summary>
    /// <param name="server">Server that owns the artist.</param>
    /// <param name="artistId">Server-side artist identifier.</param>
    /// <param name="row">Artist row to update.</param>
    private static void EnsureOrynivoArtistArtworkPaths(
        OrynivoServerSettings server,
        long artistId,
        ContentRow row)
    {
        var imageUrl = OrynivoServerClient.GetArtistArtworkUrl(server, artistId);
        row.ArtworkPath = imageUrl;
        row.ThumbnailPath = imageUrl;
    }

    /// <summary>Copies server-cached artist profile metadata into a remote artist row.</summary>
    /// <param name="server">Server that owns the artist.</param>
    /// <param name="row">Row to update.</param>
    /// <param name="artist">Server artist profile response.</param>
    private static void ApplyOrynivoArtistProfile(
        OrynivoServerSettings server,
        ContentRow row,
        OrynivoArtistInfo artist)
    {
        row.ArtistId = artist.Id;
        row.Biography = artist.Biography;
        row.SourceUrl = artist.SourceUrl;
        row.ProfileLanguage = artist.ProfileLanguage;
        row.ProfileFetchedAt = artist.ProfileFetchedAt;
        row.ImageIsManual = artist.ImageIsManual;
        if (artist.HasImage)
        {
            EnsureOrynivoArtistArtworkPaths(server, artist.Id, row);
            row.ArtworkLoadCompleted = false;
            row.ThumbnailLoadCompleted = false;
        }
        else
        {
            row.ArtworkPath = null;
            row.ThumbnailPath = null;
            row.Artwork = null;
            row.Thumbnail = null;
            row.ArtworkLoadCompleted = true;
            row.ThumbnailLoadCompleted = true;
        }
    }

    private static void ApplyRemoteArtwork(ContentRow row, byte[] imageData)
    {
        InvalidateRemoteArtworkCache(row.ArtworkPath);
        InvalidateRemoteArtworkCache(row.ThumbnailPath);
        WriteRemoteArtworkCache(row.ArtworkPath, imageData);
        WriteRemoteArtworkCache(row.ThumbnailPath, imageData);
        using var stream = new MemoryStream(imageData);
        var bitmap = new Bitmap(stream);
        row.Artwork = bitmap;
        row.Thumbnail = bitmap;
        row.ArtworkLoadQueued = false;
        row.ArtworkLoadCompleted = true;
        row.ThumbnailLoadQueued = false;
        row.ThumbnailLoadCompleted = true;
    }

    /// <summary>
    /// Stores a manually selected artist image for every matching local and Orynivo Server
    /// identity so a unified artist never shows different artwork depending on its source.
    /// Unreachable servers are skipped and will not prevent the selected image from being used.
    /// </summary>
    /// <param name="artistName">Artist display name used for normalized identity matching.</param>
    /// <param name="imageData">Selected image bytes.</param>
    /// <param name="mimeType">Image MIME type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the synchronization.</returns>
    private async Task SynchronizeUnifiedArtistImageAsync(
        string? artistName,
        byte[] imageData,
        string? mimeType,
        CancellationToken cancellationToken = default)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        if (comparisonKey.Length == 0)
            return;

        var localArtists = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetArtistsLite()
                .Where(artist => ArtistNameNormalizer.CreateComparisonKey(artist.Artist) == comparisonKey)
                .ToList();
        }, cancellationToken);
        foreach (var artist in localArtists)
        {
            var imagePath = await ArtistImageSearchService.SaveImageAsync(
                artist.Id,
                imageData,
                mimeType,
                cancellationToken);
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.UpdateArtistImage(artist.Id, imagePath);
            }, cancellationToken);
        }

        foreach (var server in _settings.OrynivoServers)
        {
            try
            {
                var artists = await _orynivoClient.GetArtistsAsync(server, cancellationToken);
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    await _orynivoClient.UploadArtistImageAsync(
                        server,
                        artist.Id,
                        imageData,
                        mimeType,
                        cancellationToken);
                }
                DeleteOrynivoArtistListCache(server);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The image remains available in every reachable matching library.
            }
        }
        InvalidateUnifiedLibraryViewCache();
    }

    /// <summary>
    /// Copies downloaded biography metadata and optional automatic artwork to every normalized
    /// local and remote identity of an artist. Manually selected images remain protected by the
    /// database update rules on both sides.
    /// </summary>
    /// <param name="artistName">Artist display name used for normalized identity matching.</param>
    /// <param name="biography">Downloaded biography.</param>
    /// <param name="sourceUrl">Biography source URL.</param>
    /// <param name="language">Profile language code.</param>
    /// <param name="imageData">Optional downloaded image bytes.</param>
    /// <param name="imageMimeType">Optional image MIME type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the synchronization.</returns>
    private async Task SynchronizeUnifiedArtistProfileAsync(
        string? artistName,
        string? biography,
        string? sourceUrl,
        string language,
        byte[]? imageData,
        string? imageMimeType,
        CancellationToken cancellationToken)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        if (comparisonKey.Length == 0)
            return;

        var localArtists = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetArtistsLite()
                .Where(artist => ArtistNameNormalizer.CreateComparisonKey(artist.Artist) == comparisonKey)
                .ToList();
        }, cancellationToken);
        foreach (var artist in localArtists)
        {
            string? imagePath = null;
            if (imageData is { Length: > 0 } && !artist.ImageIsManual)
            {
                imagePath = await ArtistImageSearchService.SaveImageAsync(
                    artist.Id,
                    imageData,
                    imageMimeType,
                    cancellationToken);
            }
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.UpdateArtistProfile(artist.Id, biography, imagePath, sourceUrl, language);
            }, cancellationToken);
        }

        foreach (var server in _settings.OrynivoServers)
        {
            try
            {
                var artists = await _orynivoClient.GetArtistsAsync(server, cancellationToken);
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var synchronizedImageData = artist.ImageIsManual ? null : imageData;
                    await _orynivoClient.UpdateArtistProfileAsync(
                        server,
                        artist.Id,
                        biography,
                        sourceUrl,
                        language,
                        synchronizedImageData,
                        synchronizedImageData is null ? null : imageMimeType,
                        cancellationToken);
                }
                DeleteOrynivoArtistListCache(server);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Profile synchronization is best-effort for temporarily unavailable servers.
            }
        }
        InvalidateUnifiedLibraryViewCache();
    }

    private async Task ReloadAlbumRowsAsync(
        long? selectedAlbumId = null,
        double? verticalOffset = null)
    {
        var selectedRow = GetSelectedContentRow();
        selectedAlbumId ??= selectedRow?.Id;
        verticalOffset ??= CaptureCurrentVerticalOffset();
        await BindLocalRowsAndStartRemoteAppendAsync("Albums");
        RestoreSelectionFromCurrentItems(
            selectedAlbumId,
            verticalOffset,
            selectedRow?.SourceKey);
    }
}
