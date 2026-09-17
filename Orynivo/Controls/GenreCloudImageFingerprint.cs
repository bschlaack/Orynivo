namespace Orynivo.Controls;

/// <summary>
/// Computes a 64-bit perceptual luminance fingerprint used to de-duplicate
/// genre-cloud background images reached through different source identities.
/// </summary>
internal static class GenreCloudImageFingerprint
{
    /// <summary>The number of luminance samples in the 8×8 grid.</summary>
    internal const int SampleCount = 64;

    /// <summary>
    /// Computes the fingerprint by setting one bit per sample whose luminance is at
    /// or above the average of all samples.
    /// </summary>
    /// <param name="luminance">Exactly <see cref="SampleCount"/> luminance samples in row-major order.</param>
    /// <returns>The 64-bit fingerprint.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the sample count is not <see cref="SampleCount"/>.
    /// </exception>
    internal static ulong Compute(ReadOnlySpan<float> luminance)
    {
        if (luminance.Length != SampleCount)
            throw new ArgumentException(
                $"Expected {SampleCount} luminance samples.",
                nameof(luminance));

        var total = 0f;
        foreach (var value in luminance)
            total += value;
        var average = total / SampleCount;

        var fingerprint = 0UL;
        for (var index = 0; index < SampleCount; index++)
        {
            if (luminance[index] >= average)
                fingerprint |= 1UL << index;
        }

        return fingerprint;
    }
}
