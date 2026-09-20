using Avalonia.Animation;
using Orynivo.Controls;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Verifies the central reduce-motion decisions.</summary>
public sealed class MotionPreferencesTests
{
    /// <summary>Motion runs unless the user reduced it.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldAnimate_ReflectsThePreference(bool reduceMotion, bool expected)
    {
        Assert.Equal(expected, MotionPreferences.ShouldAnimate(reduceMotion));
    }

    /// <summary>A reduced preference collapses every optional duration to zero.</summary>
    [Fact]
    public void ResolveDuration_CollapsesWhenReduced()
    {
        var normal = TimeSpan.FromMilliseconds(260);

        Assert.Equal(normal, MotionPreferences.ResolveDuration(reduceMotion: false, normal));
        Assert.Equal(TimeSpan.Zero, MotionPreferences.ResolveDuration(reduceMotion: true, normal));
    }

    /// <summary>A reduced preference leaves a manual animation with one final step.</summary>
    [Theory]
    [InlineData(false, 15, 15)]
    [InlineData(true, 15, 1)]
    [InlineData(false, 0, 1)]
    public void ResolveStepCount_KeepsAtLeastOneStep(bool reduceMotion, int normalSteps, int expected)
    {
        Assert.Equal(expected, MotionPreferences.ResolveStepCount(reduceMotion, normalSteps));
    }

    /// <summary>A reduced preference drops the transition set entirely.</summary>
    [Fact]
    public void ResolveTransitions_DropsWhenReduced()
    {
        var transitions = new Transitions();

        Assert.Same(transitions, MotionPreferences.ResolveTransitions(reduceMotion: false, transitions));
        Assert.Null(MotionPreferences.ResolveTransitions(reduceMotion: true, transitions));
        Assert.Null(MotionPreferences.ResolveTransitions(reduceMotion: false, null));
    }
}
