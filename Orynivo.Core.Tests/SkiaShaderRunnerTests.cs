using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Renders the same shader on the GPU and on the CPU interpreter and compares the pixels. The CPU
/// path is the reference, so this is what keeps the Skia translation honest: a shader that reaches
/// the GPU through <see cref="ShaderTranspiler"/> has to produce the same picture the interpreter
/// produces for the same input.
/// </summary>
public sealed class SkiaShaderRunnerTests
{
    private const int Width = 8;
    private const int Height = 8;

    /// <summary>A constant source keeps the comparison about the math, not about sampling.</summary>
    private static readonly float[] GreySource = CreateConstantSource(0.5f, 0.25f, 0.75f);

    /// <summary>The GPU and the interpreter agree on a sampled, scaled shader.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForASampledShader()
    {
        const string Source = """
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float3 c = tex2D(sampler_main, uv).rgb;
                return float4(c * 0.5 + float3(bass, mid, treb) * 0.25, 1);
            }
            """;

        AssertMatchesInterpreter(Source);
    }

    /// <summary>The GPU and the interpreter agree on a shader that writes ret and loops.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForALoopingShader()
    {
        const string Source = """
            float3 sum = 0;
            int n = 0;
            while (n < 3) {
                sum += tex2D(sampler_main, uv + float2(0.01, 0.0) * n).rgb;
                n++;
            }
            ret = sum / 3;
            """;

        AssertMatchesInterpreter(Source);
    }

    /// <summary>The GPU and the interpreter agree on the frame helpers.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForTheFrameHelpers()
    {
        const string Source = """
            float3 a = GetPixel(uv);
            float3 b = GetBlur1(uv);
            ret = lerp(a, b, saturate(bass * 2));
            """;

        AssertMatchesInterpreter(Source);
    }

    /// <summary>The volume noise behind tex3D runs on the interpreter and is deterministic.</summary>
    [Fact]
    public void Render_Tex3DUsesTheVolumeNoiseOnTheInterpreter()
    {
        var node = ShaderParser.Parse("""
            float3 n = tex3D(sampler_noisevol_hq, float3(uv * 3, time)).rgb;
            ret = n * 0.5;
            """);
        var first = RenderOnCpu(node, new Dictionary<string, float>(StringComparer.Ordinal) { ["time"] = 0.5f });
        var second = RenderOnCpu(node, new Dictionary<string, float>(StringComparer.Ordinal) { ["time"] = 0.5f });

        Assert.Equal(first, second);
        Assert.Contains(first, value => value > 0.01f);
    }

    /// <summary>A helper function is called with its arguments, not run where it is defined.</summary>
    [Fact]
    public void Render_CallsAHelperFunction()
    {
        var node = ShaderParser.Parse("""
            float3 tint (float3 c, float amount) : COLOR
            {
                return c * amount;
            }

            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                ret = tint(tex2D(sampler_main, uv).rgb, 0.5);
            }
            """);
        var interpreter = new ShaderInterpreter(node, new ConstantSampler(0.5f, 0.25f, 0.75f));
        interpreter.SetVariable("uv", ShaderValue.Vector(0.5f, 0.5f, 0f, 0f, 2));
        interpreter.Run();

        // The constant sampler returns (0.5, 0.25, 0.75), and the helper halves it.
        var ret = interpreter.Variables["ret"];
        Assert.Equal(0.25f, ret.X, 3);
        Assert.Equal(0.125f, ret.Y, 3);
        Assert.Equal(0.375f, ret.Z, 3);
    }

    /// <summary>The GPU and the interpreter agree on a shader that calls its own helper.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForAHelperFunction()
    {
        const string Source = """
            float3 tint (float3 c, float amount) : COLOR
            {
                return c * amount;
            }

            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                ret = tint(tex2D(sampler_main, uv).rgb, 0.5);
            }
            """;

        AssertMatchesInterpreter(Source);
    }

