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

    private async void EditArtistNameButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            await EditOrynivoArtistNameAsync(remoteRow);
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var editDialog = new EditArtistNameDialog(artist.Id, artist.Artist);
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        var result = editDialog.Result;
        long? matchingArtistId = null;
        bool? preferCurrentArtistOnMerge = null;
        if (result is null && editDialog.MatchingArtist is { } matchingArtist)
        {
            matchingArtistId = matchingArtist.Id;
            var mergeDialog = new ArtistMergeDialog(
                artist.Id,
                artist.Artist,
                matchingArtist.Id,
                matchingArtist.Artist);
            if (await mergeDialog.ShowDialog<bool>(this) == false)
                return;

            preferCurrentArtistOnMerge = mergeDialog.PreferredArtistId == artist.Id;

            try
            {
                result = await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    return db.MergeArtists(
                        artist.Id,
                        matchingArtist.Id,
                        mergeDialog.PreferredArtistId,
                        editDialog.ArtistName);
                });
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Artist merge");
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
                ArtistInfoStatusTextBlock.IsVisible = true;
                return;
            }
        }

        if (result is null)
            return;

        var remoteRenameSucceeded = await RenameMatchingOrynivoArtistsAsync(
            artist.Artist,
            result.ArtistName,
            preferCurrentArtistOnMerge);

        if (_currentArtistId == artistId || _currentArtistId == matchingArtistId)
        {
            _currentArtistId = result.ArtistId;
            _currentArtistName = result.ArtistName;
            NowPlayingArtistBlock.Text = result.ArtistName;
        }
        if (_activeArtistFilterId == artistId || _activeArtistFilterId == matchingArtistId)
        {
            _activeArtistFilterId = result.ArtistId;
            _activeArtistFilterName = result.ArtistName;
        }

        _artistInfoDisplayedId = result.ArtistId;
        ArtistInfoTitleButton.Content = result.ArtistName;
        ArtistInfoStatusTextBlock.IsVisible = false;
        await ReloadVisibleArtistListAsync(result.ArtistId);
        await ShowArtistInfoAsync(result.ArtistId, forceRefresh: false);
        _ = UpdateSearchIndexAfterArtistRenameAsync(result.ArtistId);
        if (!remoteRenameSucceeded)
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            ArtistInfoStatusTextBlock.IsVisible = true;
        }
    }

    /// <summary>
    /// Renames every reachable Orynivo Server artist whose normalized identity matches the
    /// artist's previous local name. When the local rename required a merge, the same choice
    /// of surviving identity is applied to an equivalent collision on every server.
    /// </summary>
    /// <param name="previousArtistName">Display name used to find matching remote identities.</param>
    /// <param name="artistName">New display name to apply.</param>
    /// <param name="preferCurrentArtistOnMerge">
    /// <see langword="true"/> to retain the identity being renamed,
    /// <see langword="false"/> to retain the existing target-name identity, or
    /// <see langword="null"/> when no merge decision was made.
    /// </param>
    /// <returns><see langword="true"/> when every matching reachable identity was renamed.</returns>
    private async Task<bool> RenameMatchingOrynivoArtistsAsync(
        string? previousArtistName,
        string artistName,
        bool? preferCurrentArtistOnMerge = null)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(previousArtistName);
        if (comparisonKey.Length == 0)
            return true;

        var succeeded = true;
        foreach (var server in _settings.OrynivoServers)
        {
            try
            {
                var artists = await _orynivoClient.GetArtistsAsync(server);
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var response = await _orynivoClient.RenameArtistAsync(
                        server,
                        artist.Id,
                        artistName,
                        preferredArtistId: null);
                    if (response?.Result is null &&
                        response?.MatchingArtist is { } matchingArtist &&
                        preferCurrentArtistOnMerge is bool preferCurrent)
                    {
                        response = await _orynivoClient.RenameArtistAsync(
                            server,
                            artist.Id,
                            artistName,
                            preferCurrent ? artist.Id : matchingArtist.Id);
                    }
                    if (response?.Result is null)
                        succeeded = false;
                }
                DeleteOrynivoArtistListCache(server);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Unified remote artist rename");
                succeeded = false;
            }
        }

        return succeeded;
    }

    private async Task EditOrynivoArtistNameAsync(ContentRow row)
    {
        if (ResolveRowOrynivoServer(row) is not { } server ||
            row.Id is not long artistId ||
            row.EntityType != "OrynivoArtist")
        {
            return;
        }

        var editDialog = new EditArtistNameDialog(
            artistId,
            row.Title ?? string.Empty,
            (_, name) => CommitOrynivoArtistRenameAsync(server, artistId, name, preferredArtistId: null));
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        var result = editDialog.Result;
        long? matchingArtistId = null;
        if (result is null && editDialog.MatchingArtist is { } matchingArtist)
        {
            matchingArtistId = matchingArtist.Id;
            var mergeDialog = new ArtistMergeDialog(
                artistId,
                row.Title ?? string.Empty,
                matchingArtist.Id,
                matchingArtist.Artist);
            if (await mergeDialog.ShowDialog<bool>(this) == false)
                return;

            try
            {
                (result, _) = await CommitOrynivoArtistRenameAsync(
                    server,
                    artistId,
                    editDialog.ArtistName,
                    mergeDialog.PreferredArtistId);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Remote artist merge");
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
                ArtistInfoStatusTextBlock.IsVisible = true;
                return;
            }
        }

        if (result is null)
            return;

        var previousArtistName = row.Title;
        var localRenameSucceeded = await RenameMatchingLocalArtistAsync(
            previousArtistName,
            result.ArtistName);
        var otherRemoteRenamesSucceeded = await RenameMatchingOrynivoArtistsAsync(
            previousArtistName,
            result.ArtistName);

        row.ArtistId = result.ArtistId;
        ArtistInfoTitleButton.Content = result.ArtistName;
        ArtistInfoStatusTextBlock.IsVisible = false;

        if (_currentOrynivoTrackRow is { } currentRemoteTrack &&
            (currentRemoteTrack.ArtistId == artistId || currentRemoteTrack.ArtistId == matchingArtistId))
        {
            currentRemoteTrack.ArtistId = result.ArtistId;
            NowPlayingArtistBlock.Text = result.ArtistName;
        }

        ContentRow detailRow;
        var refreshed = await _orynivoClient.GetArtistAsync(server, result.ArtistId);
        if (refreshed is not null)
        {
            detailRow = ToOrynivoArtistContentRow(server, refreshed);
        }
        else
        {
            detailRow = new ContentRow
            {
                Id = result.ArtistId,
                ArtistId = result.ArtistId,
                Title = result.ArtistName,
                EntityType = "OrynivoArtist",
                ExternalId = result.ArtistId.ToString(CultureInfo.InvariantCulture),
                OrynivoServer = server,
                FilePath = string.Empty
            };
        }

        if (_activeOrynivoView == "Artists")
            await LoadOrynivoViewAsync();
        await ShowOrynivoArtistInfoAsync(detailRow, forceRefresh: false);
        if (!localRenameSucceeded || !otherRemoteRenamesSucceeded)
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            ArtistInfoStatusTextBlock.IsVisible = true;
        }
    }

    /// <summary>
    /// Renames the local artist whose normalized identity matches a remotely renamed artist.
    /// A target-name collision is left unresolved because a merge requires an explicit choice.
    /// </summary>
    /// <param name="previousArtistName">Display name used to find the local identity.</param>
    /// <param name="artistName">New display name to apply.</param>
    /// <returns><see langword="true"/> when no local match exists or the match was renamed.</returns>
    private static Task<bool> RenameMatchingLocalArtistAsync(
        string? previousArtistName,
        string artistName) => Task.Run(() =>
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(previousArtistName);
        if (comparisonKey.Length == 0)
            return true;

        using var db = AudioDatabase.OpenDefault();
        var localArtist = db.GetArtistsLite().FirstOrDefault(candidate =>
            ArtistNameNormalizer.CreateComparisonKey(candidate.Artist) == comparisonKey);
        if (localArtist is null)
            return true;
        if (db.FindArtistByName(artistName, localArtist.Id) is not null)
            return false;

        db.RenameArtist(localArtist.Id, artistName);
        TrackSearchIndex.UpdateMany(db.GetTracksForArtistSearchIndex(localArtist.Id));
        return true;
    });

    private async Task<(ArtistRenameResult? Result, ArtistInfo? MatchingArtist)> CommitOrynivoArtistRenameAsync(
        OrynivoServerSettings server,
        long artistId,
        string artistName,
        long? preferredArtistId)
    {
        var response = await _orynivoClient.RenameArtistAsync(
            server,
            artistId,
            artistName,
            preferredArtistId);
        if (response is null)
            throw new InvalidOperationException("The remote artist could not be renamed.");

        return (response.Result, response.MatchingArtist is null ? null : ToArtistInfo(response.MatchingArtist));
    }

    private static ArtistInfo ToArtistInfo(OrynivoArtistInfo artist) => new(
        artist.Id,
        artist.Name,
        artist.IsFavorite,
        artist.Biography,
        null,
        artist.SourceUrl,
        artist.ProfileLanguage,
        artist.ProfileFetchedAt,
        artist.ImageIsManual);

    private ContentRow ToOrynivoArtistContentRow(OrynivoServerSettings server, OrynivoArtistInfo artist)
    {
        var artworkUrl = artist.HasImage
            ? OrynivoServerClient.GetArtistArtworkUrl(server, artist.Id)
            : null;
        return new ContentRow
        {
            Id = artist.Id,
            ArtistId = artist.Id,
            Title = string.IsNullOrWhiteSpace(artist.Name) ? LocalizationManager.Current.Unknown : artist.Name,
            IsFavorite = IsOrynivoFavorite(server, "Artist", artist.Id),
            ArtworkPath = artworkUrl,
            ThumbnailPath = artworkUrl,
            Biography = artist.Biography,
            SourceUrl = artist.SourceUrl,
            ProfileLanguage = artist.ProfileLanguage,
            ProfileFetchedAt = artist.ProfileFetchedAt,
            ImageIsManual = artist.ImageIsManual,
            EntityType = "OrynivoArtist",
            ExternalId = artist.Id.ToString(CultureInfo.InvariantCulture),
            OrynivoServer = server,
            FilePath = string.Empty
        };
    }

    private async Task ReloadVisibleArtistListAsync(long? selectedArtistId = null)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: "Artists" })
            return;

        var selectedRow = GetSelectedContentRow();
        selectedArtistId ??= selectedRow?.Id;
        var verticalOffset = CaptureCurrentVerticalOffset();
        await BindLocalRowsAndStartRemoteAppendAsync("Artists");
        RestoreSelectionFromCurrentItems(
            selectedArtistId,
            verticalOffset,
            selectedRow?.SourceKey);
    }

    private void ArtistInfoSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_artistInfoSourceUrl))
            return;
        Process.Start(new ProcessStartInfo(_artistInfoSourceUrl) { UseShellExecute = true });
    }

    /// <summary>Clears and hides the album and track sections shown under the artist biography.</summary>
    private void ResetArtistInfoAlbums()
    {
        ArtistInfoAlbumsPanel.Children.Clear();
        ArtistInfoAlbumsSection.IsVisible = false;
        ArtistInfoTracksDataGrid.ItemsSource = null;
        ArtistInfoTracksSection.IsVisible = false;
    }

    /// <summary>Renders the source-aware artist tracks in album and track-number order.</summary>
    /// <param name="sources">Catalog tracks paired with their owning remote server, or <see langword="null"/> for local tracks.</param>
    private void PopulateArtistInfoTracks(
        IReadOnlyList<(LibraryCatalogTrack Track, OrynivoServerSettings? Server)> sources)
    {
        var rows = sources
            .OrderBy(item => item.Track.Album ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Track.DiscNumber ?? 0)
            .ThenBy(item => item.Track.TrackNumber ?? int.MaxValue)
            .ThenBy(item => item.Track.Title ?? item.Track.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => ToCatalogTrackContentRow(item.Track, item.Server))
            .ToList();

        if (ArtistInfoTracksDataGrid.Columns.Count == 0)
            ConfigureArtistInfoTracksGrid();
        ArtistInfoTracksDataGrid.ItemsSource = rows;
        ArtistInfoTracksSection.IsVisible = rows.Count > 0;
    }

    /// <summary>Configures the compact track table embedded in the artist detail page.</summary>
    private void ConfigureArtistInfoTracksGrid()
    {
        ApplyColumns(
            "Tracks",
            ArtistInfoTracksDataGrid,
            captureCurrentWidths: false,
            tableKeyOverride: "ArtistInfoTracks");
    }

    /// <summary>Loads unified artist tracks independently from biography rendering and applies still-current results.</summary>
    /// <param name="requests">Source-aware provider, artist, and album requests.</param>
    /// <param name="loadVersion">Artist-detail load version used to discard stale results.</param>
    /// <param name="artistName">Displayed normalized artist identity.</param>
    /// <returns>A task representing the asynchronous track load.</returns>
    private async Task LoadAndPopulateArtistInfoTracksAsync(
        IReadOnlyList<(ILibraryCatalogProvider Provider, OrynivoServerSettings? Server, long ArtistId, long AlbumId)> requests,
        int loadVersion,
        string artistName)
    {
        await Task.Yield();
        var sources = new List<(LibraryCatalogTrack Track, OrynivoServerSettings? Server)>();
        foreach (var request in requests)
        {
            if (!IsCurrentArtistDetailLoad(loadVersion, artistName))
                return;
            try
            {
                IReadOnlyList<LibraryCatalogTrack> tracks;
                if (request.Server is null)
                {
                    tracks = await Task.Run(async () =>
                        await request.Provider.GetTracksByAlbumAsync(
                            request.AlbumId,
                            request.ArtistId));
                }
                else
                {
                    tracks = await request.Provider.GetTracksByAlbumAsync(
                        request.AlbumId,
                        request.ArtistId);
                }
                sources.AddRange(tracks.Select(track => (track, request.Server)));
            }
            catch
            {
                // One unavailable album must not hide tracks already loaded from
                // the local library or another reachable server.
            }
        }

        if (IsCurrentArtistDetailLoad(loadVersion, artistName))
            PopulateArtistInfoTracks(sources);
    }

    /// <summary>
    /// Loads the displayed artist's albums through a catalog provider and renders them as a wrapped
    /// strip of clickable cards under the biography. Used for local, remote-library, and now-playing
    /// remote artist-info views so all three look and navigate identically.
    /// </summary>
    /// <param name="provider">Local or remote catalog provider that owns the artist.</param>
    /// <param name="artistId">Provider-local artist identifier.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for the local library.</param>
    /// <param name="cancellationToken">Token cancelling a superseded load.</param>
    /// <returns>A task representing the asynchronous load.</returns>
    private async Task LoadArtistInfoAlbumsAsync(
        ILibraryCatalogProvider provider,
        long artistId,
        OrynivoServerSettings? server,
        CancellationToken cancellationToken)
    {
        try
        {
            var albums = await provider.GetAlbumsByArtistAsync(artistId, includeArtwork: true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PopulateArtistInfoAlbums(albums, server);
            try
            {
                var trackLists = await Task.WhenAll(albums.Select(album =>
                    provider.GetTracksByAlbumAsync(album.Id, artistId, cancellationToken)));
                cancellationToken.ThrowIfCancellationRequested();
                PopulateArtistInfoTracks(trackLists
                    .SelectMany(tracks => tracks)
                    .Select(track => (track, server))
                    .ToList());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Album cards remain useful even when the optional track table
                // cannot be loaded from this provider.
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ResetArtistInfoAlbums();
        }
    }

    /// <summary>Renders the artist album cards, or hides the section when there are none.</summary>
    /// <param name="albums">Albums to render.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for local albums.</param>
    private void PopulateArtistInfoAlbums(IReadOnlyList<LibraryCatalogAlbum> albums, OrynivoServerSettings? server)
    {
        ArtistInfoAlbumsPanel.Children.Clear();
        if (albums.Count == 0)
        {
            ArtistInfoAlbumsSection.IsVisible = false;
            return;
        }

        foreach (var album in albums)
            ArtistInfoAlbumsPanel.Children.Add(BuildArtistInfoAlbumCard(album, server));
        ArtistInfoAlbumsSection.IsVisible = true;
    }

    /// <summary>Renders one de-duplicated album strip combined from local and remote matching artists.</summary>
    /// <param name="sources">Source-aware album rows ordered with local entries first.</param>
    private void PopulateUnifiedArtistInfoAlbums(
        IReadOnlyList<(LibraryCatalogAlbum Album, OrynivoServerSettings? Server)> sources)
    {
        ArtistInfoAlbumsPanel.Children.Clear();
        var groups = sources
            .GroupBy(source =>
                $"{ArtistNameNormalizer.CreateComparisonKey(source.Album.Title)}|{source.Album.Year}",
                StringComparer.Ordinal)
            .OrderBy(group => group.Min(item => item.Album.Year ?? int.MaxValue))
            .ThenBy(group => group.First().Album.Title, StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in groups)
        {
            var groupedSources = group
                .OrderBy(item => item.Server is null ? 0 : 1)
                .ToList();
            var primary = groupedSources[0];
            ArtistInfoAlbumsPanel.Children.Add(BuildArtistInfoAlbumCard(
                primary.Album,
                primary.Server,
                groupedSources));
        }

        ArtistInfoAlbumsSection.IsVisible = ArtistInfoAlbumsPanel.Children.Count > 0;
    }

    /// <summary>Reloads the unified local/server album strip for an artist identity.</summary>
    /// <param name="artistName">Artist display name used for normalized identity matching.</param>
    /// <param name="cancellationToken">Token cancelling a superseded detail load.</param>
    private async Task LoadUnifiedArtistInfoAlbumsAsync(
        string artistName,
        CancellationToken cancellationToken)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        var sources = new List<(LibraryCatalogAlbum Album, OrynivoServerSettings? Server)>();
        var localArtists = await _localCatalogProvider.GetArtistsAsync(cancellationToken);
        foreach (var artist in localArtists.Where(candidate =>
                     ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
        {
            var albums = await _localCatalogProvider.GetAlbumsByArtistAsync(
                artist.Id,
                includeArtwork: true,
                cancellationToken);
            sources.AddRange(albums.Select(album => (album, (OrynivoServerSettings?)null)));
        }

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            try
            {
                var provider = CreateOrynivoCatalogProvider(server);
                var artists = await provider.GetArtistsAsync(cancellationToken);
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var albums = await provider.GetAlbumsByArtistAsync(
                        artist.Id,
                        includeArtwork: true,
                        cancellationToken);
                    sources.AddRange(albums.Select(album => (
                        album,
                        (OrynivoServerSettings?)server)));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // An unavailable server must not hide albums from other sources.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        PopulateUnifiedArtistInfoAlbums(sources);
    }

    /// <summary>Builds one album card opened by double-click from the artist-info album strip.</summary>
    /// <param name="album">The album to render.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for a local album.</param>
    /// <param name="logicalSources">Equivalent local and remote album identities represented by the card.</param>
    /// <returns>The card control.</returns>
    private Control BuildArtistInfoAlbumCard(
        LibraryCatalogAlbum album,
        OrynivoServerSettings? server,
        IReadOnlyList<(LibraryCatalogAlbum Album, OrynivoServerSettings? Server)>? logicalSources = null)
    {
        var row = ToCatalogAlbumContentRow(album, server);
        var representedSources = logicalSources is { Count: > 0 }
            ? logicalSources
            : [(album, server)];
        var distinctSourceCount = representedSources
            .Select(source => source.Server?.Id ?? LocalSourceKey)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctSourceCount > 1)
        {
            row.EntityType = "UnifiedAlbum";
            row.LogicalAlbumIds = representedSources.Select(source => source.Album.Id).Distinct().ToList();
            row.LogicalAlbumParts = representedSources
                .Select(source => new LogicalAlbumPart(
                    source.Album.Id,
                    source.Album.ArtistId,
                    source.Server))
                .ToList();
            row.IsFavorite = representedSources.Any(source => source.Album.IsFavorite);
        }
        if (server is null)
        {
            var localPath = !string.IsNullOrWhiteSpace(album.ThumbnailPath) && File.Exists(album.ThumbnailPath)
                ? album.ThumbnailPath
                : !string.IsNullOrWhiteSpace(album.ArtworkPath) && File.Exists(album.ArtworkPath)
                    ? album.ArtworkPath
                    : null;
            if (localPath is not null)
                _ = LoadArtistInfoAlbumArtworkAsync(row, localPath);
        }
        else
        {
            EnsureArtworkHydrated(row);
        }
        var card = new Border
        {
            Width           = 150,
            Margin          = new Thickness(0, 0, 12, 12),
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(10),
            Cursor          = new Cursor(StandardCursorType.Hand),
            ClipToBounds    = true
        };

        var stack = new StackPanel { Spacing = 2 };

        var avatar = new InitialsAvatar
        {
            DisplayName = album.Title,
            FontSize    = 30,
            Width       = 150,
            Height      = 150
        };
        var image = new Image
        {
            Width   = 150,
            Height  = 150,
            Stretch = Stretch.UniformToFill
        };
        var artworkHost = new Grid { Width = 150, Height = 150, ClipToBounds = true };
        artworkHost.Children.Add(avatar);
        artworkHost.Children.Add(image);
        image.Bind(Image.SourceProperty, new Binding(nameof(ContentRow.Artwork)) { Source = row });
        avatar.Bind(IsVisibleProperty, new Binding(nameof(ContentRow.Artwork))
        {
            Source = row,
            Converter = ObjectConverters.IsNull
        });
        var searchCoverButton = new Button
        {
            Content = LocalizationManager.Current.SearchCover,
            Tag = row,
            Width = 112,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Background = FindResource<IBrush>("AppAccentBrush"),
            Foreground = FindResource<IBrush>("AppAccentTextBrush"),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        searchCoverButton.Bind(IsVisibleProperty, new Binding(nameof(ContentRow.Artwork))
        {
            Source = row,
            Converter = ObjectConverters.IsNull
        });
        searchCoverButton.Click += SearchCoverButton_OnClick;
        artworkHost.Children.Add(searchCoverButton);
        stack.Children.Add(artworkHost);

        var titleButton = new Button
        {
            Content = album.Title,
            Margin = new Thickness(10, 8, 10, 1),
            Padding = new Thickness(0),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(titleButton, album.Title);
        titleButton.Click += (_, e) =>
        {
            e.Handled = true;
            _ = OpenArtistInfoAlbumCardAsync(album, server, row);
        };
        stack.Children.Add(titleButton);

        stack.Children.Add(new TextBlock
        {
            Text       = album.Year is int year && year > 0 ? year.ToString(CultureInfo.CurrentCulture) : string.Empty,
            FontSize   = 11,
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            Margin     = new Thickness(10, 0, 10, 2)
        });

        var footer = new Grid { Margin = new Thickness(8, 2, 8, 8) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var favoriteButton = new Button
        {
            Tag = row,
            Width = 28,
            Height = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = FindResource<IBrush>("AppFavoriteBrush"),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = ResolveFontSize("FontSizeBodyStrong"),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        favoriteButton.Bind(Button.ContentProperty, new Binding(nameof(ContentRow.FavoriteGlyph)) { Source = row });
        favoriteButton.Click += FavoriteButton_OnClick;
        footer.Children.Add(favoriteButton);

        var sourceBadgeText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppAccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        sourceBadgeText.Bind(TextBlock.TextProperty, new Binding(nameof(ContentRow.SourceBadge)) { Source = row });
        var sourceBadge = new Border
        {
            Height = 20,
            MinWidth = 26,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(10),
            Background = FindResource<IBrush>("AppSurfaceHoverBrush"),
            BorderBrush = FindResource<IBrush>("AppAccentBrush"),
            BorderThickness = new Thickness(1),
            Child = sourceBadgeText
        };
        ToolTip.SetTip(sourceBadge, row.SourceName);
        Grid.SetColumn(sourceBadge, 1);
        footer.Children.Add(sourceBadge);
        stack.Children.Add(footer);

        card.Child = stack;
        card.DoubleTapped += (_, e) =>
        {
            if (FindAncestor<Button>(e.Source as Visual) is not null)
                return;
            e.Handled = true;
            _ = OpenArtistInfoAlbumCardAsync(album, server, row);
        };
        return card;
    }

    /// <summary>Opens every represented source for a unified card, or the single owning album otherwise.</summary>
    /// <param name="album">Primary album represented by the card.</param>
    /// <param name="server">Primary album's remote server, or <see langword="null"/> for local.</param>
    /// <param name="row">Source-aware card row carrying optional logical album parts.</param>
    /// <returns>A task representing the navigation.</returns>
    private async Task OpenArtistInfoAlbumCardAsync(
        LibraryCatalogAlbum album,
        OrynivoServerSettings? server,
        ContentRow row)
    {
        if (row.EntityType != "UnifiedAlbum")
        {
            await OpenArtistInfoAlbumAsync(album, server);
            return;
        }

        await OpenLogicalAlbumTracksAsync(row);
        ArtistInfoView.IsVisible = false;
        _artistInfoUnifiedArtistName = null;
        BackButton.IsVisible = true;
    }

    /// <summary>Decodes a local artist-detail album image off the UI thread and applies it to its card row.</summary>
    /// <param name="row">Album row bound to the artist-detail card.</param>
    /// <param name="path">Local cached artwork path.</param>
    /// <returns>A task representing the asynchronous decode.</returns>
    private static async Task LoadArtistInfoAlbumArtworkAsync(ContentRow row, string path)
    {
        var artwork = await Task.Run(() => CreateArtworkImage(path, 320));
        if (artwork is null)
            return;
        row.Artwork = artwork;
        row.Thumbnail = artwork;
        row.ArtworkLoadCompleted = true;
        row.ThumbnailLoadCompleted = true;
    }

    /// <summary>Opens the artist-scoped album tracks and then closes the artist-info overlay.</summary>
    /// <param name="album">The album to open.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for a local album.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task OpenArtistInfoAlbumAsync(LibraryCatalogAlbum album, OrynivoServerSettings? server)
    {
        var artistName = _artistInfoUnifiedArtistName ??
                         ArtistInfoTitleButton.Content as string ??
                         album.DisplayArtist;
        if (server is null)
        {
            await ShowAlbumTracksAsync(album.Id, album.Title, album.ArtistId, artistName);
        }
        else
        {
            _activeOrynivoServer = server;
            await OpenOrynivoAlbumTracksAsync(
                album.Id,
                album.Title,
                album.DisplayArtist,
                album.ArtistId,
                artistName);
        }

        ArtistInfoView.IsVisible = false;
        _artistInfoUnifiedArtistName = null;
        BackButton.IsVisible = true;
    }

    private async Task ShowArtistInfoAsync(
        long artistId,
        bool forceRefresh,
        string? profileLookupName = null)
    {
        _artistInfoDisplayedRemoteRow = null;
        _nowPlayingRemoteArtistInfo = false;
        _artistInfoDisplayedId = artistId;
        if (string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
            ResetArtistInfoAlbums();
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
        EditArtistNameButton.IsVisible = true;
        SetArtistImageActionsVisible(true);
        RefreshArtistInfoButton.IsEnabled = false;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;

        try
        {
            var artist = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetArtistById(artistId);
            }, cts.Token);
            if (artist is null)
            {
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
                return;
            }

            ArtistInfoTitleButton.Content = artist.Artist;
            SetArtistInfoFavoriteState(artist.IsFavorite);
            var language = GetProfileLanguageCode();
            var fetchedAt = artist.ProfileFetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var needsDownload = forceRefresh ||
                string.IsNullOrWhiteSpace(artist.Biography) ||
                !string.Equals(artist.ProfileLanguage, language, StringComparison.OrdinalIgnoreCase) ||
                fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-90);

            if (needsDownload)
            {
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
                var profile = await ArtistProfileService.DownloadAsync(
                    artist.Id,
                    profileLookupName ?? artist.Artist,
                    language,
                    downloadImage: !artist.ImageIsManual,
                    cancellationToken: cts.Token,
                    musicBrainzArtistId: artist.MusicBrainzArtistId);
                cts.Token.ThrowIfCancellationRequested();
                artist = await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    db.UpdateArtistProfile(
                        artist.Id,
                        profile?.Biography,
                        profile?.ImagePath,
                        profile?.SourceUrl,
                        language);
                    return db.GetArtistById(artist.Id);
                }, cts.Token);

                byte[]? imageData = null;
                string? imageMimeType = null;
                if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
                {
                    imageData = await File.ReadAllBytesAsync(profile.ImagePath, cts.Token);
                    imageMimeType = GuessImageMimeType(profile.ImagePath);
                }
                await SynchronizeUnifiedArtistProfileAsync(
                    artist?.Artist,
                    profile?.Biography,
                    profile?.SourceUrl,
                    language,
                    imageData,
                    imageMimeType,
                    cts.Token);
            }

            if (artist is null)
                return;

            ArtistInfoBiographyTextBlock.Text = artist.Biography ?? string.Empty;
            ArtistInfoImage.Source = await Task.Run(
                () => CreateArtworkImage(artist.ImagePath, 1000, ignoreCache: true),
                cts.Token);
            if (ArtistInfoImage.Source is null)
            {
                ArtistInfoImagePlaceholder.IsVisible = true;
                string imageMsg;
                if (string.IsNullOrWhiteSpace(artist.ImagePath))
                {
                    imageMsg = LocalizationManager.Current.ArtistInfoNoImage;
                    var diag = ArtistProfileService.LastImageDiagnostic;
                    if (!string.IsNullOrWhiteSpace(diag))
                        imageMsg += $"\n{diag}";
                }
                else if (!File.Exists(artist.ImagePath))
                    imageMsg = $"{LocalizationManager.Current.ArtistInfoImageMissing}:\n{artist.ImagePath}";
                else
                    imageMsg = $"{LocalizationManager.Current.ArtistInfoImageLoadError}:\n{artist.ImagePath}";
                ArtistInfoImageStatusText.Text = imageMsg;
                ArtistInfoImageStatusText.IsVisible = true;
            }
            else
            {
                ArtistInfoImagePlaceholder.IsVisible = false;
                ArtistInfoImageStatusText.IsVisible = false;
            }
            _artistInfoSourceUrl = artist.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.IsVisible = !(string.IsNullOrWhiteSpace(_artistInfoSourceUrl));
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible = string.IsNullOrWhiteSpace(artist.Biography);
            await RefreshVisibleArtistRowAsync(artist);
            if (string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
                await LoadArtistInfoAlbumsAsync(_localCatalogProvider, artistId, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloadFailed;
        }
        finally
        {
            if (_artistProfileCts == cts)
            {
                _artistProfileCts = null;
                RefreshArtistInfoButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    private async Task ShowOrynivoArtistInfoAsync(
        ContentRow row,
        bool forceRefresh,
        string? profileLookupName = null)
    {
        if (ResolveRowOrynivoServer(row) is not { } server ||
            row.Id is not long artistId ||
            row.EntityType != "OrynivoArtist")
        {
            return;
        }

        _artistInfoDisplayedRemoteRow = row;
        _nowPlayingRemoteArtistInfo = false;
        _artistInfoDisplayedId = null;
        if (string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
            ResetArtistInfoAlbums();
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
        EditArtistNameButton.IsVisible = true;
        SetArtistImageActionsVisible(true);
        RefreshArtistInfoButton.IsEnabled = false;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;

        try
        {
            ArtistInfoTitleButton.Content = row.Title ?? LocalizationManager.Current.Unknown;
            SetArtistInfoFavoriteState(row.IsFavorite);
            var language = GetProfileLanguageCode();
            if (!forceRefresh &&
                (string.IsNullOrWhiteSpace(row.Biography) ||
                 string.IsNullOrWhiteSpace(row.ArtworkPath) ||
                 row.ProfileFetchedAt is null))
            {
                var cached = await _orynivoClient.GetArtistAsync(server, artistId, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (cached is not null)
                {
                    ApplyOrynivoArtistProfile(server, row, cached);
                    ArtistInfoTitleButton.Content = row.Title ?? LocalizationManager.Current.Unknown;
                }
            }

            var fetchedAt = row.ProfileFetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var needsDownload = forceRefresh ||
                string.IsNullOrWhiteSpace(row.Biography) ||
                !string.Equals(row.ProfileLanguage, language, StringComparison.OrdinalIgnoreCase) ||
                fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-90);

            if (needsDownload)
            {
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
                var profile = await ArtistProfileService.DownloadAsync(
                    artistId,
                    profileLookupName ?? row.Title ?? string.Empty,
                    language,
                    downloadImage: !row.ImageIsManual,
                    cancellationToken: cts.Token);
                cts.Token.ThrowIfCancellationRequested();

                byte[]? imageData = null;
                string? imageMimeType = null;
                if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
                {
                    imageData = await File.ReadAllBytesAsync(profile.ImagePath, cts.Token);
                    imageMimeType = GuessImageMimeType(profile.ImagePath);
                }

                var refreshed = await _orynivoClient.UpdateArtistProfileAsync(
                    server,
                    artistId,
                    profile?.Biography,
                    profile?.SourceUrl,
                    profile?.Language ?? language,
                    imageData,
                    imageMimeType,
                    cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (refreshed is null)
                {
                    ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
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
                }
                DeleteOrynivoArtistListCache(server);
                await SynchronizeUnifiedArtistProfileAsync(
                    row.Title,
                    refreshed.Biography,
                    refreshed.SourceUrl,
                    refreshed.ProfileLanguage ?? language,
                    imageData,
                    imageMimeType,
                    cts.Token);
            }

            ArtistInfoBiographyTextBlock.Text = row.Biography ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(row.ArtworkPath))
                ArtistInfoImage.Source = await LoadRemoteArtworkImageAsync(row.ArtworkPath, 1000, cts.Token);

            if (ArtistInfoImage.Source is null)
            {
                ArtistInfoImagePlaceholder.IsVisible = true;
                ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistInfoNoImage;
                ArtistInfoImageStatusText.IsVisible = true;
            }
            else
            {
                ArtistInfoImagePlaceholder.IsVisible = false;
                ArtistInfoImageStatusText.IsVisible = false;
            }

            _artistInfoSourceUrl = row.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.IsVisible = !string.IsNullOrWhiteSpace(_artistInfoSourceUrl);
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible = string.IsNullOrWhiteSpace(row.Biography);
            if (string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
            {
                await LoadArtistInfoAlbumsAsync(
                    CreateOrynivoCatalogProvider(server),
                    artistId,
                    server,
                    cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloadFailed;
        }
        finally
        {
            if (_artistProfileCts == cts)
            {
                _artistProfileCts = null;
                RefreshArtistInfoButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    /// <summary>
    /// Shows the artist-info view for the artist of the currently playing remote Orynivo Server
    /// track, resolving the profile through the active now-playing metadata provider so the
    /// biography and image are cached on that server.
    /// </summary>
    /// <param name="forceRefresh">Whether to force a fresh download of the artist profile.</param>
    /// <param name="profileLookupName">Optional external lookup name that leaves the library name unchanged.</param>
    private async Task ShowNowPlayingRemoteArtistInfoAsync(
        bool forceRefresh,
        string? profileLookupName = null)
    {
        if (_currentOrynivoTrackRow is not { ArtistId: long artistId } row ||
            _currentNowPlayingProvider is not OrynivoServerNowPlayingMetadataProvider provider)
        {
            return;
        }

        _nowPlayingRemoteArtistInfo = true;
        _artistInfoDisplayedRemoteRow = null;
        _artistInfoDisplayedId = null;
        ResetArtistInfoAlbums();
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
        EditArtistNameButton.IsVisible = false;
        SetArtistImageActionsVisible(row.OrynivoServer is not null);
        RefreshArtistInfoButton.IsEnabled = false;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;

        var artistName = string.IsNullOrWhiteSpace(row.Artist)
            ? LocalizationManager.Current.Unknown
            : row.Artist;
        ArtistInfoTitleButton.Content = artistName;
        SetArtistInfoFavoriteState(row.IsFavorite);

        try
        {
            var language = GetProfileLanguageCode();
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
            var profile = await provider.GetArtistProfileAsync(
                new NowPlayingArtistContext(artistId, profileLookupName ?? artistName),
                language,
                forceRefresh,
                cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            byte[]? imageData = null;
            string? imageMimeType = null;
            if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
            {
                imageData = await File.ReadAllBytesAsync(profile.ImagePath, cts.Token);
                imageMimeType = GuessImageMimeType(profile.ImagePath);
            }
            if (profile is not null)
            {
                await SynchronizeUnifiedArtistProfileAsync(
                    artistName,
                    profile.Biography,
                    profile.SourceUrl,
                    language,
                    imageData,
                    imageMimeType,
                    cts.Token);
            }

            ArtistInfoBiographyTextBlock.Text = profile?.Biography ?? string.Empty;
            ArtistInfoImage.Source = await Task.Run(
                () => CreateArtworkImage(profile?.ImagePath, 1000, ignoreCache: true),
                cts.Token);
            if (ArtistInfoImage.Source is null)
            {
                ArtistInfoImagePlaceholder.IsVisible = true;
                ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistInfoNoImage;
                ArtistInfoImageStatusText.IsVisible = true;
            }
            else
            {
                ArtistInfoImagePlaceholder.IsVisible = false;
                ArtistInfoImageStatusText.IsVisible = false;
            }

            _artistInfoSourceUrl = profile?.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.IsVisible = !string.IsNullOrWhiteSpace(_artistInfoSourceUrl);
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible = string.IsNullOrWhiteSpace(profile?.Biography);
            if (row.OrynivoServer is { } npServer)
                await LoadArtistInfoAlbumsAsync(
                    CreateOrynivoCatalogProvider(npServer),
                    artistId,
                    npServer,
                    cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloadFailed;
        }
        finally
        {
            if (_artistProfileCts == cts)
            {
                _artistProfileCts = null;
                RefreshArtistInfoButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    private async Task EnsureArtistProfileAsync(ContentRow row)
    {
        if (row.Id is not long artistId || row.EntityType != "Artist")
            return;

        var language = GetProfileLanguageCode();
        var fetchedAt = row.ProfileFetchedAt is long timestamp
            ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
            : (DateTimeOffset?)null;
        if (fetchedAt >= DateTimeOffset.UtcNow.AddDays(-90) &&
            string.Equals(row.ProfileLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!_artistProfilesLoading.Add(artistId))
            return;

        // Mark this row as fetched for the current session up front. If the download or
        // database write below fails (e.g. no network, or a concurrent library scan holds
        // the SQLite write lock), the freshness check above must still short-circuit so the
        // same row does not re-trigger a network request and database open on every scroll
        // pass. A freshly merged library has thousands of artists without cached profiles;
        // retrying each visible row repeatedly previously flooded the UI thread and froze
        // the artist table.
        row.ProfileLanguage = language;
        row.ProfileFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var backgroundToken = _backgroundArtistLoadCts.Token;
        try
        {
            var profile = await ArtistProfileService.DownloadAsync(
                artistId,
                row.Title ?? string.Empty,
                language,
                downloadImage: !row.ImageIsManual,
                cancellationToken: backgroundToken);

            // Persist the (possibly negative) result and decode the artwork off the UI thread.
            // Opening the database runs connection pragmas and schema DDL, which must never
            // happen on the UI thread once per visible artist row.
            var imagePath = profile?.ImagePath;
            var (artwork, thumbnail) = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.UpdateArtistProfile(
                    artistId,
                    profile?.Biography,
                    profile?.ImagePath,
                    profile?.SourceUrl,
                    language);
                return string.IsNullOrWhiteSpace(imagePath)
                    ? ((IImage?)null, (IImage?)null)
                    : (CreateArtworkImage(imagePath, 320, ignoreCache: true),
                       CreateArtworkImage(imagePath, 96, ignoreCache: true));
            }, backgroundToken);

            if (backgroundToken.IsCancellationRequested)
                return;

            // The awaits above do not suppress the captured context, so these row updates
            // run on the UI thread as required by INotifyPropertyChanged.
            row.Biography = profile?.Biography;
            row.SourceUrl = profile?.SourceUrl;
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                row.ArtworkPath = imagePath;
                row.ThumbnailPath = imagePath;
                row.ArtworkLoadCompleted = false;
                row.Artwork = artwork;
                row.Thumbnail = thumbnail;
                row.ArtworkLoadCompleted = true;
            }
        }
        catch
        {
            // Künstlerkarten bleiben auch ohne Netzwerk nutzbar.
        }
        finally
        {
            _artistProfilesLoading.Remove(artistId);
        }
    }

    private async Task RefreshVisibleArtistRowAsync(ArtistInfo artist)
    {
        var rows = ContentDataGrid.ItemsSource as IEnumerable<ContentRow>
            ?? ArtistArtworkListBox.ItemsSource as IEnumerable<ContentRow>;
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artist.Artist);
        var row = rows?.FirstOrDefault(item =>
            (item.EntityType == "Artist" && item.Id == artist.Id) ||
            (item.EntityType == "UnifiedArtist" &&
             ArtistNameNormalizer.CreateComparisonKey(item.Title) == comparisonKey));
        if (row is null)
            return;

        row.Biography = artist.Biography;
        row.SourceUrl = artist.SourceUrl;
        row.ProfileLanguage = artist.ProfileLanguage;
        row.ProfileFetchedAt = artist.ProfileFetchedAt;
        row.ImageIsManual = artist.ImageIsManual;
        row.ArtworkPath = artist.ImagePath;
        row.ThumbnailPath = artist.ImagePath;
        row.ArtworkLoadCompleted = false;
        row.Artwork = await Task.Run(() => CreateArtworkImage(artist.ImagePath, 320, ignoreCache: true));
        row.Thumbnail = await Task.Run(() => CreateArtworkImage(artist.ImagePath, 96, ignoreCache: true));
        row.ArtworkLoadCompleted = true;
    }

    private string GetProfileLanguageCode() => _settings.Language switch
    {
        Orynivo.Localization.Language.German => "de",
        Orynivo.Localization.Language.French => "fr",
        Orynivo.Localization.Language.Spanish => "es",
        Orynivo.Localization.Language.Russian => "ru",
        Orynivo.Localization.Language.ChineseSimplified => "zh",
        Orynivo.Localization.Language.Hindi => "hi",
        _ => "en"
    };

    private void CancelArtistProfileLoad()
    {
        _artistProfileCts?.Cancel();
        _backgroundArtistLoadCts.Cancel();
        _backgroundArtistLoadCts = new CancellationTokenSource();
    }
}
