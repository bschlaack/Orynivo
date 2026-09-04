using Orynivo.Library;
using Orynivo.Remote;
using Orynivo.Streaming;
using System.Globalization;

namespace Orynivo;

public partial class MainWindow
{
    /// <summary>Returns a bounded artist list across local and configured server catalogs.</summary>
    private async Task<IReadOnlyList<MobileRemoteArtist>> BrowseMobileRemoteArtistsAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 250);
        var providers = GetMobileRemoteProviders();
        var tasks = providers.Select(async provider =>
        {
            try
            {
                var artists = await provider.GetArtistsAsync(cancellationToken);
                return artists
                    .Where(artist => string.IsNullOrWhiteSpace(query) ||
                        artist.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    .Select(artist => new MobileRemoteArtist(
                        CreateMobileIdentity(provider, artist.Id), artist.Name, provider.DisplayName))
                    .ToList();
            }
            catch
            {
                return [];
            }
        });
        var rows = await Task.WhenAll(tasks);
        return rows.SelectMany(static row => row)
            .OrderBy(static artist => artist.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static artist => artist.Source, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>Returns albums for one opaque, provider-bound artist identity.</summary>
    private async Task<IReadOnlyList<MobileRemoteAlbum>> BrowseMobileRemoteAlbumsAsync(
        string identity,
        CancellationToken cancellationToken)
    {
        var target = ResolveMobileProviderIdentity(identity);
        if (target is null)
            return [];
        try
        {
            var albums = await target.Value.Provider.GetAlbumsByArtistAsync(
                target.Value.Id, includeArtwork: false, cancellationToken);
            return albums
                .OrderBy(static album => album.Year ?? int.MaxValue)
                .ThenBy(static album => album.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(album => new MobileRemoteAlbum(
                    CreateMobileIdentity(target.Value.Provider, album.Id),
                    album.Title,
                    album.DisplayArtist,
                    album.Year,
                    target.Value.Provider.DisplayName))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Returns tracks for one opaque, provider-bound album identity.</summary>
    private async Task<IReadOnlyList<MobileRemoteTrack>> BrowseMobileRemoteAlbumTracksAsync(
        string identity,
        CancellationToken cancellationToken)
    {
        var target = ResolveMobileProviderIdentity(identity);
        if (target is null)
            return [];
        try
        {
            var tracks = await target.Value.Provider.GetTracksByAlbumAsync(target.Value.Id, cancellationToken: cancellationToken);
            return tracks
                .OrderBy(static track => track.DiscNumber ?? 0)
                .ThenBy(static track => track.TrackNumber ?? int.MaxValue)
                .ThenBy(static track => track.Title ?? track.FileName, StringComparer.CurrentCultureIgnoreCase)
                .Select(track => new MobileRemoteTrack(
                    CreateMobileIdentity(target.Value.Provider, track.Id),
                    track.Title ?? track.FileName,
                    track.Artist,
                    track.Album,
                    track.Year,
                    target.Value.Provider.DisplayName))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<ILibraryCatalogProvider> GetMobileRemoteProviders()
    {
        var providers = new List<ILibraryCatalogProvider> { _localCatalogProvider };
        providers.AddRange((_settings.OrynivoServers ?? []).Select(CreateOrynivoCatalogProvider));
        return providers;
    }

    private static string CreateMobileIdentity(ILibraryCatalogProvider provider, long id) =>
        provider.Source == LibraryCatalogSource.Local
            ? $"local:{id.ToString(CultureInfo.InvariantCulture)}"
            : $"server:{Uri.EscapeDataString(provider.Id)}:{id.ToString(CultureInfo.InvariantCulture)}";

    private (ILibraryCatalogProvider Provider, long Id)? ResolveMobileProviderIdentity(string identity)
    {
        if (identity.StartsWith("local:", StringComparison.Ordinal) &&
            long.TryParse(identity[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var localId))
            return (_localCatalogProvider, localId);
        if (!identity.StartsWith("server:", StringComparison.Ordinal))
            return null;
        var separator = identity.LastIndexOf(':');
        if (separator <= 7 ||
            !long.TryParse(identity[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var remoteId))
            return null;
        var serverId = Uri.UnescapeDataString(identity[7..separator]);
        var server = (_settings.OrynivoServers ?? []).FirstOrDefault(candidate => candidate.Id == serverId);
        return server is null ? null : (CreateOrynivoCatalogProvider(server), remoteId);
    }

    /// <summary>Searches local and configured Orynivo Server tracks without returning playable paths.</summary>
    /// <param name="query">User-entered search text.</param>
    /// <param name="limit">Maximum combined result count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Safe track summaries with opaque provider identities.</returns>
    private async Task<IReadOnlyList<MobileRemoteTrack>> SearchMobileRemoteTracksAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length < 2)
            return [];
        limit = Math.Clamp(limit, 1, 50);

        var localTask = Task.Run(() =>
        {
            var ids = TrackSearchIndex.SearchByCategory(query, limit).Tracks.Ids.Take(limit).ToList();
            using var db = AudioDatabase.OpenDefault();
            return db.GetTrackListByIds(ids)
                .Select(track => new MobileRemoteTrack(
                    $"local:{track.Id.ToString(CultureInfo.InvariantCulture)}",
                    track.Title ?? track.FileName,
                    track.Artist,
                    track.Album,
                    track.Year,
                    "Local"))
                .ToList();
        }, cancellationToken);

        var servers = (_settings.OrynivoServers ?? []).ToList();
        var remoteTasks = servers.Select(async server =>
        {
            try
            {
                using var client = new OrynivoServerClient();
                var result = await client.SearchStructuredAsync(
                    server, query, "tracks", null, null, null, null, "relevance", limit,
                    cancellationToken);
                return result.Tracks.Select(track => new MobileRemoteTrack(
                    $"server:{Uri.EscapeDataString(server.Id)}:{track.Id.ToString(CultureInfo.InvariantCulture)}",
                    track.Title ?? track.FileName,
                    track.Artist,
                    track.Album,
                    track.Year,
                    server.Name)).ToList();
            }
            catch
            {
                return [];
            }
        });
        var remote = await Task.WhenAll(remoteTasks);
        var local = await localTask;
        return local.Concat(remote.SelectMany(static tracks => tracks))
            .Take(limit)
            .ToList();
    }

    /// <summary>Resolves an opaque mobile result and applies a bounded playback or queue action.</summary>
    /// <param name="identity">Opaque local or server track identity.</param>
    /// <param name="action">One of <c>play</c>, <c>next</c>, or <c>append</c>.</param>
    /// <returns><see langword="true"/> when the identity and action were accepted.</returns>
    private async Task<bool> QueueMobileRemoteTrackAsync(string identity, string action)
    {
        string? path = null;
        if (identity.StartsWith("local:", StringComparison.Ordinal) &&
            long.TryParse(identity[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var localId))
        {
            path = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackById(localId)?.Path;
            });
        }
        else if (identity.StartsWith("server:", StringComparison.Ordinal))
        {
            var separator = identity.LastIndexOf(':');
            if (separator > 7 &&
                long.TryParse(identity[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var trackId))
            {
                var serverId = Uri.UnescapeDataString(identity[7..separator]);
                path = await ResolveRemoteMcpTrackAsync(
                    $"orynivo://{Uri.EscapeDataString(serverId)}/track/{trackId.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (string.IsNullOrWhiteSpace(path))
            return false;
        switch (action.Trim().ToLowerInvariant())
        {
            case "play":
                await StartPlaybackAsync(path);
                return true;
            case "next":
                if (_mcpBridge.PlayNextFunc is null)
                    return false;
                await _mcpBridge.PlayNextFunc(path);
                return true;
            case "append":
                if (_mcpBridge.AppendToQueueFunc is null)
                    return false;
                await _mcpBridge.AppendToQueueFunc(path);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves, removes, or clears queue entries through the same state-maintenance path as the desktop.</summary>
    /// <param name="action">One of <c>remove</c>, <c>up</c>, <c>down</c>, or <c>clear</c>.</param>
    /// <param name="index">Target queue index for item actions.</param>
    /// <returns><see langword="true"/> when the requested edit was valid.</returns>
    private async Task<bool> EditMobileRemoteQueueAsync(string action, int? index)
    {
        action = action.Trim().ToLowerInvariant();
        if (action == "clear")
        {
            ClearPlaybackQueue();
            return true;
        }
        if (index is not int target || target < 0 || target >= _queue.Count)
            return false;
        if (action == "up" && target > 0)
        {
            await MoveQueueItemAsync(target, target - 1);
            return true;
        }
        if (action == "down" && target + 1 < _queue.Count)
        {
            await MoveQueueItemAsync(target, target + 1);
            return true;
        }
        if (action != "remove")
            return false;

        var removed = _queue[target];
        var current = GetCurrentQueueItem();
        _queue.RemoveAt(target);
        _queueIndex = ReferenceEquals(current, removed)
            ? target - 1
            : IndexOfQueueItem(current);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        await RefreshActiveGaplessQueueAsync();
        return true;
    }
}
