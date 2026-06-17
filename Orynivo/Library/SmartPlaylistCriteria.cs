namespace Orynivo.Library;

/// <summary>
/// Serialisable filter criteria for a smart playlist.
/// Persisted as JSON in <c>playlists.filter_criteria</c>.
/// </summary>
/// <param name="FavoritesOnly">Include only tracks marked as favourites.</param>
/// <param name="Genres">Allowed genre names; empty means no restriction.</param>
/// <param name="Formats">Allowed file format identifiers, e.g. <c>flac</c>, <c>mp3</c>; empty means no restriction.</param>
/// <param name="Bitrates">Allowed bitrate values in kbps; empty means no restriction.</param>
public sealed record SmartPlaylistCriteria(
    bool FavoritesOnly,
    List<string> Genres,
    List<string> Formats,
    List<int>    Bitrates);
