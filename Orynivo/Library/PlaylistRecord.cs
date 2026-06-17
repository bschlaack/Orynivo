namespace Orynivo.Library;

/// <summary>
/// Database model for a regular or smart playlist.
/// </summary>
public sealed class PlaylistRecord
{
    /// <summary>Primary key of the <c>playlists</c> table.</summary>
    public long    Id          { get; set; }

    /// <summary>Display name of the playlist.</summary>
    public string  Name        { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Denormalised track count populated via JOIN.</summary>
    public int     TrackCount  { get; set; }

    /// <summary>Creation timestamp as a Unix timestamp in seconds (UTC).</summary>
    public long    CreatedAt       { get; set; }

    /// <summary>Last-modified timestamp as a Unix timestamp in seconds (UTC).</summary>
    public long    ModifiedAt      { get; set; }

    /// <summary><see langword="true"/> when this is a smart playlist with stored filter criteria.</summary>
    public bool    IsSmartPlaylist { get; set; }

    /// <summary>
    /// JSON-serialised <see cref="SmartPlaylistCriteria"/>; only set when <see cref="IsSmartPlaylist"/> is <see langword="true"/>.
    /// </summary>
    public string? FilterCriteria  { get; set; }
}
