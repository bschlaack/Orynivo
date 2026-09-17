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
/// Unified local and Orynivo Server artist album navigation.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Opens every matching local and Orynivo Server album for a local artist name.</summary>
    /// <param name="artistId">Local artist identifier retained by the calling navigation surface.</param>
    /// <param name="artistName">Artist display name used for cross-library identity matching.</param>
    /// <returns>A task representing the unified navigation operation.</returns>
    private Task ShowArtistAlbumsAsync(long artistId, string artistName) =>
        ShowUnifiedArtistAlbumsAsync(artistName);

    private bool IsCurrentArtistDetailLoad(int loadVersion, string artistName) =>
        loadVersion == _artistDetailLoadVersion &&
        ArtistInfoView.IsVisible &&
        string.Equals(_artistInfoUnifiedArtistName, artistName, StringComparison.Ordinal);

    private async Task ShowUnifiedArtistAlbumsAsync(string artistName)
    {
        var loadVersion = ++_artistDetailLoadVersion;
        CancelArtistProfileLoad();
        PushCurrentNavigationState();
        _currentTopLevelTag = "UnifiedArtistAlbums";
        _activeArtistFilterId = null;
        _activeArtistFilterName = artistName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        ContentTitleTextBlock.Text = artistName;
        ContentCountTextBlock.Text = string.Empty;
        AlbumViewModeBorder.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        SaveSmartPlaylistButton.IsVisible = false;
        UpdateEntityFavoritesFilterToggle(null);
        UpdateLibraryIntroCard(null);
        LyricsView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        ArtistInfoView.IsVisible = true;
        ArtistInfoTitleButton.Content = artistName;
        ResetArtistInfoSurface(artistName);
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ResetArtistInfoAlbums();
        SetArtistInfoFavoriteState(false);
        _artistInfoUnifiedArtistName = artistName;
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        var albumSources = new List<(LibraryCatalogAlbum Album, OrynivoServerSettings? Server)>();
        var artistSources = new List<(LibraryCatalogArtist Artist, OrynivoServerSettings? Server)>();
        var trackRequests = new List<(ILibraryCatalogProvider Provider, OrynivoServerSettings? Server, long ArtistId, long AlbumId)>();
        var localArtists = await Task.Run(async () =>
            await _localCatalogProvider.GetArtistsAsync());
        if (!IsCurrentArtistDetailLoad(loadVersion, artistName))
            return;
        var matchingLocalArtists = localArtists.Where(candidate =>
                ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey)
            .ToList();
        artistSources.AddRange(matchingLocalArtists.Select(artist => (
            artist,
            (OrynivoServerSettings?)null)));
        var isFavorite = matchingLocalArtists.Any(artist => artist.IsFavorite);
        var localCatalogLists = await Task.Run(async () =>
        {
            var lists = new List<(long ArtistId, IReadOnlyList<LibraryCatalogAlbum> Albums)>();
            foreach (var artist in matchingLocalArtists)
            {
                var albums = await _localCatalogProvider.GetAlbumsByArtistAsync(artist.Id, includeArtwork: true);
                lists.Add((artist.Id, albums));
            }
            return lists;
        });
        if (!IsCurrentArtistDetailLoad(loadVersion, artistName))
            return;
        foreach (var catalog in localCatalogLists)
        {
            albumSources.AddRange(catalog.Albums.Select(album => (album, (OrynivoServerSettings?)null)));
            trackRequests.AddRange(catalog.Albums.Select(album => (
                (ILibraryCatalogProvider)_localCatalogProvider,
                (OrynivoServerSettings?)null,
                catalog.ArtistId,
                album.Id)));
        }
        PopulateUnifiedArtistInfoAlbums(albumSources);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        ContentRow? matchingRemoteRow = null;
        var remoteLoads = (_settings.OrynivoServers ?? [])
            .Select(async server =>
            {
                try
                {
                    var provider = CreateOrynivoCatalogProvider(server);
                    var artists = await provider.GetArtistsAsync();
                    var matches = artists.Where(candidate =>
                            ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey)
                        .ToList();
                    var albums = new List<LibraryCatalogAlbum>();
                    var albumRequests = new List<(long ArtistId, long AlbumId)>();
                    foreach (var artist in matches)
                    {
                        var artistAlbums = await provider.GetAlbumsByArtistAsync(
                            artist.Id,
                            includeArtwork: true);
                        albums.AddRange(artistAlbums);
                        foreach (var album in artistAlbums)
                            albumRequests.Add((artist.Id, album.Id));
                    }
                    return (Server: server, Artists: matches, Albums: albums, AlbumRequests: albumRequests);
                }
                catch
                {
                    return (
                        Server: server,
                        Artists: new List<LibraryCatalogArtist>(),
                        Albums: new List<LibraryCatalogAlbum>(),
                        AlbumRequests: new List<(long ArtistId, long AlbumId)>());
                }
            })
            .ToList();
        foreach (var remote in await Task.WhenAll(remoteLoads))
        {
            if (!IsCurrentArtistDetailLoad(loadVersion, artistName))
                return;
            foreach (var artist in remote.Artists)
            {
                isFavorite |= artist.IsFavorite;
                matchingRemoteRow ??= ToCatalogArtistContentRow(artist, remote.Server);
                artistSources.Add((artist, remote.Server));
            }
            albumSources.AddRange(remote.Albums.Select(album => (
                album,
                (OrynivoServerSettings?)remote.Server)));
            var provider = CreateOrynivoCatalogProvider(remote.Server);
            trackRequests.AddRange(remote.AlbumRequests.Select(request => (
                (ILibraryCatalogProvider)provider,
                (OrynivoServerSettings?)remote.Server,
                request.ArtistId,
                request.AlbumId)));
            PopulateUnifiedArtistInfoAlbums(albumSources);
        }
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (!IsCurrentArtistDetailLoad(loadVersion, artistName))
            return;

        using (var artworkSyncCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            try
            {
                await SynchronizeMissingArtistArtworkAsync(artistName, artistSources, artworkSyncCts.Token);
                albumSources = await SynchronizeMissingAlbumArtworkAsync(albumSources, artworkSyncCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Artwork synchronization is optional and must not block artist navigation.
            }
        }
        if (!IsCurrentArtistDetailLoad(loadVersion, artistName))
            return;

        _ = LoadAndPopulateArtistInfoTracksAsync(
            trackRequests,
            loadVersion,
            artistName);

        if (matchingLocalArtists.FirstOrDefault() is { } localArtist)
            await ShowArtistInfoAsync(localArtist.Id, forceRefresh: false);
        else if (matchingRemoteRow is not null)
            await ShowOrynivoArtistInfoAsync(matchingRemoteRow, forceRefresh: false);
        else
        {
            ArtistInfoTitleButton.Content = artistName;
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible = true;
        }

        _artistInfoUnifiedArtistName = artistName;
        PopulateUnifiedArtistInfoAlbums(albumSources);
        SetArtistInfoFavoriteState(isFavorite);
        ArtistInfoCloseButton.IsVisible = false;
        BackButton.IsVisible = true;
    }
}
