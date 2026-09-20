using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the cross-device resume decision and position normalisation.</summary>
public sealed class CrossDeviceResumeTests
{
    /// <summary>A meaningfully advanced position is offered.</summary>
    [Fact]
    public void ShouldOffer_AcceptsAnAdvancedPosition()
    {
        Assert.True(CrossDeviceResume.ShouldOffer(remoteSeconds: 120, durationSeconds: 300, localSeconds: 0));
    }

    /// <summary>Nearly untouched tracks are not offered.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(19.9)]
    public void ShouldOffer_RejectsEarlyPositions(double remoteSeconds)
    {
        Assert.False(CrossDeviceResume.ShouldOffer(remoteSeconds, durationSeconds: 300, localSeconds: 0));
    }

    /// <summary>Finished tracks are not offered again.</summary>
    [Theory]
    [InlineData(300)]
    [InlineData(285)]
    [InlineData(400)]
    public void ShouldOffer_RejectsFinishedPositions(double remoteSeconds)
    {
        Assert.False(CrossDeviceResume.ShouldOffer(remoteSeconds, durationSeconds: 300, localSeconds: 0));
    }

    /// <summary>The position is offered only when it leads the local position.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(104, true)]
    [InlineData(105, false)]
    [InlineData(120, false)]
    public void ShouldOffer_RequiresAMeaningfulLead(double localSeconds, bool expected)
    {
        Assert.Equal(expected, CrossDeviceResume.ShouldOffer(120, durationSeconds: 600, localSeconds));
    }

    /// <summary>An unknown duration still allows the offer.</summary>
    [Fact]
    public void ShouldOffer_WorksWithoutADuration()
    {
        Assert.True(CrossDeviceResume.ShouldOffer(120, durationSeconds: 0, localSeconds: 0));
    }

    /// <summary>Invalid positions are never offered.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ShouldOffer_RejectsInvalidPositions(double remoteSeconds)
    {
        Assert.False(CrossDeviceResume.ShouldOffer(remoteSeconds, durationSeconds: 300, localSeconds: 0));
    }

    /// <summary>Normalisation clamps into the track.</summary>
    [Theory]
    [InlineData(0, 300, 0)]
    [InlineData(-5, 300, 0)]
    [InlineData(120, 300, 120)]
    [InlineData(400, 300, 300)]
    [InlineData(120, 0, 120)]
    [InlineData(double.NaN, 300, 0)]
    public void Normalize_ClampsIntoTheTrack(double seconds, double durationSeconds, double expected)
    {
        Assert.Equal(expected, CrossDeviceResume.Normalize(seconds, durationSeconds));
    }
}
