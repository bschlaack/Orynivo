namespace Orynivo.Library;

/// <summary>
/// Database model for a single track entry in a playlist (<c>playlist_tracks</c> table).
/// </summary>
public sealed class PlaylistTrackRecord
{
    /// <summary>Primary key of the <c>playlist_tracks</c> table.</summary>
    public long   Id         { get; set; }

    /// <summary>Foreign key referencing the parent playlist.</summary>
    public long   PlaylistId { get; set; }

    /// <summary>
    /// Optional foreign key referencing the library track.
    /// <see langword="null"/> when the track has been removed from the library; the path is still retained.
    /// </summary>
    public long?  TrackId    { get; set; }

    /// <summary>Absolute file path; always present even when <see cref="TrackId"/> is <see langword="null"/>.</summary>
    public string Path       { get; set; } = string.Empty;

    /// <summary>One-based, contiguous position within the playlist.</summary>
    public int    Position   { get; set; }

    /// <summary>Timestamp when the entry was added, as a Unix timestamp in seconds (UTC).</summary>
    public long   AddedAt    { get; set; }
}
