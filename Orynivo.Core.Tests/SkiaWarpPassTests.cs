using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the GPU warp pass against the interpreter. The pass carries the frame through eight-bit
/// textures, so the two pictures differ by at most a level; these tests keep them from drifting and
/// cover the per-pixel expression block, the warp shader, and the two together.
/// </summary>
public sealed class SkiaWarpPassTests
{
    /// <summary>The warp pass reproduces the CPU warp when a per-pixel block moves the sample.</summary>
    [Fact]
    public void WarpPass_MatchesTheCpuWarpWithAPerPixelBlock()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var perPixel = PresetCompiler.Compile("x = (x * 0.5) + (rad * 0.2); y = y * 0.5;", layout);
        var parameters = new SkiaShaderRunner.WarpParameters(1.3f, 1f, 0.4f, 0.1f, -0.2f, 0.05f, -0.03f, 1.1f, 0.9f);

        var previous = CreatePattern();
        var gpu = new PixelBuffer(previous.Width, previous.Height);
        var pass = SkiaShaderRunner.WarpPass.TryCreate(null, perPixel, new VisualizerTextureBank(), out _);
        Assert.NotNull(pass);
        try
        {
            pass!.Render(
                previous,
                gpu,
                parameters,
                new Dictionary<string, float>(StringComparer.Ordinal),
                new Dictionary<string, float[]>(StringComparer.Ordinal));
        }
        finally
        {
            pass!.Dispose();
        }

        var cpu = new PixelBuffer(previous.Width, previous.Height);
        CpuWarpWithPerPixel(previous, cpu, parameters, perPixel);

        var difference = MeanAbsoluteDifference(cpu.Pixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>A per-pixel temporary written before it is read matches the interpreter.</summary>
    [Fact]
    public void WarpPass_MatchesTheCpuWarpWithATemporaryVariable()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var perPixel = PresetCompiler.Compile("num = x; x = num * 0.5; y = y * 0.5;", layout);
        var parameters = new SkiaShaderRunner.WarpParameters(1.1f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f);

        var previous = CreatePattern();
        var gpu = new PixelBuffer(previous.Width, previous.Height);
        var pass = SkiaShaderRunner.WarpPass.TryCreate(null, perPixel, new VisualizerTextureBank(), out _);
        Assert.NotNull(pass);
        try
        {
            pass!.Render(
                previous,
                gpu,
                parameters,
                new Dictionary<string, float>(StringComparer.Ordinal),
                new Dictionary<string, float[]>(StringComparer.Ordinal));
        }
        finally
        {
            pass!.Dispose();
        }

        var cpu = new PixelBuffer(previous.Width, previous.Height);
        CpuWarpWithPerPixel(previous, cpu, parameters, perPixel);

        var difference = MeanAbsoluteDifference(cpu.Pixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>The shared q and t values reach the shader on both paths.</summary>
    [Fact]
    public void RenderFrame_SeedsTheSharedQAndTVariables()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "per_frame_1=q1 = 0.5; t1 = 0.25;\n" +
            "warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(q1, t1, 0.0); }";

        var cpu = Render(Preset, skia: false, frames: 1);
        var gpu = Render(Preset, skia: true, frames: 1);

        // Both paths read the per-frame values, so the picture is the shader's output rather than
        // the zero the interpreter used to bind.
        Assert.Contains(cpu, value => value > 0.1f);
        Assert.Contains(gpu, value => value > 0.1f);
        var difference = MeanAbsoluteDifference(cpu, gpu);
        Assert.InRange(difference, 0.0001f, 0.02f);
    }

    /// <summary>The first frame reports a finite fps, so a preset cannot accumulate an infinity.</summary>
    [Fact]
    public void RenderFrame_FirstFrameFpsIsFinite()
    {
        var renderer = new PresetRenderer(
            VisualizerPreset.Parse("fDecay=1\nwave_a=0\nper_frame_1=accumulator = accumulator + (1 / fps);"),
            8,
            8);
        try
        {
            renderer.RenderFrame(new Silent(), 1d / 60d);
            Assert.True(float.IsFinite(renderer.ReadVariable("accumulator")));
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>A warp shader that reads the reciprocal aspect matches the interpreter.</summary>
    [Fact]
    public void RenderFrame_SkiaWarpShaderAspectMatchesTheInterpreter()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv * aspect.zw, 0.5); }";

        var difference = CompareRendererPaths(Preset, frames: 2);
        Assert.InRange(difference, 0.0001f, 0.02f);
    }

