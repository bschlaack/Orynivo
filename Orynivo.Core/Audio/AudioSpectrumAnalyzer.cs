using Orynivo.Visualization;

namespace Orynivo.Audio;

/// <summary>
/// Turns the PCM stream a player is about to output into the compact spectrum a
/// visualization preset reacts to. It owns the windowing, the FFT, the logarithmic band
/// grouping, and the attack/decay smoothing, so a preset only ever sees stable values in
/// the range zero to one.
/// </summary>
public sealed class AudioSpectrumAnalyzer : IVisualizerAudioSource
{
    /// <summary>Number of logarithmic bands produced for every analyzed frame.</summary>
    public const int BandCount = 64;

    /// <summary>Number of time-domain points kept for the waveform overlay.</summary>
    public const int WaveformPoints = 256;

    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly float[] _window;
    private readonly float[] _scratch;
    private readonly float[] _monoSamples;
    private readonly float[] _magnitudes;
    private readonly int[] _bandEdges;
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _waveform = new float[WaveformPoints];
    private readonly float[] _rawSamples;

    /// <summary>Creates an analyzer for one output sample rate.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <param name="fftSize">FFT length; a power of two, defaulting to 2048.</param>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate or FFT size is unusable.</exception>
    public AudioSpectrumAnalyzer(int sampleRate, int fftSize = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8_000);
        if (!Fft.IsSupportedSize(fftSize))
            throw new ArgumentOutOfRangeException(nameof(fftSize), "The FFT size must be a power of two.");

        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _window = new float[fftSize];
        for (var index = 0; index < fftSize; index++)
            _window[index] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * index / (fftSize - 1)));

        _scratch = new float[fftSize];
        _rawSamples = new float[fftSize];
        _monoSamples = new float[fftSize];
        _magnitudes = new float[fftSize / 2];
        _bandEdges = BuildBandEdges(sampleRate, fftSize);
    }

    /// <summary>Gets the sample rate this analyzer was created for.</summary>
    public int SampleRate => _sampleRate;

    /// <summary>Gets the current smoothed band levels, each between zero and one.</summary>
    public ReadOnlySpan<float> Bands => _bands;

    /// <summary>
    /// Gets the most recent mono samples, decimated to <see cref="WaveformPoints"/> points in
    /// the range -1 to 1, for the visualizer waveform overlay.
    /// </summary>
    public ReadOnlySpan<float> Waveform => _waveform;

    /// <summary>Gets the current bass energy, between zero and one.</summary>
    public float Bass { get; private set; }

    /// <summary>Gets the current mid energy, between zero and one.</summary>
    public float Mid { get; private set; }

    /// <summary>Gets the current treble energy, between zero and one.</summary>
    public float Treble { get; private set; }

    /// <summary>Gets the current overall level, between zero and one.</summary>
    public float Volume { get; private set; }

    /// <summary>Gets the number of analyzed frames since the last reset.</summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// Analyzes one block of interleaved stereo PCM. A block shorter than the FFT size is
    /// zero-padded at the front, so a freshly started player still reports a frame.
    /// </summary>
    /// <param name="interleavedStereo">Interleaved left/right samples in the range -1 to 1.</param>
    public void Analyze(ReadOnlySpan<float> interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        var copied = Math.Min(frames, _fftSize);
        var start = _fftSize - copied;
        _monoSamples.AsSpan(0, start).Clear();
        for (var index = 0; index < copied; index++)
        {
            var left = interleavedStereo[index * 2];
            var right = interleavedStereo[(index * 2) + 1];
            _monoSamples[start + index] = (left + right) * 0.5f;
        }

        _monoSamples.AsSpan(0, _fftSize).CopyTo(_rawSamples);
        for (var index = 0; index < _fftSize; index++)
            _monoSamples[index] *= _window[index];

        Fft.ComputeMagnitudes(_monoSamples, _magnitudes, _scratch);

        var level = 0f;
        for (var band = 0; band < BandCount; band++)
        {
            var from = _bandEdges[band];
            var to = Math.Max(from + 1, _bandEdges[band + 1]);
            var sum = 0f;
            for (var bin = from; bin < to && bin < _magnitudes.Length; bin++)
                sum += _magnitudes[bin];

            var value = Math.Clamp(sum / (to - from) * 8f, 0f, 1f);
            level += value;
            _bands[band] = Smooth(_bands[band], value);
        }

        UpdateWaveform();

        var average = level / BandCount;
        Volume = Smooth(Volume, average);
        Bass = Smooth(Bass, Average(0, 16));
        Mid = Smooth(Mid, Average(16, 44));
        Treble = Smooth(Treble, Average(44, BandCount));
        FrameCount++;
    }

    /// <summary>Clears every smoothed value, for example when playback stops.</summary>
    public void Reset()
    {
        Array.Clear(_bands);
        Array.Clear(_waveform);
        Bass = Mid = Treble = Volume = 0f;
        FrameCount = 0;
    }

    /// <summary>Decimates the analyzed mono block into the waveform overlay buffer.</summary>
    private void UpdateWaveform()
    {
        var step = Math.Max(1, _fftSize / WaveformPoints);
        for (var point = 0; point < WaveformPoints; point++)
        {
            var index = Math.Min(_fftSize - 1, point * step);
            // The window applied for the FFT would flatten the ends, so re-read the plain
            // mono samples instead by undoing the window is not possible; use the magnitudes
            // of the raw samples kept in the scratch buffer region we control.
            _waveform[point] = _rawSamples[index];
        }
    }

    /// <summary>Applies a fast attack and a slow decay so bands do not flicker.</summary>
    /// <param name="current">Previous smoothed value.</param>
    /// <param name="target">Newly measured value.</param>
    /// <returns>The smoothed value.</returns>
    private static float Smooth(float current, float target) =>
        target > current ? current + ((target - current) * 0.55f) : current + ((target - current) * 0.12f);

    private float Average(int from, int to)
    {
        var sum = 0f;
        for (var band = from; band < to; band++)
            sum += _bands[band];
        return sum / Math.Max(1, to - from);
    }

    /// <summary>Builds logarithmic band boundaries between 30 Hz and the Nyquist rate.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <param name="fftSize">FFT length.</param>
    /// <returns>Band edges as FFT bin indices; the length is <see cref="BandCount"/> plus one.</returns>
    private static int[] BuildBandEdges(int sampleRate, int fftSize)
    {
        var edges = new int[BandCount + 1];
        var bins = fftSize / 2;
        const double minimum = 30.0;
        var maximum = Math.Min(sampleRate / 2.0, 18_000.0);
        var ratio = Math.Pow(maximum / minimum, 1.0 / BandCount);
        for (var band = 0; band <= BandCount; band++)
        {
            var frequency = minimum * Math.Pow(ratio, band);
            var bin = (int)Math.Round(frequency / (sampleRate / (double)fftSize));
            edges[band] = Math.Clamp(bin, 0, bins - 1);
        }

        // Guarantee a strictly rising sequence so every band has at least one bin.
        for (var band = 1; band <= BandCount; band++)
        {
            if (edges[band] <= edges[band - 1])
                edges[band] = Math.Min(edges[band - 1] + 1, bins - 1);
        }

        return edges;
    }
}
