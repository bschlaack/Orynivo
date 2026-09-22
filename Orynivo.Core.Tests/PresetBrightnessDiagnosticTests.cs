using System;
using System.IO;
using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Core.Tests;

/// <summary>
/// Renders one configured preset file for a few frames and reports the mean brightness and the
/// saturated share of each frame, so a preset that turns white can be located to the frame it
/// happens on instead of only to the finished picture. It does nothing unless
/// <c>ORYNIVO_PRESET_BRIGHTNESS_FILE</c> points at a preset, because a render is not a test
/// fixture. Run it with <c>--logger "console;verbosity=detailed"</c> to read the report.
/// </summary>
public sealed class PresetBrightnessDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic.</summary>
    /// <param name="output">Test output writer.</param>
    public PresetBrightnessDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Renders the configured preset and reports the per-frame brightness.</summary>
    [Fact]
    public void Report_ConfiguredPresetBrightnessPerFrame()
    {
        var file = Environment.GetEnvironmentVariable("ORYNIVO_PRESET_BRIGHTNESS_FILE");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            _output.WriteLine("no preset configured; nothing to report");
            return;
        }

        var width = ReadInt("ORYNIVO_PRESET_BRIGHTNESS_WIDTH", 160);
        var height = ReadInt("ORYNIVO_PRESET_BRIGHTNESS_HEIGHT", 90);
        var frames = ReadInt("ORYNIVO_PRESET_BRIGHTNESS_FRAMES", 12);

        var preset = VisualizerPreset.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
        var renderer = new PresetRenderer(preset, width, height) { UseSkiaPasses = true };
        var audio = new ConstantAudio();
        _output.WriteLine(
            $"preset '{preset.Name}' {width}x{height} shaders=warp{preset.WarpShaders.Count}/" +
            $"comp{preset.CompShaders.Count} failedBlocks={preset.FailedBlocks.Count}");
        for (var frame = 0; frame < frames; frame++)
        {
            renderer.RenderFrame(audio, 1d / 60d);
            var pixels = renderer.Output.Pixels;
            _output.WriteLine(
                $"frame={frame} brightness={MeanBrightness(pixels):F4} " +
                $"saturated={SaturatedShare(pixels):P1} " +
                $"shaderError=[{renderer.ShaderError ?? "none"}] gridReduced={renderer.ShaderGridReduced}");
        }
    }

    /// <summary>Reads an integer environment variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="fallback">Value used when the variable is unset or unusable.</param>
    /// <returns>The value.</returns>
    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    /// <summary>Measures the mean RGB brightness of a float RGBA frame.</summary>
    /// <param name="pixels">Frame samples.</param>
    /// <returns>The mean channel value.</returns>
    private static float MeanBrightness(ReadOnlySpan<float> pixels)
    {
        var total = 0f;
        var samples = 0;
        for (var index = 0; index + 2 < pixels.Length; index += 64)
        {
            total += pixels[index] + pixels[index + 1] + pixels[index + 2];
            samples += 3;
        }

        return samples == 0 ? 0f : total / samples;
    }

    /// <summary>Measures the share of sampled pixels whose RGB channels are all saturated.</summary>
    /// <param name="pixels">Frame samples.</param>
    /// <returns>The saturated share.</returns>
    private static float SaturatedShare(ReadOnlySpan<float> pixels)
    {
        var saturated = 0;
        var sampled = 0;
        for (var index = 0; index + 2 < pixels.Length; index += 64)
        {
            sampled++;
            if (pixels[index] >= 0.99f && pixels[index + 1] >= 0.99f && pixels[index + 2] >= 0.99f)
                saturated++;
        }

        return sampled == 0 ? 0f : saturated / (float)sampled;
    }

    /// <summary>A source with fixed levels and a constant waveform, so two runs agree.</summary>
    private sealed class ConstantAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.5f, 0.5f, 0.5f, 0.5f];
        private readonly float[] _waveform = new float[64];

        /// <summary>Creates the source.</summary>
        public ConstantAudio() => Array.Fill(_waveform, 0.5f);

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Volume => 0.5f;

        /// <inheritdoc/>
        public float Bass => 0.5f;

        /// <inheritdoc/>
        public float Mid => 0.5f;

        /// <inheritdoc/>
        public float Treble => 0.5f;
    }
}
