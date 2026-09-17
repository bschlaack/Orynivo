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
/// Artist detail surface (biography, image actions, and rename/merge), unified
/// artist album and track population, and now-playing detail-view handling for
/// <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private async void ArtistInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ArtistInfoCloseButton.IsVisible = true;
        if (_currentPodcastPlayback is { } podcastPlayback)
        {
            LyricsView.IsVisible = false;
            ArtistInfoView.IsVisible = false;
            PodcastInfoView.IsVisible = !(PodcastInfoView.IsVisible);
            if (PodcastInfoView.IsVisible)
                ShowPodcastInfo(podcastPlayback);
            UpdateBackButtonForDetailView();
            return;
        }

        if (_currentOrynivoTrackRow is { ArtistId: not null } &&
            _currentNowPlayingProvider is OrynivoServerNowPlayingMetadataProvider)
        {
            _artistInfoUnifiedArtistName = null;
            LyricsView.IsVisible = false;
            PodcastInfoView.IsVisible = false;
            ArtistInfoView.IsVisible = !ArtistInfoView.IsVisible;
            UpdateBackButtonForDetailView();
            if (ArtistInfoView.IsVisible)
                await ShowNowPlayingRemoteArtistInfoAsync(forceRefresh: false);
            return;
        }

        if (_currentArtistId is not long artistId || string.IsNullOrWhiteSpace(_currentArtistName))
            return;

        LyricsView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        _artistInfoUnifiedArtistName = null;
        ArtistInfoView.IsVisible = !(ArtistInfoView.IsVisible);
        UpdateBackButtonForDetailView();
        if (ArtistInfoView.IsVisible)
            await ShowArtistInfoAsync(artistId, forceRefresh: false);
    }

    private void CloseArtistInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
        {
            BackButton_OnClick(sender, e);
            return;
        }

        CloseNowPlayingDetailViews();
    }

    private void ClosePodcastInfoButton_OnClick(object? sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void ArtistInfoTitleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
            return;
        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            CloseNowPlayingDetailViews();
            await HandleContentRowDoubleClickAsync(remoteRow);
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        var artistName = ArtistInfoTitleButton.Content as string;
        CloseNowPlayingDetailViews();
        await ShowArtistAlbumsAsync(
            artistId,
            artistName ?? LocalizationManager.Current.Unknown);
    }

    private async void ArtistInfoListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        e.Handled = true;
        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        var artistId = row.EntityType switch
        {
            "Artist" or "OrynivoArtist" => row.Id,
            _ => row.ArtistId
        };

        if (artistId is null)
        {
            if (row.EntityType == "Album" && (row.AlbumId ?? row.Id) is long albumId)
            {
                using var db = AudioDatabase.OpenDefault();
                artistId = db.GetAlbumArtistId(albumId);
            }
            else if (!string.IsNullOrWhiteSpace(row.FilePath))
            {
                using var db = AudioDatabase.OpenDefault();
                artistId = db.GetTrackNavigationIds(row.FilePath).ArtistId;
            }
            row.ArtistId = artistId;
        }

        if (artistId is not long id)
            return;

        var artistName = row.EntityType is "Artist" or "OrynivoArtist"
            ? row.Title
            : row.Artist;
        await ShowUnifiedArtistAlbumsAsync(artistName ?? LocalizationManager.Current.Unknown);
    }

    private void UpdateBackButtonForDetailView()
    {
        BackButton.IsVisible =
            LyricsView.IsVisible ||
            ArtistInfoView.IsVisible ||
            PodcastInfoView.IsVisible ||
            _plexNavigationStack.Count > 0 ||
            _navigationStack.Count > 0;
    }

    private void CloseNowPlayingDetailViews()
    {
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        BackButton.IsVisible = _plexNavigationStack.Count > 0 || _navigationStack.Count > 0;
    }

    private void ResetArtistInfoSurface(string artistName)
    {
        _artistInfoDisplayedId = null;
        _artistInfoDisplayedRemoteRow = null;
        _nowPlayingRemoteArtistInfo = false;
        _artistInfoSourceUrl = null;
        ArtistInfoTitleButton.Content = artistName;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.Content = LocalizationManager.Current.ArtistInfoSource;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;
        SetArtistInfoFavoriteState(false);
        if (string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
            ResetArtistInfoAlbums();
    }

    private async void RefreshArtistInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var currentArtistName = ArtistInfoTitleButton.Content as string;
        if (string.IsNullOrWhiteSpace(currentArtistName))
            return;
        var dialog = new ArtistProfileSearchDialog(currentArtistName);
        if (await dialog.ShowDialog<bool>(this) == false || string.IsNullOrWhiteSpace(dialog.Query))
            return;

        var profileLookupName = dialog.Query;
        var unifiedArtistName = _artistInfoUnifiedArtistName;
        if (_nowPlayingRemoteArtistInfo)
        {
            await ShowNowPlayingRemoteArtistInfoAsync(forceRefresh: true, profileLookupName);
        }
        else if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            await ShowOrynivoArtistInfoAsync(remoteRow, forceRefresh: true, profileLookupName);
        }
        else if (_artistInfoDisplayedId is long artistId)
        {
            await ShowArtistInfoAsync(artistId, forceRefresh: true, profileLookupName);
        }

        if (!string.IsNullOrWhiteSpace(unifiedArtistName))
        {
            _artistInfoUnifiedArtistName = unifiedArtistName;
            await LoadUnifiedArtistInfoAlbumsAsync(unifiedArtistName, CancellationToken.None);
        }
    }

    private async void SearchArtistImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_nowPlayingRemoteArtistInfo)
        {
            await OpenNowPlayingRemoteArtistImageSearchAsync();
            return;
        }

        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            var server = ResolveRowOrynivoServer(remoteRow);
            if (server is null ||
                remoteRow.Id is not long remoteArtistId ||
                remoteRow.EntityType != "OrynivoArtist")
            {
                return;
            }

            await OpenOrynivoArtistImageSearchAsync(server, remoteArtistId, remoteRow);
            ArtistInfoImage.Source = remoteRow.Artwork;
            ArtistInfoImagePlaceholder.IsVisible = ArtistInfoImage.Source is null;
            ArtistInfoImageStatusText.IsVisible = false;
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var dialog = new ArtistImageSearchWindow(
            artist.Artist,
            _settings.FanartTvApiKey);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        try
        {
            var imagePath = await ArtistImageSearchService.SaveImageAsync(
                artistId,
                selected.ImageData,
                selected.MimeType);
            using (var db = AudioDatabase.OpenDefault())
            {
                db.UpdateArtistImage(artistId, imagePath);
                artist = db.GetArtistById(artistId);
            }

            if (artist is null)
                return;

            ArtistInfoImage.Source = CreateArtworkImage(imagePath, 1000, ignoreCache: true);
            ArtistInfoImagePlaceholder.IsVisible = ArtistInfoImage.Source is null;
            ArtistInfoImageStatusText.IsVisible = false;
            await SynchronizeUnifiedArtistImageAsync(
                artist.Artist,
                selected.ImageData,
                selected.MimeType);
            await RefreshVisibleArtistRowAsync(artist);
        }
        catch
        {
            ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistImageDownloadFailed;
            ArtistInfoImageStatusText.IsVisible = true;
        }
    }

    private async void UploadArtistImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetDisplayedArtistName(out var artistName) ||
            await PickImageAsync() is not { } image)
        {
            return;
        }

        await SynchronizeUnifiedArtistImageAsync(artistName, image.Data, image.MimeType);
        using var stream = new MemoryStream(image.Data);
        ArtistInfoImage.Source = new Bitmap(stream);
        ArtistInfoImagePlaceholder.IsVisible = false;
        ArtistInfoImageStatusText.IsVisible = false;

        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            if (ResolveRowOrynivoServer(remoteRow) is { } server &&
                remoteRow.Id is long artistId)
            {
                EnsureOrynivoArtistArtworkPaths(server, artistId, remoteRow);
                ApplyRemoteArtwork(remoteRow, image.Data);
                remoteRow.ImageIsManual = true;
            }
        }
        else if (_artistInfoDisplayedId is long localArtistId)
        {
            ArtistInfo? artist;
            using (var db = AudioDatabase.OpenDefault())
                artist = db.GetArtistById(localArtistId);
            if (artist is not null)
                await RefreshVisibleArtistRowAsync(artist);
        }

        StatusTextBlock.Text = string.Empty;
    }

    private async void DeleteArtistImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetDisplayedArtistName(out var artistName))
            return;

        await SynchronizeUnifiedArtistImageDeletionAsync(artistName);
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoImageStatusText.IsVisible = false;

        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            InvalidateRemoteArtworkCache(remoteRow.ArtworkPath);
            InvalidateRemoteArtworkCache(remoteRow.ThumbnailPath);
            remoteRow.ArtworkPath = null;
            remoteRow.ThumbnailPath = null;
            UpdateRowArtworkFromBytes(remoteRow, null);
            remoteRow.ImageIsManual = false;
        }
        else if (_artistInfoDisplayedId is long localArtistId)
        {
            ArtistInfo? artist;
            using (var db = AudioDatabase.OpenDefault())
                artist = db.GetArtistById(localArtistId);
            if (artist is not null)
                await RefreshVisibleArtistRowAsync(artist);
        }

        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Resolves the artist currently displayed in the artist-information surface.</summary>
    /// <param name="artistName">Receives the artist display name.</param>
    /// <returns><see langword="true"/> when an artist context is available.</returns>
    private bool TryGetDisplayedArtistName(out string artistName)
    {
        artistName = ArtistInfoTitleButton.Content as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(artistName))
            return true;

        if (_currentOrynivoTrackRow is { Artist: { } nowPlayingArtist })
        {
            artistName = nowPlayingArtist;
            return !string.IsNullOrWhiteSpace(artistName);
        }

        return false;
    }

    /// <summary>Shows or hides the complete manual artist-image action group.</summary>
    /// <param name="isVisible">Whether image search, upload, and deletion are available.</param>
    private void SetArtistImageActionsVisible(bool isVisible)
    {
        SearchArtistImageButton.IsVisible = isVisible;
        UploadArtistImageButton.IsVisible = isVisible;
        DeleteArtistImageButton.IsVisible = isVisible;
    }

    /// <summary>Opens manual artist-image search for the currently playing remote track's artist.</summary>
    /// <returns>A task representing the asynchronous search and upload flow.</returns>
    private async Task OpenNowPlayingRemoteArtistImageSearchAsync()
    {
        if (_currentOrynivoTrackRow is not { OrynivoServer: { } server, ArtistId: long artistId })
            return;

        var artistName = ArtistInfoTitleButton.Content as string;
        if (string.IsNullOrWhiteSpace(artistName))
            artistName = _currentOrynivoTrackRow.Artist ?? string.Empty;

        var dialog = new ArtistImageSearchWindow(
            artistName,
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
            ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistImageDownloadFailed;
            ArtistInfoImageStatusText.IsVisible = true;
            return;
        }

        var imageUrl = OrynivoServerClient.GetArtistArtworkUrl(server, artistId);
        InvalidateRemoteArtworkCache(imageUrl);
        WriteRemoteArtworkCache(imageUrl, selected.ImageData);
        using var stream = new MemoryStream(selected.ImageData);
        ArtistInfoImage.Source = new Bitmap(stream);
        ArtistInfoImagePlaceholder.IsVisible = ArtistInfoImage.Source is null;
        ArtistInfoImageStatusText.IsVisible = false;
        await SynchronizeUnifiedArtistImageAsync(
            artistName,
            selected.ImageData,
            selected.MimeType);
        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Deletes the image from every local and reachable remote identity of an artist.</summary>
    /// <param name="artistName">Artist display name used for normalized identity matching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the synchronized deletion.</returns>
    private async Task SynchronizeUnifiedArtistImageDeletionAsync(
        string artistName,
        CancellationToken cancellationToken = default)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        if (comparisonKey.Length == 0)
            return;

        await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            foreach (var artist in db.GetArtistsLite().Where(candidate =>
                         ArtistNameNormalizer.CreateComparisonKey(candidate.Artist) == comparisonKey))
            {
                if (db.ClearArtistImage(artist.Id))
                    ArtistImageSearchService.DeleteImage(artist.Id);
            }
        }, cancellationToken);

        foreach (var server in _settings.OrynivoServers)
        {
            try
            {
                var artists = await _orynivoClient.GetArtistsAsync(server, cancellationToken);
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    await _orynivoClient.DeleteArtistImageAsync(server, artist.Id, cancellationToken);
                    var imageUrl = OrynivoServerClient.GetArtistArtworkUrl(server, artist.Id);
                    InvalidateRemoteArtworkCache(imageUrl);
                }
                DeleteOrynivoArtistListCache(server);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Deletion remains applied to every reachable matching library.
            }
        }
        InvalidateUnifiedLibraryViewCache();
    }
}
