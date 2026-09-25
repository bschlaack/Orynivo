using Orynivo.Audio;
using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Renders every built-in preset with the same audio and reports how bright the picture
/// becomes. A preset that stays black here is broken, which is exactly the symptom a user
/// sees as an empty visualizer window.
/// </summary>
public sealed class VisualizerBrightnessDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with its output sink.</summary>
    /// <param name="output">Test output.</param>
    public VisualizerBrightnessDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Every built-in preset must light up within a second of audio.</summary>
    [Fact]
    public void EveryBuiltInPreset_BecomesVisible()
    {
        foreach (var preset in VisualizerPresets.BuiltIn)
        {
            var renderer = new PresetRenderer(preset, 160, 90);
            var audio = new TestAudio();
            var brightness = 0f;
            for (var frame = 0; frame < 60; frame++)
            {
                audio.Advance();
                renderer.RenderFrame(audio, 1d / 60d);
                brightness = Measure(renderer);
            }

            _output.WriteLine($"{preset.Name}: brightness={brightness:F3}");
            Assert.True(brightness > 0.01f, $"{preset.Name} rendered an empty picture ({brightness:F4}).");
        }
    }

    private static float Measure(PresetRenderer renderer)
    {
        var total = 0f;
        var pixels = renderer.Output.Pixels;
        for (var index = 0; index < pixels.Length; index += 4)
            total += pixels[index] + pixels[index + 1] + pixels[index + 2];
        return total / (pixels.Length / 4 * 3);
    }

    /// <summary>Audio source with a moving spectrum so the picture cannot stand still.</summary>
    private sealed class TestAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = new float[AudioSpectrumAnalyzer.BandCount];
        private readonly float[] _waveform = new float[AudioSpectrumAnalyzer.WaveformPoints];
        private readonly float[] _spectrum = new float[AudioSpectrumAnalyzer.SpectrumPoints];
        private int _phase;

        public ReadOnlySpan<float> Bands => _bands;

        public ReadOnlySpan<float> Waveform => _waveform;

        public ReadOnlySpan<float> Spectrum => _spectrum;

        public float Bass => 0.7f;

        public float Mid => 0.5f;

        public float Treble => 0.3f;

        public float Volume => 0.5f;

        /// <summary>Moves the spectrum and waveform one frame forward.</summary>
        public void Advance()
        {
            _phase++;
            for (var index = 0; index < _bands.Length; index++)
                _bands[index] = 0.5f + (0.5f * MathF.Sin((index + _phase) * 0.3f));
            for (var index = 0; index < _waveform.Length; index++)
                _waveform[index] = 0.6f * MathF.Sin((index * 0.2f) + (_phase * 0.1f));
            for (var index = 0; index < _spectrum.Length; index++)
                _spectrum[index] = 0.5f + (0.5f * MathF.Sin((index + _phase) * 0.15f));
        }
    }
}
