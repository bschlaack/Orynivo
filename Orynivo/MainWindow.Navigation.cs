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
/// Navigation stack, drill-down state capture/restore, and back-navigation
/// selection restoration for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void ResetDrilldownState(bool clearNavigationHistory = true)
    {
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
        _activeAlbumCatalogProvider = null;
        _activeCatalogAlbum = null;
        _activeLogicalAlbumIds = null;
        _activeLogicalAlbumParts = null;
        _activeLogicalAlbumRow = null;
        _plexNavigationStack.Clear();
        if (clearNavigationHistory)
            _navigationStack.Clear();
        BackButton.IsVisible = _navigationStack.Count > 0;
    }

    private void PushCurrentNavigationState()
    {
        if (_restoringNavigationHistory)
            return;

        var state = CaptureCurrentNavigationState();
        if (state is null)
            return;
        if (_navigationStack.TryPeek(out var previous) && previous == state)
            return;

        _navigationStack.Push(state);
        BackButton.IsVisible = true;
    }

    private NavigationState? CaptureCurrentNavigationState()
    {
        var selectedRow = GetSelectedContentRow();
        if (SearchResultsScrollViewer.IsVisible)
            return new NavigationState(
                "Search",
                selectedRow?.Id,
                null,
                null,
                SearchTextBox.Text ?? string.Empty,
                CaptureCurrentVerticalOffset(),
                SelectedSourceKey: selectedRow?.SourceKey);

        if (string.Equals(_currentTopLevelTag, "GenreCloud", StringComparison.Ordinal) &&
            GenreCloudPanel.IsVisible)
        {
            return new NavigationState(
                "GenreCloud",
                selectedRow?.Id,
                null,
                null,
                VerticalOffset: CaptureCurrentVerticalOffset(),
                SelectedSourceKey: selectedRow?.SourceKey,
                GenreKey: _genreCloudSelectedKey,
                GenreAlbums: GenreAlbumRecommendationsRadioButton.IsChecked == true);
        }

        if (_activeAlbumFilterId is long logicalAlbumId &&
            _activeLogicalAlbumParts is { Count: > 0 })
        {
            return new NavigationState(
                "LogicalAlbumTracks",
                logicalAlbumId,
                _activeArtistFilterId,
                _activeArtistFilterName,
                _activeAlbumFilterTitle,
                CaptureCurrentVerticalOffset(),
                LogicalAlbumParts: _activeLogicalAlbumParts);
        }

        if (_activeAlbumFilterId is long orynivoAlbumId &&
            _activeCatalogAlbum is { Source: LibraryCatalogSource.OrynivoServer } &&
            _activeOrynivoServer is { } orynivoAlbumServer)
        {
            // A provider-backed remote album must be restored through the remote
            // path. Detecting it only via the top-level tag failed when the album
            // was opened from the dashboard (tag "Dashboard"), so it was captured
            // as a local "AlbumTracks" state and Back reopened a local album that
            // merely shared the numeric id. Detect it by the catalog album source
            // instead and rebuild a server navigation tag when needed.
            var albumNavigationTag =
                _currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true
                    ? _currentTopLevelTag
                    : $"OrynivoServer:{orynivoAlbumServer.Id}:Albums";
            return new NavigationState(
                "OrynivoAlbumTracks",
                orynivoAlbumId,
                _activeArtistFilterId,
                _activeArtistFilterName,
                _activeAlbumFilterTitle,
                CaptureCurrentVerticalOffset(),
                albumNavigationTag,
                LogicalAlbumIds: _activeLogicalAlbumIds);
        }

        if (_activeAlbumFilterId is long albumId)
            return new NavigationState(
                "AlbumTracks",
                albumId,
                _activeArtistFilterId,
                _activeArtistFilterName,
                _activeAlbumFilterTitle,
                LogicalAlbumIds: _activeLogicalAlbumIds);

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long remoteArtistId &&
            _currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true)
        {
            return new NavigationState(
                "OrynivoArtistAlbums",
                selectedRow?.Id,
                remoteArtistId,
                _activeArtistFilterName,
                _activeArtistFilterName,
                CaptureCurrentVerticalOffset(),
                _currentTopLevelTag,
                selectedRow?.SourceKey);
        }

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long artistId &&
            _activeAlbumCatalogProvider is null &&
            !(_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true))
        {
            return new NavigationState(
                "ArtistAlbums",
                selectedRow?.Id,
                artistId,
                _activeArtistFilterName,
                _activeArtistFilterName,
                CaptureCurrentVerticalOffset(),
                SelectedSourceKey: selectedRow?.SourceKey);
        }

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is null &&
            !string.IsNullOrWhiteSpace(_activeArtistFilterName) &&
            string.Equals(_currentTopLevelTag, "Albums", StringComparison.Ordinal))
        {
            return new NavigationState(
                "UnifiedArtistAlbums",
                selectedRow?.Id,
                null,
                _activeArtistFilterName,
                _activeArtistFilterName,
                CaptureCurrentVerticalOffset(),
                SelectedSourceKey: selectedRow?.SourceKey);
        }

        if (!string.IsNullOrWhiteSpace(_currentTopLevelTag) &&
            !_currentTopLevelTag.StartsWith("Section:", StringComparison.Ordinal))
        {
            return new NavigationState(
                _currentTopLevelTag,
                selectedRow?.Id,
                _activeArtistFilterId,
                _activeArtistFilterName,
                SearchTextBox.Text ?? string.Empty,
                CaptureCurrentVerticalOffset(),
                SelectedSourceKey: selectedRow?.SourceKey);
        }

        return null;
    }

    private double? CaptureCurrentVerticalOffset()
    {
        if (SearchResultsScrollViewer.IsVisible)
            return SearchResultsScrollViewer.Offset.Y;
        if (ContentDataGrid.IsVisible)
        {
            AttachContentDataGridVerticalScrollBar();
            return _contentDataGridVerticalScrollBar?.Value;
        }

        var listBox = AlbumArtworkListBox.IsVisible
            ? AlbumArtworkListBox
            : ArtistArtworkListBox.IsVisible
                ? ArtistArtworkListBox
                : null;
        return listBox?
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault()?
            .Offset.Y;
    }

    private long? GetSelectedContentRowId()
        => GetSelectedContentRow()?.Id;

    private ContentRow? GetSelectedContentRow()
    {
        if (SearchResultsScrollViewer.IsVisible)
        {
            if (SearchTracksDataGrid.SelectedItem is ContentRow searchTrack)
                return searchTrack;
            if (SearchAlbumsDataGrid.SelectedItem is ContentRow searchAlbum)
                return searchAlbum;
            if (SearchArtistsDataGrid.SelectedItem is ContentRow searchArtist)
                return searchArtist;
            return null;
        }
        if (ContentDataGrid.IsVisible &&
            ContentDataGrid.SelectedItem is ContentRow gridRow)
            return gridRow;
        if (AlbumArtworkListBox.IsVisible &&
            AlbumArtworkListBox.SelectedItem is ContentRow albumRow)
            return albumRow;
        if (ArtistArtworkListBox.IsVisible &&
            ArtistArtworkListBox.SelectedItem is ContentRow artistRow)
            return artistRow;
        return null;
    }

    private async void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ArtistInfoView.IsVisible && !string.IsNullOrWhiteSpace(_artistInfoUnifiedArtistName))
        {
            CloseNowPlayingDetailViews();
            _artistInfoUnifiedArtistName = null;
            if (_navigationStack.Count > 0)
            {
                var artistOrigin = _navigationStack.Pop();
                await RestoreNavigationStateAsync(artistOrigin);
            }
            BackButton.IsVisible = _navigationStack.Count > 0;
            return;
        }

        if (LyricsView.IsVisible ||
            ArtistInfoView.IsVisible ||
            PodcastInfoView.IsVisible)
        {
            CloseNowPlayingDetailViews();
            return;
        }

        if (_plexNavigationStack.Count > 0)
        {
            var plexState = _plexNavigationStack.Pop();
            _activePlexView = plexState.View;
            ContentTitleTextBlock.Text = plexState.Title;
            ApplyColumns("Plex" + plexState.View);
            ContentDataGrid.ItemsSource = plexState.Rows;
            ContentDataGrid.IsVisible = true;
            FolderTreeView.IsVisible = false;
            PlexViewModeBorder.IsVisible = _plexNavigationStack.Count == 0;
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(plexState.Rows.Count);
            UpdateAlphabetIndex(plexState.Rows, true);
            BackButton.IsVisible = _plexNavigationStack.Count > 0 || _navigationStack.Count > 0;
            return;
        }

        if (_navigationStack.Count == 0)
            return;

        var state = _navigationStack.Pop();
        await RestoreNavigationStateAsync(state);
        BackButton.IsVisible = _navigationStack.Count > 0;
    }

    private async Task RestoreNavigationStateAsync(NavigationState state)
    {
        _restoringNavigationHistory = true;
        try
        {
            await RestoreNavigationStateCoreAsync(state);
        }
        finally
        {
            _restoringNavigationHistory = false;
        }
    }

    private async Task RestoreNavigationStateCoreAsync(NavigationState state)
    {
        _activeArtistFilterId = state.ArtistFilterId;
        _activeArtistFilterName = state.ArtistFilterName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        HideAlbumDetailHeader();

        switch (state.View)
        {
            case "LogicalAlbumTracks" when
                state.SelectedId is long logicalAlbumId &&
                state.LogicalAlbumParts is { Count: > 0 } parts:
                var representativePart = parts.First();
                var logicalRow = new ContentRow
                {
                    Id = logicalAlbumId,
                    AlbumId = logicalAlbumId,
                    ArtistId = representativePart.ArtistId,
                    Title = string.IsNullOrWhiteSpace(state.SearchQuery)
                        ? LocalizationManager.Current.Unknown
                        : state.SearchQuery,
                    Artist = state.ArtistFilterName,
                    EntityType = "UnifiedAlbum",
                    LogicalAlbumParts = parts,
                    OrynivoServer = representativePart.Server,
                    FilePath = ""
                };
                await OpenLogicalAlbumTracksAsync(logicalRow);
                return;

            case "AlbumTracks" when state.SelectedId is long albumId:
                await ShowAlbumTracksAsync(
                    albumId,
                    string.IsNullOrWhiteSpace(state.SearchQuery)
                        ? LocalizationManager.Current.Unknown
                        : state.SearchQuery,
                    state.ArtistFilterId,
                    state.ArtistFilterName,
                    state.LogicalAlbumIds);
                return;

            case "ArtistAlbums" when state.ArtistFilterId is long artistId:
                await ShowArtistAlbumsAsync(
                    artistId,
                    string.IsNullOrWhiteSpace(state.SearchQuery)
                        ? LocalizationManager.Current.Unknown
                        : state.SearchQuery);
                RestoreSelectionFromCurrentItems(
                    state.SelectedId,
                    state.VerticalOffset,
                    state.SelectedSourceKey);
                return;

            case "UnifiedArtistAlbums" when !string.IsNullOrWhiteSpace(state.ArtistFilterName):
                await ShowUnifiedArtistAlbumsAsync(state.ArtistFilterName);
                RestoreSelectionFromCurrentItems(state.SelectedId, state.VerticalOffset, state.SelectedSourceKey);
                return;

            case "OrynivoAlbumTracks" when state.SelectedId is long albumId:
                await RestoreOrynivoAlbumTracksAsync(
                    state.NavigationTag,
                    albumId,
                    state.SearchQuery,
                    state.ArtistFilterId,
                    state.ArtistFilterName,
                    state.VerticalOffset,
                    state.LogicalAlbumIds);
                return;

            case "OrynivoArtistAlbums" when state.ArtistFilterId is long artistId:
                await RestoreOrynivoArtistAlbumsAsync(
                    state.NavigationTag,
                    artistId,
                    state.SearchQuery,
                    state.SelectedId,
                    state.VerticalOffset);
                return;

            case "Search":
                SearchTextBox.Text = state.SearchQuery ?? string.Empty;
                await ShowSearchResultsAsync(state.SearchQuery ?? string.Empty);
                RestoreSearchSelection(state.SelectedId, state.SelectedSourceKey, state.VerticalOffset);
                return;

            case "GenreCloud":
                _genreCloudSelectedKey = state.GenreKey;
                GenreAlbumRecommendationsRadioButton.IsChecked = state.GenreAlbums == true;
                GenreTrackRecommendationsRadioButton.IsChecked = state.GenreAlbums != true;
                SelectNavigationItem("GenreCloud");
                await ShowTopLevelViewAsync("GenreCloud");
                RestoreSelectionFromCurrentItems(
                    state.SelectedId,
                    state.VerticalOffset,
                    state.SelectedSourceKey);
                return;

            case "Artists":
            case "Albums":
                SelectNavigationItem(state.View);
                await ShowTopLevelViewAsync(state.View);
                RestoreSelectionFromCurrentItems(
                    state.SelectedId,
                    state.VerticalOffset,
                    state.SelectedSourceKey);
                return;

            default:
                SelectNavigationItem(state.View);
                await ShowTopLevelViewAsync(state.View);
                RestoreSelectionFromCurrentItems(
                    state.SelectedId,
                    state.VerticalOffset,
                    state.SelectedSourceKey);
                break;
        }
    }

    private void SelectNavigationItem(string tag)
    {
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(navItem => string.Equals(navItem.Tag as string, tag, StringComparison.Ordinal));
        if (item is null || ReferenceEquals(NavListBox.SelectedItem, item))
            return;

        _suppressNavSelectionChanged = true;
        try
        {
            NavListBox.SelectedItem = item;
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    private void RestoreSelectionFromCurrentItems(
        long? selectedId,
        double? verticalOffset = null,
        string? selectedSourceKey = null)
    {
        var rows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? (AlbumArtworkListBox.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? (ArtistArtworkListBox.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? [];
        RestoreSelection(rows, selectedId, verticalOffset, selectedSourceKey);
    }

    private void RestoreSelection(
        List<ContentRow> rows,
        long? selectedId,
        double? verticalOffset = null,
        string? selectedSourceKey = null)
    {
        var row = selectedId is long id
            ? rows.FirstOrDefault(candidate =>
                candidate.Id == id &&
                (selectedSourceKey is null ||
                 string.Equals(candidate.SourceKey, selectedSourceKey, StringComparison.OrdinalIgnoreCase)))
            : null;

        if (ContentDataGrid.IsVisible)
        {
            ContentDataGrid.SelectedItem = row;
            RestoreDataGridPositionAfterLayout(row, verticalOffset);
            return;
        }

        var listBox = AlbumArtworkListBox.IsVisible
            ? AlbumArtworkListBox
            : ArtistArtworkListBox.IsVisible
                ? ArtistArtworkListBox
                : null;
        if (listBox is null)
            return;

        if (row is not null)
        {
            EnsureArtworkRowBound(listBox, row);
            listBox.SelectedItem = row;
        }
        RestoreArtworkPositionAfterLayout(listBox, row, verticalOffset);
    }

    private void RestoreSearchSelection(
        long? selectedId,
        string? selectedSourceKey,
        double? verticalOffset)
    {
        if (selectedId is long id)
        {
            foreach (var grid in new[] { SearchTracksDataGrid, SearchAlbumsDataGrid, SearchArtistsDataGrid })
            {
                var row = (grid.ItemsSource as IEnumerable<ContentRow>)?.FirstOrDefault(candidate =>
                    candidate.Id == id &&
                    (selectedSourceKey is null ||
                     string.Equals(candidate.SourceKey, selectedSourceKey, StringComparison.OrdinalIgnoreCase)));
                if (row is null)
                    continue;
                grid.SelectedItem = row;
                grid.ScrollIntoView(row, null);
                break;
            }
        }
        if (verticalOffset is double offset)
        {
            Dispatcher.UIThread.Post(
                () => SearchResultsScrollViewer.Offset = new Vector(
                    SearchResultsScrollViewer.Offset.X,
                    Math.Clamp(
                        offset,
                        0,
                        Math.Max(0, SearchResultsScrollViewer.Extent.Height -
                                    SearchResultsScrollViewer.Viewport.Height))),
                DispatcherPriority.Loaded);
        }
    }

    private void RestoreDataGridPositionAfterLayout(
        ContentRow? row,
        double? verticalOffset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AttachContentDataGridVerticalScrollBar();
            if (verticalOffset is double offset &&
                _contentDataGridVerticalScrollBar is { } scrollBar)
            {
                scrollBar.Value = Math.Clamp(offset, scrollBar.Minimum, scrollBar.Maximum);
            }
            else if (row is not null)
            {
                ContentDataGrid.ScrollIntoView(row, null);
            }
        }, DispatcherPriority.Loaded);
    }

    private void RestoreArtworkPositionAfterLayout(
        ListBox listBox,
        ContentRow? row,
        double? verticalOffset)
    {
        var bindingVersion = _artworkBindingVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (bindingVersion != _artworkBindingVersion)
                return;

            var scrollViewer = listBox.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (scrollViewer is null)
                return;

            if (verticalOffset is double offset)
            {
                var itemWidth = GetArtworkItemWidth(listBox);
                var itemHeight = GetArtworkItemHeight(listBox);
                var perRow = Math.Max(1, (int)Math.Floor(scrollViewer.Viewport.Width / itemWidth));
                var requiredItems = Math.Max(
                    ArtworkPageSize,
                    ((int)Math.Ceiling((offset + scrollViewer.Viewport.Height) / itemHeight) + 1) * perRow);
                var visibleRows = ReferenceEquals(listBox, AlbumArtworkListBox)
                    ? _visibleAlbumArtworkRows
                    : _visibleArtistArtworkRows;
                while (visibleRows.Count < requiredItems && AppendArtworkRows(listBox))
                {
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (bindingVersion != _artworkBindingVersion)
                        return;
                    var restoredViewer = listBox.GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .FirstOrDefault();
                    if (restoredViewer is null)
                        return;
                    restoredViewer.Offset = new Vector(
                        restoredViewer.Offset.X,
                        Math.Clamp(offset, 0, Math.Max(0, restoredViewer.Extent.Height - restoredViewer.Viewport.Height)));
                    QueueHydrateVisibleArtworkRows(listBox);
                    UpdateActiveAlphabetButton();
                }, DispatcherPriority.Background);
            }
            else if (row is not null)
            {
                ScrollArtworkRowIntoViewAfterLayout(listBox, row);
            }
        }, DispatcherPriority.Loaded);
    }
}
