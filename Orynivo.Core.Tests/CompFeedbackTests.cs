using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the comp stage's feedback rule: the comp shader is the display pass, so its output is
/// what the presenter shows and the next frame warps from the pre-comp composite. Feeding the comp
/// output back lets a preset that amplifies inside the comp shader (a common "gamma" idiom such as
/// <c>ret *= 10</c>) diverge until the whole frame is white, which is exactly the failure these
/// tests pin down.
/// </summary>
public sealed class CompFeedbackTests
{
    private const int Width = 48;
    private const int Height = 27;

    /// <summary>A comp shader that writes white must paint the display white without whitening the feedback.</summary>
    [Fact]
    public void CompOutput_IsTheDisplayNotTheFeedback()
    {
        var renderer = CreateRenderer(
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(1, 1, 1, 1); }");

        for (var frame = 0; frame < 10; frame++)
            renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        // The display is the comp output.
        Assert.True(renderer.Output.MeanBrightness() > 0.9f, $"display={renderer.Output.MeanBrightness():F3}");
        // The feedback is the pre-comp composite, which the white comp output must not have reached.
        Assert.True(renderer.MeshSource.MeanBrightness() < 0.5f, $"feedback={renderer.MeshSource.MeanBrightness():F3}");
    }

    /// <summary>An amplifying comp shader must settle instead of diverging to a white feedback frame.</summary>
    [Fact]
    public void AmplifyingCompShader_DoesNotDiverge()
    {
        var renderer = CreateRenderer(
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(sampler_main, uv) * 10.0; }");

        for (var frame = 0; frame < 60; frame++)
            renderer.RenderFrame(new MovingAudio(), 1d / 60d);

        var feedback = renderer.MeshSource.MeanBrightness();
        Assert.True(float.IsFinite(feedback), "the feedback brightness is not finite");
        Assert.True(feedback < 0.9f, $"the feedback diverged to {feedback:F3}");
    }

    /// <summary>Creates a renderer whose shader budgets never abandon the comp pass.</summary>
    /// <param name="comp">The preset body, including its comp shader.</param>
    /// <returns>The renderer.</returns>
    private static PresetRenderer CreateRenderer(string comp) =>
        new(VisualizerPreset.Parse("fDecay=0.95\n" + comp), Width, Height)
        {
            ShaderTimeBudgetMilliseconds = 10_000d,
            ShaderPassBudgetMilliseconds = 10_000d
        };

    /// <summary>An audio source that reports silence, so only the preset's own feedback is measured.</summary>
    private sealed class SilentAudio : IVisualizerAudioSource
    {
        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => [];

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => [];

        /// <inheritdoc/>
        public float Bass => 0f;

        /// <inheritdoc/>
        public float Mid => 0f;

        /// <inheritdoc/>
        public float Treble => 0f;

        /// <inheritdoc/>
        public float Volume => 0f;
    }

    /// <summary>An audio source with content, so the overlay keeps adding to the pre-comp frame.</summary>
    private sealed class MovingAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.6f, 0.5f, 0.4f, 0.3f];
        private readonly float[] _waveform = new float[64];

        /// <summary>Creates the source.</summary>
        public MovingAudio() => Array.Fill(_waveform, 0.4f);

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => 0.6f;

        /// <inheritdoc/>
        public float Mid => 0.5f;

        /// <inheritdoc/>
        public float Treble => 0.4f;

        /// <inheritdoc/>
        public float Volume => 0.6f;
    }
}
