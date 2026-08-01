using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

    /// <summary>Loads and renders one level of the unified local and remote genre cloud.</summary>
    /// <param name="parentKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
    private async Task ShowGenreCloudAsync(string? parentKey)
    {
        CancelAndDispose(ref _genreCloudCts);
        _genreCloudCts = new CancellationTokenSource();
        var cancellationToken = _genreCloudCts.Token;
        GenreCloudNodesPanel.Children.Clear();
        GenreCloudBreadcrumbPanel.Children.Clear();
        GenreCloudEmptyTextBlock.IsVisible = false;
        ContentDataGrid.ItemsSource = null;
        ContentCountTextBlock.Text = string.Empty;

        try
        {
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
                    group.Any(node => node.HasChildren)))
                .OrderByDescending(node => node.TrackCount)
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            BuildGenreCloudBreadcrumb(parentKey);
            BuildGenreCloudNodes(nodes);
            GenreCloudEmptyTextBlock.IsVisible = nodes.Count == 0;

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
            cancellationToken.ThrowIfCancellationRequested();
            ContentDataGrid.ItemsSource = rows;
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        }
        catch (OperationCanceledException)
        {
            // Navigation or a newer drill-down superseded this request.
        }
    }

    /// <summary>Loads a compact genre snapshot from one server, with a facet fallback for older servers.</summary>
    private async Task<GenreCloudSource> LoadRemoteGenreCloudAsync(
        OrynivoServerSettings server,
        string? parentKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await _orynivoClient.GetGenreCloudAsync(server, parentKey, 500, cancellationToken);
        if (snapshot.Nodes.Count == 0 && snapshot.Candidates.Count == 0)
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

    /// <summary>Builds clickable breadcrumbs from the selected taxonomy path.</summary>
    private void BuildGenreCloudBreadcrumb(string? parentKey)
    {
        AddGenreBreadcrumbButton(LocalizationManager.Current.AllGenres, null);
        if (string.IsNullOrWhiteSpace(parentKey))
            return;
        var path = GenreCloudService.BuildSnapshot([], parentKey).BreadcrumbKeys;
        foreach (var key in path)
            AddGenreBreadcrumbButton(GenreCloudService.GetDisplayName(key), key);
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
    private void BuildGenreCloudNodes(IReadOnlyList<GenreCloudNode> nodes)
    {
        var maximum = Math.Max(1, nodes.Select(node => node.TrackCount).DefaultIfEmpty(1).Max());
        var minimumSize = GetTypographySize("FontSizeBody", 13);
        var maximumSize = GetTypographySize("FontSizeHeadline", 28);
        foreach (var node in nodes)
        {
            var scale = Math.Log(1 + node.TrackCount) / Math.Log(1 + maximum);
            var button = new Button
            {
                Content = $"{node.DisplayName}  ·  {node.TrackCount:N0}",
                Tag = node.Key,
                Margin = new Thickness(0, 0, 9, 9),
                Padding = new Thickness(12, 7),
                FontSize = minimumSize + (maximumSize - minimumSize) * scale,
                FontWeight = node.TrackCount == maximum ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Click += GenreCloudButton_OnClick;
            GenreCloudNodesPanel.Children.Add(button);
        }
    }

    /// <summary>Returns a numeric typography token for dynamic count-based scaling.</summary>
    private double GetTypographySize(string resourceKey, double fallback) =>
        this.TryFindResource(resourceKey, out var value) && value is double size ? size : fallback;

    /// <summary>Handles cloud and breadcrumb drill-down actions.</summary>
    private async void GenreCloudButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button)
            await ShowGenreCloudAsync(button.Tag as string);
    }
}
