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
/// Sidebar construction, visibility, accordion state, and navigation tag
/// helpers for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void LoadNavPlaylists()
    {
        var selectedTag = (NavListBox.SelectedItem as ListBoxItem)?.Tag as string;
        _suppressNavSelectionChanged = true;
        try
        {
        PlaylistsHeaderItem.ContextFlyout = BuildPlaylistsHeaderContextFlyout();
        foreach (var dynamicItem in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    (tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                     tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                     tag.StartsWith("Plex", StringComparison.Ordinal) ||
                                     tag == "LibraryGroup:LocalPlaylists" ||
                                     tag.StartsWith("Playlist:", StringComparison.Ordinal)))
                     .ToList())
            NavListBox.Items.Remove(dynamicItem);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var podcastHeaderIndex = NavListBox.Items.IndexOf(MyPodcastsHeaderItem);
            var savedRadios = db.GetRadioStations().ToList();
            if (savedRadios.Count == 0 && podcastHeaderIndex >= 0)
            {
                NavListBox.Items.Insert(podcastHeaderIndex++, CreateSidebarHintItem(
                    "Radio:EmptyHint",
                    LocalizationManager.Current.OwnRadiosEmptyHint));
            }

            foreach (var radio in savedRadios)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(CreateSidebarIcon("IconRadio"));
                content.Children.Add(CreateSidebarEntryText(radio.Name));

                NavListBox.Items.Insert(podcastHeaderIndex++, new ListBoxItem
                {
                    Content = content,
                    Tag = $"Radio:{radio.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildDeleteRadioContextFlyout(radio)
                });
            }

            var plexHeaderIndex = NavListBox.Items.IndexOf(PlexHeaderItem);
            var savedPodcasts = db.GetPodcasts().ToList();
            if (savedPodcasts.Count == 0 && plexHeaderIndex >= 0)
            {
                NavListBox.Items.Insert(plexHeaderIndex++, CreateSidebarHintItem(
                    "Podcast:EmptyHint",
                    LocalizationManager.Current.MyPodcastsEmptyHint));
            }

            foreach (var podcast in savedPodcasts)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(CreateSidebarIcon("IconPodcast"));
                content.Children.Add(CreateSidebarEntryText(podcast.Name));

                NavListBox.Items.Insert(plexHeaderIndex++, new ListBoxItem
                {
                    Content = content,
                    Tag = $"Podcast:{podcast.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildDeletePodcastContextFlyout(podcast)
                });
            }

            var localPlaylistInsertIndex = NavListBox.Items.IndexOf(FoldersNavItem);
            if (localPlaylistInsertIndex >= 0)
            {
                localPlaylistInsertIndex++;
                NavListBox.Items.Insert(localPlaylistInsertIndex++, new ListBoxItem
                {
                    Content = CreateLibraryGroupHeader(LocalizationManager.Current.Playlists, _settings.IsPlaylistsSectionExpanded),
                    Tag = "LibraryGroup:LocalPlaylists",
                    FontWeight = FontWeight.SemiBold,
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildPlaylistsHeaderContextFlyout()
                });
            }

            foreach (var pl in db.GetAllPlaylists())
            {
                Control content = pl.IsSmartPlaylist
                    ? CreateSmartPlaylistSidebarContent(pl.Name)
                    : CreateSidebarEntryContent("IconPlaylist", pl.Name);
                content.Margin = new Thickness(16, 0, 0, 0);

                var item = new ListBoxItem
                {
                    Content = content,
                    Tag     = $"Playlist:{pl.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildPlaylistSidebarContextFlyout(pl)
                };
                if (localPlaylistInsertIndex >= 0)
                    NavListBox.Items.Insert(localPlaylistInsertIndex++, item);
                else
                    NavListBox.Items.Add(item);
            }

            LoadPlexNavigationAsync();
            LoadOrynivoServerNavigation();
        }
        catch { /* DB noch nicht angelegt */ }

        ApplySidebarNavigationSettings();
        RestoreSelectedNavigationTag(selectedTag);
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    private void RestoreSelectedNavigationTag(string? selectedTag)
    {
        if (string.IsNullOrWhiteSpace(selectedTag))
            return;

        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Tag as string, selectedTag, StringComparison.Ordinal));
        if (item is not null)
            NavListBox.SelectedItem = item;
    }

    private async void LoadPlexNavigationAsync()
    {
        var loadVersion = ++_plexNavigationLoadVersion;
        foreach (var item in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    tag.StartsWith("Plex", StringComparison.Ordinal))
                     .ToList())
            NavListBox.Items.Remove(item);

        Dictionary<string, string> tokens;
        try
        {
            tokens = await new WindowsPlexCredentialStore().LoadAllAsync();
        }
        catch
        {
            tokens = [];
        }
        if (loadVersion != _plexNavigationLoadVersion)
            return;

        var insertIndex = NavListBox.Items.IndexOf(PlaylistsHeaderItem);
        var client = new PlexServerClient();
        foreach (var server in _settings.PlexServers ?? [])
        {
            NavListBox.Items.Insert(insertIndex++, new ListBoxItem
            {
                Content = CreateSidebarEntryContent("IconServer", server.Name),
                Tag = $"PlexServer:{server.Id}",
                IsEnabled = false,
                FontWeight = FontWeight.SemiBold,
                Theme = FindResource<ControlTheme>("NavItemTheme")
            });

            try
            {
                var libraries = await client.GetAudioLibrariesAsync(
                    server,
                    tokens.GetValueOrDefault(server.Id));
                if (loadVersion != _plexNavigationLoadVersion)
                    return;
                foreach (var library in libraries)
                    NavListBox.Items.Insert(insertIndex++, CreatePlexLibraryItem(
                        server.Id,
                        library.Key,
                        library.Title));
            }
            catch { }
        }

        ApplySidebarNavigationSettings();
        RestorePendingInitialNavigationTag();
    }

    /// <summary>Restores a Plex library tag after its asynchronous navigation load completes.</summary>
    private void RestorePendingInitialNavigationTag()
    {
        if (_pendingInitialNavigationTag is not { } tag)
            return;
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (item is null)
            return;

        _pendingInitialNavigationTag = null;
        NavListBox.SelectedItem = item;
    }

    private ListBoxItem CreatePlexLibraryItem(string serverId, string libraryKey, string title)
    {
        var content = CreateSidebarEntryContent("IconAlbum", title);
        content.Margin = new Thickness(16, 0, 0, 0);
        return new ListBoxItem
        {
            Content = content,
            Tag = $"PlexLibrary:{serverId}:{libraryKey}",
            Theme = FindResource<ControlTheme>("NavItemTheme")
        };
    }

    private TextBlock CreateSidebarEntryText(string text)
    {
        var tb = new TextBlock { Text = text };
        tb.Classes.Add("navItemText");
        return tb;
    }

    private StackPanel CreateSidebarEntryContent(string iconResourceKey, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateSidebarIcon(iconResourceKey));
        content.Children.Add(CreateSidebarEntryText(text));
        return content;
    }

    private AvaloniaPath CreateSidebarIcon(string resourceKey)
    {
        return new AvaloniaPath
        {
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Data = FindResource<Geometry>(resourceKey),
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
    }

    private ListBoxItem CreateSidebarHintItem(string tag, string text)
    {
        var hint = CreateSidebarEntryText(text);
        hint.Margin = new Thickness(16, 0, 8, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Foreground = FindResource<IBrush>("AppMutedTextBrush");
        hint.FontSize = ResolveFontSize("FontSizeMeta");
        return new ListBoxItem
        {
            Content = hint,
            Tag = tag,
            IsHitTestVisible = false,
            Focusable = false,
            Theme = FindResource<ControlTheme>("NavItemTheme")
        };
    }

    private Grid CreateLibraryGroupHeader(string title, bool isExpanded, double leftIndent = 0)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(20)));

        var content = CreateSidebarEntryContent("IconPlaylist", title);
        content.Children.OfType<TextBlock>().First().FontWeight = FontWeight.SemiBold;
        if (leftIndent > 0)
            content.Margin = new Thickness(leftIndent, 0, 0, 0);
        Grid.SetColumn(content, 0);
        grid.Children.Add(content);

        var arrow = new Avalonia.Controls.Shapes.Path
        {
            Width = 8,
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse(isExpanded ? "M 0 5 L 4 0 L 8 5" : "M 0 0 L 4 5 L 8 0"),
            Stroke = FindResource<IBrush>("AppNavHoverTextBrush"),
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);

        return grid;
    }

    private void NavListBox_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavListBox).Properties.IsLeftButtonPressed)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not { Tag: string tag })
        {
            return;
        }

        if (tag.StartsWith("Section:", StringComparison.Ordinal))
        {
            var section = tag["Section:".Length..];
            SetSidebarSectionExpanded(section, !IsSidebarSectionExpanded(section));
            ApplySidebarNavigationSettings();
            e.Handled = true;
            return;
        }

        if (tag.StartsWith("LibraryGroup:", StringComparison.Ordinal))
        {
            ToggleLibraryGroupExpanded(tag["LibraryGroup:".Length..]);
            ApplySidebarNavigationSettings();
            e.Handled = true;
        }
    }

    private void NavListBox_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavListBox).Properties.IsRightButtonPressed)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not
            { ContextFlyout: PopupFlyoutBase flyout } item)
            return;

        // Prevent SelectingItemsControl from treating a context click as a
        // primary selection click, then show the flyout at the pointer location.
        e.Handled = true;
        flyout.ShowAt(item, showAtPointer: true);
    }

    private void ApplySidebarNavigationSettings()
    {
        SetSidebarItemVisibility(InternetRadioNavItem, _settings.ShowInternetRadioItem);
        SetSidebarItemVisibility(PodcastsNavItem, _settings.ShowPodcastsItem);
        SetSidebarItemVisibility(QueueNavItem, _settings.ShowQueueItem);
        SetSidebarItemVisibility(AiChatNavItem, _settings.ShowAiChatItem);
        ApplyLibrarySectionVisibility();
        SetSidebarSectionVisibility(
            OwnRadiosHeaderItem,
            "OwnRadios",
            _settings.ShowOwnRadiosSection,
            dynamicPrefix: "Radio:");
        SetSidebarSectionVisibility(
            MyPodcastsHeaderItem,
            "MyPodcasts",
            _settings.ShowMyPodcastsSection,
            dynamicPrefix: "Podcast:");
        SetSidebarSectionVisibility(
            PlexHeaderItem,
            "Plex",
            _settings.ShowPlexSection,
            dynamicPrefix: "Plex");
        SetSidebarItemVisibility(PlaylistsHeaderItem, false);
    }

    private void SetSidebarSectionVisibility(
        ListBoxItem header,
        string section,
        bool isVisible,
        IReadOnlyList<ListBoxItem>? staticItems = null,
        string? dynamicPrefix = null)
    {
        SetSidebarItemVisibility(header, isVisible);
        var showItems = isVisible && IsSidebarSectionExpanded(section);
        var arrow = section switch
        {
            "LocalLibrary" => LocalLibraryHeaderArrow,
            "OwnRadios" => OwnRadiosHeaderArrow,
            "MyPodcasts" => MyPodcastsHeaderArrow,
            "Plex" => PlexHeaderArrow,
            "Playlists" => PlaylistsHeaderArrow,
            _ => null
        };
        if (arrow is not null)
            arrow.Data = Geometry.Parse(showItems ? "M 0 5 L 4 0 L 8 5" : "M 0 0 L 4 5 L 8 0");

        if (staticItems is not null)
        {
            foreach (var item in staticItems)
                SetSidebarItemVisibility(item, showItems);
        }

        if (dynamicPrefix is null)
            return;

        foreach (var item in NavListBox.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is string tag && tag.StartsWith(dynamicPrefix, StringComparison.Ordinal))
                SetSidebarItemVisibility(item, showItems);
        }
    }

    private void ApplyLibrarySectionVisibility()
    {
        SetSidebarItemVisibility(LocalLibraryHeaderItem, _settings.ShowLocalLibrarySection);
        var showLibraryItems = _settings.ShowLocalLibrarySection && IsSidebarSectionExpanded("LocalLibrary");
        SetArrowData(LocalLibraryHeaderArrow, showLibraryItems);

        var hasLocalMedia = (_settings.LibraryPaths?.Count ?? 0) > 0;
        var hasOrynivoServers = (_settings.OrynivoServers?.Count ?? 0) > 0;

        // Hint shown directly under the Library header when neither local media
        // directories nor any Orynivo Server is configured. It disappears as soon
        // as a directory or server is added (this method re-runs on every settings
        // save and navigation rebuild).
        SetSidebarItemVisibility(LibraryEmptyHintItem, showLibraryItems && !hasLocalMedia && !hasOrynivoServers);

        SetSidebarItemVisibility(LocalLibraryRootItem, false);
        SetArrowData(LocalMediaGroupArrow, false);
        var showUnifiedLibraryItems = showLibraryItems && (hasLocalMedia || hasOrynivoServers);
        SetSidebarItemVisibility(ArtistsNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(AlbumsNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(TracksNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(GenreCloudNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(FoldersNavItem, showUnifiedLibraryItems);

        foreach (var item in NavListBox.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not string tag)
                continue;

            if (tag.StartsWith("LibraryGroup:OrynivoServerPlaylists:", StringComparison.Ordinal))
            {
                var serverId = tag["LibraryGroup:OrynivoServerPlaylists:".Length..];
                SetSidebarItemVisibility(item, showLibraryItems && IsOrynivoServerLibraryGroupExpanded(serverId));
                UpdateLibraryGroupHeaderArrow(item, IsOrynivoServerPlaylistGroupExpanded(serverId));
                continue;
            }

            if (tag.StartsWith("LibraryGroup:OrynivoServer:", StringComparison.Ordinal))
            {
                var serverId = tag["LibraryGroup:OrynivoServer:".Length..];
                SetSidebarItemVisibility(item, showLibraryItems);
                UpdateLibraryGroupHeaderArrow(item, IsOrynivoServerLibraryGroupExpanded(serverId));
                continue;
            }

            if (tag == "LibraryGroup:LocalPlaylists")
            {
                SetSidebarItemVisibility(item, showLibraryItems);
                UpdateLibraryGroupHeaderArrow(item, _settings.IsPlaylistsSectionExpanded);
                continue;
            }

            if (tag.StartsWith("Playlist:", StringComparison.Ordinal))
            {
                SetSidebarItemVisibility(item, showLibraryItems && _settings.IsPlaylistsSectionExpanded);
                continue;
            }

            if (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            {
                var parts = tag.Split(':');
                var serverId = parts.Length > 1 ? parts[1] : string.Empty;
                SetSidebarItemVisibility(item, showLibraryItems && IsOrynivoServerLibraryGroupExpanded(serverId));
                continue;
            }

            if (tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal))
            {
                var parts = tag.Split(':');
                var serverId = parts.Length > 1 ? parts[1] : string.Empty;
                SetSidebarItemVisibility(
                    item,
                    showLibraryItems
                    && IsOrynivoServerLibraryGroupExpanded(serverId)
                    && IsOrynivoServerPlaylistGroupExpanded(serverId));
            }
        }
    }

    private static void SetArrowData(Avalonia.Controls.Shapes.Path arrow, bool isExpanded) =>
        arrow.Data = Geometry.Parse(isExpanded ? "M 0 5 L 4 0 L 8 5" : "M 0 0 L 4 5 L 8 0");

    private static void SetSidebarItemVisibility(ListBoxItem item, bool isVisible)
    {
        if (isVisible)
        {
            item.IsVisible = true;
            item.MaxHeight = 96;
            item.Opacity = 1;
            return;
        }

        item.Opacity = 0;
        item.MaxHeight = 0;
        _ = HideCollapsedSidebarItemAsync(item);
    }

    private static async Task HideCollapsedSidebarItemAsync(ListBoxItem item)
    {
        await Task.Delay(180);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (item.Opacity <= 0.05 && item.MaxHeight <= 0.5)
                item.IsVisible = false;
        });
    }

    private static void UpdateLibraryGroupHeaderArrow(ListBoxItem item, bool isExpanded)
    {
        if (item.Content is not Grid grid)
            return;

        foreach (var child in grid.Children)
        {
            if (child is Avalonia.Controls.Shapes.Path arrow)
            {
                SetArrowData(arrow, isExpanded);
                return;
            }
        }
    }

    private bool IsOrynivoServerLibraryGroupExpanded(string serverId) =>
        !_settings.CollapsedOrynivoServerLibraryGroups.Contains(serverId);

    private bool IsOrynivoServerPlaylistGroupExpanded(string serverId) =>
        !_settings.CollapsedOrynivoServerPlaylistGroups.Contains(serverId);

    private void ToggleLibraryGroupExpanded(string group)
    {
        if (group.Equals("LocalMedia", StringComparison.Ordinal))
        {
            _settings.IsLocalMediaLibraryGroupExpanded = !_settings.IsLocalMediaLibraryGroupExpanded;
            return;
        }

        if (group.Equals("LocalPlaylists", StringComparison.Ordinal))
        {
            _settings.IsPlaylistsSectionExpanded = !_settings.IsPlaylistsSectionExpanded;
            return;
        }

        if (group.StartsWith("OrynivoServerPlaylists:", StringComparison.Ordinal))
        {
            var playlistServerId = group["OrynivoServerPlaylists:".Length..];
            if (!_settings.CollapsedOrynivoServerPlaylistGroups.Add(playlistServerId))
                _settings.CollapsedOrynivoServerPlaylistGroups.Remove(playlistServerId);
            return;
        }

        if (!group.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            return;

        var serverId = group["OrynivoServer:".Length..];
        if (!_settings.CollapsedOrynivoServerLibraryGroups.Add(serverId))
            _settings.CollapsedOrynivoServerLibraryGroups.Remove(serverId);
    }

    private bool IsSidebarSectionExpanded(string section) => section switch
    {
        "LocalLibrary" => _settings.IsLocalLibrarySectionExpanded,
        "OwnRadios" => _settings.IsOwnRadiosSectionExpanded,
        "MyPodcasts" => _settings.IsMyPodcastsSectionExpanded,
        "Plex" => _settings.IsPlexSectionExpanded,
        "Playlists" => _settings.IsPlaylistsSectionExpanded,
        _ => false
    };

    private void SetSidebarSectionExpanded(string section, bool isExpanded)
    {
        switch (section)
        {
            case "LocalLibrary":
                _settings.IsLocalLibrarySectionExpanded = isExpanded;
                break;
            case "OwnRadios":
                _settings.IsOwnRadiosSectionExpanded = isExpanded;
                break;
            case "MyPodcasts":
                _settings.IsMyPodcastsSectionExpanded = isExpanded;
                break;
            case "Plex":
                _settings.IsPlexSectionExpanded = isExpanded;
                break;
            case "Playlists":
                _settings.IsPlaylistsSectionExpanded = isExpanded;
                break;
        }
    }

    private async void NavListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelectionChanged)
            return;
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;
        if (IsNavigationContainerTag(tag))
            return;

        if (_pendingInitialNavigationTag is not null)
        {
            if (tag == "Tracks")
            {
                // Tracks is only a temporary startup fallback until asynchronous
                // Plex navigation has materialized the persisted library item.
            }
            else
            {
                _pendingInitialNavigationTag = null;
            }
        }

        CloseEmbeddedSettings();
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        if (_pendingInitialNavigationTag is null && IsPersistableMainViewTag(tag))
            _settings.LastMainView = tag;
        await ShowTopLevelViewAsync(tag);
    }

    private async void NavListBox_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not { Tag: string tag, IsSelected: true })
            return;
        if (IsNavigationContainerTag(tag))
            return;

        // Beim erneuten Klick auf den bereits markierten Hauptpunkt feuert kein SelectionChanged.
        // Trotzdem soll die ungefilterte Top-Level-Ansicht wiederhergestellt werden.
        if (_activeAlbumFilterId is null && _activeArtistFilterId is null)
            return;

        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        await ShowTopLevelViewAsync(tag);
    }

    private static bool IsNavigationContainerTag(string tag) =>
        tag.StartsWith("Section:", StringComparison.Ordinal) ||
        tag.StartsWith("LibraryGroup:", StringComparison.Ordinal);

    /// <summary>Determines whether a sidebar tag represents a restorable content view.</summary>
    /// <param name="tag">Sidebar navigation tag.</param>
    /// <returns><see langword="true"/> for selectable leaf views.</returns>
    private static bool IsPersistableMainViewTag(string tag) =>
        !IsNavigationContainerTag(tag) &&
        !tag.EndsWith(":EmptyHint", StringComparison.Ordinal) &&
        !tag.StartsWith("PlexServer:", StringComparison.Ordinal);
}
