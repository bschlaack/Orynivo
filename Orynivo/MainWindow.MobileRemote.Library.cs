using Orynivo.Remote;
using Orynivo.Library;

namespace Orynivo;

/// <summary>Adapts bounded mobile catalog selections to the existing playback queue.</summary>
public partial class MainWindow
{
    /// <summary>Browses albums independently of artist navigation, preserving provider identities.</summary>
    /// <param name="query">Literal title or artist filter.</param>
    /// <param name="ct">Request cancellation.</param>
    /// <returns>Bounded display-only albums.</returns>
    private async Task<IReadOnlyList<MobileRemoteAlbum>> BrowseMobileRemoteAllAlbumsAsync(string? query, CancellationToken ct)
    {
        var providers = GetMobileRemoteProviders();
        var sources = await Task.WhenAll(providers.Select(async provider =>
        {
            var albums = await provider.GetAlbumsAsync(false, ct);
            return albums.Where(a => !string.IsNullOrWhiteSpace(a.Title) && (string.IsNullOrWhiteSpace(query) ||
                    a.Title.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                    a.DisplayArtist?.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) == true))
                .Select(a => new MobileRemoteAlbum(CreateMobileIdentity(provider, a.Id), a.Title, a.DisplayArtist, a.Year, provider.DisplayName));
        }));
        return sources.SelectMany(x => x).OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).Take(500).ToArray();
    }

    /// <summary>Resolves the displayed library/search suffix under shared queue and playback ownership.</summary>
    /// <param name="identities">Ordered source-aware identities, including duplicate occurrences.</param>
    /// <returns>Whether the current queue replacement prepared a player.</returns>
    private async Task<bool> QueueMobileRemoteTracksAsync(IReadOnlyList<string> identities)
    {
        if (_playbackShuttingDown || identities.Count is < 1 or > 500) return false;
        _queueEdits.Invalidate();
        var edit = _queueEdits.Capture();
        var intent = _playbackStarts.Begin();
        var generation = _unifiedLibraryLoads.Generation;
        _playbackPreparations.CancelAll();
        using var preparation = _playbackPreparations.Begin();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(preparation.Token, edit.Token);
        bool Current() => !_playbackShuttingDown && _playbackStarts.IsCurrent(intent) &&
            _queueEdits.IsCurrent(edit.Version) && generation == _unifiedLibraryLoads.Generation;
        try
        {
            var sources = identities.Select(id => (Identity: id, Source: ResolveMobileProviderIdentity(id))).ToArray();
            if (sources.Any(x => x.Source is null)) return false;
            var providers = sources.Select(x => x.Source!.Value.Provider)
                .GroupBy(p => (p.Source, p.Id)).ToDictionary(g => g.Key, g => g.First());
            var requests = sources.GroupBy(x => (x.Source!.Value.Provider.Source, x.Source.Value.Provider.Id))
                .ToDictionary(g => g.Key.Source + ":" + g.Key.Id, g => (IReadOnlyList<long>)g.Select(x => x.Source!.Value.Id).ToArray());
            var keys = providers.ToDictionary(p => p.Key.Source + ":" + p.Key.Id, p => p.Value);
            var loaded = await PlaylistTrackBatchLoader.LoadAsync(requests, async (key, ids, token) =>
            {
                var tracks = await keys[key].GetTracksByIdsAsync(ids, token);
                return (IReadOnlyList<(string Key, LibraryCatalogTrack Track)>)tracks.Where(t => ids.Contains(t.Id)).Select(t => (key, t)).ToArray();
            }, cancellation.Token, timeout: TimeSpan.FromSeconds(20));
            if (!Current()) return false;
            var byId = loaded.Rows.GroupBy(x => (x.Key, x.Track.Id)).ToDictionary(g => g.Key, g => g.First().Track);
            var paths = new List<string>();
            foreach (var entry in sources)
            {
                var (provider, id) = entry.Source!.Value;
                if (!byId.TryGetValue((provider.Source + ":" + provider.Id, id), out var track))
                {
                    if (paths.Count == 0) return false;
                    continue;
                }
                if (track.Source == LibraryCatalogSource.OrynivoServer)
                {
                    var server = (_settings.OrynivoServers ?? []).FirstOrDefault(s => s.Id == provider.Id);
                    if (server is null) return false;
                    ToCatalogTrackContentRow(track, server);
                }
                paths.Add(track.PlaybackPath);
            }
            if (!Current() || paths.Count == 0 || _mcpBridge.ReplaceQueueFunc is not { } replace) return false;
            preparation.Dispose();
            return await replace(paths) > 0;
        }
        catch (Exception) when (!Current() || cancellation.IsCancellationRequested) { return false; }
    }
}
