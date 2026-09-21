using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Guards the render cost profile of phase 39a over the built-in presets. Every preset is
/// measured at two resolutions and the numbers are written to the test output, so the profile
/// is documented where it is measured. The assertion is a ratio (four times the pixels must
/// cost more), not an absolute duration, so the test stays meaningful on any machine. The same
/// numbers reach the user through the visualizer's diagnostic line.
/// </summary>
public sealed class RenderTimingDiagnosticTests
{
    private const int Frames = 5;
    private const int LowWidth = 480;
    private const int LowHeight = 270;
    private const int HighWidth = 960;
    private const int HighHeight = 540;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic test.</summary>
    /// <param name="output">Test output writer.</param>
    public RenderTimingDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Every built-in preset costs more at four times the pixel count.</summary>
    [Fact]
    public void Measure_BuiltInPresetsScaleWithThePixelCount()
    {
        _output.WriteLine("preset                 size        total    warp    blur    post overlay   comp");
        foreach (var preset in VisualizerPresets.BuiltIn)
        {
            var low = Measure(preset, LowWidth, LowHeight);
            var high = Measure(preset, HighWidth, HighHeight);
            _output.WriteLine(Format(preset.Name, LowWidth, LowHeight, low));
            _output.WriteLine(Format(preset.Name, HighWidth, HighHeight, high));

            Assert.True(low.Total > 0d);
            Assert.True(low.Measured <= low.Total + 0.5d);
            Assert.True(high.Measured <= high.Total + 0.5d);
            Assert.True(high.Total > low.Total, $"{preset.Name}: {high.Total:F2} ms is not above {low.Total:F2} ms");
        }
    }

    /// <summary>Measures one preset, skipping a warm-up frame so JIT cost stays out of the average.</summary>
    /// <param name="preset">Preset to measure.</param>
    /// <param name="width">Render width.</param>
    /// <param name="height">Render height.</param>
    /// <returns>The averaged stage timings.</returns>
    private static RenderTimings Measure(VisualizerPreset preset, int width, int height)
    {
        var renderer = new PresetRenderer(preset, width, height);
        var audio = new TimingAudio();
        renderer.RenderFrame(audio, 1d / 60d);
        renderer.ResetTimings();
        for (var frame = 0; frame < Frames; frame++)
            renderer.RenderFrame(audio, 1d / 60d);

        return renderer.AverageTimings;
    }

    private static string Format(string name, int width, int height, RenderTimings timings) =>
        $"{name,-20} {width}x{height,-5} {timings.Total,7:F2} {timings.Warp,7:F2} {timings.Blur,7:F2} "
        + $"{timings.PostProcess,7:F2} {timings.Overlay,7:F2} {timings.Shader,7:F2}";

    /// <summary>An audio source with content on every band and a waveform.</summary>
    private sealed class TimingAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.7f, 0.55f, 0.4f, 0.25f, 0.15f, 0.1f];
        private readonly float[] _waveform = Enumerable.Range(0, 256)
            .Select(index => MathF.Sin(index * 0.13f) * 0.6f)
            .ToArray();

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => 0.7f;

        /// <inheritdoc/>
        public float Mid => 0.55f;

        /// <inheritdoc/>
        public float Treble => 0.4f;

        /// <inheritdoc/>
        public float Volume => 0.6f;
    }
}
