using Orynivo.Visualization;

namespace Orynivo.Audio;

/// <summary>
/// Turns the PCM stream a player is about to output into the compact spectrum a
/// visualization preset reacts to. It owns the windowing, the FFT, the logarithmic band
/// grouping, and the attack/decay smoothing. Display bands are normalized to zero to one;
/// preset-facing MilkDrop loudness values are relative and may exceed one.
/// </summary>
public sealed class AudioSpectrumAnalyzer : IVisualizerAudioSource
{
    /// <summary>Number of logarithmic bands produced for every analyzed frame.</summary>
    public const int BandCount = 64;

    /// <summary>Number of time-domain points kept for the waveform overlay.</summary>
    public const int WaveformPoints = 512;

    /// <summary>Number of frequency points kept for a spectrum-reading custom waveform.</summary>
    public const int SpectrumPoints = 256;

    /// <summary>Samples the waveform aligner may shift a window by; the reference's own margin.</summary>
    private const int AlignMargin = 96;

    /// <summary>Samples the aligner compares to find the shift.</summary>
    private const int AlignBufferPoints = WaveformPoints + AlignMargin;

    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly float[] _window;
    private readonly float[] _scratch;
    private readonly float[] _monoSamples;
    private readonly float[] _magnitudes;
    private readonly int[] _bandEdges;

    /// <summary>
    /// The reference's logarithmic frequency equalization, applied to the magnitudes its loudness bands
    /// and spectrum read: <c>-0.02 * ln((half - bin) / half)</c>, zero at DC and rising with frequency.
    /// </summary>
    private readonly float[] _equalize;

    /// <summary>Channel-averaged magnitudes the reference loudness bands and spectrum read.</summary>
    private readonly float[] _bandMagnitudes;
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _waveform = new float[WaveformPoints];
    private readonly float[] _spectrum = new float[SpectrumPoints];
    private readonly float[] _rawSamples;

    // Milkdrop's own analysis: per-channel data for the custom waveforms, and the loudness bands
    // relative to their long-term average.
    private readonly float[] _leftSamples;
    private readonly float[] _rightSamples;
    private readonly float[] _leftMagnitudes;
    private readonly float[] _rightMagnitudes;
    private readonly float[] _milkdropSamples;
    private readonly float[] _milkdropMagnitudes;
    private readonly float[] _waveformLeft = new float[WaveformPoints];
    private readonly float[] _waveformRight = new float[WaveformPoints];
    private readonly float[] _alignLeft = new float[AlignBufferPoints];
    private readonly float[] _alignRight = new float[AlignBufferPoints];
    private readonly WaveformAligner _alignerLeft = new(AlignBufferPoints, WaveformPoints);
    private readonly WaveformAligner _alignerRight = new(AlignBufferPoints, WaveformPoints);
    private readonly float[] _spectrumLeft = new float[SpectrumPoints];
    private readonly float[] _spectrumRight = new float[SpectrumPoints];
    private readonly Loudness _bass = new();
    private readonly Loudness _mid = new();
    private readonly Loudness _treble = new();
    private long _analysisFrame;

    /// <summary>
    /// Samples the reference's custom sound analysis transforms from its 576-sample waveform.
    /// </summary>
    public const int ReferenceAnalysisSamples = 576;

    /// <summary>Creates an analyzer for one output sample rate.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <param name="fftSize">FFT length; a power of two, defaulting to the reference's 1024.</param>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate or FFT size is unusable.</exception>
    public AudioSpectrumAnalyzer(int sampleRate, int fftSize = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8_000);
        if (!Fft.IsSupportedSize(fftSize))
            throw new ArgumentOutOfRangeException(nameof(fftSize), "The FFT size must be a power of two.");

        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _window = new float[fftSize];
        // The reference transforms 576 waveform samples. The additional 96 samples in the
        // aligner give it room to choose the best overlapping window.
        var windowed = Math.Min(ReferenceAnalysisSamples, fftSize);
        var windowStart = Math.Max(0, fftSize - Math.Min(AlignBufferPoints, fftSize));
        for (var index = 0; index < windowed; index++)
            _window[windowStart + index] = 0.5f - (0.5f * MathF.Cos(2f * MathF.PI * index / windowed));

