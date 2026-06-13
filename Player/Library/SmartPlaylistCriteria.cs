namespace Player.Library;

public sealed record SmartPlaylistCriteria(
    bool FavoritesOnly,
    List<string> Genres,
    List<string> Formats,
    List<int>    Bitrates);
