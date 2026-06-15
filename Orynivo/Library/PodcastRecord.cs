namespace Orynivo.Library;

public sealed record PodcastRecord(
    long Id,
    long CollectionId,
    string Name,
    string? Author,
    string FeedUrl,
    string? ArtworkUrl,
    string? Genre);

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

public sealed record PodcastCategory(string Id, string Name);

public sealed record PodcastEpisode(
    string EpisodeKey,
    string Title,
    string AudioUrl,
    string? Description,
    DateTimeOffset? PublishedAt,
    TimeSpan? FeedDuration);

public sealed record PodcastFeed(
    IReadOnlyList<PodcastEpisode> Episodes,
    string? Description,
    string? Language,
    IReadOnlyList<string> Categories,
    string? Website,
    string? Copyright);

public sealed record PodcastEpisodeProgress(
    string EpisodeKey,
    double PositionSeconds,
    double? DurationSeconds,
    bool IsCompleted);
