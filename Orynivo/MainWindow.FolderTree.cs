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
/// Local, remote, and unified folder tree construction, lazy loading, and
/// navigation for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void ShowOrynivoFolderLoadingState()
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        FolderTreeView.Items.Clear();
        FolderTreeView.Items.Add(new TreeViewItem
        {
            Header = new TextBlock
            {
                Text = LocalizationManager.Current.OrynivoLoading,
                Foreground = FindResource<IBrush>("AppMutedTextBrush")
            },
            IsEnabled = false
        });
        ContentCountTextBlock.Text = string.Empty;
    }

    private async Task<List<OrynivoTrackLiteInfo>> LoadOrynivoFolderTracksAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken)
    {
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        if (libraryChangedAt.HasValue)
        {
            // Reading and deserializing the (potentially tens of MB) cache file must not block
            // the UI thread; only re-download from the server when the cache misses.
            var cachedTracks = await Task.Run(
                () => TryLoadOrynivoFolderTrackCache(server, libraryChangedAt.Value, out var t) ? t : null,
                cancellationToken);
            if (cachedTracks is not null)
                return cachedTracks;
        }

        var tracks = await _orynivoClient.GetTrackFoldersAsync(server, cancellationToken);
        if (libraryChangedAt.HasValue)
            await Task.Run(() => SaveOrynivoFolderTrackCache(server, libraryChangedAt.Value, tracks), cancellationToken);
        return tracks;
    }

    private static bool TryLoadOrynivoFolderTrackCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<OrynivoTrackLiteInfo> tracks)
    {
        tracks = [];
        try
        {
            var path = GetOrynivoFolderTrackCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoFolderTrackCache>(File.ReadAllText(path));
            if (cache?.Tracks is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            tracks = cache.Tracks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveOrynivoFolderTrackCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks)
    {
        try
        {
            var path = GetOrynivoFolderTrackCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoFolderTrackCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                tracks.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // Cache failures must never prevent browsing a remote server.
        }
    }

    private static string GetOrynivoFolderTrackCachePath(OrynivoServerSettings server)
        => RemoteServerCache.FolderTrackCachePath(server);

    private async Task<Dictionary<long, OrynivoTrackInfo>> LoadOrynivoFolderTrackMetadataAsync(
        OrynivoServerSettings server,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, OrynivoTrackInfo>();
        if (tracks.All(track =>
                !string.IsNullOrWhiteSpace(track.Artist) ||
                !string.IsNullOrWhiteSpace(track.Album) ||
                track.ArtistId.HasValue ||
                track.AlbumId.HasValue))
        {
            return result;
        }

        var ids = tracks
            .Where(track => track.Id > 0)
            .Select(track => track.Id)
            .Distinct()
            .ToList();
        const int batchSize = 500;
        for (var index = 0; index < ids.Count; index += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(index).Take(batchSize).ToList();
            var rows = await _orynivoClient.GetTracksByIdsAsync(server, batch, cancellationToken);
            foreach (var row in rows)
                result[row.Id] = row;
        }

        return result;
    }

    private void BuildOrynivoFolderTree(
        OrynivoServerSettings server,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks,
        IReadOnlyDictionary<long, OrynivoTrackInfo> metadata)
    {
        BuildFolderTree(MapOrynivoFolderTrackLites(server, tracks, metadata), server);
    }

    /// <summary>
    /// Maps remote Orynivo Server folder tracks to <see cref="TrackLite"/> rows whose
    /// playback path is the authenticated stream URL and whose grouping path is the
    /// server-side source path. Registers each mapped row for transport metadata.
    /// </summary>
    /// <param name="server">The remote server the tracks belong to.</param>
    /// <param name="tracks">Lightweight folder tracks reported by the server.</param>
    /// <param name="metadata">Optional richer metadata keyed by track ID.</param>
    /// <returns>The mapped <see cref="TrackLite"/> rows for the folder tree.</returns>
    private List<TrackLite> MapOrynivoFolderTrackLites(
        OrynivoServerSettings server,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks,
        IReadOnlyDictionary<long, OrynivoTrackInfo> metadata)
    {
        return tracks
            .Where(track => track.Id > 0)
            .Select(track =>
            {
                var row = metadata.TryGetValue(track.Id, out var fullTrack)
                    ? ToOrynivoTrackContentRow(server, fullTrack)
                    : ToOrynivoTrackContentRow(server, track);
                return new TrackLite(
                    row.FilePath,
                    string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath,
                    track.FileName,
                    track.Title,
                    track.DiscNumber,
                    track.TrackNumber);
            })
            .ToList();
    }

    /// <summary>
    /// Builds the unified folder-structure view: a top-level <c>Local</c> group (only
    /// when a local library directory is configured) followed by one group per
    /// configured Orynivo Server (only when the server reports folder tracks), each
    /// containing its own folder tree. Loading runs off the UI thread and is cancelled
    /// when the user navigates away.
    /// </summary>
    /// <returns>A task that completes when the unified folder tree has been built.</returns>
    private async Task ShowUnifiedFolderTreeAsync()
    {
        CancelAndDispose(ref _folderViewCts);
        _folderViewCts = new CancellationTokenSource();
        var ct = _folderViewCts.Token;

        UpdateAlphabetIndex(null, false);
        ShowOrynivoFolderLoadingState();

        var hasLocalDirectory = _settings.LibraryPaths
            .Any(path => !string.IsNullOrWhiteSpace(path));
        var servers = _settings.OrynivoServers ?? [];

        try
        {
            var localTracks = hasLocalDirectory
                ? await Task.Run(() =>
                {
                    try { using var db = AudioDatabase.OpenDefault(); return db.GetTracksLite(); }
                    catch { return new List<TrackLite>(); }
                }, ct)
                : [];
            if (ct.IsCancellationRequested)
                return;

            var serverGroups = new List<(OrynivoServerSettings Server, List<TrackLite> Tracks)>();
            foreach (var server in servers)
            {
                try
                {
                    var tracks = await LoadOrynivoFolderTracksAsync(server, ct);
                    if (ct.IsCancellationRequested)
                        return;
                    var metadata = await LoadOrynivoFolderTrackMetadataAsync(server, tracks, ct);
                    if (ct.IsCancellationRequested)
                        return;
                    serverGroups.Add((server, MapOrynivoFolderTrackLites(server, tracks, metadata)));
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    // An unavailable server must not prevent browsing the remaining sources.
                }
            }

            if (ct.IsCancellationRequested)
                return;

            var totalTracks = BuildUnifiedFolderTree(localTracks, serverGroups);
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(totalTracks);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Populates <see cref="FolderTreeView"/> with a top-level source group per available
    /// source (local library plus each Orynivo Server) and their folder roots below.
    /// </summary>
    /// <param name="localTracks">Local library tracks (empty when no local directory is configured).</param>
    /// <param name="serverGroups">Per-server mapped folder tracks.</param>
    /// <returns>The total number of tracks placed in the tree.</returns>
    private int BuildUnifiedFolderTree(
        List<TrackLite> localTracks,
        IReadOnlyList<(OrynivoServerSettings Server, List<TrackLite> Tracks)> serverGroups)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        _folderTreesBySource.Clear();
        FolderTreeView.Items.Clear();

        var total = 0;
        var hasLocal = localTracks.Count > 0 &&
                       _settings.LibraryPaths.Any(path => !string.IsNullOrWhiteSpace(path));
        var populatedServers = serverGroups.Where(group => group.Tracks.Count > 0).ToList();

        // With a single source, expand its group and roots so the folders show immediately (as
        // the local-only view did before). With several sources, keep the groups collapsed so
        // every source stays visible at the top instead of one burying the others.
        var expandGroups = (hasLocal ? 1 : 0) + populatedServers.Count <= 1;

        if (hasLocal)
        {
            var localNode = CreateFolderSourceGroupNode(LocalizationManager.Current.LocalSource, server: null);
            AddFolderRootsInto(localNode, localTracks, _settings.LibraryPaths, server: null, autoExpandRoots: expandGroups);
            localNode.IsExpanded = expandGroups;
            FolderTreeView.Items.Add(localNode);
            total += localTracks.Count;
        }

        foreach (var (server, tracks) in populatedServers)
        {
            var serverNode = CreateFolderSourceGroupNode(server.Name, server);
            AddFolderRootsInto(serverNode, tracks, preferredRoots: null, server, autoExpandRoots: expandGroups);
            serverNode.IsExpanded = expandGroups;
            FolderTreeView.Items.Add(serverNode);
            total += tracks.Count;
        }

        return total;
    }

    /// <summary>
    /// Creates a non-playable top-level tree node that groups the folder roots of one
    /// library source (the local library or a specific Orynivo Server).
    /// </summary>
    /// <param name="title">The display label of the source group.</param>
    /// <param name="server">The Orynivo Server this group represents, or <see langword="null"/> for local.</param>
    /// <returns>The created source group tree node.</returns>
    private TreeViewItem CreateFolderSourceGroupNode(string title, OrynivoServerSettings? server)
    {
        var item = new TreeViewItem
        {
            Header = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            },
            Tag = new FolderTag(false, string.Empty, string.Empty, server)
        };
        return item;
    }

    private ContentRow ToOrynivoTrackContentRow(OrynivoServerSettings server, OrynivoTrackLiteInfo track)
    {
        var streamUrl = OrynivoServerClient.GetStreamUrl(server, track.Id);
        var row = new ContentRow
        {
            Title = track.Title?.Trim() ?? track.FileName.Trim(),
            AlphabetIndexText = track.Title?.Trim() ?? track.FileName.Trim(),
            Id = track.Id,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            TrackNumber = FormatPartNumber(track.TrackNumber, null),
            DiscNumber = FormatPartNumber(track.DiscNumber, null),
            Duration = FormatSeconds(track.Duration),
            Format = track.Format?.ToUpperInvariant(),
            FileName = track.FileName,
            FilePath = streamUrl,
            SourcePath = string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath,
            ArtworkPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 320),
            ThumbnailPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 96),
            IsFavorite = IsOrynivoFavorite(server, "Track", track.Id),
            ArtistId = track.ArtistId,
            AlbumId = track.AlbumId,
            EntityType = "OrynivoTrack",
            ExternalId = track.Id.ToString(CultureInfo.InvariantCulture),
            KnownDuration = track.Duration.HasValue ? TimeSpan.FromSeconds(track.Duration.Value) : null,
            OrynivoServer = server
        };
        _orynivoTracksByUrl[streamUrl] = row;
        return row;
    }

    // ------------------------------------------------------------------
    // Ordnerstruktur-Baum  (lazy loading für Performance)
    // ------------------------------------------------------------------

    // Vorberechnete Baumstruktur – reine C#-Objekte, keine WPF-Elemente
    private sealed class FolderTree
    {
        private readonly Dictionary<string, List<string>>    _childDirs;
        private readonly Dictionary<string, List<TrackLite>> _filesPerDir;
        private readonly HashSet<string>                     _allDirs;

        public FolderTree(List<TrackLite> tracks)
        {
            _filesPerDir = tracks
                .GroupBy(t => Path.GetDirectoryName(t.SourcePath) ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(t => t.DiscNumber ?? 0)
                           .ThenBy(t => t.TrackNumber ?? 0)
                           .ThenBy(t => t.FileName).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // Alle Zwischenverzeichnisse ergänzen
            _allDirs = new HashSet<string>(_filesPerDir.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in _filesPerDir.Keys.ToList())
            {
                var anc = Path.GetDirectoryName(dir);
                while (anc != null && _allDirs.Add(anc))
                    anc = Path.GetDirectoryName(anc);
            }

            // Parent→Children-Map aufbauen (sortiert)
            _childDirs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _allDirs)
            {
                var parent = Path.GetDirectoryName(d);
                if (parent == null) continue;
                if (!_childDirs.TryGetValue(parent, out var list))
                    _childDirs[parent] = list = [];
                list.Add(d);
            }
            foreach (var list in _childDirs.Values)
                list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string>    SubDirs(string dir)    => _childDirs.TryGetValue(dir,  out var d) ? d : [];
        public IReadOnlyList<TrackLite> Files(string dir)      => _filesPerDir.TryGetValue(dir, out var f) ? f : [];
        public bool HasChildren(string dir)                    => _childDirs.ContainsKey(dir) || _filesPerDir.ContainsKey(dir);

        /// <summary>Returns every track playback path at or below <paramref name="dir"/>, regardless of whether the tree nodes are materialized.</summary>
        /// <param name="dir">Directory to collect files under.</param>
        /// <returns>All descendant file playback paths.</returns>
        public IEnumerable<string> AllFilePathsUnder(string dir)
        {
            foreach (var file in Files(dir))
                yield return file.Path;
            foreach (var sub in SubDirs(dir))
                foreach (var path in AllFilePathsUnder(sub))
                    yield return path;
        }
        public bool HasRoot(string root)
            => _allDirs.Any(dir => IsSameOrBelow(dir, root)) ||
               _filesPerDir.Keys.Any(dir => IsSameOrBelow(dir, root));

        public IEnumerable<string> AutoRoots() =>
            _allDirs
                .Where(d => { var p = Path.GetDirectoryName(d); return p is null || !_allDirs.Contains(p); })
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

        private static bool IsSameOrBelow(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
                return false;
            var normalizedPath = Path.TrimEndingDirectorySeparator(path);
            var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void BuildFolderTree(List<TrackLite> tracks, OrynivoServerSettings? server = null)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        _folderTreesBySource.Clear();
        FolderTreeView.Items.Clear();
        if (tracks.Count == 0) return;

        // Local trees prefer the configured library roots; remote trees derive their
        // roots automatically from the server-side source paths.
        AddFolderRootsInto(
            FolderTreeView,
            tracks,
            server is null ? _settings.LibraryPaths : null,
            server,
            autoExpandRoots: true);
    }

    /// <summary>
    /// Builds a folder tree from <paramref name="tracks"/> and appends its root directory
    /// nodes to <paramref name="parent"/>. Child nodes are materialized lazily.
    /// </summary>
    /// <param name="parent">The tree control or node that receives the root directory items.</param>
    /// <param name="tracks">The tracks the folder tree is built from.</param>
    /// <param name="preferredRoots">Preferred root directories (e.g. configured local paths), or <see langword="null"/>.</param>
    /// <param name="server">The Orynivo Server the tracks belong to, or <see langword="null"/> for the local library.</param>
    /// <param name="autoExpandRoots">Whether to expand each root one level immediately.</param>
    private void AddFolderRootsInto(
        ItemsControl parent,
        List<TrackLite> tracks,
        IReadOnlyList<string>? preferredRoots,
        OrynivoServerSettings? server,
        bool autoExpandRoots)
    {
        if (tracks.Count == 0) return;

        var tree = new FolderTree(tracks);
        _folderTreesBySource[server is null ? LocalSourceKey : GetServerSourceKey(server.Id)] = tree;
        var roots = preferredRoots?
                        .Where(p => !string.IsNullOrWhiteSpace(p) && tree.HasRoot(p))
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    ?? [];
        if (roots.Count == 0)
            roots = [.. tree.AutoRoots()];

        foreach (var root in roots)
            parent.Items.Add(CreateDirItemLazy(root, tree, isRoot: true, server, autoExpandRoots));
    }

    private TreeViewItem CreateDirItemLazy(
        string dirPath,
        FolderTree tree,
        bool isRoot,
        OrynivoServerSettings? server = null,
        bool autoExpand = false)
    {
        var name = Path.GetFileName(dirPath);
        var item = new TreeViewItem
        {
            Header = isRoot ? dirPath : (string.IsNullOrEmpty(name) ? dirPath : name),
            Tag = new FolderTag(false, dirPath, dirPath, server),
            ContextFlyout = CreateSidebarMenuFlyout()
        };
        ApplyNowPlayingClass(item);
        AttachFolderPlaylistContextHandler(item);

        if (!tree.HasChildren(dirPath))
            return item;

        // Lazy population: add a placeholder so the expander shows, and materialize the real
        // child nodes only the first time the node is expanded. Building the whole subtree up
        // front froze the UI for many seconds on large (merged) libraries. Avalonia does not
        // reliably re-render a node whose children are swapped inside its Expanded pass, so the
        // real children must be inserted *before* the node expands. This mirrors the proven
        // Plex lazy-folder pattern: intercept the expand gesture, populate, then expand.
        var populated = false;
        item.Items.Add(CreateFolderPlaceholderNode());

        void Populate()
        {
            if (populated)
                return;
            populated = true;
            item.Items.Clear();
            PopulateDirNode(item, dirPath, tree, server);
        }

        item.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>((_, e) =>
            {
                if (!e.GetCurrentPoint(item).Properties.IsLeftButtonPressed ||
                    !ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), item))
                {
                    return;
                }

                var isChevronPress = FindAncestor<ToggleButton>(e.Source as Visual) is not null;
                if (isChevronPress)
                {
                    if (populated)
                        return;
                    e.Handled = true;
                    Populate();
                    item.IsExpanded = true;
                    return;
                }

                if (e.ClickCount < 2)
                    return;
                e.Handled = true;
                if (item.IsExpanded)
                {
                    item.IsExpanded = false;
                    return;
                }
                Populate();
                item.IsExpanded = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Suppress the default double-tap expand-toggle on the header; the pointer handler above
        // already performs the populate-then-expand, so the default toggle would only re-collapse.
        item.AddHandler(
            InputElement.DoubleTappedEvent,
            new EventHandler<TappedEventArgs>((_, e) =>
            {
                if (ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), item) &&
                    FindAncestor<ToggleButton>(e.Source as Visual) is null)
                    e.Handled = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Keyboard-accessibility fallback: collapse, populate, then expand so the children exist
        // before the visual expansion.
        item.Expanded += (_, _) =>
        {
            if (populated)
                return;
            item.IsExpanded = false;
            Populate();
            item.IsExpanded = true;
        };

        if (autoExpand)
        {
            // Auto-expansion happens before the node is attached, so populating first and then
            // expanding renders correctly.
            Populate();
            item.IsExpanded = true;
        }

        return item;
    }

    /// <summary>Creates a lightweight, non-interactive placeholder child that makes a lazy folder node show its expander.</summary>
    /// <returns>The placeholder tree node.</returns>
    private static TreeViewItem CreateFolderPlaceholderNode() =>
        new() { Header = string.Empty, IsHitTestVisible = false, Focusable = false };

    private void PopulateDirNode(TreeViewItem parent, string dirPath, FolderTree tree, OrynivoServerSettings? server = null)
    {
        parent.Items.Clear();
        foreach (var sub in tree.SubDirs(dirPath))
            parent.Items.Add(CreateDirItemLazy(sub, tree, isRoot: false, server));
        foreach (var track in tree.Files(dirPath))
        {
            var title = new TextBlock
            {
                Text = track.DisplayName,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Children = { title }
                }
            };
            var item = new TreeViewItem
            {
                Header = header,
                Tag = new FolderTag(true, track.Path, dirPath, server),
                ContextFlyout = CreateSidebarMenuFlyout()
            };
            _localFolderTrackItems[track.Path] = item;
            _localFolderTrackHeaders[track.Path] = header;
            ApplyNowPlayingClass(item);
            AttachFolderPlaylistContextHandler(item);
            parent.Items.Add(item);
        }
    }

    private void AttachFolderPlaylistContextHandler(TreeViewItem item)
    {
        item.AddHandler(
            PointerPressedEvent,
            PlaylistContextItem_OnPreviewMouseRightButtonDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private async void FolderTreeView_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FolderTreeView.SelectedItem is TreeViewItem
            {
                Tag: PlexFolderTag { IsTrack: true, Track: not null } plexTag
            } plexTreeItem)
        {
            e.Handled = true;
            var parent = ItemsControl.ItemsControlFromItemContainer(plexTreeItem);
            var siblingTracks = parent?.Items
                .OfType<TreeViewItem>()
                .Select(item => item.Tag)
                .OfType<PlexFolderTag>()
                .Where(tag => tag.IsTrack && tag.Track is not null)
                .Select(tag => tag.Track!)
                .ToList() ?? [];
            if (siblingTracks.Count == 0)
                siblingTracks.Add(plexTag.Track);

            await PlayTrackFromRowsAsync(plexTag.Track, siblingTracks);
            return;
        }

        if (FolderTreeView.SelectedItem is not TreeViewItem { Tag: FolderTag { IsFile: true } tag })
            return;
        e.Handled = true;

        var filePath   = tag.FilePath;
        var folderPath = tag.FolderPath;

        try
        {
            if (!CanPersistQueuePath(filePath))
            {
                var parent = ItemsControl.ItemsControlFromItemContainer((TreeViewItem)FolderTreeView.SelectedItem);
                var siblingPaths = parent?.Items
                    .OfType<TreeViewItem>()
                    .Select(item => item.Tag)
                    .OfType<FolderTag>()
                    .Where(folder => folder.IsFile)
                    .Select(folder => folder.FilePath)
                    .ToList() ?? [filePath];
                _queue.Clear();
                foreach (var path in siblingPaths)
                    _queue.Add(CreatePlaylistItem(path));
                _queueIndex = Math.Max(0, siblingPaths.FindIndex(path =>
                    string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase)));
                ResetQueuePlaybackState();
                PersistPlaybackQueue();
                RefreshQueueNavigationButtons();
                await StartPlaybackAsync(filePath);
                return;
            }

            var folderTracks = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTracksByDirectory(folderPath);
            });

            _queue.Clear();
            foreach (var t in folderTracks)
            _queue.Add(CreatePlaylistItem(t.Path));

            _queueIndex = 0;
            for (int i = 0; i < _queue.Count; i++)
            {
                if (string.Equals(_queue[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                { _queueIndex = i; break; }
            }
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueNavigationButtons();

            await StartPlaybackAsync(filePath);
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private IReadOnlyList<string> GetPathsForFolderItem(TreeViewItem treeItem)
    {
        if (treeItem.Tag is not FolderTag tag)
            return [];

        if (tag.IsFile)
            return [tag.FilePath];

        // Remote server directory (and source-group) nodes cannot be resolved through the
        // local database; their descendant stream URLs are collected from the built tree.
        // This is detected from the node itself so it works in the unified folder view too,
        // not only when a single server's folder view is active.
        var isRemoteNode = tag.Server is not null ||
            (_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
             _activeOrynivoView == "Folders");
        if (isRemoteNode)
        {
            // Child nodes are materialized lazily and may not exist yet, so collect the
            // descendant paths from the in-memory folder tree rather than the visual tree.
            if (tag.Server is { } remoteServer &&
                _folderTreesBySource.TryGetValue(GetServerSourceKey(remoteServer.Id), out var remoteTree))
            {
                return remoteTree.AllFilePathsUnder(tag.FolderPath)
                    .Where(path => !CanPersistQueuePath(path))
                    .ToList();
            }

            return CollectFolderTreePaths(treeItem)
                .Where(path => !CanPersistQueuePath(path))
                .ToList();
        }

        // A local source-group node has no directory of its own; collect its subtree.
        if (string.IsNullOrEmpty(tag.FilePath))
            return CollectFolderTreePaths(treeItem);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetTrackPathsUnderDirectory(tag.FilePath);
        }
        catch
        {
            return [];
        }
    }

    private static List<string> CollectFolderTreePaths(TreeViewItem item)
    {
        var result = new List<string>();
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (child.Tag is FolderTag { IsFile: true } file)
                result.Add(file.FilePath);
            else
                result.AddRange(CollectFolderTreePaths(child));
        }
        return result;
    }
}
