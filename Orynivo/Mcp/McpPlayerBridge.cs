using Avalonia.Threading;
using Orynivo.Streaming;

namespace Orynivo.Mcp;

/// <summary>
/// Immutable snapshot of the player's current playback state for MCP tools.
/// </summary>
/// <param name="Status">Human-readable status string: <c>playing</c>, <c>paused</c>, or <c>stopped</c>.</param>
/// <param name="Title">Display title of the current track, or <see langword="null"/> when stopped.</param>
/// <param name="Artist">Artist of the current track, or <see langword="null"/>.</param>
/// <param name="Album">Album of the current track, or <see langword="null"/>.</param>
/// <param name="FilePath">Absolute path of the current file, or <see langword="null"/> when stopped.</param>
/// <param name="PositionSeconds">Current playback position in seconds.</param>
/// <param name="DurationSeconds">Total track duration in seconds, or zero if unknown.</param>
/// <param name="Volume">Current volume level between 0.0 and 1.0.</param>
/// <param name="QueueIndex">Zero-based index of the current item in the queue, or -1 when empty.</param>
/// <param name="QueueCount">Total number of items in the playback queue.</param>
public sealed record PlayerState(
    string Status,
    string? Title,
    string? Artist,
    string? Album,
    string? FilePath,
    double PositionSeconds,
    double DurationSeconds,
    double Volume,
    int QueueIndex,
    int QueueCount);

/// <summary>
/// One entry in the current playback queue as seen by MCP tools.
/// </summary>
/// <param name="Index">Zero-based position in the queue.</param>
/// <param name="IsCurrent">Whether this entry is the item currently playing or queued to play.</param>
/// <param name="Path">Absolute file path or stream URL.</param>
/// <param name="FileName">File name without directory path.</param>
public sealed record QueueEntry(int Index, bool IsCurrent, string Path, string FileName);

/// <summary>Bounded artwork payload exposed to trusted local remote-control transports.</summary>
/// <param name="Data">Encoded image bytes.</param>
/// <param name="MimeType">Image media type.</param>
/// <param name="CacheKey">Non-sensitive cache identity for conditional requests.</param>
public sealed record RemoteArtwork(byte[] Data, string MimeType, string CacheKey);

/// <summary>Snapshot of the configured equalizer profiles exposed to remote-control tools.</summary>
/// <param name="Profiles">Available profile names.</param>
/// <param name="SelectedProfile">Selected profile name, or <see langword="null"/>.</param>
/// <param name="Enabled">Whether equalization is enabled.</param>
public sealed record EqualizerProfileState(
    IReadOnlyList<string> Profiles,
    string? SelectedProfile,
    bool Enabled);

/// <summary>
/// Thread-safe bridge between the MCP tool layer and the Avalonia main-window player.
/// All delegate members are populated by <see cref="MainWindow"/> before the MCP server starts.
/// Tool methods invoke these delegates on the Avalonia UI thread via
/// <see cref="OnUiAsync{T}"/> and <see cref="OnUiAsync(Action, CancellationToken)"/>.
/// </summary>
public sealed class McpPlayerBridge
{
    /// <summary>Gets or sets a function that returns the current player state.</summary>
    public Func<PlayerState>? GetStateFunc { get; set; }

    /// <summary>Gets or sets a function that returns the current queue entries.</summary>
    public Func<IReadOnlyList<QueueEntry>>? GetQueueFunc { get; set; }

    /// <summary>Gets or sets an asynchronous function returning bounded current-track artwork.</summary>
    public Func<Task<RemoteArtwork?>>? GetCurrentArtworkFunc { get; set; }

    /// <summary>Gets or sets a function that starts a queue entry by zero-based index.</summary>
    public Func<int, Task<bool>>? PlayQueueIndexFunc { get; set; }

    /// <summary>Gets or sets a cross-library track search for the mobile remote.</summary>
    public Func<string, int, CancellationToken, Task<IReadOnlyList<global::Orynivo.Remote.MobileRemoteTrack>>>? SearchMobileTracksFunc { get; set; }

    /// <summary>Gets or sets a cross-library artist browser for the mobile remote.</summary>
    public Func<string?, int, CancellationToken, Task<IReadOnlyList<global::Orynivo.Remote.MobileRemoteArtist>>>? BrowseMobileArtistsFunc { get; set; }

    /// <summary>Gets or sets a provider-bound album browser for one mobile artist identity.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<global::Orynivo.Remote.MobileRemoteAlbum>>>? BrowseMobileAlbumsFunc { get; set; }

    /// <summary>Gets or sets a provider-bound track browser for one mobile album identity.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<global::Orynivo.Remote.MobileRemoteTrack>>>? BrowseMobileAlbumTracksFunc { get; set; }

    /// <summary>Gets or sets a function that resolves a safe mobile track identity and applies a queue action.</summary>
    public Func<string, string, Task<bool>>? QueueMobileTrackFunc { get; set; }

    /// <summary>Gets or sets a function returning the current track favorite state, or no value when unavailable.</summary>
    public Func<bool?>? GetCurrentFavoriteFunc { get; set; }

    /// <summary>Gets or sets a function applying a validated edit to the current queue.</summary>
    public Func<string, int?, Task<bool>>? EditMobileQueueFunc { get; set; }

    /// <summary>Gets or sets a function that starts playback of the given absolute file path.</summary>
    public Func<string, Task>? PlayFileFunc { get; set; }

    /// <summary>Gets or sets a function that toggles between pause and resume.</summary>
    public Func<Task>? TogglePauseFunc { get; set; }

    /// <summary>Gets or sets a function that skips to the next queue entry.</summary>
    public Func<Task>? SkipNextFunc { get; set; }

