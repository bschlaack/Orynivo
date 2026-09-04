namespace Orynivo.Library;

/// <summary>Raw compact metadata used to construct one similarity feature vector.</summary>
/// <param name="TrackId">Provider-local track identifier.</param>
/// <param name="SourceKey">Credential-free provider identity.</param>
/// <param name="AlbumId">Provider-local album identifier.</param>
/// <param name="Artist">Primary artist name.</param>
/// <param name="Genre">Effective embedded and supplemental genre text.</param>
/// <param name="Bpm">Tagged tempo in beats per minute.</param>
/// <param name="Mood">Tagged mood text.</param>
/// <param name="IsFavorite">Whether the track is a favourite.</param>
/// <param name="UserRating">Personal zero-to-five rating.</param>
/// <param name="MusicBrainzRating">Community zero-to-five rating.</param>
/// <param name="MusicBrainzRatingVotes">Number of community votes.</param>
/// <param name="PlayCount">Number of recorded playback sessions.</param>
/// <param name="LastPlayedAt">Most recent playback timestamp.</param>
/// <param name="Energy">Optional cached audio energy from zero through one.</param>
/// <param name="Brightness">Optional cached high-frequency/transient proxy from zero through one.</param>
/// <param name="Dynamics">Optional cached dynamic-range proxy from zero through one.</param>
public sealed record SimilarityTrackProfile(
    long TrackId,
    string SourceKey,
    long? AlbumId,
    string? Artist,
    string? Genre,
    int? Bpm,
    string? Mood,
    bool IsFavorite,
    int UserRating,
    double? MusicBrainzRating,
    int? MusicBrainzRatingVotes,
    int PlayCount,
    long? LastPlayedAt,
    double? Energy = null,
    double? Brightness = null,
    double? Dynamics = null);

/// <summary>Versioned provider-neutral feature vector used by similarity and mood ranking.</summary>
/// <param name="Version">Feature schema version.</param>
/// <param name="SourceKey">Credential-free provider identity.</param>
/// <param name="TrackId">Provider-local track identifier.</param>
/// <param name="AlbumId">Provider-local album identifier.</param>
/// <param name="ArtistKey">Conservative normalized artist identity.</param>
/// <param name="GenreKeys">Resolved stable Genre Cloud taxonomy keys.</param>
/// <param name="MoodKeys">Normalized explicit mood tags.</param>
/// <param name="Tempo">Tempo normalized to zero through one, or <see langword="null"/>.</param>
/// <param name="PersonalAffinity">Personal favourite/rating signal from zero through one.</param>
/// <param name="CommunityAffinity">Vote-confidence-adjusted community signal from zero through one.</param>
/// <param name="Familiarity">Playback-count familiarity from zero through one.</param>
/// <param name="LastPlayedAt">Most recent playback timestamp.</param>
/// <param name="Energy">Optional cached audio energy from zero through one.</param>
/// <param name="Brightness">Optional cached high-frequency/transient proxy from zero through one.</param>
/// <param name="Dynamics">Optional cached dynamic-range proxy from zero through one.</param>
public sealed record SimilarityFeatureVector(
    int Version,
    string SourceKey,
    long TrackId,
    long? AlbumId,
    string ArtistKey,
    IReadOnlyList<string> GenreKeys,
    IReadOnlyList<string> MoodKeys,
    double? Tempo,
    double PersonalAffinity,
    double CommunityAffinity,
    double Familiarity,
    long? LastPlayedAt,
    double? Energy = null,
    double? Brightness = null,
    double? Dynamics = null);

/// <summary>One ranked similarity candidate.</summary>
/// <param name="Vector">Candidate vector.</param>
/// <param name="Score">Normalized similarity score from zero through one.</param>
public sealed record SimilarityFeatureMatch(SimilarityFeatureVector Vector, double Score);

