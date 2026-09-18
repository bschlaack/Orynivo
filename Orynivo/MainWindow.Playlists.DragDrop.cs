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
/// Queue drag-and-drop from track rows, album rows, and folder nodes onto the
/// always-visible Up Next sidebar item.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Application data format carrying the JSON-serialized queue tokens for an
    /// in-process drag. A single string payload keeps the tokens intact without
    /// depending on a platform data format.
    /// </summary>
    private static DataFormat<string> QueueDragFormat => OrynivoDataFormats.QueueDragTokens;

    /// <summary>
    /// Wires drag-and-drop so track rows, local album rows, and folder nodes can be dragged
    /// onto the always-visible "Up Next" sidebar item to append them to the queue. The queue
    /// view and the source lists are never visible at the same time, so the sidebar item is the
    /// drop target.
    /// </summary>
    private void SetupQueueDragAndDrop()
    {
        ContentDataGrid.AddHandler(PointerPressedEvent, QueueDragSource_OnPointerPressed, RoutingStrategies.Tunnel);
        ContentDataGrid.AddHandler(PointerMovedEvent, QueueDragSource_OnPointerMoved, RoutingStrategies.Tunnel);
        AlbumArtworkListBox.AddHandler(PointerPressedEvent, QueueDragSource_OnPointerPressed, RoutingStrategies.Tunnel);
        AlbumArtworkListBox.AddHandler(PointerMovedEvent, QueueDragSource_OnPointerMoved, RoutingStrategies.Tunnel);
        FolderTreeView.AddHandler(PointerPressedEvent, QueueDragSource_OnPointerPressed, RoutingStrategies.Tunnel);
        FolderTreeView.AddHandler(PointerMovedEvent, QueueDragSource_OnPointerMoved, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(QueueNavItem, true);
        QueueNavItem.AddHandler(DragDrop.DragOverEvent, QueueNavItem_OnDragOver);
        QueueNavItem.AddHandler(DragDrop.DropEvent, QueueNavItem_OnDrop);
    }

    private void QueueDragSource_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _queueDragOrigin = e.GetPosition(control);
            _queueDragPending = true;
        }
        else
        {
            _queueDragPending = false;
        }
    }

    private async void QueueDragSource_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_queueDragPending || sender is not Control control)
            return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _queueDragPending = false;
            return;
        }

        var position = e.GetPosition(control);
        if (Math.Abs(position.X - _queueDragOrigin.X) < 5 && Math.Abs(position.Y - _queueDragOrigin.Y) < 5)
            return;

        _queueDragPending = false;
        if (!TryCollectQueueDragPaths(e.Source, out var paths))
            return;

        e.Handled = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(QueueDragFormat, JsonSerializer.Serialize(paths)));
        try { await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy); }
        catch { /* A failed drag must never disrupt normal list interaction. */ }
        e.Handled = true;
    }

    private void QueueNavItem_OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(QueueDragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void QueueNavItem_OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var tokens = GetDroppedQueueTokens(e.DataTransfer);
        if (tokens.Length == 0)
            return;

        var paths = await ResolveDroppedQueuePathsAsync(tokens);
        if (paths.Count == 0)
            return;

        foreach (var path in paths)
            _queue.Add(CreatePlaylistItem(path));
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksAppendedToQueue, paths.Count);
    }

    private static string[] GetDroppedQueueTokens(IDataTransfer data)
    {
        if (!data.Contains(QueueDragFormat))
            return [];

        var payload = data.TryGetValue(QueueDragFormat);
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        try
        {
            return CleanQueueDragTokens(JsonSerializer.Deserialize<string[]>(payload) ?? []);
        }
        catch (JsonException)
        {
            // An unreadable payload is treated like a drag without our tokens.
            return [];
        }
    }

    private static string[] CleanQueueDragTokens(IEnumerable<string> items) =>
        items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

    /// <summary>
    /// Expands dropped queue tokens into playable paths, resolving remote-album references
    /// (<c>orynivo-album:serverId:albumId</c>) to their registered track stream URLs.
    /// </summary>
    /// <param name="tokens">The dropped tokens (real paths and/or references).</param>
    /// <returns>The resolved, ordered playable paths.</returns>
    private async Task<List<string>> ResolveDroppedQueuePathsAsync(string[] tokens)
    {
        var paths = new List<string>();
        foreach (var token in tokens)
        {
            if (!PlaylistReferences.TryParseAlbum(token, out var serverId, out var albumId))
            {
                paths.Add(token);
                continue;
            }

            var server = (_settings.OrynivoServers ?? [])
                .FirstOrDefault(item => item.Id == serverId);
            if (server is null)
                continue;

            try
            {
                var tracks = await _orynivoClient.GetTracksByAlbumAsync(server, albumId);
                foreach (var track in tracks)
                    paths.Add(ToOrynivoTrackContentRow(server, track).FilePath);
            }
            catch { /* Skip an album that can no longer be resolved. */ }
        }
        return paths;
    }

    /// <summary>Collects the playable paths represented by a drag source (grid row or folder node).</summary>
    /// <param name="source">The pointer-event source visual.</param>
    /// <param name="paths">The resolved paths, or empty when the source is not draggable.</param>
    /// <returns><see langword="true"/> when at least one path was collected.</returns>
    private bool TryCollectQueueDragPaths(object? source, out string[] paths)
    {
        paths = [];
        var visual = source as Visual;

        if (FindAncestor<TreeViewItem>(visual) is { } treeItem)
        {
            paths = CollectFolderDragPaths(treeItem);
            return paths.Length > 0;
        }

        if (FindAncestor<DataGridRow>(visual)?.DataContext is ContentRow row)
        {
            paths = CollectRowDragPaths(row);
            return paths.Length > 0;
        }

        if (FindAncestor<ListBoxItem>(visual)?.DataContext is ContentRow cardRow)
        {
            paths = CollectRowDragPaths(cardRow);
            return paths.Length > 0;
        }

        return false;
    }

    private string[] CollectRowDragPaths(ContentRow row)
    {
        // A local album row resolves to its full track list synchronously.
        if (row.EntityType == "Album" && row.AlbumId is long albumId)
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackListByAlbum(albumId).Select(track => track.Path).ToArray();
            }
            catch { return []; }
        }

        // A remote album row carries a reference that is resolved to its tracks asynchronously
        // on drop (the album track list needs a server request).
        if (row.EntityType == "OrynivoAlbum" && row.OrynivoServer is { } server && row.Id is long remoteAlbumId)
            return [PlaylistReferences.BuildAlbum(server.Id, remoteAlbumId)];

        // A track row (local, remote, Plex, or queue/search row) carries a playable path directly.
        if (IsQueueDraggableTrackRow(row) && !string.IsNullOrEmpty(row.FilePath))
            return [row.FilePath];

        return [];
    }

    private static bool IsQueueDraggableTrackRow(ContentRow row) =>
        row.EntityType is "Track" or "OrynivoTrack" or "PlexTrack" or "Queue" ||
        (row.AlbumId is not null && !string.IsNullOrEmpty(row.FilePath));

    private string[] CollectFolderDragPaths(TreeViewItem item)
    {
        if (item.Tag is FolderTag folder)
        {
            // A file node is a single track (local path or remote stream URL).
            if (folder.IsFile)
                return string.IsNullOrEmpty(folder.FilePath) ? [] : [folder.FilePath];

            // A remote directory resolves through the in-memory folder tree (registered
            // stream URLs); a local directory queries the database.
            if (folder.Server is { } server)
            {
                return _folderTreesBySource.TryGetValue(GetServerSourceKey(server.Id), out var tree)
                    ? tree.AllFilePathsUnder(folder.FilePath).ToArray()
                    : [];
            }

            try
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackPathsUnderDirectory(folder.FilePath).ToArray();
            }
            catch { return []; }
        }

        if (item.Tag is PlexFolderTag { IsTrack: true, Track: { } plexTrack } &&
            !string.IsNullOrEmpty(plexTrack.FilePath))
            return [plexTrack.FilePath];

        return [];
    }
}
