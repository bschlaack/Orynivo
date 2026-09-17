using Orynivo.Scrobbling;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies Last.fm scrobble eligibility: longer than 30 seconds and played for
/// at least half the duration, capped at four minutes.
/// </summary>
public sealed class ScrobbleRulesTests
{
    /// <summary>Eligibility follows the documented threshold rules.</summary>
    /// <param name="durationSeconds">Track duration in seconds.</param>
    /// <param name="playedSeconds">Played time in seconds.</param>
    /// <param name="expected">Expected eligibility.</param>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(29, 29, false)]
    [InlineData(30, 30, false)]
    [InlineData(31, 15, false)]
    [InlineData(31, 16, true)]
    [InlineData(300, 120, false)]
    [InlineData(300, 150, true)]
    [InlineData(600, 239, false)]
    [InlineData(600, 240, true)]
    [InlineData(600, 400, true)]
    public void ShouldScrobble_AppliesLastFmRules(int durationSeconds, int playedSeconds, bool expected)
        => Assert.Equal(
            expected,
            ScrobbleRules.ShouldScrobble(
                TimeSpan.FromSeconds(playedSeconds),
                TimeSpan.FromSeconds(durationSeconds)));

    /// <summary>The documented thresholds are exposed unchanged.</summary>
    [Fact]
    public void Thresholds_AreDocumentedValues()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ScrobbleRules.MinimumDuration);
        Assert.Equal(TimeSpan.FromMinutes(4), ScrobbleRules.MaximumThreshold);
    }
}
