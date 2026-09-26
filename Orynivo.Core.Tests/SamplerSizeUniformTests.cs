using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that the shader sampler-size uniforms are bound. MilkDrop exposes the size of each
/// generated texture as <c>texsize_noise_lq</c>/<c>texsize_noise_mq</c>/<c>texsize_noise_hq</c> and
/// the volume pair, and a preset such as Royal Mashup (188) reads their <c>.zw</c> reciprocal for a
/// dither coordinate; leaving them unset collapsed that coordinate to a constant.
/// </summary>
public sealed class SamplerSizeUniformTests
{
    /// <summary>A warp shader that paints the reciprocal sizes into the frame.</summary>
    private const string SizePreset = """
        name=Size
        decay=1
        wave_a=0
        warp_1=`shader_body
        warp_2=`{
        warp_3=`  ret = float3(texsize_noise_lq.z, texsize_noise_mq.z, texsize_noisevol_lq.z);
        warp_4=`}
        """;

    /// <summary>The interpreter reads each generated texture's reciprocal size.</summary>
    [Fact]
    public void Interpreter_BindsTheGeneratedTextureSizes()
    {
        using var renderer = new PresetRenderer(VisualizerPreset.Parse(SizePreset), 32, 32)
        {
            ShaderTimeBudgetMilliseconds = 10_000d,
            ShaderPassBudgetMilliseconds = 10_000d
        };

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        Assert.Equal(1f / VisualizerTextureBank.MediumSize, renderer.Output.GetPixel(16, 16, 0), 4);
        Assert.Equal(1f / VisualizerTextureBank.MediumSize, renderer.Output.GetPixel(16, 16, 1), 4);
        Assert.Equal(1f / VisualizerTextureBank.VolumeSize, renderer.Output.GetPixel(16, 16, 2), 4);
    }

    /// <summary>The OpenGL uniform table carries the same values as four components.</summary>
    [Fact]
    public void WriteShaderUniforms_CarriesTheGeneratedTextureSizes()
    {
        using var renderer = new PresetRenderer(VisualizerPreset.Parse(SizePreset), 40, 24);
        var uniforms = new Dictionary<string, ShaderValue>(StringComparer.Ordinal);

        renderer.WriteShaderUniforms(uniforms);

        Assert.Equal(1f / VisualizerTextureBank.MediumSize, uniforms["texsize_noise_lq"].Get(2), 4);
        Assert.Equal(1f / VisualizerTextureBank.MediumSize, uniforms["texsize_noise_mq"].Get(2), 4);
        Assert.Equal(1f / VisualizerTextureBank.MediumSize, uniforms["texsize_noise_hq"].Get(2), 4);
        Assert.Equal(1f / VisualizerTextureBank.VolumeSize, uniforms["texsize_noisevol_lq"].Get(2), 4);
        Assert.Equal(40f, uniforms["texsize_main"].Get(0), 4);
        Assert.Equal(24f, uniforms["texsize_main"].Get(1), 4);
    }

    /// <summary>An audio source that reports silence.</summary>
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
}
