using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class MainWindow
{
    private sealed record GenreCloudSource(
        OrynivoServerSettings? Server,
        GenreCloudSnapshot Snapshot);

    private sealed record GenreCloudCandidate(
        OrynivoServerSettings? Server,
        GenreCloudTrackCandidate Track,
        double Score);

    private CancellationTokenSource? _genreCloudCts;
    private string? _genreCloudSelectedKey;
    private List<GenreCloudNode> _genreCloudNodes = [];
    private List<ContentRow> _genreCloudTrackRows = [];
    private List<ContentRow> _genreCloudAlbumRows = [];

    /// <summary>Loads and renders one level of the unified local and remote genre cloud.</summary>
    /// <param name="parentKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
    private async Task ShowGenreCloudAsync(string? parentKey)
    {
        _genreCloudSelectedKey = parentKey;
        CancelAndDispose(ref _genreCloudCts);
        _genreCloudCts = new CancellationTokenSource();
        var cancellationToken = _genreCloudCts.Token;
        try
        {
            if (GenreCloudNodesCanvas.Children.Count > 0)
            {
                GenreCloudNodesCanvas.Opacity = 0;
                await Task.Delay(140, cancellationToken);
            }
            GenreCloudNodesCanvas.Children.Clear();
            GenreCloudBreadcrumbPanel.Children.Clear();
            GenreCloudEmptyTextBlock.IsVisible = false;
            GenreCloudLeafTextBlock.IsVisible = false;
            ContentDataGrid.ItemsSource = null;
            AlbumArtworkListBox.ItemsSource = null;
            ContentCountTextBlock.Text = string.Empty;

            var localTask = Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return new GenreCloudSource(
                    null,
                    GenreCloudService.BuildSnapshot(db.GetTrackFacets(), parentKey, 500));
            }, cancellationToken);
            var remoteTasks = _settings.OrynivoServers
                .Select(server => LoadRemoteGenreCloudAsync(server, parentKey, cancellationToken))
                .ToArray();
            var sources = new List<GenreCloudSource> { await localTask };
            sources.AddRange(await Task.WhenAll(remoteTasks));
            cancellationToken.ThrowIfCancellationRequested();

            var nodes = sources
                .SelectMany(source => source.Snapshot.Nodes)
                .GroupBy(node => node.Key, StringComparer.Ordinal)
                .Select(group => new GenreCloudNode(
                    group.Key,
                    group.First().DisplayName,
                    group.Sum(node => node.TrackCount),
                    group.Sum(node => node.AlbumCount),
                    group.Any(node => node.HasChildren)))
                .OrderByDescending(node => node.TrackCount)
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _genreCloudNodes = nodes;
            BuildGenreCloudBreadcrumb(parentKey);
            BuildGenreCloudNodes(nodes, GenreAlbumRecommendationsRadioButton.IsChecked == true);
            GenreCloudNodesCanvas.Opacity = 1;
            GenreCloudEmptyTextBlock.IsVisible = nodes.Count == 0 && string.IsNullOrWhiteSpace(parentKey);
            GenreCloudLeafTextBlock.IsVisible = nodes.Count == 0 && !string.IsNullOrWhiteSpace(parentKey);
            GenreCloudLeafTextBlock.Text = GenreCloudLeafTextBlock.IsVisible
                ? GetGenreCloudDisplayName(parentKey!)
                : string.Empty;

            var listeningWeights = await Task.Run(LoadGenreListeningWeights, cancellationToken);
            var candidates = sources
                .SelectMany(source => source.Snapshot.Candidates.Select(candidate =>
                    new GenreCloudCandidate(
                        source.Server,
                        candidate,
                        CalculateGenreCandidateScore(source.Server, candidate, listeningWeights))))
                .OrderByDescending(candidate => candidate.Score)
                .Take(100)
                .ToList();
            var rows = await ResolveGenreCandidateRowsAsync(candidates, cancellationToken);
            var albumRows = await ResolveGenreCandidateAlbumRowsAsync(rows, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _genreCloudTrackRows = rows;
            _genreCloudAlbumRows = albumRows;
            ApplyGenreRecommendationMode();
        }
        catch (OperationCanceledException)
        {
            // Navigation or a newer drill-down superseded this request.
        }
    }

    /// <summary>Collapses the Genre Cloud surfaces before an album detail view is displayed.</summary>
    private void HideGenreCloudForDetailView()
    {
        GenreCloudPanel.IsVisible = false;
        GenreCloudSurface.IsVisible = false;
    }

    /// <summary>Loads a compact genre snapshot from one server, with a facet fallback for older servers.</summary>
    private async Task<GenreCloudSource> LoadRemoteGenreCloudAsync(
        OrynivoServerSettings server,
        string? parentKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await _orynivoClient.GetGenreCloudAsync(server, parentKey, 500, cancellationToken);
        var returnedWrongLevel = !string.Equals(
            snapshot.ParentKey,
            parentKey,
            StringComparison.Ordinal);
        if (returnedWrongLevel || snapshot.Nodes.Count == 0 && snapshot.Candidates.Count == 0)
        {
            var facets = await _orynivoClient.GetTrackFacetsAsync(server, cancellationToken);
            snapshot = GenreCloudService.BuildSnapshot(facets, parentKey, 500);
        }
        return new GenreCloudSource(server, snapshot);
    }

    /// <summary>Returns taxonomy weights derived from listening time across all recorded playback sources.</summary>
    private static IReadOnlyDictionary<string, double> LoadGenreListeningWeights()
    {
        using var db = AudioDatabase.OpenDefault();
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (genre, seconds) in db.GetTopGenres(100))
        {
            foreach (var key in GenreCloudService.ResolveGenreKeys(genre))
                result[key] = result.GetValueOrDefault(key) + seconds;
        }
        return result;
    }

    /// <summary>Combines listening affinity, favorite state, and stable variation into a recommendation score.</summary>
    private double CalculateGenreCandidateScore(
        OrynivoServerSettings? server,
        GenreCloudTrackCandidate candidate,
        IReadOnlyDictionary<string, double> listeningWeights)
    {
        var isFavorite = server is null
            ? candidate.IsFavorite
            : IsOrynivoFavorite(server, "Track", candidate.TrackId);
        var affinity = listeningWeights
            .Where(pair => GenreCloudService.IsDescendantOrSelf(candidate.GenreKey, pair.Key) ||
                           GenreCloudService.IsDescendantOrSelf(pair.Key, candidate.GenreKey))
            .Sum(pair => pair.Value);
        var stableVariation = Math.Abs(HashCode.Combine(server?.Id, candidate.TrackId)) % 1000 / 1000d;
        return Math.Log10(1 + affinity) * 100 + (isFavorite ? 25 : 0) + stableVariation;
    }

    /// <summary>Resolves compact candidate identifiers to shared playable table rows.</summary>
    private async Task<List<ContentRow>> ResolveGenreCandidateRowsAsync(
        IReadOnlyList<GenreCloudCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<(string Source, long Id), ContentRow>();
        var localIds = candidates.Where(candidate => candidate.Server is null)
            .Select(candidate => candidate.Track.TrackId).Distinct().ToList();
        if (localIds.Count > 0)
        {
            var localTracks = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackListByIds(localIds);
            }, cancellationToken);
            foreach (var track in localTracks)
                resolved[("local", track.Id)] = ToTrackContentRow(track);
        }

        var remoteGroups = candidates.Where(candidate => candidate.Server is not null)
            .GroupBy(candidate => candidate.Server!.Id, StringComparer.Ordinal);
        foreach (var group in remoteGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = group.First().Server!;
            var ids = group.Select(candidate => candidate.Track.TrackId).Distinct().ToList();
            var tracks = await _orynivoClient.GetTracksByIdsAsync(server, ids, cancellationToken);
            foreach (var track in tracks)
                resolved[($"server:{server.Id}", track.Id)] = ToOrynivoTrackContentRow(server, track);
        }

        return candidates
            .Select(candidate => resolved.GetValueOrDefault((
                candidate.Server is null ? "local" : $"server:{candidate.Server.Id}",
                candidate.Track.TrackId)))
            .Where(row => row is not null)
            .Cast<ContentRow>()
            .DistinctBy(row => (row.OrynivoServer?.Id ?? "local", row.Id))
            .ToList();
    }

    /// <summary>Resolves the distinct albums represented by the ranked track recommendations.</summary>
    private async Task<List<ContentRow>> ResolveGenreCandidateAlbumRowsAsync(
        IReadOnlyList<ContentRow> trackRows,
        CancellationToken cancellationToken)
    {
        var result = new List<ContentRow>();
        var localAlbumIds = trackRows
            .Where(row => row.OrynivoServer is null && row.AlbumId.HasValue)
            .Select(row => row.AlbumId!.Value)
            .Distinct()
            .ToHashSet();
        if (localAlbumIds.Count > 0)
        {
            var localAlbums = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetAlbumsLite(includeArtwork: true)
                    .Where(album => localAlbumIds.Contains(album.Id))
                    .Select(album => new LibraryCatalogAlbum(
                        LibraryCatalogSource.Local,
                        album.Id,
                        album.Album,
                        album.DisplayArtist,
                        album.Year,
                        album.ArtworkPath,
                        album.ThumbnailPath,
                        album.IsFavorite,
                        album.ArtistId))
                    .ToList();
            }, cancellationToken);
            result.AddRange(localAlbums.Select(album => ToCatalogAlbumContentRow(album)));
        }

        foreach (var serverGroup in trackRows
                     .Where(row => row.OrynivoServer is not null && row.AlbumId.HasValue)
                     .GroupBy(row => row.OrynivoServer!.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = serverGroup.First().OrynivoServer!;
            var albumIds = serverGroup.Select(row => row.AlbumId!.Value).Distinct().ToHashSet();
            var provider = CreateOrynivoCatalogProvider(server);
            var albums = await LoadAllOrynivoAlbumsAsync(server, provider, cancellationToken);
            result.AddRange(albums
                .Where(album => albumIds.Contains(album.Id))
                .Select(album => ToCatalogAlbumContentRow(album, server)));
        }

        var rank = trackRows
            .Where(row => row.AlbumId.HasValue)
            .Select((row, index) => new
            {
                Key = (row.OrynivoServer?.Id ?? "local", row.AlbumId!.Value),
                Index = index
            })
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Index));
        return result
            .Where(row => IsKnownAlbumTitle(row.Title))
            .DistinctBy(row => (row.OrynivoServer?.Id ?? "local", row.AlbumId ?? row.Id ?? 0))
            .OrderBy(row => rank.GetValueOrDefault((
                row.OrynivoServer?.Id ?? "local",
                row.AlbumId ?? row.Id ?? 0), int.MaxValue))
            .Take(40)
            .ToList();
    }

    /// <summary>Switches the recommendation result between playable tracks and album artwork cards.</summary>
    private void ApplyGenreRecommendationMode()
    {
        if (_currentTopLevelTag != "GenreCloud")
            return;
        var showAlbums = GenreAlbumRecommendationsRadioButton.IsChecked == true;
        ContentDataGrid.IsVisible = !showAlbums;
        AlbumArtworkListBox.IsVisible = showAlbums;
        ArtistArtworkListBox.IsVisible = false;
        if (showAlbums)
        {
            _albumArtworkRows = _genreCloudAlbumRows;
            _visibleAlbumArtworkRows.Clear();
            foreach (var album in _genreCloudAlbumRows)
                _visibleAlbumArtworkRows.Add(album);
            AlbumArtworkListBox.ItemsSource = _visibleAlbumArtworkRows;
            QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(_genreCloudAlbumRows.Count);
        }
        else
        {
            ContentDataGrid.ItemsSource = _genreCloudTrackRows;
            AlbumArtworkListBox.ItemsSource = null;
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(_genreCloudTrackRows.Count);
        }
    }

    /// <summary>Handles an explicit click on the Track/Album recommendation-mode selector.</summary>
    private void GenreRecommendationMode_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
            return;
        var showAlbums = ReferenceEquals(radioButton, GenreAlbumRecommendationsRadioButton);
        GenreAlbumRecommendationsRadioButton.IsChecked = showAlbums;
        GenreTrackRecommendationsRadioButton.IsChecked = !showAlbums;
        BuildGenreCloudNodes(_genreCloudNodes, showAlbums);
        ApplyGenreRecommendationMode();
    }

    /// <summary>Builds clickable breadcrumbs from the selected taxonomy path.</summary>
    private void BuildGenreCloudBreadcrumb(string? parentKey)
    {
        AddGenreBreadcrumbButton(LocalizationManager.Current.AllGenres, null);
        if (string.IsNullOrWhiteSpace(parentKey))
            return;
        var path = GenreCloudService.BuildSnapshot([], parentKey).BreadcrumbKeys;
        foreach (var key in path)
            AddGenreBreadcrumbButton(GetGenreCloudDisplayName(key), key);
    }

    /// <summary>Adds one clickable genre breadcrumb.</summary>
    private void AddGenreBreadcrumbButton(string label, string? key)
    {
        var button = new Button
        {
            Content = label,
            Tag = key,
            Margin = new Thickness(0, 0, 7, 4),
            Padding = new Thickness(10, 5),
            FontSize = GetTypographySize("FontSizeCaption", 12)
        };
        button.Click += GenreCloudButton_OnClick;
        GenreCloudBreadcrumbPanel.Children.Add(button);
    }

    /// <summary>Builds count-scaled buttons for the visible genre nodes.</summary>
    private void BuildGenreCloudNodes(IReadOnlyList<GenreCloudNode> nodes, bool useAlbumCounts)
    {
        GenreCloudNodesCanvas.Children.Clear();
        var maximum = Math.Max(1, nodes.Select(node => useAlbumCounts ? node.AlbumCount : node.TrackCount)
            .DefaultIfEmpty(1).Max());
        var minimumSize = GetTypographySize("FontSizeBody", 13);
        var maximumSize = GetTypographySize("FontSizeHeadline", 28);
        foreach (var node in nodes)
        {
            var count = useAlbumCounts ? node.AlbumCount : node.TrackCount;
            var scale = Math.Log(1 + count) / Math.Log(1 + maximum);
            var button = new Button
            {
                Content = $"{GetGenreCloudDisplayName(node.Key)}  ·  {count:N0}",
                Tag = node.Key,
                Margin = new Thickness(0, 0, 9, 9),
                Padding = new Thickness(12, 7),
                FontSize = minimumSize + (maximumSize - minimumSize) * scale,
                FontWeight = count == maximum ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Opacity = 0;
            button.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }
            };
            var scaleTransform = new ScaleTransform(0.82, 0.82);
            scaleTransform.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(260) },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(260) }
            };
            button.RenderTransform = scaleTransform;
            button.RenderTransformOrigin = RelativePoint.Center;
            button.Click += GenreCloudButton_OnClick;
            GenreCloudNodesCanvas.Children.Add(button);
        }

        Dispatcher.UIThread.Post(ArrangeGenreCloudNodes, DispatcherPriority.Loaded);
    }

    /// <summary>Places genre buttons in centered, collision-free cloud rows and starts their staggered transition.</summary>
    private void ArrangeGenreCloudNodes()
    {
        const double horizontalGap = 14;
        const double verticalGap = 12;
        var width = Math.Max(320, GenreCloudSurface.Bounds.Width - 40);
        var maximumRowWidth = Math.Max(280, width - 24);
        var entries = new List<(Button Button, double Width, double Height, int Index)>();
        for (var index = 0; index < GenreCloudNodesCanvas.Children.Count; index++)
        {
            if (GenreCloudNodesCanvas.Children[index] is not Button button)
                continue;
            button.Measure(new Size(maximumRowWidth, double.PositiveInfinity));
            entries.Add((
                button,
                Math.Min(maximumRowWidth, Math.Max(90, button.DesiredSize.Width)),
                Math.Max(34, button.DesiredSize.Height),
                index));
        }

        var rows = new List<List<(Button Button, double Width, double Height, int Index)>>();
        var rowWidths = new List<double>();
        foreach (var entry in entries)
        {
            if (rows.Count == 0 || rowWidths[^1] + horizontalGap + entry.Width > maximumRowWidth)
            {
                rows.Add([]);
                rowWidths.Add(0);
            }
            var rowIndex = rows.Count - 1;
            rows[rowIndex].Add(entry);
            rowWidths[rowIndex] += (rows[rowIndex].Count > 1 ? horizontalGap : 0) + entry.Width;
        }

        var rowHeights = rows.Select(row => row.Max(entry => entry.Height)).ToList();
        var contentHeight = rowHeights.Sum() + Math.Max(0, rows.Count - 1) * verticalGap + 16;
        GenreCloudNodesCanvas.Height = Math.Max(330, contentHeight);
        var top = Math.Max(8, (GenreCloudNodesCanvas.Height - contentHeight) / 2 + 8);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var left = Math.Max(8, (width - rowWidths[rowIndex]) / 2);
            foreach (var entry in rows[rowIndex])
            {
                Canvas.SetLeft(entry.Button, left);
                Canvas.SetTop(entry.Button, top + (rowHeights[rowIndex] - entry.Height) / 2);
                left += entry.Width + horizontalGap;
                var delay = TimeSpan.FromMilliseconds(Math.Min(entry.Index, 14) * 28);
                DispatcherTimer.RunOnce(() =>
                {
                    entry.Button.Opacity = 1;
                    if (entry.Button.RenderTransform is ScaleTransform transform)
                    {
                        transform.ScaleX = 1;
                        transform.ScaleY = 1;
                    }
                }, delay);
            }
            top += rowHeights[rowIndex] + verticalGap;
        }
    }

    /// <summary>Returns a numeric typography token for dynamic count-based scaling.</summary>
    private double GetTypographySize(string resourceKey, double fallback) =>
        this.TryFindResource(resourceKey, out var value) && value is double size ? size : fallback;

    /// <summary>Returns a localized UI name for virtual nodes and the taxonomy fallback for actual genres.</summary>
    private static string GetGenreCloudDisplayName(string key) => key == "more-genres"
        ? LocalizationManager.Current.MoreGenres
        : GenreCloudService.GetDisplayName(key);

    /// <summary>Handles cloud and breadcrumb drill-down actions.</summary>
    private async void GenreCloudButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button)
            await ShowGenreCloudAsync(button.Tag as string);
    }
}
