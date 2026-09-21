using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Milkdrop 2 shader storage: a shader is written one source line per numbered key,
/// each line carrying a backtick marker, and the body marker only says where it starts. Reading
/// only the first key, which is the body marker, is what used to leave real preset collections
/// without any shader at all.
/// </summary>
public sealed class PresetShaderDialectTests
{
    private const string LineBasedPreset = """
        PSVERSION_COMP=2
        comp_1=`shader_body
        comp_2=`{
        comp_3=`    float3 col = tex2D(sampler_main, uv).rgb;
        comp_4=`    return float4(col * saturate(0.5 + bass), 1.0);
        comp_5=`}
        """;

    /// <summary>The lines are joined into one shader and the markers are removed.</summary>
    [Fact]
    public void Parse_JoinsTheLineBasedShader()
    {
        var preset = VisualizerPreset.Parse(LineBasedPreset);

        var shader = Assert.Single(preset.CompShaders);
        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(1, shader.Index);
    }

    /// <summary>The joined shader renders its own picture.</summary>
    [Fact]
    public void RenderFrame_RunsTheLineBasedShader()
    {
        var preset = VisualizerPreset.Parse("fDecay=1\nwave_a=0\n" + LineBasedPreset);
        var renderer = new PresetRenderer(preset, 8, 8)
        {
            ShaderTimeBudgetMilliseconds = 100_000d
        };

        renderer.RenderFrame(new Silent(), 1d / 60d);

        Assert.False(renderer.ShadersSkipped);
        Assert.True(renderer.LastShaderMilliseconds > 0d);
    }

    /// <summary>A define ends at its line comment, so the macro does not swallow the call.</summary>
    [Fact]
    public void Parse_IgnoresATrailingCommentOnAShaderDefine()
    {
        var preset = VisualizerPreset.Parse("""
            PSVERSION_WARP=2
            warp_1=`shader_body
            warp_2=`{
            warp_3=`#define MyGet GetPixel //GetBlur1
            warp_4=`    float3 c = MyGet(uv);
            warp_5=`    return float4(c, 1);
            warp_6=`}
            """);

        Assert.Empty(preset.FailedBlocks);
        Assert.Single(preset.WarpShaders);
    }

    /// <summary>A preset that stores a whole shader in one key still works.</summary>
    [Fact]
    public void Parse_KeepsTheSingleKeyForm()
    {
        var preset = VisualizerPreset.Parse(
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(1, 0, 0, 1); }");

        Assert.Single(preset.CompShaders);
        Assert.Empty(preset.FailedBlocks);
    }

    /// <summary>An audio source that reports silence.</summary>
    private sealed class Silent : IVisualizerAudioSource
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
