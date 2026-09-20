using Orynivo.Library;

namespace Orynivo.Server.Services;

/// <summary>
/// Resolves smart-playlist criteria for the server's playlist endpoints.
/// </summary>
public static class SmartPlaylistResolver
{
    /// <summary>
    /// Resolves the criteria against the supplied candidates, supplying the cached
    /// similarity feature vectors when the criteria references a track. Without
    /// them the shared resolver would return an empty list, because a similarity
    /// reference cannot be honoured without vectors.
    /// </summary>
    /// <param name="database">Open library database used to load the vectors.</param>
    /// <param name="criteria">Criteria to resolve.</param>
    /// <param name="candidates">Provider-local candidates with favourites already applied.</param>
    /// <returns>The resolved track rows.</returns>
    public static List<SmartPlaylistTrackInfo> Resolve(
        AudioDatabase database,
        SmartPlaylistCriteria criteria,
        List<SmartPlaylistTrackInfo> candidates)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(candidates);
        return criteria.Resolve(candidates, BuildSimilarityFeatures(database, criteria));
    }

    /// <summary>
    /// Builds the similarity vectors for a criteria with a reference track, or
    /// <see langword="null"/> when no reference is configured or the profiles
    /// cannot be read.
    /// </summary>
    /// <param name="database">Open library database.</param>
    /// <param name="criteria">Criteria that may carry a similarity reference.</param>
    /// <returns>The provider-local feature vectors, or <see langword="null"/>.</returns>
    private static IReadOnlyList<SimilarityFeatureVector>? BuildSimilarityFeatures(
        AudioDatabase database,
        SmartPlaylistCriteria criteria)
    {
        if (string.IsNullOrWhiteSpace(criteria.SimilaritySourceKey) || criteria.SimilarityTrackId is null)
            return null;

        try
        {
            return database.GetSimilarityTrackProfiles()
                .Select(SimilarityFeatureService.Create)
                .ToList();
        }
        catch
        {
            // An unreadable feature set must behave like a missing reference.
            return null;
        }
    }
}
