namespace Orynivo.Scrobbling;

/// <summary>
/// Applies Last.fm's scrobble eligibility rules to a playback.
/// </summary>
public static class ScrobbleRules
{
    /// <summary>The minimum track length Last.fm accepts; shorter tracks are never scrobbled.</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(30);

    /// <summary>The upper bound on the playback threshold, reached by long tracks.</summary>
    public static readonly TimeSpan MaximumThreshold = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Determines whether a playback should be submitted as a scrobble: the track
    /// must be longer than <see cref="MinimumDuration"/> and must have been played
    /// for at least half its duration, capped at <see cref="MaximumThreshold"/>.
    /// </summary>
    /// <param name="played">The audible playback time.</param>
    /// <param name="duration">The total track duration.</param>
    /// <returns><see langword="true"/> when the playback qualifies.</returns>
    public static bool ShouldScrobble(TimeSpan played, TimeSpan duration)
    {
        if (duration <= MinimumDuration)
            return false;

        var threshold = duration / 2;
        if (threshold > MaximumThreshold)
            threshold = MaximumThreshold;

        return played >= threshold;
    }
}