        _scratch = new float[fftSize];
        _rawSamples = new float[fftSize];
        _monoSamples = new float[fftSize];
        _magnitudes = new float[fftSize / 2];
        _bandEdges = BuildBandEdges(sampleRate, fftSize);
        var half = fftSize / 2;
        _bandMagnitudes = new float[fftSize / 2];
        _equalize = new float[half];
        for (var index = 0; index < half; index++)
            _equalize[index] = -0.02f * MathF.Log((half - index) / (float)half);
        _leftSamples = new float[fftSize];
        _rightSamples = new float[fftSize];
        _leftMagnitudes = new float[fftSize / 2];
        _rightMagnitudes = new float[fftSize / 2];
        _milkdropSamples = new float[fftSize];
        _milkdropMagnitudes = new float[fftSize / 2];
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

    /// <summary>
    /// Gets the recent spectrum magnitudes, normalized to zero to one and decimated to
    /// <see cref="SpectrumPoints"/> points, lowest frequency first, for a custom waveform that reads
    /// the spectrum.
    /// </summary>
    public ReadOnlySpan<float> Spectrum => _spectrum;

    /// <inheritdoc/>
    public ReadOnlySpan<float> WaveformLeft => _waveformLeft;

    /// <inheritdoc/>
    public ReadOnlySpan<float> WaveformRight => _waveformRight;

    /// <inheritdoc/>
    public ReadOnlySpan<float> SpectrumLeft => _spectrumLeft;

    /// <inheritdoc/>
    public ReadOnlySpan<float> SpectrumRight => _spectrumRight;

    /// <inheritdoc/>
    public float BassRelative => _bass.CurrentRelative;

    /// <inheritdoc/>
    public float MidRelative => _mid.CurrentRelative;

    /// <inheritdoc/>
    public float TrebleRelative => _treble.CurrentRelative;

    /// <inheritdoc/>
    public float BassAttRelative => _bass.AverageRelative;

    /// <inheritdoc/>
    public float MidAttRelative => _mid.AverageRelative;

    /// <inheritdoc/>
    public float TrebleAttRelative => _treble.AverageRelative;

    /// <summary>
    /// One MilkDrop custom-sound band. Its running averages start at zero and use the distinct
    /// thirty-frame-per-second rates in <c>CPlugin::DoCustomSoundAnalysis</c>.
    /// </summary>
    private sealed class Loudness
    {
        /// <summary>
    /// The reference's guard against dividing by an empty long-term band average, in its
    /// unnormalized FFT magnitude units.
        /// </summary>
        private const float EmptyBandThreshold = 0.001f;

        private float _average;
        private float _longAverage;

        /// <summary>Gets the current band sum divided by the long-term average.</summary>
        public float CurrentRelative { get; private set; } = 1f;

        /// <summary>Gets the attenuated band sum divided by the long-term average.</summary>
        public float AverageRelative { get; private set; } = 1f;

        /// <summary>Updates the band with this frame's sum.</summary>
        /// <param name="current">Band sum for this frame.</param>
        /// <param name="secondsSinceLastFrame">Time since the previous frame.</param>
        /// <param name="frame">Analysis frame count for the reference's first-fifty-frame rate.</param>
        public void Update(float current, double secondsSinceLastFrame, long frame)
        {
            var rate = AdjustRateToFps(current > _average ? 0.2f : 0.5f, secondsSinceLastFrame);
            _average = (_average * rate) + (current * (1f - rate));

            rate = AdjustRateToFps(frame < 50 ? 0.9f : 0.992f, secondsSinceLastFrame);
            _longAverage = (_longAverage * rate) + (current * (1f - rate));

            CurrentRelative = MathF.Abs(_longAverage) < EmptyBandThreshold ? 1f : current / _longAverage;
            AverageRelative = MathF.Abs(_longAverage) < EmptyBandThreshold ? 1f : _average / _longAverage;
        }

        /// <summary>Resets the band to its neutral state.</summary>
        public void Reset()
        {
            _average = 0f;
            _longAverage = 0f;
            CurrentRelative = 1f;
            AverageRelative = 1f;
        }

