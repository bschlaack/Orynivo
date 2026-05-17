namespace Player.Library;

public sealed class PlaylistRecord
{
    public long    Id          { get; set; }
    public string  Name        { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int     TrackCount  { get; set; }   // denormalisiert, aus JOIN befüllt
    public long    CreatedAt   { get; set; }   // Unix-Timestamp (Sekunden)
    public long    ModifiedAt  { get; set; }   // Unix-Timestamp (Sekunden)
}
