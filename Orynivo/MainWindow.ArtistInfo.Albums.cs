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
/// Artist row conversion plus unified artist album and track population.
/// </summary>
public partial class MainWindow : Window
{
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
}
