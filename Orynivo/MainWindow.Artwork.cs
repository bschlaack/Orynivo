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
/// Album and artist artwork image loading, caching, and virtualized-row
/// hydration for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private static IImage? CreateArtworkImage(string? path, int decodeWidth, bool ignoreCache = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return decodeWidth > 0
                ? Bitmap.DecodeToWidth(stream, decodeWidth)
                : new Bitmap(stream);
        }
        catch { return null; }
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme is "http" or "https";

    private static string GetRemoteArtworkCachePath(string url, int decodeWidth)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return AppPaths.GetDataPath("remote-artworks", $"{hash}_{decodeWidth}.img");
    }

    private static void InvalidateRemoteArtworkCache(string? url)
    {
        if (!IsHttpUrl(url))
            return;

        var prefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url!)));
        var directory = AppPaths.GetDataPath("remote-artworks");
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, $"{prefix}_*.img"))
        {
            try { File.Delete(file); }
            catch { }
        }
    }

    private static void WriteRemoteArtworkCache(string? url, byte[] imageData)
    {
        if (!IsHttpUrl(url))
            return;

        var directory = AppPaths.GetDataPath("remote-artworks");
        Directory.CreateDirectory(directory);
        var prefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url!)));
        foreach (var width in new[] { 96, 320, 1000 })
        {
            try
            {
                File.WriteAllBytes(Path.Combine(directory, $"{prefix}_{width}.img"), imageData);
            }
            catch { }
        }
    }

    private static async Task<IImage?> LoadRemoteArtworkImageAsync(
        string url,
        int decodeWidth,
        CancellationToken cancellationToken = default)
    {
        var cachePath = GetRemoteArtworkCachePath(url, decodeWidth);
        if (File.Exists(cachePath))
        {
            var cached = await Task.Run(() => CreateArtworkImage(cachePath, decodeWidth), cancellationToken);
            if (cached is not null)
                return cached;

            try { File.Delete(cachePath); }
            catch { }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var data = await RemoteArtworkHttpClient.GetByteArrayAsync(url, cancellationToken);
            await File.WriteAllBytesAsync(cachePath, data, cancellationToken);
            await using var stream = new MemoryStream(data);
            return Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureArtworkHydrated(ContentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ArtworkPath))
            row.Artwork = null;
        else if (IsHttpUrl(row.ArtworkPath) && row.Artwork is null && !row.ArtworkLoadQueued && !row.ArtworkLoadCompleted)
        {
            row.ArtworkLoadQueued = true;
            _ = LoadArtworkAsync(row, row.ArtworkPath);
        }
        else if (row.Artwork is null)
            row.Artwork = CreateArtworkImage(row.ArtworkPath, 320);

        if (string.IsNullOrWhiteSpace(row.ThumbnailPath))
            row.Thumbnail = null;
        else if (IsHttpUrl(row.ThumbnailPath) && row.Thumbnail is null && !row.ThumbnailLoadQueued && !row.ThumbnailLoadCompleted)
        {
            row.ThumbnailLoadQueued = true;
            _ = LoadRemoteThumbnailAsync(row, row.ThumbnailPath);
        }
        else if (row.Thumbnail is null)
            row.Thumbnail = CreateArtworkImage(row.ThumbnailPath, 96);
    }

    private void QueueHydrateVisibleArtworkRows(ListBox listBox)
    {
        if (!listBox.IsVisible)
            return;

        Dispatcher.UIThread.Post(() => HydrateVisibleArtworkRows(listBox), DispatcherPriority.Background);
    }

    private void BindArtworkRows(string tag, IReadOnlyList<ContentRow> rows)
    {
        var bindingVersion = ++_artworkBindingVersion;
        if (tag == "Albums")
        {
            _albumArtworkRows = rows as List<ContentRow> ?? rows.ToList();
            ResetVisibleArtworkRows(_visibleAlbumArtworkRows, _albumArtworkRows);
            AlbumArtworkListBox.ItemsSource = _visibleAlbumArtworkRows;
            ResetArtworkScrollPositionAfterLayout(AlbumArtworkListBox, bindingVersion);
            QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        }
        else if (tag == "Artists")
        {
            _artistArtworkRows = rows as List<ContentRow> ?? rows.ToList();
            ResetVisibleArtworkRows(_visibleArtistArtworkRows, _artistArtworkRows);
            ArtistArtworkListBox.ItemsSource = _visibleArtistArtworkRows;
            ResetArtworkScrollPositionAfterLayout(ArtistArtworkListBox, bindingVersion);
            QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
        }
    }

    private void ResetArtworkScrollPositionAfterLayout(ListBox listBox, int bindingVersion)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (bindingVersion != _artworkBindingVersion)
                return;

            var scrollViewer = listBox.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (scrollViewer is not null)
                scrollViewer.Offset = new Vector(0, 0);
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Loaded);
    }

    private static void ResetVisibleArtworkRows(
        ObservableCollection<ContentRow> visibleRows,
        IReadOnlyList<ContentRow> allRows)
    {
        visibleRows.Clear();
        var count = Math.Min(ArtworkPageSize, allRows.Count);
        for (var i = 0; i < count; i++)
            visibleRows.Add(allRows[i]);
    }

    private double GetArtworkItemWidth(ListBox listBox) =>
        ReferenceEquals(listBox, AlbumArtworkListBox)
            ? AlbumArtworkItemWidth
            : ArtistArtworkItemWidth;

    private double GetArtworkItemHeight(ListBox listBox) =>
        ReferenceEquals(listBox, AlbumArtworkListBox)
            ? AlbumArtworkItemHeight
            : ArtistArtworkItemHeight;

    private void AppendArtworkRowsIfNeeded(ListBox listBox)
    {
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
            return;

        var itemHeight = GetArtworkItemHeight(listBox);
        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height < scrollViewer.Extent.Height - itemHeight * 3)
            return;

        if (AppendArtworkRows(listBox))
            QueueHydrateVisibleArtworkRows(listBox);
    }

    private bool AppendArtworkRows(ListBox listBox)
    {
        var allRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _albumArtworkRows
            : _artistArtworkRows;
        var visibleRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _visibleAlbumArtworkRows
            : _visibleArtistArtworkRows;

        if (visibleRows.Count >= allRows.Count)
            return false;

        var end = Math.Min(visibleRows.Count + ArtworkPageSize, allRows.Count);
        for (var i = visibleRows.Count; i < end; i++)
            visibleRows.Add(allRows[i]);
        return true;
    }

    private void EnsureArtworkRowBound(ListBox listBox, ContentRow row)
    {
        var allRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _albumArtworkRows
            : _artistArtworkRows;
        var visibleRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _visibleAlbumArtworkRows
            : _visibleArtistArtworkRows;
        var index = allRows.IndexOf(row);
        if (index < 0)
            return;
        while (visibleRows.Count <= index && AppendArtworkRows(listBox))
        {
        }
    }

    private void ScrollArtworkRowIntoViewAfterLayout(ListBox listBox, ContentRow row)
    {
        var bindingVersion = _artworkBindingVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (bindingVersion != _artworkBindingVersion ||
                (listBox.ItemsSource as System.Collections.IList)?.Contains(row) != true)
            {
                _isAlphabetProgrammaticScroll = false;
                return;
            }

            listBox.ScrollIntoView(row);
            Dispatcher.UIThread.Post(() =>
            {
                if (bindingVersion == _artworkBindingVersion)
                {
                    listBox.ScrollIntoView(row);
                    QueueHydrateVisibleArtworkRows(listBox);
                }
                _isAlphabetProgrammaticScroll = false;
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private void HydrateVisibleArtworkRows(ListBox listBox)
    {
        if (!listBox.IsVisible)
            return;

        var rows = listBox.ItemsSource as IReadOnlyList<ContentRow>
            ?? (listBox.ItemsSource as IEnumerable<ContentRow>)?.ToList();
        if (rows is null || rows.Count == 0)
            return;

        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            HydrateInitialArtworkRows(rows);
            Dispatcher.UIThread.Post(() => QueueHydrateVisibleArtworkRows(listBox), DispatcherPriority.Loaded);
            return;
        }

        var itemWidth = GetArtworkItemWidth(listBox);
        var itemHeight = GetArtworkItemHeight(listBox);
        if (scrollViewer.Viewport.Width <= 0 || scrollViewer.Viewport.Height <= 0)
        {
            HydrateInitialArtworkRows(rows);
            Dispatcher.UIThread.Post(() => QueueHydrateVisibleArtworkRows(listBox), DispatcherPriority.Loaded);
            return;
        }

        var perRow = Math.Max(1, (int)Math.Floor(scrollViewer.Viewport.Width / itemWidth));
        var firstRow = Math.Max(0, (int)Math.Floor(scrollViewer.Offset.Y / itemHeight) - 1);
        var lastRow = Math.Min(
            (int)Math.Ceiling(rows.Count / (double)perRow) - 1,
            (int)Math.Ceiling((scrollViewer.Offset.Y + scrollViewer.Viewport.Height) / itemHeight) + 1);

        for (var index = firstRow * perRow; index < rows.Count && index <= ((lastRow + 1) * perRow) - 1; index++)
        {
            QueueArtworkHydration(rows[index]);
            if (rows[index].EntityType == "Artist")
                _ = EnsureArtistProfileAsync(rows[index]);
        }
    }

    private void HydrateInitialArtworkRows(IReadOnlyList<ContentRow> rows)
    {
        var count = Math.Min(ArtworkPageSize, rows.Count);
        for (var index = 0; index < count; index++)
        {
            QueueArtworkHydration(rows[index]);
            if (rows[index].EntityType == "Artist")
                _ = EnsureArtistProfileAsync(rows[index]);
        }
    }

    private void QueueArtworkHydration(ContentRow row)
    {
        if (row.Artwork is not null || row.ArtworkLoadQueued || row.ArtworkLoadCompleted)
            return;

        var path = row.ArtworkPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            row.Artwork = null;
            row.ArtworkLoadCompleted = true;
            return;
        }

        row.ArtworkLoadQueued = true;
        _ = LoadArtworkAsync(row, path);
    }

    private static async Task LoadArtworkAsync(ContentRow row, string path)
    {
        IImage? image = null;
        try
        {
            if (IsHttpUrl(path))
            {
                image = await LoadRemoteArtworkImageAsync(path, 320);
            }
            else
            {
                image = await Task.Run(() => CreateArtworkImage(path, 320));
            }
        }
        catch
        {
            image = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.ArtworkLoadQueued = false;
            row.ArtworkLoadCompleted = image is not null || !IsHttpUrl(path);
            if (string.Equals(row.ArtworkPath, path, StringComparison.OrdinalIgnoreCase))
                row.Artwork = image;
        }, DispatcherPriority.Background);
    }

    private static void EnsureThumbnailHydrated(ContentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ThumbnailPath))
            row.Thumbnail = null;
        else if (IsHttpUrl(row.ThumbnailPath) &&
                 row.Thumbnail is null &&
                 !row.ThumbnailLoadQueued &&
                 !row.ThumbnailLoadCompleted)
        {
            row.ThumbnailLoadQueued = true;
            _ = LoadRemoteThumbnailAsync(row, row.ThumbnailPath);
        }
        else if (row.Thumbnail is null)
            row.Thumbnail = CreateArtworkImage(row.ThumbnailPath, 96);
    }

    private static async Task LoadRemoteThumbnailAsync(ContentRow row, string url)
    {
        IImage? image = null;
        try
        {
            image = await LoadRemoteArtworkImageAsync(url, 96);
        }
        catch
        {
            image = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.ThumbnailLoadQueued = false;
            row.ThumbnailLoadCompleted = image is not null || !IsHttpUrl(url);
            if (string.Equals(row.ThumbnailPath, url, StringComparison.OrdinalIgnoreCase))
                row.Thumbnail = image;
        }, DispatcherPriority.Background);
    }
}
