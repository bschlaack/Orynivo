using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the shader bindings of the renderer: parsing the numbered <c>warp_N</c> and
/// <c>comp_N</c> keys with their multi-line source, skipping disabled or broken shaders, running
/// a warp and a comp shader over a frame, the sampler and blur/pixel bindings, and the time
/// budget that skips the shaders instead of stalling.
/// </summary>
public sealed class VisualizerShaderTests
{
    private const string WarpShader = """
        float4 main(float2 uv : TEXCOORD0) : COLOR
        {
            return float4(0.0, 0.5, 1.0, 1.0);
        }
        """;

    /// <summary>The numbered warp and comp keys are parsed.</summary>
    [Fact]
    public void Parse_ReadsWarpAndCompShaders()
    {
        var preset = VisualizerPreset.Parse($"warp_1={WarpShader}\ncomp_1={WarpShader}");

        Assert.Single(preset.WarpShaders);
        Assert.Single(preset.CompShaders);
        Assert.Equal(1, preset.WarpShaders[0].Index);
    }

    /// <summary>A shader keeps its source lines even though the preset format is line based.</summary>
    [Fact]
    public void Parse_KeepsTheMultiLineShaderSource()
    {
        var preset = VisualizerPreset.Parse($"warp_1={WarpShader}\nname=After");

        Assert.Single(preset.WarpShaders);
        Assert.Equal("After", preset.Name);
        var body = Assert.Single(preset.WarpShaders[0].Program.Items);
        Assert.True(body.Items.Count > 0);
    }

    /// <summary>A shader switched off by its key is ignored.</summary>
    [Fact]
    public void Parse_SkipsDisabledShaders()
    {
        var preset = VisualizerPreset.Parse($"warp_1={WarpShader}\nwarp_1_enabled=0");

        Assert.Empty(preset.WarpShaders);
    }

    /// <summary>A shader that does not parse is skipped instead of rejecting the preset.</summary>
    [Fact]
    public void Parse_SkipsBrokenShaderSource()
    {
        var preset = VisualizerPreset.Parse("warp_1=this is not a shader\nwarp_2=" + WarpShader);

        Assert.Single(preset.WarpShaders);
        Assert.Equal(2, preset.WarpShaders[0].Index);
    }

    /// <summary>The optional per-frame and per-pixel blocks of a shader are parsed.</summary>
    [Fact]
    public void Parse_ReadsShaderExpressionBlocks()
    {
        var preset = VisualizerPreset.Parse(
            $"warp_1={WarpShader}\nwarp_1_per_frame_1=q1 = 2;\nwarp_1_per_pixel_1=q2 = 3;");

        Assert.False(preset.WarpShaders[0].PerFrame.IsEmpty);
        Assert.False(preset.WarpShaders[0].PerPixel.IsEmpty);
    }

    /// <summary>A warp shader decides the colour of the warped frame.</summary>
    [Fact]
    public void RenderFrame_AppliesAWarpShader()
    {
        var preset = VisualizerPreset.Parse($"decay=1\nwarp_1={WarpShader}");
        var renderer = new PresetRenderer(preset, 8, 8);

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        var pixels = renderer.Output.Pixels;
        Assert.Equal(0f, pixels[0], 3);
        Assert.Equal(0.5f, pixels[1], 3);
        Assert.Equal(1f, pixels[2], 3);
    }

    /// <summary>A comp shader decides the colour of the composited frame.</summary>
    [Fact]
    public void RenderFrame_AppliesACompShader()
    {
        var preset = VisualizerPreset.Parse("""
            decay=1
            wave_a = 0;
            comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(1, 1, 1, 1); }
            """);
        var renderer = new PresetRenderer(preset, 8, 8);

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        Assert.Equal(1f, renderer.Output.Pixels[0], 3);
        Assert.Equal(1f, renderer.Output.Pixels[1], 3);
    }

    /// <summary>sampler_main gives the warp shader the frame it is sampling.</summary>
    [Fact]
    public void RenderFrame_BindsSamplerMain()
    {
        var preset = VisualizerPreset.Parse(
            "decay=1\nwarp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(sampler_main, uv); }");
        var renderer = new PresetRenderer(preset, 8, 8);

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);
        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        Assert.True(renderer.HasShaders);
        Assert.True(renderer.LastShaderMilliseconds > 0d);
    }

    /// <summary>The blur and pixel intrinsics resolve against the frame buffers.</summary>
    [Fact]
    public void RenderFrame_UsesTheBlurAndPixelIntrinsics()
    {
        var preset = VisualizerPreset.Parse("""
            decay=1
            comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float3 blurred = GetBlur1(uv).rgb;
                float3 pixel = GetPixel(1, 1).rgb;
                return float4(blurred + pixel, 1.0);
            }
            """);
        var renderer = new PresetRenderer(preset, 8, 8);

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        Assert.True(renderer.LastShaderMilliseconds > 0d);
        Assert.Equal(1f, renderer.Output.Pixels[3], 3);
    }

    /// <summary>A shader that cannot meet the budget keeps running on a coarser grid.</summary>
    [Fact]
    public void RenderFrame_KeepsShadersOverBudgetAtACoarserGrid()
    {
        var preset = VisualizerPreset.Parse($"decay=1\nwarp_1={WarpShader}");
        var renderer = new PresetRenderer(preset, 64, 36)
        {
            ShaderTimeBudgetMilliseconds = 0d
        };

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);
        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        // Dropping the shader is what used to leave a real preset showing only the shared overlay,
        // so an over-budget shader loses resolution and keeps drawing.
        Assert.True(renderer.ShaderGridReduced);
        Assert.True(renderer.LastShaderMilliseconds > 0d);
        Assert.Equal(0.5f, renderer.Output.Pixels[1], 3);
    }

    /// <summary>A preset without shaders reports no shader work and never measures any.</summary>
    [Fact]
    public void RenderFrame_WithoutShadersReportsNoCost()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("plain", null, null), 8, 8);

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        Assert.False(renderer.HasShaders);
        Assert.Equal(0d, renderer.LastShaderMilliseconds);
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
