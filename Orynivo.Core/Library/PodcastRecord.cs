namespace Orynivo.Library;

/// <summary>A podcast pinned by the user, stored in the <c>podcasts</c> database table.</summary>
/// <param name="Id">Local database row ID.</param>
/// <param name="CollectionId">Apple Podcasts collection ID.</param>
/// <param name="Name">Display name of the podcast.</param>
/// <param name="Author">Podcast author or publisher.</param>
/// <param name="FeedUrl">RSS/Atom feed URL.</param>
/// <param name="ArtworkUrl">URL to the podcast cover artwork.</param>
/// <param name="Genre">Primary genre label.</param>
public sealed record PodcastRecord(
    long Id,
    long CollectionId,
    string Name,
    string? Author,
    string FeedUrl,
    string? ArtworkUrl,
    string? Genre);

/// <summary>A podcast returned by an Apple Podcasts catalogue search.</summary>
/// <param name="CollectionId">Apple Podcasts collection ID.</param>
/// <param name="Name">Display name of the podcast.</param>
/// <param name="Author">Podcast author or publisher.</param>
/// <param name="FeedUrl">RSS/Atom feed URL.</param>
/// <param name="ArtworkUrl">URL to the podcast cover artwork.</param>
/// <param name="Genre">Primary genre label.</param>
/// <param name="Genres">All genre labels associated with this podcast.</param>
/// <param name="GenreIds">Apple genre IDs corresponding to <paramref name="Genres"/>.</param>
/// <param name="Language">BCP 47 language tag detected from the RSS feed.</param>
public sealed record PodcastSearchResult(
    long CollectionId,
    string Name,
    string? Author,
    string FeedUrl,
    string? ArtworkUrl,
    string? Genre,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> GenreIds,
    string? Language);

/// <summary>A single Apple Podcasts genre category used as a search filter.</summary>
/// <param name="Id">Apple genre ID.</param>
/// <param name="Name">Human-readable genre name.</param>
public sealed record PodcastCategory(string Id, string Name);

/// <summary>A single playable episode parsed from a podcast RSS/Atom feed.</summary>
/// <param name="EpisodeKey">RSS GUID if present, otherwise the audio URL.</param>
/// <param name="Title">Episode title.</param>
/// <param name="AudioUrl">Direct URL to the episode audio file.</param>
/// <param name="Description">Optional episode description from the RSS feed.</param>
/// <param name="PublishedAt">Optional publication date.</param>
/// <param name="FeedDuration">Optional duration as declared in the feed.</param>
public sealed record PodcastEpisode(
    string EpisodeKey,
    string Title,
    string AudioUrl,
    string? Description,
    DateTimeOffset? PublishedAt,
    TimeSpan? FeedDuration);

/// <summary>Parsed content of a podcast RSS/Atom feed.</summary>
/// <param name="Episodes">All episodes, sorted newest first.</param>
/// <param name="Description">Feed-level description or subtitle.</param>
/// <param name="Language">BCP 47 language tag from the feed.</param>
/// <param name="Categories">Genre/category labels declared by the feed.</param>
/// <param name="Website">Podcast website URL.</param>
/// <param name="Copyright">Copyright statement from the feed.</param>
public sealed record PodcastFeed(
    IReadOnlyList<PodcastEpisode> Episodes,
    string? Description,
    string? Language,
    IReadOnlyList<string> Categories,
    string? Website,
    string? Copyright);

/// <summary>Persisted playback progress for a single podcast episode.</summary>
/// <param name="EpisodeKey">Episode key matching <see cref="PodcastEpisode.EpisodeKey"/>.</param>
/// <param name="PositionSeconds">Last known playback position in seconds.</param>
/// <param name="DurationSeconds">Known duration in seconds, or <see langword="null"/> if not yet determined.</param>
/// <param name="IsCompleted"><see langword="true"/> when the episode reached 95 % or ended normally.</param>
public sealed record PodcastEpisodeProgress(
    string EpisodeKey,
    double PositionSeconds,
    double? DurationSeconds,
    bool IsCompleted);
