using System.Globalization;

namespace Orynivo.Audio;

/// <summary>
/// Converts persisted ReplayGain values into linear PCM gain factors.
/// </summary>
internal static class ReplayGain
{
    /// <summary>
    /// Resolves the configured ReplayGain value and converts it from decibels to a linear multiplier.
    /// </summary>
    /// <param name="mode">Configured ReplayGain mode.</param>
    /// <param name="trackGain">Track-level gain text read from metadata.</param>
    /// <param name="albumGain">Album-level gain text read from metadata.</param>
    /// <returns>A positive linear gain factor; <c>1.0</c> when no usable value is available.</returns>
    internal static float GetLinearFactor(ReplayGainMode mode, string? trackGain, string? albumGain)
    {
        var preferred = mode switch
        {
            ReplayGainMode.Track => ParseDecibels(trackGain) ?? ParseDecibels(albumGain),
            ReplayGainMode.Album => ParseDecibels(albumGain) ?? ParseDecibels(trackGain),
            _ => null
        };

        if (preferred is null)
            return 1.0f;

        var factor = Math.Pow(10.0, preferred.Value / 20.0);
        return double.IsFinite(factor) && factor > 0 && factor <= float.MaxValue
            ? (float)factor
            : 1.0f;
    }

    private static double? ParseDecibels(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^2].Trim();

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var decibels) &&
               double.IsFinite(decibels)
            ? decibels
            : null;
    }
}
