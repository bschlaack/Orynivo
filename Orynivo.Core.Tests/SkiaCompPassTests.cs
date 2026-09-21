using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Skia comp pass against the interpreter. The pass carries the frame through eight-bit
/// textures, so the two pictures differ by at most a level or two; the test keeps them from drifting
/// further and covers the sampler bindings a comp shader reads.
/// </summary>
public sealed class SkiaCompPassTests
{
    /// <summary>A comp shader with no sampler renders the same gradient on both paths.</summary>
    [Fact]
    public void RenderFrame_SkiaCompPassMatchesTheInterpreter()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5); }";

        var cpu = Render(Preset, skia: false);
        var gpu = Render(Preset, skia: true);

        // A non-zero difference proves the Skia pass actually ran; the bound keeps it to the
        // eight-bit quantisation of the Skia surface instead of a wrong picture.
        var difference = MeanAbsoluteDifference(cpu, gpu);
        Assert.InRange(difference, 0.0002f, 0.002f);
        Assert.Contains(gpu, value => value > 0.1f);
    }

    /// <summary>A comp shader with a per-pixel block runs on the Skia path and matches the interpreter.</summary>
    [Fact]
    public void RenderFrame_SkiaCompPassMatchesTheInterpreterWithAPerPixelBlock()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "comp_1_per_pixel=num = rad * 0.5; q1 = num + bass;\n" +
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5); }";

        var cpu = Render(Preset, skia: false);
        var gpu = Render(Preset, skia: true);

        // A non-zero difference proves the Skia pass actually ran with the block; the bound keeps it
        // to the eight-bit quantisation instead of a wrong picture.
        var difference = MeanAbsoluteDifference(cpu, gpu);
        Assert.InRange(difference, 0.0002f, 0.002f);
        Assert.Contains(gpu, value => value > 0.1f);
    }

    /// <summary>The Skia pass resolves the noise sampler the way the interpreter does.</summary>
    [Fact]
    public void RenderFrame_SkiaCompPassResolvesTheNoiseSampler()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(sampler_noise_lq, uv * 3); }";

        var cpu = Render(Preset, skia: false);
        var gpu = Render(Preset, skia: true);

        // The noise texture itself is quantised to eight bits on the Skia path, so the two pictures
        // may differ by a couple of levels while still describing the same texture.
        Assert.True(MeanAbsoluteDifference(cpu, gpu) < 0.004f);
        Assert.Contains(gpu, value => value > 0.05f);
    }

    /// <summary>The Skia box blur matches the CPU one within the eight-bit quantisation.</summary>
    [Fact]
    public void BlurFrame_MatchesTheCpuBlur()
    {
        var gpu = CreatePattern();
        var cpu = CreatePattern();
        SkiaShaderRunner.BlurFrame(gpu, 2);
        cpu.Blur();
        cpu.Blur();

        var difference = MeanAbsoluteDifference(cpu.Pixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>The Skia video echo matches the CPU echo within the eight-bit quantisation.</summary>
    [Fact]
    public void VideoEcho_MatchesTheCpuEcho()
    {
        var source = CreatePattern();
        var gpu = new PixelBuffer(source.Width, source.Height);
        gpu.CopyFrom(source);
        SkiaShaderRunner.VideoEcho(gpu, 0.5f, 1.5f, 0);

        var cpu = new PixelBuffer(source.Width, source.Height);
        cpu.CopyFrom(source);
        CpuVideoEcho(cpu, 0.5f, 1.5f, 0);

        var difference = MeanAbsoluteDifference(cpu.Pixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>The CPU video echo, mirroring <c>PresetRenderer.ApplyVideoEcho</c>.</summary>
    /// <param name="buffer">Frame to modify in place.</param>
    /// <param name="alpha">Echo blend amount.</param>
    /// <param name="zoom">Echo zoom.</param>
    /// <param name="orientation">Echo orientation.</param>
    private static void CpuVideoEcho(PixelBuffer buffer, float alpha, float zoom, int orientation)
    {
        var width = buffer.Width;
        var height = buffer.Height;
        var copy = new PixelBuffer(width, height);
        copy.CopyFrom(buffer);
        var source = copy.Pixels;
        var target = buffer.Pixels;
        var sample = new float[4];
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) / height;
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width;
                var sampleU = ((u - 0.5f) / zoom) + 0.5f;
                var sampleV = ((v - 0.5f) / zoom) + 0.5f;
                if (orientation is 1 or 3)
                    sampleU = 1f - sampleU;
                if (orientation is 2 or 3)
                    sampleV = 1f - sampleV;
                if (sampleU < 0f || sampleU > 1f || sampleV < 0f || sampleV > 1f)
                    continue;

                copy.SampleBilinear(sampleU, sampleV, sample);
                var offset = (((y * width) + x) * 4);
                target[offset] = Math.Clamp((target[offset] * (1f - alpha)) + (sample[0] * alpha), 0f, 1f);
                target[offset + 1] = Math.Clamp((target[offset + 1] * (1f - alpha)) + (sample[1] * alpha), 0f, 1f);
                target[offset + 2] = Math.Clamp((target[offset + 2] * (1f - alpha)) + (sample[2] * alpha), 0f, 1f);
            }
        }
    }

    /// <summary>The Skia composite matches the CPU additive composite within one level.</summary>
    [Fact]
    public void Composite_MatchesTheCpuComposite()
    {
        var baseFrame = CreatePattern();
        var overlay = CreatePattern(5);
        var gpu = new PixelBuffer(baseFrame.Width, baseFrame.Height);
        gpu.CopyFrom(baseFrame);
        SkiaShaderRunner.Composite(gpu, overlay);

        var cpu = new PixelBuffer(baseFrame.Width, baseFrame.Height);
        cpu.CopyFrom(baseFrame);
        var cpuPixels = cpu.Pixels;
        var overlayPixels = overlay.Pixels;
        for (var index = 0; index < cpuPixels.Length; index += 4)
        {
            cpuPixels[index] = Math.Clamp(cpuPixels[index] + overlayPixels[index], 0f, 1f);
            cpuPixels[index + 1] = Math.Clamp(cpuPixels[index + 1] + overlayPixels[index + 1], 0f, 1f);
            cpuPixels[index + 2] = Math.Clamp(cpuPixels[index + 2] + overlayPixels[index + 2], 0f, 1f);
        }

        var difference = MeanAbsoluteDifference(cpuPixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.005f);
    }

    /// <summary>The Skia border pass matches the CPU border pass within one level.</summary>
    [Fact]
    public void Borders_MatchesTheCpuBorders()
    {
        var source = CreatePattern();
        var outer = new SkiaShaderRunner.BorderBand(0f, 0.06f, 1f, 0.5f, 0f, 0.7f);
        var inner = new SkiaShaderRunner.BorderBand(0.1f, 0.05f, 0f, 0.2f, 1f, 0.4f);

        var gpu = new PixelBuffer(source.Width, source.Height);
        gpu.CopyFrom(source);
        SkiaShaderRunner.Borders(gpu, outer, inner);

        var cpu = new PixelBuffer(source.Width, source.Height);
        cpu.CopyFrom(source);
        CpuBorderBand(cpu, outer);
        CpuBorderBand(cpu, inner);

        var difference = MeanAbsoluteDifference(cpu.Pixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>Draws one border band, mirroring <c>PresetRenderer.DrawBorderFrame</c>.</summary>
    /// <param name="frame">Frame to draw on.</param>
    /// <param name="band">Band to draw.</param>
    private static void CpuBorderBand(PixelBuffer frame, SkiaShaderRunner.BorderBand band)
    {
        if (band.Alpha <= 0f)
            return;

        var width = frame.Width;
        var height = frame.Height;
        var smallest = Math.Min(width, height);
        var thickness = Math.Max(1, (int)(smallest * band.Thickness));
        var margin = (int)(smallest * band.Inset);
        var pixels = frame.Pixels;
        for (var offset = 0; offset < thickness; offset++)
        {
            var left = margin + offset;
            var top = margin + offset;
            var right = width - 1 - margin - offset;
            var bottom = height - 1 - margin - offset;
            if (left > right || top > bottom)
                break;

            for (var x = left; x <= right; x++)
            {
                Paint(pixels, width, x, top, band);
                Paint(pixels, width, x, bottom, band);
            }

            for (var y = top; y <= bottom; y++)
            {
                Paint(pixels, width, left, y, band);
                Paint(pixels, width, right, y, band);
            }
        }
    }

    /// <summary>Blends one border pixel, mirroring <c>PresetRenderer.PaintWarped</c>.</summary>
    /// <param name="pixels">Frame pixels.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="band">Band colour.</param>
    private static void Paint(Span<float> pixels, int width, int x, int y, SkiaShaderRunner.BorderBand band)
    {
        var offset = (((y * width) + x) * 4);
        pixels[offset] = Math.Clamp((pixels[offset] * (1f - band.Alpha)) + (band.Red * band.Alpha), 0f, 1f);
        pixels[offset + 1] = Math.Clamp((pixels[offset + 1] * (1f - band.Alpha)) + (band.Green * band.Alpha), 0f, 1f);
        pixels[offset + 2] = Math.Clamp((pixels[offset + 2] * (1f - band.Alpha)) + (band.Blue * band.Alpha), 0f, 1f);
    }

    /// <summary>The Skia warp runs end to end in the renderer without the interpreter.</summary>
    [Fact]
    public void RenderFrame_SkiaWarpRuns()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\nzoom=1.2\nrot=0.2\n" +
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5); }";
        var renderer = new PresetRenderer(VisualizerPreset.Parse(Preset), 32, 18)
        {
            ShaderTimeBudgetMilliseconds = 100_000d,
            UseSkiaPasses = true
        };
        try
        {
            renderer.RenderFrame(new Silent(), 1d / 60d);
            renderer.RenderFrame(new Silent(), 1d / 60d);
            Assert.Contains(renderer.Output.Pixels.ToArray(), value => value > 0.1f);
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>The Skia warp matches the CPU warp for a plain zoom, rotation, and offset.</summary>
    [Fact]
    public void Warp_MatchesTheCpuWarp()
    {
        var parameters = new SkiaShaderRunner.WarpParameters(1.3f, 1f, 0.4f, 0.1f, -0.2f, 0.05f, -0.03f, 1.1f, 0.9f);
        AssertWarpMatches(parameters);
    }

    /// <summary>The Skia warp matches the CPU warp when the zoom exponent bends the radius.</summary>
    [Fact]
    public void Warp_MatchesTheCpuWarpWithZoomExponent()
    {
        var parameters = new SkiaShaderRunner.WarpParameters(1.2f, 0.5f, -0.3f, 0f, 0f, 0f, 0f, 1f, 1f);
        AssertWarpMatches(parameters);
    }

    /// <summary>Warps a pattern on both paths and compares them.</summary>
    /// <param name="parameters">Motion parameters.</param>
    private static void AssertWarpMatches(SkiaShaderRunner.WarpParameters parameters)
    {
        var previous = CreatePattern();
        var gpu = new PixelBuffer(previous.Width, previous.Height);
        SkiaShaderRunner.Warp(previous, gpu, parameters);

        var cpu = new PixelBuffer(previous.Width, previous.Height);
        CpuWarp(previous, cpu, parameters);

        var difference = MeanAbsoluteDifference(cpu.Pixels.ToArray(), gpu.Pixels.ToArray());
        Assert.InRange(difference, 0.0001f, 0.01f);
    }

    /// <summary>The geometric warp, mirroring the CPU branch of <c>PresetRenderer.Warp</c>.</summary>
    /// <param name="previous">Frame to sample.</param>
    /// <param name="target">Frame to write.</param>
    /// <param name="parameters">Motion parameters.</param>
    private static void CpuWarp(PixelBuffer previous, PixelBuffer target, SkiaShaderRunner.WarpParameters parameters)
    {
        var width = target.Width;
        var height = target.Height;
        var cos = MathF.Cos(parameters.Rotation);
        var sin = MathF.Sin(parameters.Rotation);
        var needsRadius = parameters.ZoomExp != 1f;
        var sample = new float[4];
        for (var y = 0; y < height; y++)
        {
            var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
            for (var x = 0; x < width; x++)
            {
                var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                var warpedX = (normalizedX - parameters.CentreX) * parameters.StretchX;
                var warpedY = (normalizedY - parameters.CentreY) * parameters.StretchY;
                var rotatedX = (warpedX * cos) - (warpedY * sin);
                var rotatedY = (warpedX * sin) + (warpedY * cos);
                if (needsRadius)
                {
                    var radius = MathF.Sqrt((rotatedX * rotatedX) + (rotatedY * rotatedY));
                    var pixelZoom = MathF.Pow(parameters.Zoom, 1f + (parameters.ZoomExp * radius * 2f));
                    warpedX = (rotatedX * pixelZoom) + parameters.CentreX + parameters.OffsetX;
                    warpedY = (rotatedY * pixelZoom) + parameters.CentreY + parameters.OffsetY;
                }
                else
                {
                    warpedX = (rotatedX * parameters.Zoom) + parameters.CentreX + parameters.OffsetX;
                    warpedY = (rotatedY * parameters.Zoom) + parameters.CentreY + parameters.OffsetY;
                }

                previous.SampleBilinear((warpedX * 0.5f) + 0.5f, (warpedY * 0.5f) + 0.5f, sample);
                var offset = (((y * width) + x) * 4);
                target.Pixels[offset] = sample[0];
                target.Pixels[offset + 1] = sample[1];
                target.Pixels[offset + 2] = sample[2];
                target.Pixels[offset + 3] = sample[3];
            }
        }
    }

    /// <summary>Builds a frame with structure, so a blur visibly changes it.</summary>
    /// <param name="shift">Value that makes one pattern differ from another.</param>
    /// <returns>The frame.</returns>
    private static PixelBuffer CreatePattern(int shift = 0)
    {
        var buffer = new PixelBuffer(32, 18);
        var pixels = buffer.Pixels;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                var offset = ((y * buffer.Width) + x) * 4;
                var value = (((x + shift) * 7) + (y * 13)) % 17 / 16f;
                pixels[offset] = value;
                pixels[offset + 1] = 1f - value;
                pixels[offset + 2] = value * 0.5f;
                pixels[offset + 3] = 1f;
            }
        }

        return buffer;
    }

    /// <summary>Renders a preset with the Skia comp pass enabled or disabled.</summary>
    /// <param name="preset">Preset source.</param>
    /// <param name="skia">Whether the Skia comp pass is enabled.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] Render(string preset, bool skia)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(preset), 32, 18)
        {
            ShaderTimeBudgetMilliseconds = 100_000d,
            UseSkiaPasses = skia
        };
        try
        {
            renderer.RenderFrame(new Silent(), 1d / 60d);
            return renderer.Output.Pixels.ToArray();
        }
        finally
        {
            renderer.Dispose();
        }
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