    /// <summary>A warp shader that samples a blurred frame matches the interpreter.</summary>
    [Fact]
    public void RenderFrame_SkiaWarpShaderBlurMatchesTheInterpreter()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5) * 0.5 + GetBlur1(uv) * 0.5; }";

        var difference = CompareRendererPaths(Preset, frames: 3);
        Assert.InRange(difference, 0.0001f, 0.02f);
    }

    /// <summary>A warp shader runs on the Skia path and matches the interpreter.</summary>
    [Fact]
    public void RenderFrame_SkiaWarpShaderMatchesTheInterpreter()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5) + tex2D(sampler_main, uv).rgb * 0.5; }";

        var difference = CompareRendererPaths(Preset, frames: 3);
        // The feedback frame is resampled every frame, so the eight-bit quantisation accumulates a
        // little faster here than for a single comp pass; the bound still catches a wrong picture.
        Assert.InRange(difference, 0.0001f, 0.02f);
    }

    /// <summary>A warp shader and the per-pixel block run together on the Skia path.</summary>
    [Fact]
    public void RenderFrame_SkiaWarpShaderWithPerPixelMatchesTheInterpreter()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "per_pixel_1=x = x * 0.5; y = y * 0.5;\n" +
            "warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5); }";

        var difference = CompareRendererPaths(Preset, frames: 2);
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>Renders a preset with and without the Skia passes and returns their mean difference.</summary>
    /// <param name="preset">Preset source.</param>
    /// <param name="frames">Frames to render, so a feedback picture exists.</param>
    /// <returns>The mean absolute difference per channel.</returns>
    private static float CompareRendererPaths(string preset, int frames)
    {
        var cpu = Render(preset, skia: false, frames);
        var gpu = Render(preset, skia: true, frames);
        return MeanAbsoluteDifference(cpu, gpu);
    }

    /// <summary>Renders a preset and returns its last frame.</summary>
    /// <param name="preset">Preset source.</param>
    /// <param name="skia">Whether the Skia passes are enabled.</param>
    /// <param name="frames">Frames to render.</param>
    /// <returns>The last frame's pixels.</returns>
    private static float[] Render(string preset, bool skia, int frames)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(preset), 32, 18)
        {
            ShaderTimeBudgetMilliseconds = 100_000d,
            UseSkiaPasses = skia
        };
        try
        {
            for (var frame = 0; frame < frames; frame++)
                renderer.RenderFrame(new Silent(), 1d / 60d);
            return renderer.Output.Pixels.ToArray();
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>The CPU warp with a per-pixel block, mirroring <c>PresetRenderer.WarpRows</c>.</summary>
    /// <param name="previous">Frame to sample.</param>
    /// <param name="target">Frame to write.</param>
    /// <param name="parameters">Motion parameters.</param>
    /// <param name="perPixel">Per-pixel program to run.</param>
    private static void CpuWarpWithPerPixel(
        PixelBuffer previous,
        PixelBuffer target,
        SkiaShaderRunner.WarpParameters parameters,
        PresetProgram perPixel)
    {
        var layout = perPixel.Layout;
        var slots = new float[layout.Count];
        var slotX = layout.IndexOf("x");
        var slotY = layout.IndexOf("y");
        var slotRad = layout.IndexOf("rad");
        var slotAng = layout.IndexOf("ang");
        var width = target.Width;
        var height = target.Height;
        var aspectX = width / (float)height;
        const float aspectY = 1f;
        var sample = new float[4];
        for (var y = 0; y < height; y++)
        {
            var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
            for (var x = 0; x < width; x++)
            {
                var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                WarpSampling.SamplePosition(
                    normalizedX,
                    normalizedY,
                    parameters.Zoom,
                    parameters.ZoomExp,
                    parameters.Rotation,
                    parameters.CentreX,
                    parameters.CentreY,
                    parameters.OffsetX,
                    parameters.OffsetY,
                    parameters.StretchX,
                    parameters.StretchY,
                    aspectX,
                    aspectY,
                    true,
                    out var sampleX,
                    out var sampleY);
                if (slotRad >= 0)
                    slots[slotRad] = MathF.Sqrt(
                        ((normalizedX * aspectX) * (normalizedX * aspectX)) + ((normalizedY * aspectY) * (normalizedY * aspectY)));
                if (slotAng >= 0)
                    slots[slotAng] = MathF.Atan2(normalizedY * aspectY, normalizedX * aspectX);
                slots[slotX] = sampleX;
                slots[slotY] = sampleY;
                perPixel.Execute(slots);
                sampleX = slots[slotX];
                sampleY = slots[slotY];

                previous.SampleBilinear((sampleX * 0.5f) + 0.5f, (sampleY * 0.5f) + 0.5f, sample);
                var offset = (((y * width) + x) * 4);
                target.Pixels[offset] = sample[0];
                target.Pixels[offset + 1] = sample[1];
                target.Pixels[offset + 2] = sample[2];
                target.Pixels[offset + 3] = sample[3];
            }
        }
    }

    /// <summary>Builds a frame with structure, so the warp visibly changes it.</summary>
    /// <returns>The frame.</returns>
    private static PixelBuffer CreatePattern()
    {
        var buffer = new PixelBuffer(32, 18);
        var pixels = buffer.Pixels;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var offset = ((y * buffer.Width) + x) * 4;
                var value = (((x * 7) + (y * 13)) % 17) / 16f;
                pixels[offset] = value;
                pixels[offset + 1] = 1f - value;
                pixels[offset + 2] = value * 0.5f;
                pixels[offset + 3] = 1f;
            }
        }

        return buffer;
    }

    /// <summary>Computes the mean absolute difference of two frames.</summary>
    /// <param name="left">First frame.</param>
    /// <param name="right">Second frame.</param>
    /// <returns>The mean difference per channel.</returns>
    private static float MeanAbsoluteDifference(float[] left, float[] right)
    {
        var total = 0f;
        var count = 0;
        for (var index = 0; index < Math.Min(left.Length, right.Length); index += 4)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                total += Math.Abs(left[index + channel] - right[index + channel]);
                count++;
            }
        }

        return total / Math.Max(1, count);
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
