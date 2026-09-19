using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// Pure helpers for the smart-playlist editor. They keep the reference
/// similarity criterion — which the editor cannot recreate from its own input
/// fields — intact across a save unless the user explicitly removes it.
/// </summary>
internal static class SmartPlaylistCriteriaEditing
{
    /// <summary>
    /// Resolves the similarity reference to persist after an editor save.
    /// </summary>
    /// <param name="initial">Criteria loaded into the editor.</param>
    /// <param name="referenceCleared">Whether the user explicitly removed the reference.</param>
    /// <param name="minimumScore">Edited inclusive minimum score, or <see langword="null"/>.</param>
    /// <returns>
    /// The reference source key, provider-local track id, and minimum score. All
    /// three are <see langword="null"/> when the user removed the reference or the
    /// loaded criteria had none.
    /// </returns>
    public static (string? SourceKey, long? TrackId, double? MinimumScore) ResolveSimilarityReference(
        SmartPlaylistCriteria initial,
        bool referenceCleared,
        double? minimumScore)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (referenceCleared ||
            string.IsNullOrWhiteSpace(initial.SimilaritySourceKey) ||
            initial.SimilarityTrackId is null)
        {
            return (null, null, null);
        }

        return (initial.SimilaritySourceKey, initial.SimilarityTrackId, minimumScore);
    }
}
