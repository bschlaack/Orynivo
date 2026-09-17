using System.Globalization;
using System.Text;
using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// Pure recommendation scoring for genre-cloud candidates: listening affinity,
/// favorite weighting, and a deterministic tie-break variation.
/// </summary>
internal static class GenreRecommendationScore
{
    /// <summary>Computes the combined recommendation score for one candidate.</summary>
    /// <param name="serverId">Owning server identifier, or <see langword="null"/> for the local library.</param>
    /// <param name="trackId">Provider-local track identifier.</param>
    /// <param name="candidateGenreKey">Candidate taxonomy key.</param>
    /// <param name="isFavorite">Whether the candidate track is a favorite.</param>
    /// <param name="listeningWeights">Listening seconds keyed by taxonomy genre key.</param>
    /// <returns>The recommendation score; higher values rank first.</returns>
    internal static double Compute(
        string? serverId,
        long trackId,
        string candidateGenreKey,
        bool isFavorite,
        IReadOnlyDictionary<string, double> listeningWeights)
    {
        var affinity = SumRelatedAffinity(candidateGenreKey, listeningWeights);
        return Math.Log10(1 + affinity) * 100
            + (isFavorite ? 25 : 0)
            + StableVariation(serverId, trackId);
    }

    /// <summary>
    /// Sums the listening time of every genre related to the candidate in either
    /// direction, so directly tagged parents and subgenres both contribute.
    /// </summary>
    /// <param name="candidateGenreKey">Candidate taxonomy key.</param>
    /// <param name="listeningWeights">Listening seconds keyed by taxonomy genre key.</param>
    /// <returns>The summed related listening time in seconds.</returns>
    internal static double SumRelatedAffinity(
        string candidateGenreKey,
        IReadOnlyDictionary<string, double> listeningWeights)
    {
        var total = 0d;
        foreach (var pair in listeningWeights)
        {
            if (GenreCloudService.IsDescendantOrSelf(candidateGenreKey, pair.Key) ||
                GenreCloudService.IsDescendantOrSelf(pair.Key, candidateGenreKey))
            {
                total += pair.Value;
            }
        }

        return total;
    }

    /// <summary>
    /// Returns a stable tie-break variation in the range [0, 1). It uses an FNV-1a
    /// hash over the server identifier and track identifier so the ordering is
    /// identical across processes and platforms.
    /// </summary>
    /// <param name="serverId">Owning server identifier, or <see langword="null"/> for the local library.</param>
    /// <param name="trackId">Provider-local track identifier.</param>
    /// <returns>A deterministic value in the range [0, 1).</returns>
    internal static double StableVariation(string? serverId, long trackId)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        var text = $"{serverId ?? string.Empty}\u0000{trackId.ToString(CultureInfo.InvariantCulture)}";
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            hash ^= value;
            hash *= prime;
        }

        return hash % 1000 / 1000d;
    }
}