        /// <summary>Scales a per-frame rate from the reference's thirty frames per second.</summary>
        /// <param name="rate">Rate at thirty frames per second.</param>
        /// <param name="secondsSinceLastFrame">Time since the previous frame.</param>
        /// <returns>The adjusted rate.</returns>
        private static float AdjustRateToFps(float rate, double secondsSinceLastFrame)
        {
            var perSecond = MathF.Pow(rate, 30f);
            return MathF.Pow(perSecond, (float)secondsSinceLastFrame);
        }
    }

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
    /// <param name="secondsSinceLastFrame">Time since the previous analysis, for the loudness rates.</param>
    public void Analyze(ReadOnlySpan<float> interleavedStereo, double secondsSinceLastFrame = 1d / 60d)
    {
        var frames = interleavedStereo.Length / 2;
        var copied = Math.Min(frames, _fftSize);
        var start = _fftSize - copied;
        _leftSamples.AsSpan(0, start).Clear();
        _rightSamples.AsSpan(0, start).Clear();
        for (var index = 0; index < copied; index++)
        {
            _leftSamples[start + index] = interleavedStereo[index * 2];
            _rightSamples[start + index] = interleavedStereo[(index * 2) + 1];
        }

        for (var index = 0; index < _fftSize; index++)
            _monoSamples[index] = (_leftSamples[index] + _rightSamples[index]) * 0.5f;
        _monoSamples.AsSpan(0, _fftSize).CopyTo(_rawSamples);
        UpdateWaveform();
        UpdateStereoWaveform();

        ApplyPreEmphasis(_monoSamples);
        ApplyPreEmphasis(_leftSamples);
        ApplyPreEmphasis(_rightSamples);

        for (var index = 0; index < _fftSize; index++)
            _monoSamples[index] *= _window[index];

        Fft.ComputeMagnitudes(_monoSamples, _magnitudes, _scratch);

        // The per-channel magnitudes feed the stereo spectrum a custom waveform may read.
        for (var index = 0; index < _fftSize; index++)
            _leftSamples[index] *= _window[index];
        Fft.ComputeMagnitudes(_leftSamples, _leftMagnitudes, _scratch);
        for (var index = 0; index < _fftSize; index++)
            _rightSamples[index] *= _window[index];
        Fft.ComputeMagnitudes(_rightSamples, _rightMagnitudes, _scratch);

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

        // The reference equalizes the magnitudes on a logarithmic frequency scale before its loudness
        // bands and its spectrum read them, which is what makes its middle and treble sums smaller than
        // the raw ones. Orynivo's own normalized display bands above stay un-equalized, because that is
        // a separate contract other consumers use.
        ApplyEqualize(_magnitudes);
        ApplyEqualize(_leftMagnitudes);
        ApplyEqualize(_rightMagnitudes);

        // The reference averages the two channels' equalized magnitudes for its loudness bands instead
        // of transforming their mix, so a phase-inverted stereo pair is not cancelled out of the bands.
        var bins = Math.Min(_bandMagnitudes.Length, Math.Min(_leftMagnitudes.Length, _rightMagnitudes.Length));
        for (var index = 0; index < bins; index++)
            _bandMagnitudes[index] = 0.5f * (_leftMagnitudes[index] + _rightMagnitudes[index]);

        UpdateSpectrum();
        UpdateStereoSpectrum();
        UpdateLoudness(secondsSinceLastFrame);

        var average = level / BandCount;
        Volume = Smooth(Volume, average);
        Bass = Smooth(Bass, Average(0, 16));
        Mid = Smooth(Mid, Average(16, 44));
        Treble = Smooth(Treble, Average(44, BandCount));
        FrameCount++;
    }

    /// <summary>
    /// Copies the latest contiguous PCM window into the stereo waveform buffers, after aligning each
    /// channel to the previous frame so a custom waveform holds its shape instead of sliding.
    /// </summary>
    private void UpdateStereoWaveform()
    {
        var copied = Math.Min(_fftSize, AlignBufferPoints);
        var padding = AlignBufferPoints - copied;
        Array.Clear(_alignLeft, 0, padding);
        Array.Clear(_alignRight, 0, padding);
        _leftSamples.AsSpan(_fftSize - copied, copied).CopyTo(_alignLeft.AsSpan(padding));
        _rightSamples.AsSpan(_fftSize - copied, copied).CopyTo(_alignRight.AsSpan(padding));

        _alignerLeft.Align(_alignLeft);
        _alignerRight.Align(_alignRight);
        _alignLeft.AsSpan(0, WaveformPoints).CopyTo(_waveformLeft);
        _alignRight.AsSpan(0, WaveformPoints).CopyTo(_waveformRight);
    }

    /// <summary>Decimates the per-channel magnitudes into the stereo spectrum buffers.</summary>
    private void UpdateStereoSpectrum()
    {
        var bins = Math.Max(1, _leftMagnitudes.Length);
        var step = Math.Max(1, bins / SpectrumPoints);
        for (var point = 0; point < SpectrumPoints; point++)
        {
            var index = Math.Min(bins - 1, point * step);
            _spectrumLeft[point] = Math.Clamp(_leftMagnitudes[index] * 8f, 0f, 1f);
            _spectrumRight[point] = Math.Clamp(_rightMagnitudes[index] * 8f, 0f, 1f);
        }
    }

