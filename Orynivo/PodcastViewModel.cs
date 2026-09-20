using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// One podcast search result or pinned podcast row. It lives outside the main window so
/// XAML can name it in an <c>x:DataType</c> directive, which compiled bindings require
/// (a nested type cannot be referenced from XAML).
/// </summary>
internal sealed class PodcastViewModel
{
    /// <summary>Gets the Apple Podcasts collection identifier.</summary>
    public long CollectionId { get; init; }

    /// <summary>Gets the podcast name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the author, when known.</summary>
    public string? Author { get; init; }

    /// <summary>Gets the RSS feed URL.</summary>
    public required string FeedUrl { get; init; }

    /// <summary>Gets the artwork URL, when known.</summary>
    public string? ArtworkUrl { get; init; }

    /// <summary>Gets the primary genre, when known.</summary>
    public string? Genre { get; init; }

    /// <summary>Gets the complete genre list.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Gets the Apple genre identifiers used for catalog filtering.</summary>
    public IReadOnlyList<string> GenreIds { get; init; } = [];

    /// <summary>Gets or sets the language detected from the feed.</summary>
    public string? Language { get; set; }

    /// <summary>Gets the localized language label.</summary>
    public string LanguageDisplay => MainWindow.FormatPodcastLanguage(Language);

    /// <summary>Converts the row back into a podcast search result.</summary>
    /// <returns>The search result.</returns>
    public PodcastSearchResult ToSearchResult() =>
        new(CollectionId, Name, Author, FeedUrl, ArtworkUrl, Genre, Genres, GenreIds, Language);

    /// <summary>Converts the row into a persisted podcast record.</summary>
    /// <param name="id">Database identifier, or zero for a new record.</param>
    /// <returns>The persisted podcast record.</returns>
    public PodcastRecord ToRecord(long id = 0) =>
        new(id, CollectionId, Name, Author, FeedUrl, ArtworkUrl, Genre);
}
