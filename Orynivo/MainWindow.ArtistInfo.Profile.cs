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
/// Artist detail surfaces, biography/profile loading, and now-playing artist info.
/// </summary>
public partial class MainWindow : Window
{
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
