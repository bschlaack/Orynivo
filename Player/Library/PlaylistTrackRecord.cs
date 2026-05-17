namespace Player.Library;

public sealed class PlaylistTrackRecord
{
    public long   Id         { get; set; }
    public long   PlaylistId { get; set; }
    public long?  TrackId    { get; set; }   // null wenn Track nicht (mehr) in der Bibliothek
    public string Path       { get; set; } = string.Empty;
    public int    Position   { get; set; }   // 1-basiert, lückenlos
    public long   AddedAt    { get; set; }   // Unix-Timestamp (Sekunden)
}
