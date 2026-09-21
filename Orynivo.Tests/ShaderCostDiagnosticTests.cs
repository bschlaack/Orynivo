using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Measures what a per-pixel shader costs on the CPU interpreter, which is the number that decides
/// how much the shader JIT of phase 39e has to win. It reports only; it asserts nothing about
/// speed, because absolute durations are not a test.
/// </summary>
public sealed class ShaderCostDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic test.</summary>
    /// <param name="output">Test output writer.</param>
    public ShaderCostDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Reports the cost of a comp shader and of the same frame without one.</summary>
    [Fact]
    public void Measure_CompShaderCost()
    {
        const string shader = """
            comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float2 offset = uv - 0.5;
                float r = length(offset);
                float3 col = tex2D(sampler_main, uv).rgb;
                col = lerp(col, float3(1, 0.5, 0.2), saturate(1 - r * 2));
                return float4(col * saturate(0.8 + bass), 1.0);
            }
            """;
        (int Width, int Height)[] sizes = [(320, 180), (640, 360), (1280, 720)];
        var budget = new PresetRenderer(VisualizerPreset.Create("probe", null, null)).ShaderTimeBudgetMilliseconds;
        _output.WriteLine($"size        plain ms   comp ms   per pixel ns   would run in the {budget:F0} ms budget");
        foreach (var size in sizes)
        {
            var plain = Measure("fDecay=0.95\nwave_a=0;", size.Width, size.Height);
            var shaded = Measure("fDecay=0.95\nwave_a=0;\n" + shader, size.Width, size.Height);
            var pixels = (long)size.Width * size.Height;
            var perPixel = (shaded - plain) * 1_000_000d / pixels;
            _output.WriteLine(
                $"{size.Width}x{size.Height,-6} {plain,7:F2}   {shaded,7:F2}   {perPixel,12:F1}   "
                + (shaded <= budget ? "yes" : "no"));
        }
    }

    /// <summary>Measures the average frame time of a preset at one size.</summary>
    /// <param name="presetText">Preset to render.</param>
    /// <param name="width">Render width.</param>
    /// <param name="height">Render height.</param>
    /// <returns>The average frame time in milliseconds.</returns>
    private static double Measure(string presetText, int width, int height)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(presetText), width, height)
        {
            // Keep the shaders running: the measurement is worthless when the budget skips them.
            ShaderTimeBudgetMilliseconds = 100_000d
        };
        var audio = new ShaderAudio();
        renderer.RenderFrame(audio, 1d / 60d);
        renderer.ResetTimings();
        for (var frame = 0; frame < 3; frame++)
            renderer.RenderFrame(audio, 1d / 60d);

        return renderer.AverageTimings.Total;
    }

    /// <summary>An audio source with content on every band.</summary>
    private sealed class ShaderAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.7f, 0.55f, 0.4f, 0.25f];

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => [];

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
