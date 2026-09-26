namespace Orynivo.Audio;

/// <summary>
/// Real-input radix-2 fast Fourier transform used by the audio visualizer. It is kept
/// small, allocation-free, and pure so the spectrum feeding a preset can be verified
/// independently of any rendering.
/// </summary>
public static class Fft
{
    /// <summary>Returns whether a size is usable as an FFT length.</summary>
    /// <param name="size">Candidate sample count.</param>
    /// <returns><see langword="true"/> for a power of two of at least two.</returns>
    public static bool IsSupportedSize(int size) => size >= 2 && (size & (size - 1)) == 0;

    /// <summary>
    /// Computes the magnitude spectrum of a real signal.
    /// </summary>
    /// <param name="samples">Real input samples; its length must be a supported FFT size.</param>
    /// <param name="magnitudes">
    /// Destination for the positive-frequency magnitudes; its length must be half the
    /// input length.
    /// </param>
    /// <param name="scratch">
    /// Optional reusable buffer of at least the input length; allocated when omitted.
    /// </param>
    /// <exception cref="ArgumentException">A length is unsupported or mismatched.</exception>
    public static void ComputeMagnitudes(
        ReadOnlySpan<float> samples,
        Span<float> magnitudes,
        float[]? scratch = null)
    {
        var size = samples.Length;
        if (!IsSupportedSize(size))
            throw new ArgumentException("The sample count must be a power of two.", nameof(samples));
        if (magnitudes.Length != size / 2)
            throw new ArgumentException("The magnitude buffer must hold half the samples.", nameof(magnitudes));

        var real = scratch is { Length: >= 0 } buffer && buffer.Length >= size ? buffer : new float[size];
        var imaginary = new float[size];
        samples.CopyTo(real.AsSpan(0, size));
        imaginary.AsSpan(0, size).Clear();

        // Bit-reversal permutation.
        for (int index = 1, reversed = 0; index < size; index++)
        {
            var bit = size >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
                reversed ^= bit;
            reversed ^= bit;
            if (index < reversed)
            {
                (real[index], real[reversed]) = (real[reversed], real[index]);
                (imaginary[index], imaginary[reversed]) = (imaginary[reversed], imaginary[index]);
            }
        }

        // Iterative Cooley-Tukey butterflies.
        for (var length = 2; length <= size; length <<= 1)
        {
            var angleStep = -2.0 * Math.PI / length;
            var half = length >> 1;
            for (var start = 0; start < size; start += length)
            {
                for (var offset = 0; offset < half; offset++)
                {
                    var angle = angleStep * offset;
                    var cos = (float)Math.Cos(angle);
                    var sin = (float)Math.Sin(angle);
                    var even = start + offset;
                    var odd = even + half;
                    var oddReal = real[odd] * cos - imaginary[odd] * sin;
                    var oddImaginary = real[odd] * sin + imaginary[odd] * cos;
                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;
                }
            }
        }

        for (var bin = 0; bin < magnitudes.Length; bin++)
        {
            var magnitude = MathF.Sqrt(
                (real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));
            magnitudes[bin] = magnitude / size;
        }
    }
}
