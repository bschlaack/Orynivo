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
    private MenuFlyout BuildPlaylistContextFlyout(IReadOnlyList<string> paths)
    {
        var menu = CreateSidebarMenuFlyout();
        if (TryCreatePlaylistSelection(paths, out var provider, out var selection))
            AppendPlaylistItems(menu, provider, selection);
        else
            foreach (var item in CreateQueueMenuItems(paths))
                menu.Items.Add(item);
        return menu;
    }

    private MenuFlyout BuildQueueContextFlyout(IReadOnlyList<string> paths)
    {
        var menu = CreateSidebarMenuFlyout();
        foreach (var item in CreateQueueMenuItems(paths))
            menu.Items.Add(item);
        return menu;
    }

    private void AppendPlaylistItems(MenuFlyout menu, IReadOnlyList<string> paths)
    {
        if (!TryCreatePlaylistSelection(paths, out var provider, out var selection))
            return;
        AppendPlaylistItems(menu, provider, selection);
    }

    private void AppendPlaylistItems(ItemsControl menu, IReadOnlyList<string> paths)
    {
        if (!TryCreatePlaylistSelection(paths, out var provider, out var selection))
            return;
        AppendPlaylistItems(menu, provider, selection);
    }

    private void AppendPlaylistItems(ItemsControl menu, ILibraryPlaylistProvider provider, PlaylistSelection selection)
    {
        foreach (var item in CreatePlaylistMenuItems(provider, selection))
            menu.Items.Add(item);
    }

    private void AppendPlaylistItems(MenuFlyout menu, ILibraryPlaylistProvider provider, PlaylistSelection selection)
    {
        foreach (var item in CreatePlaylistMenuItems(provider, selection))
            menu.Items.Add(item);
    }

    private IReadOnlyList<Control> CreatePlaylistMenuItems(ILibraryPlaylistProvider provider, PlaylistSelection selection)
    {
        var items = new List<Control>(CreateQueueMenuItems(selection.QueuePaths));
        items.Add(new Separator());

        var header = CreateFlyoutMenuItem(
            LocalizationManager.Current.AddToPlaylist,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontSize = 11;
        header.FontWeight = FontWeight.SemiBold;
        items.Add(header);

        var sep0 = new Separator();
        items.Add(sep0);

        var playlists = provider.GetWritablePlaylists();

        foreach (var pl in playlists)
        {
            var item = CreateFlyoutMenuItem(pl.Name);
            item.Tag = new PlaylistActionTag(provider, pl.Id, selection);
            item.Click += PlaylistMenuItem_OnClick;
            items.Add(item);
        }

        if (playlists.Count > 0)
        {
            var sep1 = new Separator();
            items.Add(sep1);
        }

        var newItem = CreateFlyoutMenuItem(LocalizationManager.Current.NewPlaylist);
        newItem.Tag = new NewPlaylistActionTag(provider, selection);
        newItem.Click += NewPlaylistMenuItem_OnClick;
        items.Add(newItem);
        return items;
    }

    private bool TryCreatePlaylistSelection(
        IReadOnlyList<string> paths,
        out ILibraryPlaylistProvider provider,
        out PlaylistSelection selection)
    {
        provider = _localPlaylistProvider;
        selection = new PlaylistSelection(paths, paths, []);
        return paths.Count > 0 && paths.All(CanPersistQueuePath);
    }

    private IReadOnlyList<OrynivoPlaylistInfo> GetCachedOrynivoPlaylists(OrynivoServerSettings server) =>
        _orynivoPlaylistsByTag
            .Where(pair =>
            {
                var parts = pair.Key.Split(':');
                return parts.Length == 3 && parts[1] == server.Id;
            })
            .Select(pair => pair.Value)
            .ToList();

    private bool TryGetOrynivoPlaylistTarget(
        IReadOnlyList<string> paths,
        out OrynivoServerSettings? server,
        out List<long> trackIds)
    {
        server = null;
        trackIds = [];
        foreach (var path in paths)
        {
            if (!_orynivoTracksByUrl.TryGetValue(path, out var row) ||
                row.OrynivoServer is null ||
                row.Id is not long trackId)
            {
                return false;
            }

            server ??= row.OrynivoServer;
            if (server.Id != row.OrynivoServer.Id)
                return false;
            trackIds.Add(trackId);
        }

        return trackIds.Count > 0 && server is not null;
    }

    private IReadOnlyList<Control> CreateQueueMenuItems(IReadOnlyList<string> paths)
    {
        var items = new List<Control>();
        var queueHeader = CreateFlyoutMenuItem(
            LocalizationManager.Current.UpNext,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        queueHeader.IsHitTestVisible = false;
        queueHeader.Focusable = false;
        queueHeader.FontSize = 11;
        queueHeader.FontWeight = FontWeight.SemiBold;
        items.Add(queueHeader);

        var playNextItem = CreateFlyoutMenuItem(LocalizationManager.Current.PlayNext);
        playNextItem.Tag = paths;
        playNextItem.Click += PlayNextMenuItem_OnClick;
        items.Add(playNextItem);

        var appendQueueItem = CreateFlyoutMenuItem(LocalizationManager.Current.AppendToQueue);
        appendQueueItem.Tag = paths;
        appendQueueItem.Click += AppendToQueueMenuItem_OnClick;
        items.Add(appendQueueItem);
        return items;
    }

    private async void PlayNextMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IReadOnlyList<string> paths } || paths.Count == 0)
            return;

        var insertIndex = Math.Clamp(_queueIndex + 1, 0, _queue.Count);
        foreach (var path in paths)
            _queue.Insert(insertIndex++, CreatePlaylistItem(path));
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksQueuedNext,
            paths.Count);
        await RefreshActiveGaplessQueueAsync();
    }

    private async void AppendToQueueMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IReadOnlyList<string> paths } || paths.Count == 0)
            return;

        foreach (var path in paths)
            _queue.Add(CreatePlaylistItem(path));
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksAppendedToQueue,
            paths.Count);
        await RefreshActiveGaplessQueueAsync();
    }

    private async void PlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PlaylistActionTag tag })
            return;

        var tracksCount = GetPlaylistSelectionTrackCount(tag.Selection);
        if (tracksCount == 0)
            return;

        var playlistName = tag.Provider
            .GetWritablePlaylists()
            .FirstOrDefault(playlist => playlist.Id == tag.PlaylistId)
            ?.Name ?? string.Empty;
        var ok = await tag.Provider.AddTracksAsync(tag.PlaylistId, tag.Selection);
        if (!ok)
        {
            StatusTextBlock.Text = tag.Provider.NavigationRefresh == PlaylistNavigationRefresh.OrynivoServer
                ? LocalizationManager.Current.OrynivoConnectionFailed
                : string.Empty;
            return;
        }

        RefreshPlaylistNavigation(tag.Provider.NavigationRefresh);

        StatusTextBlock.Text = tracksCount == 1
            ? string.Format(LocalizationManager.Current.TrackAddedToPlaylist, playlistName)
            : string.Format(LocalizationManager.Current.TracksAddedToPlaylist, tracksCount, playlistName);
    }

    private async void NewPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: NewPlaylistActionTag tag })
            return;

        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false || string.IsNullOrWhiteSpace(dialog.PlaylistName))
            return;

        var tracksCount = GetPlaylistSelectionTrackCount(tag.Selection);
        if (tracksCount == 0)
            return;

        var playlistName = dialog.PlaylistName.Trim();
        var playlist = await tag.Provider.CreatePlaylistAsync(playlistName, tag.Selection);
        if (playlist is null)
        {
            StatusTextBlock.Text = tag.Provider.NavigationRefresh == PlaylistNavigationRefresh.OrynivoServer
                ? LocalizationManager.Current.OrynivoConnectionFailed
                : string.Empty;
            return;
        }

        RefreshPlaylistNavigation(tag.Provider.NavigationRefresh);
        StatusTextBlock.Text = tracksCount == 1
            ? string.Format(LocalizationManager.Current.TrackAddedToPlaylist, playlistName)
            : string.Format(LocalizationManager.Current.TracksAddedToPlaylist, tracksCount, playlistName);
    }

    private static int GetPlaylistSelectionTrackCount(PlaylistSelection selection) =>
        selection.RemoteTrackIds.Count > 0 ? selection.RemoteTrackIds.Count : selection.LocalPaths.Count;

    private void RefreshPlaylistNavigation(PlaylistNavigationRefresh refresh)
    {
        if (refresh == PlaylistNavigationRefresh.OrynivoServer)
            LoadOrynivoServerNavigation();
        else
            LoadNavPlaylists();
    }

    private List<string> GetPathsForRow(ContentRow row)
    {
        if (!string.IsNullOrEmpty(row.FilePath))
            return [GetPersistablePlaylistPath(row)];

        if (row.Id is not long albumId)
            return [];

        if (row.EntityType == "OrynivoAlbum" && row.OrynivoServer is { } server)
        {
            try
            {
                var provider = CreateOrynivoCatalogProvider(server);
                return provider.GetTracksByAlbumAsync(albumId)
                    .GetAwaiter()
                    .GetResult()
                    .Select(track => BuildOrynivoPlaylistReference(server, track.Id))
                    .ToList();
            }
            catch { return []; }
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetTrackListByAlbum(albumId).Select(t => t.Path).ToList();
        }
        catch { return []; }
    }

    private static string GetPersistablePlaylistPath(ContentRow row) =>
        row.OrynivoServer is { } server && row.Id is long trackId && row.EntityType == "OrynivoTrack"
            ? BuildOrynivoPlaylistReference(server, trackId)
            : row.FilePath;

    private static string BuildOrynivoPlaylistReference(OrynivoServerSettings server, long trackId) =>
        $"orynivo://{Uri.EscapeDataString(server.Id)}/track/{trackId.ToString(CultureInfo.InvariantCulture)}";

    private bool TryResolveOrynivoPlaylistReference(
        string path,
        out OrynivoServerSettings server,
        out long trackId)
    {
        server = null!;
        trackId = 0;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("orynivo", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2 ||
            !segments[0].Equals("track", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
        {
            return false;
        }

        var serverId = Uri.UnescapeDataString(uri.Host);
        server = (_settings.OrynivoServers ?? [])
            .FirstOrDefault(candidate => string.Equals(candidate.Id, serverId, StringComparison.Ordinal))
            ?? null!;
        return server is not null;
    }

    // ------------------------------------------------------------------
    // Playlist – Löschen aus Sidebar / Entfernen aus Playlist-Ansicht
    // ------------------------------------------------------------------

    private MenuFlyout BuildDeleteRadioContextFlyout(RadioStationRecord radio)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            radio.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var separator = new Separator();
        menu.Items.Add(separator);

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeleteRadio,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = radio;
        deleteItem.Click += DeleteRadioMenuItem_OnClick;
        menu.Items.Add(deleteItem);
        return menu;
    }

    private void DeleteRadioMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RadioStationRecord radio })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.DeleteRadioStation(radio.Id);
        }
        catch
        {
            return;
        }

        LoadNavPlaylists();
        var internetRadioItem = NavListBox.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "InternetRadio", StringComparison.Ordinal));
        NavListBox.SelectedItem = internetRadioItem;
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.RadioDeleted, radio.Name);
    }

    private MenuFlyout BuildDeletePodcastContextFlyout(PodcastRecord podcast)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            podcast.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var separator = new Separator();
        menu.Items.Add(separator);

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeletePodcast,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = podcast;
        deleteItem.Click += DeletePodcastMenuItem_OnClick;
        menu.Items.Add(deleteItem);
        return menu;
    }

    private void DeletePodcastMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PodcastRecord podcast })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.DeletePodcast(podcast.Id);
        }
        catch
        {
            return;
        }

        LoadNavPlaylists();
        var podcastsItem = NavListBox.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "Podcasts", StringComparison.Ordinal));
        NavListBox.SelectedItem = podcastsItem;
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PodcastDeleted, podcast.Name);
    }

    private MenuFlyout BuildPlaylistSidebarContextFlyout(PlaylistRecord playlist)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            playlist.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var sep = new Separator();
        menu.Items.Add(sep);

        if (playlist.IsSmartPlaylist)
        {
            var editItem = CreateFlyoutMenuItem(LocalizationManager.Current.EditSmartPlaylist);
            editItem.Tag = playlist.Id;
            editItem.Click += EditSmartPlaylistMenuItem_OnClick;
            menu.Items.Add(editItem);
        }
        else
        {
            var exportItem = CreateFlyoutMenuItem(LocalizationManager.Current.ExportM3u8Playlist);
            exportItem.Tag = playlist.Id;
            exportItem.Click += ExportM3u8PlaylistMenuItem_OnClick;
            menu.Items.Add(exportItem);
        }

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeletePlaylist,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = playlist.Id;
        deleteItem.Click += DeletePlaylistMenuItem_OnClick;
        menu.Items.Add(deleteItem);

        return menu;
    }

    private MenuFlyout BuildOrynivoPlaylistSidebarContextFlyout(
        OrynivoServerSettings server,
        OrynivoPlaylistInfo playlist)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            playlist.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        if (playlist.IsSmartPlaylist)
        {
            var editItem = CreateFlyoutMenuItem(LocalizationManager.Current.EditSmartPlaylist);
            editItem.Click += (_, _) => EditOrynivoSmartPlaylistAsync(server, playlist);
            menu.Items.Add(editItem);
        }

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeletePlaylist,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = new OrynivoPlaylistMenuTag(server, playlist.Id, []);
        deleteItem.Click += DeleteOrynivoPlaylistMenuItem_OnClick;
        menu.Items.Add(deleteItem);

        return menu;
    }

    /// <summary>Opens the shared smart-playlist editor for a remote server playlist and persists the result on that server.</summary>
    /// <param name="server">Server hosting the playlist.</param>
    /// <param name="playlist">Smart playlist to edit.</param>
    private async void EditOrynivoSmartPlaylistAsync(
        OrynivoServerSettings server,
        OrynivoPlaylistInfo playlist)
    {
        if (!playlist.IsSmartPlaylist || string.IsNullOrWhiteSpace(playlist.FilterCriteria))
            return;

        SmartPlaylistCriteria criteria;
        try
        {
            criteria = JsonSerializer.Deserialize<SmartPlaylistCriteria>(playlist.FilterCriteria)
                       ?? new SmartPlaylistCriteria();
        }
        catch
        {
            return;
        }

        var dialog = new SmartPlaylistDialog(criteria, playlist.Name);
        if (await dialog.ShowDialog<bool>(this) == false ||
            string.IsNullOrWhiteSpace(dialog.PlaylistName) ||
            dialog.Criteria is null)
            return;

        var name = dialog.PlaylistName.Trim();
        var updated = await _orynivoClient.UpdateSmartPlaylistAsync(
            server,
            playlist.Id,
            name,
            JsonSerializer.Serialize(dialog.Criteria));
        if (updated is null)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        LoadOrynivoServerNavigation();
        if (_activeOrynivoPlaylistServer?.Id == server.Id && _activeOrynivoPlaylistId == playlist.Id)
            await ShowTopLevelViewAsync($"OrynivoServerPlaylist:{server.Id}:{playlist.Id}");
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.SmartPlaylistUpdated, name);
    }

    private async void DeleteOrynivoPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: OrynivoPlaylistMenuTag tag })
            return;

        var name = _orynivoPlaylistsByTag.Values.FirstOrDefault(playlist => playlist.Id == tag.PlaylistId)?.Name ?? string.Empty;
        var ok = await _orynivoClient.DeletePlaylistAsync(tag.Server, tag.PlaylistId);
        if (!ok)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        if (_activeOrynivoPlaylistServer?.Id == tag.Server.Id && _activeOrynivoPlaylistId == tag.PlaylistId)
        {
            _activeOrynivoPlaylistServer = null;
            _activeOrynivoPlaylistId = null;
            var tracksItem = NavListBox.Items.OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Tag is string itemTag &&
                                        itemTag == $"OrynivoServer:{tag.Server.Id}:Tracks");
            if (tracksItem is not null)
                NavListBox.SelectedItem = tracksItem;
        }

        LoadOrynivoServerNavigation();
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PlaylistDeleted, name);
    }

    private MenuFlyout BuildPlaylistsHeaderContextFlyout()
    {
        var menu = CreateSidebarMenuFlyout();
        var importItem = CreateFlyoutMenuItem(LocalizationManager.Current.ImportM3u8Playlist);
        importItem.Click += ImportM3u8PlaylistMenuItem_OnClick;
        menu.Items.Add(importItem);
        return menu;
    }

    private MenuFlyout CreateSidebarMenuFlyout() => new()
    {
        FlyoutPresenterTheme = FindResource<ControlTheme>("AppMenuFlyoutPresenterTheme"),
        ItemContainerTheme = FindResource<ControlTheme>("AppMenuFlyoutItemTheme")
    };

    private MenuItem CreateFlyoutMenuItem(string header, IBrush? foreground = null)
    {
        foreground ??= FindResource<IBrush>("AppPrimaryTextBrush");
        return new MenuItem
        {
            Header = new TextBlock
            {
                Text = header,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center
            },
            Theme = FindResource<ControlTheme>("AppMenuFlyoutItemTheme"),
            Foreground = foreground
        };
    }

    private async void ImportM3u8PlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.ImportM3u8Playlist,
            FileTypeFilter =
            [
                new FilePickerFileType("M3U8") { Patterns = ["*.m3u8", "*.m3u"] }
            ],
            AllowMultiple = false
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { Length: > 0 } filePath)
            return;

        try
        {
            var result = await M3u8PlaylistService.ImportAsync(filePath);
            if (result.Entries.Count == 0)
            {
                StatusTextBlock.Text = LocalizationManager.Current.M3u8ImportNoEntries;
                return;
            }

            var name = Path.GetFileNameWithoutExtension(filePath).Trim();
            if (name.Length == 0)
                name = LocalizationManager.Current.NewPlaylist;
            using var db = AudioDatabase.OpenDefault();
            db.CreatePlaylist(name, result.Entries);
            LoadNavPlaylists();
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ImportCompleted,
                name,
                result.Entries.Count,
                result.MissingLocalFiles,
                result.RemoteEntries,
                result.SkippedCredentialUrls + result.SkippedInvalidEntries);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ImportFailed,
                ex.Message);
        }
    }

    private async void ExportM3u8PlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: long playlistId })
            return;

        PlaylistRecord? playlist;
        List<string> entries;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlist = db.GetPlaylistById(playlistId);
            if (playlist is null || playlist.IsSmartPlaylist)
                return;
            entries = db.GetPlaylistTracks(playlistId).Select(item => item.Path).ToList();
        }
        catch
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Current.ExportM3u8Playlist,
            FileTypeChoices = [new FilePickerFileType("M3U8") { Patterns = ["*.m3u8"] }],
            DefaultExtension = "m3u8",
            SuggestedFileName = SanitizeFileName(playlist.Name) + ".m3u8"
        });
        if (file?.TryGetLocalPath() is not { Length: > 0 } filePath)
            return;

        try
        {
            var result = await M3u8PlaylistService.ExportAsync(filePath, entries);
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ExportCompleted,
                playlist.Name,
                result.ExportedEntries,
                result.SkippedCredentialUrls + result.SkippedInvalidEntries);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ExportFailed,
                ex.Message);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return sanitized.Length == 0 ? "playlist" : sanitized;
    }

    private async void EditSmartPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: long playlistId })
            return;

        PlaylistRecord? playlist;
        SmartPlaylistCriteria criteria;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlist = db.GetPlaylistById(playlistId);
            if (playlist is not { IsSmartPlaylist: true } ||
                string.IsNullOrWhiteSpace(playlist.FilterCriteria))
                return;
            criteria = JsonSerializer.Deserialize<SmartPlaylistCriteria>(playlist.FilterCriteria)
                       ?? new SmartPlaylistCriteria();
        }
        catch
        {
            return;
        }

        var dialog = new SmartPlaylistDialog(criteria, playlist.Name);
        if (await dialog.ShowDialog<bool>(this) == false ||
            string.IsNullOrWhiteSpace(dialog.PlaylistName) ||
            dialog.Criteria is null)
            return;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.UpdateSmartPlaylist(
                playlistId,
                dialog.PlaylistName.Trim(),
                JsonSerializer.Serialize(dialog.Criteria));
        }
        catch
        {
            return;
        }

        LoadNavPlaylists();
        if (_activePlaylistId == playlistId)
            await ShowTopLevelViewAsync($"Playlist:{playlistId}");
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.SmartPlaylistUpdated,
            dialog.PlaylistName.Trim());
    }

    private void DeletePlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: long playlistId })
            return;

        string name;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            name = db.GetPlaylistById(playlistId)?.Name ?? string.Empty;
            db.DeletePlaylist(playlistId);
        }
        catch { return; }

        if (_activePlaylistId == playlistId)
        {
            _activePlaylistId = null;
            for (int i = 0; i < NavListBox.Items.Count; i++)
            {
                if (NavListBox.Items[i] is ListBoxItem { Tag: "Tracks" } tracksItem)
                {
                    NavListBox.SelectedItem = tracksItem;
                    break;
                }
            }
        }

        LoadNavPlaylists();
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PlaylistDeleted, name);
    }

    private MenuFlyout BuildRemoveFromPlaylistContextFlyout(
        long playlistEntryId,
        string path)
    {
        var menu = CreateSidebarMenuFlyout();
        foreach (var item in CreateQueueMenuItems([path]))
            menu.Items.Add(item);
        menu.Items.Add(new Separator());
        var header = CreateFlyoutMenuItem(
            LocalizationManager.Current.RemoveFromPlaylist,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontSize = 11;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var sep = new Separator();
        menu.Items.Add(sep);

        var removeItem = CreateFlyoutMenuItem(LocalizationManager.Current.RemoveFromPlaylist);
        removeItem.Tag = new RemovePlaylistEntryTag(playlistEntryId);
        removeItem.Click += RemoveFromPlaylistMenuItem_OnClick;
        menu.Items.Add(removeItem);

        return menu;
    }

    private MenuFlyout BuildRemoveFromOrynivoPlaylistContextFlyout(
        OrynivoServerSettings server,
        long playlistEntryId,
        string path)
    {
        var menu = CreateSidebarMenuFlyout();
        foreach (var item in CreateQueueMenuItems([path]))
            menu.Items.Add(item);
        menu.Items.Add(new Separator());
        var header = CreateFlyoutMenuItem(
            LocalizationManager.Current.RemoveFromPlaylist,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontSize = 11;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        var removeItem = CreateFlyoutMenuItem(LocalizationManager.Current.RemoveFromPlaylist);
        removeItem.Tag = new RemoveOrynivoPlaylistEntryTag(server, playlistEntryId);
        removeItem.Click += RemoveFromOrynivoPlaylistMenuItem_OnClick;
        menu.Items.Add(removeItem);
        return menu;
    }

    private async void RemoveFromPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RemovePlaylistEntryTag tag })
            return;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.RemoveTrackFromPlaylist(tag.PlaylistEntryId);
        }
        catch { return; }

        StatusTextBlock.Text = LocalizationManager.Current.TrackRemovedFromPlaylist;

        if (NavListBox.SelectedItem is ListBoxItem { Tag: string navTag })
            await ShowTopLevelViewAsync(navTag);
    }

    private async void RemoveFromOrynivoPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RemoveOrynivoPlaylistEntryTag tag })
            return;

        var ok = await _orynivoClient.RemovePlaylistEntryAsync(tag.Server, tag.PlaylistEntryId);
        if (!ok)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        StatusTextBlock.Text = LocalizationManager.Current.TrackRemovedFromPlaylist;
        LoadOrynivoServerNavigation();
        if (NavListBox.SelectedItem is ListBoxItem { Tag: string navTag })
            await ShowTopLevelViewAsync(navTag);
    }
}



