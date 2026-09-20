namespace Orynivo.Library;

/// <summary>
/// Pure decisions for resuming a track that was last played on another device. The
/// server stores the last position per profile and track; this type decides whether
/// that position is worth offering on the current device.
/// </summary>
public static class CrossDeviceResume
{
    /// <summary>Positions below this many seconds count as "started from the beginning".</summary>
    public const double MinimumPositionSeconds = 20d;

    /// <summary>A position at or beyond this fraction of the duration counts as finished.</summary>
    public const double NearEndFraction = 0.95d;

    /// <summary>The stored position must lead the local position by at least this many seconds.</summary>
    public const double MinimumLeadSeconds = 15d;

    /// <summary>Decides whether a stored remote position should be offered on this device.</summary>
    /// <param name="remoteSeconds">Position stored by the other device.</param>
    /// <param name="durationSeconds">Known track duration, or zero when unknown.</param>
    /// <param name="localSeconds">Position the current device would start at.</param>
    /// <returns><see langword="true"/> when the position should be offered.</returns>
    public static bool ShouldOffer(double remoteSeconds, double durationSeconds, double localSeconds)
    {
        if (double.IsNaN(remoteSeconds) || double.IsInfinity(remoteSeconds))
            return false;
        if (remoteSeconds < MinimumPositionSeconds)
            return false;
        if (durationSeconds > 0 && remoteSeconds >= durationSeconds * NearEndFraction)
            return false;
        if (double.IsNaN(localSeconds) || double.IsInfinity(localSeconds))
            return false;
        return localSeconds < remoteSeconds - MinimumLeadSeconds;
    }

    /// <summary>Clamps a stored position to a playable range.</summary>
    /// <param name="seconds">Stored position in seconds.</param>
    /// <param name="durationSeconds">Known track duration, or zero when unknown.</param>
    /// <returns>A non-negative position that stays inside the track.</returns>
    public static double Normalize(double seconds, double durationSeconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            return 0d;
        return durationSeconds > 0 ? Math.Min(seconds, durationSeconds) : seconds;
    }
}
