using System.Buffers;
using System.Runtime.InteropServices;
using Orynivo.Audio;
using Orynivo.Visualization;

namespace Orynivo.Visualization;

/// <summary>
/// The single hand-off point between the audio players and the visualizer. Players push the
/// PCM block they just prepared for output; the hub converts it to float, buffers it in a
/// lock-free tap, and runs the spectrum analysis when the visualizer window asks for a frame.
/// While no visualizer is open <see cref="IsActive"/> is <see langword="false"/> and every
/// push returns immediately, so playback is unaffected.
/// </summary>
internal sealed class VisualizerAudioHub
{
    private const int AnalysisFrames = 2048;

    private readonly PcmVisualizationTap _tap = new();
    private readonly float[] _conversionBuffer = new float[AnalysisFrames * 2];
    private readonly float[] _analysisBuffer = new float[AnalysisFrames * 2];
    private readonly float[] _analysisWindow = new float[1024 * 2];
    private readonly System.Diagnostics.Stopwatch _analysisClock = new();
    private AudioSpectrumAnalyzer? _analyzer;
    private int _sampleRate;
    private int _windowFrames;
    private long _lastAudioTick;

    /// <summary>Gets the shared hub instance.</summary>
    public static VisualizerAudioHub Shared { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether a visualizer is currently consuming audio.
    /// The players check this before doing any conversion work.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Gets the number of audio frames analysed since the hub was activated.</summary>
    public long AnalyzedFrames { get; private set; }

    /// <summary>Publishes interleaved float PCM.</summary>
    /// <param name="interleavedStereo">Interleaved left/right samples.</param>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    public void PushFloats(ReadOnlySpan<float> interleavedStereo, int sampleRate)
    {
        if (!IsActive || interleavedStereo.Length < 2)
            return;

        EnsureAnalyzer(sampleRate);
        _tap.Push(interleavedStereo);
    }

    /// <summary>
    /// Publishes a PCM block in the raw format FFmpeg produced, converting it to float.
    /// </summary>
    /// <param name="bytes">Raw little-endian PCM bytes.</param>
    /// <param name="ffmpegSampleFormat">FFmpeg sample format name, for example <c>s16le</c>.</param>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    public void PushPcm(ReadOnlySpan<byte> bytes, string ffmpegSampleFormat, int sampleRate)
    {
        if (!IsActive || bytes.Length < 4)
            return;

        EnsureAnalyzer(sampleRate);
        switch (ffmpegSampleFormat)
        {
            case "f32le":
                PushFloats(MemoryMarshal.Cast<byte, float>(bytes), sampleRate);
                return;
            case "s16le":
                {
                    var samples = MemoryMarshal.Cast<byte, short>(bytes);
                    var count = Math.Min(samples.Length, _conversionBuffer.Length);
                    for (var index = 0; index < count; index++)
                        _conversionBuffer[index] = samples[index] / 32768f;
                    _tap.Push(_conversionBuffer.AsSpan(0, count));
                    return;
                }
            case "s24le":
                {
                    var frames = Math.Min(bytes.Length / 3, _conversionBuffer.Length);
                    for (var index = 0; index < frames; index++)
                    {
                        var offset = index * 3;
                        var value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
                        if ((value & 0x800000) != 0)
                            value |= unchecked((int)0xFF000000);
                        _conversionBuffer[index] = value / 8388608f;
                    }

                    _tap.Push(_conversionBuffer.AsSpan(0, frames));
                    return;
                }
            case "s32le":
                {
                    var samples = MemoryMarshal.Cast<byte, int>(bytes);
                    var count = Math.Min(samples.Length, _conversionBuffer.Length);
                    for (var index = 0; index < count; index++)
                        _conversionBuffer[index] = samples[index] / 2147483648f;
                    _tap.Push(_conversionBuffer.AsSpan(0, count));
                    return;
                }
            default:
                return;
        }
    }

    /// <summary>
    /// Analyzes the newest buffered audio and returns the spectrum the renderer reacts to.
    /// </summary>
    /// <param name="source">The analyzed spectrum, or <see langword="null"/> when nothing was buffered.</param>
    /// <returns><see langword="true"/> when a frame is available.</returns>
    public bool TryAnalyze(out IVisualizerAudioSource? source)
    {
        source = null;
        if (!IsActive || _analyzer is null)
            return false;

        var frames = _tap.ReadNewest(_analysisBuffer);
        if (frames <= 0)
        {
            // Render ticks can fall between audio writes. Reuse the last measured spectrum briefly
            // instead of injecting a silent frame between two blocks of audible playback.
            if (_windowFrames == 0 ||
                System.Diagnostics.Stopwatch.GetElapsedTime(_lastAudioTick).TotalMilliseconds > 100)
                return false;
            source = _analyzer;
            return true;
        }

        // Keep a contiguous window across render ticks. At 48 kHz/60 fps a tick may only
        // receive about 800 frames, whereas the analyzer needs the preceding samples too.
        var keep = Math.Min(frames, _analysisWindow.Length / 2);
        var existing = Math.Min(_windowFrames, (_analysisWindow.Length / 2) - keep);
        _analysisWindow.AsSpan((_windowFrames - existing) * 2, existing * 2)
            .CopyTo(_analysisWindow);
        _analysisBuffer.AsSpan((frames - keep) * 2, keep * 2)
            .CopyTo(_analysisWindow.AsSpan(existing * 2));
        _windowFrames = existing + keep;
        _lastAudioTick = System.Diagnostics.Stopwatch.GetTimestamp();

        // Milkdrop scales its loudness smoothing by the actual frame time, so the hub measures it.
        var deltaSeconds = _analysisClock.Elapsed.TotalSeconds;
        _analysisClock.Restart();
        _analyzer.Analyze(
            _analysisWindow.AsSpan(0, _windowFrames * 2),
            deltaSeconds <= 0d ? 1d / 60d : deltaSeconds);
        AnalyzedFrames += frames;
        source = _analyzer;
        return true;
    }

    /// <summary>Discards buffered audio and resets the analysis.</summary>
    public void Clear()
    {
        _tap.Clear();
        _analyzer?.Reset();
        AnalyzedFrames = 0;
        _windowFrames = 0;
        _lastAudioTick = 0;
    }

    /// <summary>Creates or replaces the analyzer when the output sample rate changes.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    private void EnsureAnalyzer(int sampleRate)
    {
        if (_analyzer is not null && _sampleRate == sampleRate)
            return;

        _sampleRate = sampleRate;
        _analyzer = new AudioSpectrumAnalyzer(Math.Max(8_000, sampleRate));
        _tap.Clear();
        _windowFrames = 0;
        _lastAudioTick = 0;
    }
}
