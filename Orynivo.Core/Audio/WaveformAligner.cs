namespace Orynivo.Audio;

/// <summary>
/// Aligns each analyzed frame's waveform to the previous frame, so a custom waveform holds its shape
/// instead of sliding sideways. This is a port of the reference implementation's aligner: it compares
/// the current window against the previous frame's aligned window, coarsest octave first, and shifts
/// the window left by the offset that minimises the weighted absolute difference. The alignment only
/// affects the exposed waveform; the spectrum is analyzed before it runs.
/// </summary>
internal sealed class WaveformAligner
{
    /// <summary>The reference clamps its octave count to ten.</summary>
    private const int MaximumOctaves = 10;

    /// <summary>The reference does not align when it cannot build at least four octaves.</summary>
    private const int MinimumOctaves = 4;

    private readonly int _octaves;
    private readonly int[] _octaveSamples;
    private readonly int[] _octaveSpacing;
    private readonly float[][] _alignmentWeights;
    private readonly int[] _firstNonzero;
    private readonly int[] _lastNonzero;
    private readonly float[][] _newMips;
    private readonly float[][] _oldMips;
    private bool _weightsReady;

    /// <summary>Creates an aligner for one waveform window.</summary>
    /// <param name="bufferSamples">
    /// Samples the aligner compares; must be larger than <paramref name="waveformSamples"/> so the
    /// waveform can be shifted.
    /// </param>
    /// <param name="waveformSamples">Samples the caller exposes after alignment.</param>
    /// <exception cref="ArgumentOutOfRangeException">The sizes are unusable.</exception>
    public WaveformAligner(int bufferSamples, int waveformSamples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSamples, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(waveformSamples, 1);
        if (waveformSamples > bufferSamples)
            throw new ArgumentOutOfRangeException(nameof(waveformSamples), "The window cannot exceed the buffer.");

        WaveformSamples = waveformSamples;

        // The margin bounds how far a waveform may be shifted, so the octave count follows it exactly
        // as it does in the reference.
        var margin = bufferSamples - waveformSamples;
        var octaves = margin > 1 ? (int)Math.Floor(Math.Log(margin) / Math.Log(2.0)) : 0;
        _octaves = Math.Clamp(octaves, 0, MaximumOctaves);

        _octaveSamples = new int[_octaves];
        _octaveSpacing = new int[_octaves];
        _alignmentWeights = new float[_octaves][];
        _firstNonzero = new int[_octaves];
        _lastNonzero = new int[_octaves];
        _newMips = new float[_octaves][];
        _oldMips = new float[_octaves][];
        if (_octaves == 0)
            return;

        _octaveSamples[0] = bufferSamples;
        _octaveSpacing[0] = margin;
        for (var octave = 1; octave < _octaves; octave++)
        {
            _octaveSamples[octave] = _octaveSamples[octave - 1] / 2;
            _octaveSpacing[octave] = _octaveSpacing[octave - 1] / 2;
        }

        for (var octave = 0; octave < _octaves; octave++)
        {
            _alignmentWeights[octave] = new float[_octaveSamples[octave]];
            _newMips[octave] = new float[_octaveSamples[octave]];
            _oldMips[octave] = new float[_octaveSamples[octave]];
        }
    }

    /// <summary>Gets the number of samples the caller exposes after alignment.</summary>
    public int WaveformSamples { get; }

    /// <summary>Aligns one window in place and remembers it for the next frame.</summary>
    /// <param name="waveform">
    /// Samples to align; the length must equal the buffer size the aligner was created with. The
    /// aligned window is copied to the front and the remaining samples are cleared.
    /// </param>
    public void Align(float[] waveform)
    {
        ArgumentNullException.ThrowIfNull(waveform);

        if (_octaves < MinimumOctaves)
            return;

        ResampleOctaves(_newMips, waveform);
        if (!_weightsReady)
        {
            GenerateWeights();
            _weightsReady = true;
        }

        var alignOffset = CalculateOffset();
        if (alignOffset > 0)
        {
            Array.Copy(waveform, alignOffset, waveform, 0, WaveformSamples);
            Array.Clear(waveform, WaveformSamples, waveform.Length - WaveformSamples);
        }

        // The next frame compares against the shifted waveform, so its mips are rebuilt from it.
        ResampleOctaves(_oldMips, waveform);
    }

