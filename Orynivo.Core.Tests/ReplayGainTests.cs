using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that persisted ReplayGain metadata is converted into the expected
/// linear PCM gain factors with the configured track/album fallback.
/// </summary>
public sealed class ReplayGainTests
{
    /// <summary>The disabled mode never applies any gain.</summary>
    [Fact]
    public void GetLinearFactor_OffReturnsUnity()
        => Assert.Equal(1.0f, ReplayGain.GetLinearFactor(ReplayGainMode.Off, "-6 dB", "-3 dB"));

    /// <summary>Track gain is converted from decibels to a linear multiplier.</summary>
    [Fact]
    public void GetLinearFactor_TrackModeConvertsDecibels()
        => Assert.Equal(
            Math.Pow(10, -6.0 / 20.0),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "-6 dB", null),
            5);

    /// <summary>A positive gain above unity is converted correctly.</summary>
    [Fact]
    public void GetLinearFactor_PositiveGainIsAboveUnity()
        => Assert.Equal(
            Math.Pow(10, 3.0 / 20.0),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "+3 dB", null),
            5);

    /// <summary>Track mode prefers the track value and falls back to the album value.</summary>
    [Fact]
    public void GetLinearFactor_TrackModePrefersTrackAndFallsBackToAlbum()
    {
        Assert.Equal(
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "-6 dB", "-12 dB"),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "-6 dB", null),
            5);
        Assert.Equal(
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "-12 dB", null),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, null, "-12 dB"),
            5);
    }

    /// <summary>Album mode prefers the album value and falls back to the track value.</summary>
    [Fact]
    public void GetLinearFactor_AlbumModePrefersAlbumAndFallsBackToTrack()
    {
        Assert.Equal(
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Album, null, "-12 dB"),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Album, "-6 dB", "-12 dB"),
            5);
        Assert.Equal(
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Album, "-6 dB", null),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Album, "-6 dB", "not a number"),
            5);
    }

    /// <summary>The decibel suffix is optional and surrounding whitespace is ignored.</summary>
    [Fact]
    public void GetLinearFactor_AcceptsValuesWithoutSuffixAndWithWhitespace()
        => Assert.Equal(
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "-6", null),
            (double)ReplayGain.GetLinearFactor(ReplayGainMode.Track, "  -6 dB  ", null),
            5);

    /// <summary>Missing, blank, or invalid values leave the audio unchanged.</summary>
    /// <param name="trackGain">Track gain text.</param>
    /// <param name="albumGain">Album gain text.</param>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    [InlineData("not a number", null)]
    [InlineData("+Infinity", "-Infinity")]
    public void GetLinearFactor_InvalidValuesReturnUnity(string? trackGain, string? albumGain)
        => Assert.Equal(1.0f, ReplayGain.GetLinearFactor(ReplayGainMode.Track, trackGain, albumGain));

    /// <summary>An out-of-range factor cannot overflow the linear gain.</summary>
    [Fact]
    public void GetLinearFactor_ExtremeGainIsRejected()
        => Assert.Equal(1.0f, ReplayGain.GetLinearFactor(ReplayGainMode.Track, "10000 dB", null));
}
