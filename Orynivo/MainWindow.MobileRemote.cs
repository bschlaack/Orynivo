using Orynivo.Library;
using Orynivo.Remote;
using Orynivo.Streaming;
using System.Globalization;

namespace Orynivo;

public partial class MainWindow
{
    /// <summary>Loads the shared playlist names without exposing paths or smart criteria.</summary>
    private Task<IReadOnlyList<MobileRemotePlaylist>> BrowseMobileRemotePlaylistsAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<MobileRemotePlaylist>>(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetAllPlaylists().Select(p => new MobileRemotePlaylist(
                p.Id, p.Name, p.IsSmartPlaylist ? null : p.TrackCount, p.IsSmartPlaylist)).ToArray();
        }, ct);

    /// <summary>Resolves the desktop playlist asynchronously without mutating UI-owned metadata dictionaries.</summary>
    private async Task<IReadOnlyList<MobileRemoteTrack>> BrowseMobileRemotePlaylistTracksAsync(long id, CancellationToken ct)
    {
        var result = await LoadStoredPlaylistAsync(id, ct);
        var rows = result.Rows;
        return rows.Select((r, index) => (Row: r, Index: index)).Where(entry => entry.Row.Id.HasValue).Select(entry =>
        {
            var r = entry.Row;
            return new MobileRemoteTrack(
            r.OrynivoServer is { } server
                ? $"server:{Uri.EscapeDataString(server.Id)}:{r.Id}"
                : $"local:{r.Id}",
            r.Title ?? string.Empty, r.Artist, r.Album,
            int.TryParse(r.Year, out var year) ? year : null,
            r.OrynivoServer?.Name ?? Localization.LocalizationManager.Current.LocalSource, entry.Index);
        }).ToArray();
    }

    /// <summary>Owns playlist-play resolution before registering metadata or replacing the queue.</summary>
    /// <param name="id">The stored playlist identifier.</param>
    /// <param name="action">Play replaces the queue; append preserves independent queue semantics.</param>
    /// <param name="startIndex">Optional original selected row index.</param>
    /// <param name="expectedIdentity">Expected opaque identity of that row.</param>
    /// <returns>Whether current playable rows were accepted.</returns>
    private async Task<bool> QueueMobileRemotePlaylistAsync(long id, string action, int? startIndex = null, string? expectedIdentity = null)
    {
        if (id <= 0 || action is not ("play" or "append")) return false;
        if (_playbackShuttingDown) return false;
        if (action == "play") _queueEdits.Invalidate();
        var edit = _queueEdits.Capture();
        var generation = _unifiedLibraryLoads.Generation;
        var intent = action == "play" ? _playbackStarts.Begin() : _playbackStarts.Current;
        if (action == "play") _playbackPreparations.CancelAll();
        using var preparation = action == "play" ? _playbackPreparations.Begin() : null;
        using var lookupCancellation = CancellationTokenSource.CreateLinkedTokenSource(preparation?.Token ?? CancellationToken.None, edit.Token);
        LibraryLoadResult<ContentRow> result;
        try { result = await LoadStoredPlaylistAsync(id, lookupCancellation.Token); }
        catch (Exception) when (!_queueEdits.IsCurrent(edit.Version) || (action == "play" && !_playbackStarts.IsCurrent(intent))) { return false; }
        if (_playbackShuttingDown || !_queueEdits.IsCurrent(edit.Version) || generation != _unifiedLibraryLoads.Generation ||
            (action == "play" && !_playbackStarts.IsCurrent(intent))) return false;
        var rows = result.Rows;
        var selectedRows = startIndex is { } index
            ? MobilePlaylistSelection.From(rows, index, expectedIdentity,
                r => r.OrynivoServer is { } server ? $"server:{Uri.EscapeDataString(server.Id)}:{r.Id}" : $"local:{r.Id}",
                r => r.Id.HasValue && !string.IsNullOrEmpty(r.FilePath)) : rows;
        var playable = selectedRows.Where(r => r.Id.HasValue && !string.IsNullOrEmpty(r.FilePath)).ToList();
        if (playable.Count == 0) return false;
        foreach (var row in playable)
            if (row.OrynivoServer is not null) _orynivoTracksByUrl[row.FilePath!] = row;
        if (action == "play")
        {
            if (_mcpBridge.ReplaceQueueFunc is null) return false;
            preparation!.Dispose();
            var replacing = _mcpBridge.ReplaceQueueFunc(playable.Select(r => r.FilePath!).ToArray());
            var replacementIntent = _playbackStarts.Current;
            try { return await replacing > 0; }
            catch (OperationCanceledException) { return false; }
            catch (Exception) when (!_playbackStarts.IsCurrent(replacementIntent)) { return false; }
        }
        else
        {
            foreach (var row in playable) _queue.Add(CreatePlaylistItem(row.FilePath!));
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            try { await RefreshActiveGaplessQueueAsync(); }
            catch (Exception) when (_playbackShuttingDown || !_queueEdits.IsCurrent(edit.Version)) { return false; }
            if (_playbackShuttingDown || !_queueEdits.IsCurrent(edit.Version)) return false;
        }
        return true;
    }

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
            var ids = TrackSearchIndex.SearchByCategory(query, 500).Tracks.Ids.ToList();
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
                    server, query, "tracks", null, null, null, null, "relevance", 500,
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
            .Select(track => (Track: track, Score: MobileSearchRanking.Score(query, track)))
            .Where(match => match.Score > 0).OrderByDescending(match => match.Score)
            .ThenBy(match => match.Track.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(match => match.Track.Id, StringComparer.Ordinal).Select(match => match.Track)
            .Take(limit)
            .ToList();
    }

    /// <summary>Resolves an opaque mobile identity without publishing obsolete remote metadata.</summary>
    /// <param name="identity">The opaque local or server identity.</param>
    /// <param name="isCurrent">Dispatcher-owned publication guard.</param>
    /// <param name="cancellationToken">The owning preparation token.</param>
    /// <returns>The current playable path, or null when unavailable or superseded.</returns>
    private async Task<string?> ResolveMobilePlaybackPathAsync(
        string identity, Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        string? path = null;
        if (identity.StartsWith("local:", StringComparison.Ordinal) &&
            long.TryParse(identity[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var localId))
        {
            path = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackById(localId)?.Path;
            }, cancellationToken);
        }
        else if (identity.StartsWith("server:", StringComparison.Ordinal))
        {
            var separator = identity.LastIndexOf(':');
            if (separator > 7 &&
                long.TryParse(identity[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var trackId))
            {
                var serverId = Uri.UnescapeDataString(identity[7..separator]);
                path = await ResolveRemoteMcpTrackForPlaybackAsync(
                    $"orynivo://{Uri.EscapeDataString(serverId)}/track/{trackId.ToString(CultureInfo.InvariantCulture)}", isCurrent, cancellationToken);
            }
        }

        return isCurrent() ? path : null;
    }

    /// <summary>Resolves an opaque mobile result and applies a bounded playback or queue action.</summary>
    /// <param name="identity">Opaque local or server track identity.</param>
    /// <param name="action">One of <c>play</c>, <c>next</c>, or <c>append</c>.</param>
    /// <returns><see langword="true"/> when the identity and action were accepted.</returns>
    private async Task<bool> QueueMobileRemoteTrackAsync(string identity, string action)
    {
        var requestedAction = action.Trim().ToLowerInvariant();
        if (requestedAction == "play")
        {
            if (_playbackShuttingDown) return false;
            var intent = _playbackStarts.Begin();
            _playbackPreparations.CancelAll();
            using var preparation = _playbackPreparations.Begin();
            string? resolved;
            try
            {
                resolved = await ResolveMobilePlaybackPathAsync(identity,
                    () => _playbackStarts.IsCurrent(intent), preparation.Token);
            }
            catch (Exception) when (!_playbackStarts.IsCurrent(intent)) { return false; }
            if (!_playbackStarts.IsCurrent(intent) || string.IsNullOrWhiteSpace(resolved))
                return false;
            preparation.Dispose();
            var player = await StartReadyPlaybackAsync(resolved);
            return player is not null && ReferenceEquals(_player, player);
        }
        if (_playbackShuttingDown || requestedAction is not ("next" or "append")) return false;
        var edit = _queueEdits.Capture();
        bool IsCurrent() => !_playbackShuttingDown && _queueEdits.IsCurrent(edit.Version);
        string? path;
        try { path = await ResolveMobilePlaybackPathAsync(identity, IsCurrent, edit.Token); }
        catch (Exception) when (!IsCurrent()) { return false; }
        if (!IsCurrent()) return false;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        switch (requestedAction)
        {
            case "next":
                if (_mcpBridge.PlayNextFunc is null)
                    return false;
                try { return await _mcpBridge.PlayNextFunc(path) is not null; }
                catch (Exception) when (!IsCurrent()) { return false; }
            case "append":
                if (_mcpBridge.AppendToQueueFunc is null)
                    return false;
                try { return await _mcpBridge.AppendToQueueFunc(path) is not null; }
                catch (Exception) when (!IsCurrent()) { return false; }
            default:
                return false;
        }
    }

    /// <summary>Moves, removes, or clears queue entries through the same state-maintenance path as the desktop.</summary>
    /// <param name="action">One of <c>remove</c>, <c>up</c>, <c>down</c>, or <c>clear</c>.</param>
    /// <param name="index">Target queue index for item actions.</param>
    /// <returns><see langword="true"/> after preparation for an accepted edit; destructive supersession or shutdown returns false.</returns>
    private async Task<bool> EditMobileRemoteQueueAsync(string action, int? index)
    {
        if (_playbackShuttingDown) return false;
        var edit = _queueEdits.Capture();
        bool IsCurrent() => !_playbackShuttingDown && _queueEdits.IsCurrent(edit.Version);
        action = action.Trim().ToLowerInvariant();
        if (action == "clear")
        {
            try { await ClearPlaybackQueueAsync(); }
            catch (OperationCanceledException) { return false; }
            return true;
        }
        if (index is not int target || target < 0 || target >= _queue.Count)
            return false;
        if (action == "up" && target > 0)
        {
            try { await MoveQueueItemAsync(target, target - 1); }
            catch (Exception) when (!IsCurrent()) { return false; }
            return IsCurrent();
        }
        if (action == "down" && target + 1 < _queue.Count)
        {
            try { await MoveQueueItemAsync(target, target + 1); }
            catch (Exception) when (!IsCurrent()) { return false; }
            return IsCurrent();
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
        try { await RefreshActiveGaplessQueueAsync(); }
        catch (Exception) when (!IsCurrent()) { return false; }
        return IsCurrent();
    }
}