    /// <summary>Renders both ways and asserts that the pixels agree within one byte.</summary>
    /// <param name="source">HLSL source.</param>
    private static void AssertMatchesInterpreter(string source)
    {
        var node = ShaderParser.Parse(source);
        var uniforms = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["bass"] = 0.6f,
            ["mid"] = 0.4f,
            ["treb"] = 0.2f,
            ["time"] = 1f,
            ["frame"] = 2f,
            ["fps"] = 60f
        };

        var gpu = SkiaShaderRunner.Render(node, GreySource, Width, Height, uniforms);
        var cpu = RenderOnCpu(node, uniforms);

        var worst = 0f;
        for (var index = 0; index < gpu.Length; index += 4)
        {
            for (var channel = 0; channel < 3; channel++)
                worst = Math.Max(worst, Math.Abs(gpu[index + channel] - cpu[index + channel]));
        }

        Assert.True(worst <= 1f / 255f, $"GPU and CPU differ by {worst * 255f:F1} bytes.\n{source}");
    }

    /// <summary>Renders the shader through the CPU interpreter over the same source.</summary>
    /// <param name="node">Parsed shader.</param>
    /// <param name="uniforms">Scalar variables to seed.</param>
    /// <returns>The frame components.</returns>
    private static float[] RenderOnCpu(ShaderNode node, IReadOnlyDictionary<string, float> uniforms)
    {
        var sampler = new ConstantSampler(0.5f, 0.25f, 0.75f);
        var pixels = new float[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var interpreter = new ShaderInterpreter(node, sampler);
                foreach (var (name, value) in uniforms)
                    interpreter.SetVariable(name, value);

                var u = (x + 0.5f) / Width;
                var v = (y + 0.5f) / Height;
                interpreter.SetVariable("uv", ShaderValue.Vector(u, v, 0f, 0f, 2));
                interpreter.SetVariable("uv_orig", ShaderValue.Vector(u, v, 0f, 0f, 2));
                var centredX = (u * 2f) - 1f;
                var centredY = (v * 2f) - 1f;
                interpreter.SetVariable("rad", MathF.Sqrt((centredX * centredX) + (centredY * centredY)));
                interpreter.SetVariable("ang", MathF.Atan2(centredY, centredX));

                var colour = interpreter.Run();
                if (!interpreter.ReturnedValue && interpreter.Variables.TryGetValue("ret", out var written))
                    colour = written;

                var offset = ((y * Width) + x) * 4;
                pixels[offset] = colour.X;
                pixels[offset + 1] = colour.Y;
                pixels[offset + 2] = colour.Z;
                pixels[offset + 3] = 1f;
            }
        }

        return pixels;
    }

    /// <summary>Builds a source frame of one constant colour.</summary>
    /// <param name="red">Red component.</param>
    /// <param name="green">Green component.</param>
    /// <param name="blue">Blue component.</param>
    /// <returns>The frame components.</returns>
    private static float[] CreateConstantSource(float red, float green, float blue)
    {
        var pixels = new float[Width * Height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = red;
            pixels[index + 1] = green;
            pixels[index + 2] = blue;
            pixels[index + 3] = 1f;
        }

        return pixels;
    }

    /// <summary>A sampler that returns one constant colour for every coordinate.</summary>
    private sealed class ConstantSampler : IShaderSampler
    {
        private readonly ShaderValue _colour;

        /// <summary>Creates the sampler.</summary>
        /// <param name="red">Red component.</param>
        /// <param name="green">Green component.</param>
        /// <param name="blue">Blue component.</param>
        public ConstantSampler(float red, float green, float blue) =>
            _colour = ShaderValue.Vector(red, green, blue, 1f, 4);

        /// <inheritdoc/>
        public ShaderValue Sample(string sampler, float u, float v) => _colour;

        /// <inheritdoc/>
        public ShaderValue SampleBlur(int level, float u, float v) => _colour;

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y) => _colour;
    }
}
