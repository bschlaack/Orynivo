using Avalonia.Animation;

namespace Orynivo.Controls;

/// <summary>
/// Resolves optional UI motion from the persisted reduce-motion preference. Every
/// animated surface asks this type instead of deciding locally, so the setting
/// stays consistent across the Genre Cloud, the Dashboard cover stage, and the
/// karaoke view.
/// </summary>
public static class MotionPreferences
{
    /// <summary>Returns whether optional animations should run at all.</summary>
    /// <param name="reduceMotion">Persisted reduce-motion preference.</param>
    /// <returns><see langword="true"/> when optional motion may run.</returns>
    public static bool ShouldAnimate(bool reduceMotion) => !reduceMotion;

    /// <summary>Returns the effective duration of an optional animation.</summary>
    /// <param name="reduceMotion">Persisted reduce-motion preference.</param>
    /// <param name="normalDuration">Duration used when motion is allowed.</param>
    /// <returns>The effective duration, or <see cref="TimeSpan.Zero"/> when motion is reduced.</returns>
    public static TimeSpan ResolveDuration(bool reduceMotion, TimeSpan normalDuration) =>
        reduceMotion ? TimeSpan.Zero : normalDuration;

    /// <summary>Returns the effective number of interpolation steps for a manual animation.</summary>
    /// <param name="reduceMotion">Persisted reduce-motion preference.</param>
    /// <param name="normalSteps">Step count used when motion is allowed.</param>
    /// <returns>One step when motion is reduced, otherwise the requested count.</returns>
    public static int ResolveStepCount(bool reduceMotion, int normalSteps) =>
        reduceMotion ? 1 : Math.Max(1, normalSteps);

    /// <summary>Returns the effective transition set for a property.</summary>
    /// <param name="reduceMotion">Persisted reduce-motion preference.</param>
    /// <param name="transitions">Transitions used when motion is allowed.</param>
    /// <returns><see langword="null"/> when motion is reduced, otherwise the supplied transitions.</returns>
    public static Transitions? ResolveTransitions(bool reduceMotion, Transitions? transitions) =>
        reduceMotion ? null : transitions;
}
