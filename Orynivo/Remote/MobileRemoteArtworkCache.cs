using Orynivo.Mcp;

namespace Orynivo.Remote;

/// <summary>Coalesces current artwork on the UI dispatcher and rejects superseded completions.</summary>
internal sealed class MobileRemoteArtworkCache
{
    private string? _source;
    private object? _image;
    private Task<RemoteArtwork?>? _pending;

    /// <summary>Gets the opaque current source and image revision.</summary>
    internal string Key { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>Invalidates thumbnails when playback or the published image changes.</summary>
    /// <param name="source">In-memory playback identity; never returned or persisted.</param>
    /// <param name="image">Current immutable desktop image identity.</param>
    internal void Update(string source, object? image)
    {
        if (_source == source && ReferenceEquals(_image, image)) return;
        _source = source;
        _image = image;
        Key = Guid.NewGuid().ToString("N");
        _pending = null;
    }

    /// <summary>Shares one thumbnail load per revision without publishing an earlier source.</summary>
    /// <param name="load">Loader capturing the current source.</param>
    /// <returns>The current thumbnail, or null for obsolete work.</returns>
    internal async Task<RemoteArtwork?> GetAsync(Func<Task<RemoteArtwork?>> load)
    {
        var key = Key;
        var result = await (_pending ??= load());
        return key == Key ? result : null;
    }
}