/// <summary>Coarse playback moods supported by metadata-only similarity ranking.</summary>
public enum SimilarityMood
{
    /// <summary>Favours slower and explicitly calm tracks.</summary>
    Calm,
    /// <summary>Favours mid-tempo tracks without a strong mood bias.</summary>
    Balanced,
    /// <summary>Favours faster and explicitly energetic tracks.</summary>
    Energetic
}

/// <summary>Creates deterministic current-version similarity vectors from metadata and optional cached audio descriptors.</summary>
public static class SimilarityFeatureService
{
    /// <summary>Current serialized feature-vector schema version.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Creates one normalized vector without performing audio analysis.</summary>
    /// <param name="profile">Compact source metadata and listening signals.</param>
    /// <returns>A deterministic versioned feature vector.</returns>
    public static SimilarityFeatureVector Create(SimilarityTrackProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var genreKeys = GenreCloudService.ResolveGenreKeys(profile.Genre);
        var moodKeys = SplitKeys(profile.Mood);
        var tempo = profile.Bpm is > 0
            ? Math.Clamp((profile.Bpm.Value - 40d) / 180d, 0d, 1d)
            : (double?)null;
        var ratingAffinity = Math.Clamp(profile.UserRating, 0, 5) / 5d;
        var personalAffinity = profile.IsFavorite
            ? Math.Max(0.8d, ratingAffinity)
            : ratingAffinity;
        var communityRating = Math.Clamp(profile.MusicBrainzRating ?? 0d, 0d, 5d) / 5d;
        var voteConfidence = Math.Clamp(
            Math.Log10(Math.Max(0, profile.MusicBrainzRatingVotes ?? 0) + 1d) / 3d,
            0d,
            1d);

        return new SimilarityFeatureVector(
            CurrentVersion,
            profile.SourceKey,
            profile.TrackId,
            profile.AlbumId,
            ArtistNameNormalizer.CreateComparisonKey(profile.Artist),
            genreKeys,
            moodKeys,
            tempo,
            personalAffinity,
            communityRating * voteConfidence,
            1d - Math.Exp(-Math.Max(0, profile.PlayCount) / 5d),
            profile.LastPlayedAt,
            NormalizeOptional(profile.Energy),
            NormalizeOptional(profile.Brightness),
            NormalizeOptional(profile.Dynamics));
    }