    /// <summary>
    /// Updates the preset-facing bands from MilkDrop's separate custom-sound FFT. Unlike the
    /// display spectrum, this uses the left 576-sample eight-bit waveform and sums the first
    /// three sixths of its equalized, unnormalized 1024-point FFT.
    /// </summary>
    /// <param name="secondsSinceLastFrame">Time since the previous analysis.</param>
    private void UpdateLoudness(double secondsSinceLastFrame)
    {
        Array.Clear(_milkdropSamples);
        var count = Math.Min(ReferenceAnalysisSamples, Math.Min(_alignLeft.Length, _milkdropSamples.Length));
        for (var index = 0; index < count; index++)
        {
            var quantized = Math.Clamp((int)MathF.Round(_alignLeft[index] * 128f), -128, 127);
            var envelope = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * index / count);
            _milkdropSamples[index] = quantized * envelope;
        }
        Fft.ComputeMagnitudes(_milkdropSamples, _milkdropMagnitudes, _scratch);
        for (var index = 0; index < _milkdropMagnitudes.Length; index++)
            _milkdropMagnitudes[index] *= _fftSize * _equalize[index];

        _bass.Update(SumSixth(_milkdropMagnitudes, 0), secondsSinceLastFrame, _analysisFrame);
        _mid.Update(SumSixth(_milkdropMagnitudes, 1), secondsSinceLastFrame, _analysisFrame);
        _treble.Update(SumSixth(_milkdropMagnitudes, 2), secondsSinceLastFrame, _analysisFrame);
        _analysisFrame++;
    }

    /// <summary>Sums one sixth of the linear spectrum, as MilkDrop's preset audio path does.</summary>
    /// <param name="magnitudes">Spectrum magnitudes.</param>
    /// <param name="band">Band index, zero being the bass.</param>
    /// <returns>The unnormalized magnitude sum.</returns>
    private static float SumSixth(ReadOnlySpan<float> magnitudes, int band)
    {
        var start = magnitudes.Length * band / 6;
        var end = magnitudes.Length * (band + 1) / 6;
        var sum = 0f;
        for (var index = start; index < end; index++)
            sum += magnitudes[index];
        return sum;
    }

    /// <summary>
    /// Applies the reference's one-sample pre-emphasis to an FFT input, damping high-frequency
    /// noise. Each sample is read before it is overwritten, so a backward pass stays exact.
    /// </summary>
    /// <param name="samples">Samples to damp in place.</param>
    private static void ApplyPreEmphasis(float[] samples)
    {
        for (var index = samples.Length - 1; index >= 1; index--)
            samples[index] = 0.5f * (samples[index] + samples[index - 1]);
    }

    /// <summary>Applies the reference's logarithmic frequency equalization to magnitudes.</summary>
    /// <param name="magnitudes">Magnitudes to scale in place.</param>
    private void ApplyEqualize(float[] magnitudes)
    {
        var count = Math.Min(magnitudes.Length, _equalize.Length);
        for (var index = 0; index < count; index++)
            magnitudes[index] *= _equalize[index];
    }

    /// <summary>Clears every smoothed value, for example when playback stops.</summary>
    public void Reset()
    {
        Array.Clear(_bands);
        Array.Clear(_waveform);
        Array.Clear(_spectrum);
        Array.Clear(_waveformLeft);
        Array.Clear(_waveformRight);
        _alignerLeft.Reset();
        _alignerRight.Reset();
        Array.Clear(_spectrumLeft);
        Array.Clear(_spectrumRight);
        _bass.Reset();
        _mid.Reset();
        _treble.Reset();
        _analysisFrame = 0;
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

    /// <summary>Decimates the FFT magnitudes into the spectrum buffer a custom waveform reads.</summary>
    private void UpdateSpectrum()
    {
        var bins = Math.Max(1, _bandMagnitudes.Length);
        var step = Math.Max(1, bins / SpectrumPoints);
        for (var point = 0; point < SpectrumPoints; point++)
        {
            var index = Math.Min(bins - 1, point * step);
            // The bands scale the magnitudes the same way, so a spectrum-reading waveform sees the
            // same zero-to-one range the band levels use.
            _spectrum[point] = Math.Clamp(_bandMagnitudes[index] * 8f, 0f, 1f);
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