    /// <summary>Forgets the previous frame so a restarted analysis does not align against it.</summary>
    public void Reset()
    {
        foreach (var mip in _oldMips)
            Array.Clear(mip);
        foreach (var mip in _newMips)
            Array.Clear(mip);
    }

    /// <summary>Builds the octave mip levels of one window.</summary>
    /// <param name="destination">Mip levels to fill.</param>
    /// <param name="waveform">Samples to resample.</param>
    private void ResampleOctaves(float[][] destination, float[] waveform)
    {
        // Octave zero is a direct copy of the window.
        Array.Copy(waveform, destination[0], _octaveSamples[0]);
        for (var octave = 1; octave < _octaves; octave++)
        {
            var source = destination[octave - 1];
            var target = destination[octave];
            for (var sample = 0; sample < _octaveSamples[octave]; sample++)
                target[sample] = 0.5f * (source[sample * 2] + source[(sample * 2) + 1]);
        }
    }

    /// <summary>
    /// Builds the pyramid-shaped comparison weights once. The reference's tweak zeroes the outer
    /// weights, so the first and last non-zero positions are recorded for the search.
    /// </summary>
    private void GenerateWeights()
    {
        for (var octave = 0; octave < _octaves; octave++)
        {
            var compareSamples = _octaveSamples[octave] - _octaveSpacing[octave];
            var weights = _alignmentWeights[octave];
            for (var sample = 0; sample < compareSamples; sample++)
            {
                var weight = sample < compareSamples / 2
                    ? (sample * 2f) / compareSamples
                    : ((compareSamples - 1 - sample) * 2f) / compareSamples;

                // The reference's tweak moves the weight's zero crossings to 32% and 68%.
                weight = ((weight - 0.8f) * 5f) + 0.8f;
                weights[sample] = Math.Clamp(weight, 0f, 1f);
            }

            var first = 0;
            while (first < compareSamples && weights[first] == 0f)
                first++;
            _firstNonzero[octave] = first;

            var last = compareSamples - 1;
            while (last > 0 && weights[last] == 0f)
                last--;
            _lastNonzero[octave] = last;
        }
    }

    /// <summary>
    /// Finds the offset with the lowest weighted difference between the current window and the
    /// previous frame, narrowing the search from the coarsest octave to the finest.
    /// </summary>
    /// <returns>The offset to shift the window by, never negative.</returns>
    private int CalculateOffset()
    {
        var alignOffset = 0;
        var offsetStart = 0;
        var offsetEnd = _octaveSpacing[_octaves - 1];

        for (var octave = _octaves - 1; octave >= 0; octave--)
        {
            var lowestErrorOffset = -1;
            var lowestErrorAmount = 0f;

            for (var sample = offsetStart; sample < offsetEnd; sample++)
            {
                var errorSum = 0f;
                var weights = _alignmentWeights[octave];
                var current = _newMips[octave];
                var previous = _oldMips[octave];
                for (var index = _firstNonzero[octave]; index <= _lastNonzero[octave]; index++)
                    errorSum += MathF.Abs((current[index + sample] - previous[index]) * weights[index]);

                if (lowestErrorOffset == -1 || errorSum < lowestErrorAmount)
                {
                    lowestErrorOffset = sample;
                    lowestErrorAmount = errorSum;
                }
            }

            if (octave > 0)
            {
                // A coarse offset maps onto the finer octave's samples, widened by one on each side.
                offsetStart = Math.Max(0, (lowestErrorOffset * 2) - 1);
                offsetEnd = Math.Min(_octaveSpacing[octave - 1], (lowestErrorOffset * 2) + 3);
            }
            else
            {
                alignOffset = lowestErrorOffset;
            }
        }

        return alignOffset;
    }
}