    /// <summary>Ranks nearest metadata neighbours with artist and album diversity limits.</summary>
    /// <param name="seed">Reference vector.</param>
    /// <param name="candidates">Candidate vectors from any provider.</param>
    /// <param name="maximumResults">Maximum returned matches.</param>
    /// <param name="maximumPerArtist">Maximum matches sharing one non-empty artist key.</param>
    /// <param name="maximumPerAlbum">Maximum matches sharing one provider-local album identity.</param>
    /// <returns>Deterministically ordered nearest neighbours.</returns>
    public static IReadOnlyList<SimilarityFeatureMatch> RankSimilar(
        SimilarityFeatureVector seed,
        IEnumerable<SimilarityFeatureVector> candidates,
        int maximumResults = 50,
        int maximumPerArtist = 2,
        int maximumPerAlbum = 2)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidates);
        maximumResults = Math.Clamp(maximumResults, 1, 500);
        maximumPerArtist = Math.Clamp(maximumPerArtist, 1, maximumResults);
        maximumPerAlbum = Math.Clamp(maximumPerAlbum, 1, maximumResults);
        var artistCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var albumCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<SimilarityFeatureMatch>();
        foreach (var match in candidates
                     .Where(candidate => candidate.Version == seed.Version &&
                         (candidate.SourceKey != seed.SourceKey || candidate.TrackId != seed.TrackId))
                     .Select(candidate => new SimilarityFeatureMatch(candidate, CalculateSimilarity(seed, candidate)))
                     .Where(static match => match.Score > 0)
                     .OrderByDescending(static match => match.Score)
                     .ThenBy(static match => match.Vector.SourceKey, StringComparer.Ordinal)
                     .ThenBy(static match => match.Vector.TrackId))
        {
            var artistKey = match.Vector.ArtistKey;
            if (artistKey.Length > 0 && artistCounts.GetValueOrDefault(artistKey) >= maximumPerArtist)
                continue;
            var albumKey = match.Vector.AlbumId.HasValue
                ? $"{match.Vector.SourceKey}\u001f{match.Vector.AlbumId.Value}"
                : string.Empty;
            if (albumKey.Length > 0 && albumCounts.GetValueOrDefault(albumKey) >= maximumPerAlbum)
                continue;
            result.Add(match);
            if (artistKey.Length > 0)
                artistCounts[artistKey] = artistCounts.GetValueOrDefault(artistKey) + 1;
            if (albumKey.Length > 0)
                albumCounts[albumKey] = albumCounts.GetValueOrDefault(albumKey) + 1;
            if (result.Count == maximumResults)
                break;
        }
        return result;
    }

    /// <summary>Ranks tracks for a coarse mood using explicit mood tags, tempo, and preference signals.</summary>
    /// <param name="mood">Requested playback mood.</param>
    /// <param name="candidates">Candidate vectors from any provider.</param>
    /// <param name="maximumResults">Maximum returned matches.</param>
    /// <param name="maximumPerArtist">Maximum matches sharing one non-empty artist key.</param>
    /// <param name="maximumPerAlbum">Maximum matches sharing one provider-local album identity.</param>
    /// <returns>Deterministically ordered mood matches with artist and album diversity.</returns>
    public static IReadOnlyList<SimilarityFeatureMatch> RankMood(
        SimilarityMood mood,
        IEnumerable<SimilarityFeatureVector> candidates,
        int maximumResults = 500,
        int maximumPerArtist = 10,
        int maximumPerAlbum = 5)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        maximumResults = Math.Clamp(maximumResults, 1, 500);
        maximumPerArtist = Math.Clamp(maximumPerArtist, 1, maximumResults);
        maximumPerAlbum = Math.Clamp(maximumPerAlbum, 1, maximumResults);
        var targetTempo = mood switch
        {
            SimilarityMood.Calm => 0.25d,
            SimilarityMood.Energetic => 0.78d,
            _ => 0.5d
        };
        var desiredMoodKeys = mood switch
        {
            SimilarityMood.Calm => new[] { "calm", "chill", "relaxed", "ambient", "peaceful", "ruhig" },
            SimilarityMood.Energetic => new[] { "energetic", "upbeat", "party", "powerful", "dance", "energiegeladen" },
            _ => Array.Empty<string>()
        };
        var artistCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var albumCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<SimilarityFeatureMatch>();
        foreach (var match in candidates
                     .Where(candidate => candidate.Version == CurrentVersion)
                     .Select(candidate =>
                     {
                         var tempoScore = candidate.Tempo.HasValue
                             ? 1d - Math.Abs(candidate.Tempo.Value - targetTempo)
                             : 0.35d;
                         var explicitMood = desiredMoodKeys.Length == 0
                             ? 0.5d
                             : candidate.MoodKeys.Any(key => desiredMoodKeys.Contains(key, StringComparer.Ordinal)) ? 1d : 0d;
                         var acousticSignals = new[] { candidate.Energy, candidate.Brightness }
                             .Where(static value => value.HasValue)
                             .Select(value => 1d - Math.Abs(value!.Value - targetTempo))
                             .ToList();
                         var acousticScore = acousticSignals.Count > 0 ? acousticSignals.Average() : 0.35d;
                         var score = tempoScore * 0.4d + explicitMood * 0.2d + acousticScore * 0.15d +
                                     candidate.PersonalAffinity * 0.15d + candidate.CommunityAffinity * 0.05d +
                                     candidate.Familiarity * 0.05d;
                         return new SimilarityFeatureMatch(candidate, Math.Clamp(score, 0d, 1d));
                     })
                     .OrderByDescending(static match => match.Score)
                     .ThenBy(static match => match.Vector.SourceKey, StringComparer.Ordinal)
                     .ThenBy(static match => match.Vector.TrackId))
        {
            if (match.Vector.ArtistKey.Length > 0 &&
                artistCounts.GetValueOrDefault(match.Vector.ArtistKey) >= maximumPerArtist)
                continue;
            var albumKey = match.Vector.AlbumId.HasValue
                ? $"{match.Vector.SourceKey}\u001f{match.Vector.AlbumId.Value}"
                : string.Empty;
            if (albumKey.Length > 0 && albumCounts.GetValueOrDefault(albumKey) >= maximumPerAlbum)
                continue;
            result.Add(match);
            if (match.Vector.ArtistKey.Length > 0)
                artistCounts[match.Vector.ArtistKey] = artistCounts.GetValueOrDefault(match.Vector.ArtistKey) + 1;
            if (albumKey.Length > 0)
                albumCounts[albumKey] = albumCounts.GetValueOrDefault(albumKey) + 1;
            if (result.Count == maximumResults)
                break;
        }
        return result;
    }

    private static double CalculateSimilarity(SimilarityFeatureVector seed, SimilarityFeatureVector candidate)
    {
        var weightedScore = 0d;
        var totalWeight = 0d;
        Add(Jaccard(seed.GenreKeys, candidate.GenreKeys), 0.5, seed.GenreKeys.Count > 0 || candidate.GenreKeys.Count > 0);
        Add(Jaccard(seed.MoodKeys, candidate.MoodKeys), 0.15, seed.MoodKeys.Count > 0 && candidate.MoodKeys.Count > 0);
        Add(seed.Tempo.HasValue && candidate.Tempo.HasValue
            ? 1d - Math.Abs(seed.Tempo.Value - candidate.Tempo.Value)
            : 0d, 0.2, seed.Tempo.HasValue && candidate.Tempo.HasValue);
        Add(candidate.PersonalAffinity, 0.08, true);
        Add(candidate.CommunityAffinity, 0.04, true);
        Add(1d - Math.Abs(seed.Familiarity - candidate.Familiarity), 0.03, true);
        Add(Proximity(seed.Energy, candidate.Energy), 0.08, seed.Energy.HasValue && candidate.Energy.HasValue);
        Add(Proximity(seed.Brightness, candidate.Brightness), 0.05, seed.Brightness.HasValue && candidate.Brightness.HasValue);
        Add(Proximity(seed.Dynamics, candidate.Dynamics), 0.04, seed.Dynamics.HasValue && candidate.Dynamics.HasValue);
        return totalWeight == 0 ? 0 : Math.Clamp(weightedScore / totalWeight, 0d, 1d);

        void Add(double value, double weight, bool available)
        {
            if (!available)
                return;
            weightedScore += Math.Clamp(value, 0d, 1d) * weight;
            totalWeight += weight;
        }
    }

    private static double Proximity(double? left, double? right) =>
        left.HasValue && right.HasValue ? 1d - Math.Abs(left.Value - right.Value) : 0d;

    private static double? NormalizeOptional(double? value) =>
        value.HasValue ? Math.Clamp(value.Value, 0d, 1d) : null;

    private static double Jaccard(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
            return 0;
        var leftSet = left.ToHashSet(StringComparer.Ordinal);
        var intersection = right.Count(leftSet.Contains);
        var union = leftSet.Union(right, StringComparer.Ordinal).Count();
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static IReadOnlyList<string> SplitKeys(string? value) =>
        (value ?? string.Empty)
        .Split([';', ',', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(ArtistNameNormalizer.CreateComparisonKey)
        .Where(static key => key.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static key => key, StringComparer.Ordinal)
        .ToList();
}