    /// <summary>Gets or sets a function that skips to the previous queue entry.</summary>
    public Func<Task>? SkipPreviousFunc { get; set; }

    /// <summary>Gets or sets an action that stops playback immediately.</summary>
    public Action? StopFunc { get; set; }

    /// <summary>Gets or sets a function that seeks to the given position in seconds.</summary>
    public Func<double, Task>? SeekFunc { get; set; }

    /// <summary>Gets or sets an action that sets the player volume to a value between 0.0 and 1.0.</summary>
    public Action<double>? SetVolumeFunc { get; set; }

    /// <summary>Gets or sets an action that appends the given path to the end of the playback queue.</summary>
    public Func<string, Task>? AppendToQueueFunc { get; set; }

    /// <summary>Gets or sets an action that inserts the given path as the next queue entry after the current one.</summary>
    public Func<string, Task>? PlayNextFunc { get; set; }

    /// <summary>Gets or sets an action that clears all items from the playback queue without stopping the current track.</summary>
    public Action? ClearQueueFunc { get; set; }

    /// <summary>Gets or sets a function that replaces the entire playback queue with the given paths and starts playing the first one.</summary>
    public Func<IReadOnlyList<string>, Task>? ReplaceQueueFunc { get; set; }

    /// <summary>Gets or sets an action that triggers a sidebar playlist refresh after a playlist is created via MCP.</summary>
    public Action? RefreshPlaylistsFunc { get; set; }

    /// <summary>Gets or sets a function that returns the configured remote Orynivo Server connections.</summary>
    public Func<IReadOnlyList<OrynivoServerSettings>>? GetOrynivoServersFunc { get; set; }

    /// <summary>
    /// Gets or sets a function that turns a tool-supplied path into a playable path.
    /// A remote Orynivo Server reference (<c>orynivo://serverId/track/trackId</c>) is
    /// resolved to the real authenticated stream URL and its track metadata is
    /// registered so playback shows full transport/history/lyrics info; any other path
    /// (local file or already-real URL) is returned unchanged. Returns <see langword="null"/>
    /// when a remote reference cannot be resolved.
    /// </summary>
    public Func<string, Task<string?>>? ResolveRemoteTrackFunc { get; set; }

    /// <summary>Gets or sets a function that applies the requested favorite state to the current track.</summary>
    public Func<bool, bool>? SetCurrentFavoriteFunc { get; set; }

    /// <summary>Gets or sets a function that controls Infinite Mix and returns its resulting state.</summary>
    public Func<string, Task<string>>? ControlInfiniteMixFunc { get; set; }

    /// <summary>Gets or sets a function returning all output profile names.</summary>
    public Func<IReadOnlyList<string>>? GetOutputProfilesFunc { get; set; }

    /// <summary>Gets or sets a function selecting an output profile by name.</summary>
    public Func<string, Task<bool>>? SelectOutputProfileFunc { get; set; }

    /// <summary>Gets or sets a function returning the equalizer profile state.</summary>
    public Func<EqualizerProfileState>? GetEqualizerProfilesFunc { get; set; }

    /// <summary>Gets or sets a function selecting an equalizer profile and enable state.</summary>
    public Func<string?, bool, Task<bool>>? ConfigureEqualizerFunc { get; set; }

    /// <summary>Gets or sets a function returning cached lyrics for the current track.</summary>
    public Func<Task<string?>>? GetCurrentLyricsFunc { get; set; }

    /// <summary>Gets or sets a function returning configured Orynivo Server display names.</summary>
    public Func<IReadOnlyList<string>>? GetOrynivoServerNamesFunc { get; set; }

    /// <summary>Gets or sets a function that starts a library scan on one named Orynivo Server.</summary>
    public Func<string, Task<bool>>? TriggerOrynivoServerScanFunc { get; set; }

    /// <summary>Gets or sets the set of tool names that are individually disabled; <see langword="null"/> means all tools are enabled.</summary>
    public HashSet<string>? DisabledTools { get; set; }

    /// <summary>Gets or sets the shared web-browsing service used by the web tools (search and page fetch), or <see langword="null"/> when unavailable.</summary>
    public Orynivo.Web.WebBrowsingService? WebBrowsing { get; set; }

    /// <summary>Returns <see langword="true"/> when the named tool is not in <see cref="DisabledTools"/>.</summary>
    /// <param name="toolName">The MCP tool name to check.</param>
    /// <returns><see langword="true"/> when the tool is enabled.</returns>
    public bool IsToolEnabled(string toolName) =>
        DisabledTools is null || !DisabledTools.Contains(toolName);

    /// <summary>
    /// Dispatches <paramref name="func"/> to the Avalonia UI thread and awaits its result.
    /// </summary>
    /// <typeparam name="T">The return type of <paramref name="func"/>.</typeparam>
    /// <param name="func">The function to invoke on the UI thread.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    public async Task<T> OnUiAsync<T>(Func<T> func, CancellationToken ct = default) =>
        await Dispatcher.UIThread.InvokeAsync(func, DispatcherPriority.Normal);

    /// <summary>Dispatches an asynchronous function to the Avalonia UI thread and unwraps its result.</summary>
    /// <typeparam name="T">The function result type.</typeparam>
    /// <param name="func">Asynchronous function to start on the UI thread.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The value produced by the asynchronous function.</returns>
    public async Task<T> OnUiAsync<T>(Func<Task<T>> func, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(func, DispatcherPriority.Normal);
    }

    /// <summary>
    /// Dispatches <paramref name="action"/> to the Avalonia UI thread and awaits completion.
    /// </summary>
    /// <param name="action">The action to invoke on the UI thread.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A task that completes when the action has finished.</returns>
    public async Task OnUiAsync(Action action, CancellationToken ct = default) =>
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal);
}
