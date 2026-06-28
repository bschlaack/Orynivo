using Orynivo.Library;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>Identifies which navigation branch must be refreshed after a playlist operation.</summary>
internal enum PlaylistNavigationRefresh
{
    /// <summary>Refresh local playlist navigation.</summary>
    Local,

    /// <summary>Refresh remote Orynivo Server playlist navigation.</summary>
    OrynivoServer
}

/// <summary>Playlist metadata used by shared local and remote playlist menus.</summary>
/// <param name="Id">Provider-local playlist identifier.</param>
/// <param name="Name">Playlist display name.</param>
/// <param name="IsSmartPlaylist">Whether the playlist is generated from smart criteria.</param>
internal sealed record LibraryPlaylistInfo(long Id, string Name, bool IsSmartPlaylist);

/// <summary>Tracks selected for a playlist action in provider-specific and queue-ready forms.</summary>
/// <param name="QueuePaths">Playback paths used by the shared queue menu actions.</param>
/// <param name="LocalPaths">Local file paths used by the local playlist provider.</param>
/// <param name="RemoteTrackIds">Remote server track identifiers used by the Orynivo Server playlist provider.</param>
internal sealed record PlaylistSelection(
    IReadOnlyList<string> QueuePaths,
    IReadOnlyList<string> LocalPaths,
    IReadOnlyList<long> RemoteTrackIds);

/// <summary>Common playlist persistence surface for local and remote library providers.</summary>
internal interface ILibraryPlaylistProvider
{
    /// <summary>Gets which navigation branch should be refreshed after mutations.</summary>
    PlaylistNavigationRefresh NavigationRefresh { get; }

    /// <summary>Loads playlists that can receive manually added tracks.</summary>
    /// <returns>Regular playlists for this provider.</returns>
    IReadOnlyList<LibraryPlaylistInfo> GetWritablePlaylists();

    /// <summary>Adds the selected tracks to an existing regular playlist.</summary>
    /// <param name="playlistId">Provider-local playlist identifier.</param>
    /// <param name="selection">Selected tracks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the tracks were added.</returns>
    Task<bool> AddTracksAsync(long playlistId, PlaylistSelection selection, CancellationToken cancellationToken = default);

    /// <summary>Creates a regular playlist with the selected tracks.</summary>
    /// <param name="name">Playlist name.</param>
    /// <param name="selection">Selected tracks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created playlist, or <see langword="null"/> when creation failed.</returns>
    Task<LibraryPlaylistInfo?> CreatePlaylistAsync(string name, PlaylistSelection selection, CancellationToken cancellationToken = default);
}

/// <summary>Local SQLite-backed playlist provider.</summary>
internal sealed class LocalLibraryPlaylistProvider : ILibraryPlaylistProvider
{
    /// <inheritdoc/>
    public PlaylistNavigationRefresh NavigationRefresh => PlaylistNavigationRefresh.Local;

    /// <inheritdoc/>
    public IReadOnlyList<LibraryPlaylistInfo> GetWritablePlaylists()
    {
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetAllPlaylists()
                .Where(playlist => !playlist.IsSmartPlaylist)
                .Select(playlist => new LibraryPlaylistInfo(
                    playlist.Id,
                    playlist.Name,
                    playlist.IsSmartPlaylist))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc/>
    public Task<bool> AddTracksAsync(
        long playlistId,
        PlaylistSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.LocalPaths.Count == 0)
        {
            return Task.FromResult(false);
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            foreach (var path in selection.LocalPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                db.AddTrackToPlaylist(playlistId, path, db.GetTrackIdByPath(path));
            }

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task<LibraryPlaylistInfo?> CreatePlaylistAsync(
        string name,
        PlaylistSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.LocalPaths.Count == 0)
        {
            return Task.FromResult<LibraryPlaylistInfo?>(null);
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var playlistId = db.CreatePlaylist(name);
            foreach (var path in selection.LocalPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                db.AddTrackToPlaylist(playlistId, path, db.GetTrackIdByPath(path));
            }

            return Task.FromResult<LibraryPlaylistInfo?>(new LibraryPlaylistInfo(playlistId, name, IsSmartPlaylist: false));
        }
        catch
        {
            return Task.FromResult<LibraryPlaylistInfo?>(null);
        }
    }
}

/// <summary>Remote Orynivo Server-backed playlist provider.</summary>
internal sealed class OrynivoServerPlaylistProvider : ILibraryPlaylistProvider
{
    private readonly OrynivoServerSettings _server;
    private readonly OrynivoServerClient _client;
    private readonly Func<OrynivoServerSettings, IReadOnlyList<OrynivoPlaylistInfo>> _getCachedPlaylists;

    /// <summary>Initializes a new instance of the <see cref="OrynivoServerPlaylistProvider"/> class.</summary>
    /// <param name="server">Remote server connection settings.</param>
    /// <param name="client">Remote server HTTP client.</param>
    /// <param name="getCachedPlaylists">Delegate returning the currently loaded sidebar playlists for the server.</param>
    public OrynivoServerPlaylistProvider(
        OrynivoServerSettings server,
        OrynivoServerClient client,
        Func<OrynivoServerSettings, IReadOnlyList<OrynivoPlaylistInfo>> getCachedPlaylists)
    {
        _server = server;
        _client = client;
        _getCachedPlaylists = getCachedPlaylists;
    }

    /// <inheritdoc/>
    public PlaylistNavigationRefresh NavigationRefresh => PlaylistNavigationRefresh.OrynivoServer;

    /// <inheritdoc/>
    public IReadOnlyList<LibraryPlaylistInfo> GetWritablePlaylists() =>
        _getCachedPlaylists(_server)
            .Where(playlist => !playlist.IsSmartPlaylist)
            .Select(playlist => new LibraryPlaylistInfo(
                playlist.Id,
                playlist.Name,
                playlist.IsSmartPlaylist))
            .ToList();

    /// <inheritdoc/>
    public async Task<bool> AddTracksAsync(
        long playlistId,
        PlaylistSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.RemoteTrackIds.Count == 0)
        {
            return false;
        }

        return await _client.AddTracksToPlaylistAsync(
            _server,
            playlistId,
            selection.RemoteTrackIds,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<LibraryPlaylistInfo?> CreatePlaylistAsync(
        string name,
        PlaylistSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (selection.RemoteTrackIds.Count == 0)
        {
            return null;
        }

        var playlist = await _client.CreatePlaylistAsync(
            _server,
            name,
            selection.RemoteTrackIds,
            cancellationToken);
        return playlist is null
            ? null
            : new LibraryPlaylistInfo(playlist.Id, playlist.Name, playlist.IsSmartPlaylist);
    }
}
