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

    /// <summary>A value assigned to a narrower declared type takes its leading components.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForANarrowingDeclaration()
    {
        const string Source = """
            float z = float4(0.5, 0.25, 0.75, 0.1);
            ret = float3(tan(z), z * 0.1, 0.0);
            """;

        AssertMatchesInterpreter(Source);
    }

    /// <summary>The GPU and the interpreter agree on the extended Milkdrop vocabulary.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForTheExtendedVocabulary()
    {
        const string Source = """
            float3 c = tex2D(sampler_main, uv).rgb;
            float3 a = cross(c, float3(1.0, 0.0, 0.0));
            float l = lum(c);
            float s = (asin(clamp(c.x, -1.0, 1.0)) + atan(c.y) + acos(clamp(c.z, -1.0, 1.0))) * 0.4;
            float r = (rsqrt(max(c.x, 0.01)) + log2(max(c.y, 0.01)) + exp2(c.z * 0.1)) * 0.25;
            ret = float3(l, s, r) + a * 0.1;
            """;

        AssertMatchesInterpreter(Source);
    }

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

    /// <summary>The GPU and the interpreter agree on a counted for loop.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForAForLoop()
    {
        const string Source = """
            float3 sum = 0;
            for (int n = 0; n < 3; n++) {
                sum += tex2D(sampler_main, uv + float2(0.01, 0.0) * n).rgb;
            }
            ret = sum * 0.25;
            """;

        AssertMatchesInterpreter(Source);
    }

    /// <summary>The GPU and the interpreter both read an undeclared variable as zero.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForAnUnknownIdentifier()
    {
        const string Source = """
            float3 n = float3(roam_cos.y, roam_sin.x, 0.0);
            ret = n * 0.5 + tex2D(sampler_main, uv).rgb;
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

    /// <summary>The volume behind tex3D runs on the interpreter and is deterministic.</summary>
    [Fact]
    public void Render_Tex3DUsesTheVolumeNoiseOnTheInterpreter()
    {
        var node = ShaderParser.Parse("""
            float3 n = tex3D(sampler_noisevol_hq, float3(uv * 3, time)).rgb;
            ret = n * 0.5;
            """);
        var sampler = new BankSampler(new VisualizerTextureBank(), 0.5f, 0.25f, 0.75f);
        var uniforms = new Dictionary<string, float>(StringComparer.Ordinal) { ["time"] = 0.5f };
        var first = RenderOnCpu(node, uniforms, sampler);
        var second = RenderOnCpu(node, uniforms, sampler);

        Assert.Equal(first, second);
        Assert.Contains(first, value => value > 0.01f);
    }

    /// <summary>The GPU samples the same volume atlas the interpreter samples.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForATex3DVolumeSample()
    {
        const string Source = """
            float3 n = tex3D(sampler_noisevol_hq, float3(uv * 3, time)).rgb;
            ret = n * 0.5;
            """;

        AssertMatchesInterpreter(Source, new VisualizerTextureBank());
    }

    /// <summary>A noise texture is sampled at its own size, not at the frame size.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForANoiseTexture()
    {
        const string Source = """
            float3 n = tex2D(sampler_noise_lq, uv * 3).rgb;
            ret = n * 0.5;
            """;

        AssertMatchesInterpreter(Source, new VisualizerTextureBank());
    }

    /// <summary>A frame sample uses PixelBuffer.SampleBilinear's coordinate convention.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForAFrameSample()
    {
        const string Source = """
            float3 a = tex2D(sampler_main, uv).rgb;
            ret = a * 0.5;
            """;

        var frame = CreateGradient();
        var node = ShaderParser.Parse(Source);
        var gpu = SkiaShaderRunner.Render(
            node,
            new SkiaShaderRunner.SamplerSource(frame.Pixels.ToArray(), Width, Height),
            Width,
            Height);
        var cpu = RenderOnCpu(
            node,
            new Dictionary<string, float>(StringComparer.Ordinal),
            new PixelBufferSampler(frame));

        AssertWithinOneByte(gpu, cpu, Source);
    }

    /// <summary>Builds a frame whose colour varies across the frame.</summary>
    /// <returns>The frame.</returns>
    private static PixelBuffer CreateGradient()
    {
        var buffer = new PixelBuffer(Width, Height);
        var pixels = buffer.Pixels;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * 4;
                pixels[offset] = x / (float)(Width - 1);
                pixels[offset + 1] = y / (float)(Height - 1);
                pixels[offset + 2] = 0.5f;
                pixels[offset + 3] = 1f;
            }
        }

        return buffer;
    }

    /// <summary>Each sampler reads its own frame, which is what a comp pass needs.</summary>
    [Fact]
    public void Render_MatchesTheInterpreterForSamplerSources()
    {
        const string Source = """
            float3 a = tex2D(sampler_blur1, uv).rgb;
            float3 b = tex2D(sampler_main, uv).rgb;
            ret = mix(a, b, bass);
            """;

        var node = ShaderParser.Parse(Source);
        var uniforms = new Dictionary<string, float>(StringComparer.Ordinal) { ["bass"] = 0.6f };
        var main = new SkiaShaderRunner.SamplerSource(GreySource, Width, Height);
        var blur = new SkiaShaderRunner.SamplerSource(CreateConstantSource(0.2f, 0.4f, 0.6f), Width, Height);
        var samplers = new Dictionary<string, SkiaShaderRunner.SamplerSource>(StringComparer.Ordinal)
        {
            ["sampler_main"] = main,
            ["sampler_blur1"] = blur
        };
        var colours = new Dictionary<string, ShaderValue>(StringComparer.Ordinal)
        {
            ["sampler_main"] = ShaderValue.Vector(0.5f, 0.25f, 0.75f, 1f, 4),
            ["sampler_blur1"] = ShaderValue.Vector(0.2f, 0.4f, 0.6f, 1f, 4)
        };

        var gpu = SkiaShaderRunner.Render(node, main, Width, Height, uniforms, null, samplers);
        var cpu = RenderOnCpu(node, uniforms, new NamedSampler(colours));

        AssertWithinOneByte(gpu, cpu, Source);
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
    /// <param name="textures">Texture bank to share between the two paths, or <see langword="null"/>.</param>
    private static void AssertMatchesInterpreter(string source, VisualizerTextureBank? textures = null)
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

        var gpu = SkiaShaderRunner.Render(node, GreySource, Width, Height, uniforms, textures);
        var cpu = RenderOnCpu(
            node,
            uniforms,
            textures is null ? null : new BankSampler(textures, 0.5f, 0.25f, 0.75f));

        AssertWithinOneByte(gpu, cpu, source);
    }

    /// <summary>Asserts that two rendered frames agree within one byte per channel.</summary>
    /// <param name="gpu">GPU frame.</param>
    /// <param name="cpu">CPU frame.</param>
    /// <param name="source">Source, for the failure message.</param>
    private static void AssertWithinOneByte(float[] gpu, float[] cpu, string source)
    {
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
    /// <param name="sampler">Sampler to use, or <see langword="null"/> for the constant frame.</param>
    /// <returns>The frame components.</returns>
    private static float[] RenderOnCpu(
        ShaderNode node,
        IReadOnlyDictionary<string, float> uniforms,
        IShaderSampler? sampler = null)
    {
        sampler ??= new ConstantSampler(0.5f, 0.25f, 0.75f);
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
        public ShaderValue SampleVolume(string sampler, float x, float y, float z) => _colour;

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y) => _colour;
    }

    /// <summary>
    /// A sampler that returns one constant frame colour and reads cubic volumes from a shared texture
    /// bank, mirroring how the GPU atlas is built from the same bank.
    /// </summary>
    private sealed class BankSampler : IShaderSampler
    {
        private readonly VisualizerTextureBank _bank;
        private readonly ShaderValue _colour;
        private readonly float[] _sample = new float[4];

        /// <summary>Creates the sampler.</summary>
        /// <param name="bank">Texture bank to read volumes from.</param>
        /// <param name="red">Red component of the constant frame colour.</param>
        /// <param name="green">Green component of the constant frame colour.</param>
        /// <param name="blue">Blue component of the constant frame colour.</param>
        public BankSampler(VisualizerTextureBank bank, float red, float green, float blue)
        {
            _bank = bank;
            _colour = ShaderValue.Vector(red, green, blue, 1f, 4);
        }

        /// <inheritdoc/>
        public ShaderValue Sample(string sampler, float u, float v)
        {
            if (VisualizerTextureBank.TryResolve(sampler, out var texture) &&
                !VisualizerTextureBank.IsVolume(texture))
            {
                _bank.Sample(texture, u, v, VisualizerTextureWrap.Repeat).CopyTo(_sample);
                return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
            }

            return _colour;
        }

        /// <inheritdoc/>
        public ShaderValue SampleBlur(int level, float u, float v) => _colour;

        /// <inheritdoc/>
        public ShaderValue SampleVolume(string sampler, float x, float y, float z)
        {
            if (VisualizerTextureBank.TryResolve(sampler, out var texture) &&
                VisualizerTextureBank.IsVolume(texture))
            {
                _bank.SampleVolume(texture, x, y, z, VisualizerTextureWrap.Repeat).CopyTo(_sample);
                return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
            }

            return _colour;
        }

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y) => _colour;
    }

    /// <summary>A sampler that reads a <see cref="PixelBuffer"/> the way the renderer does.</summary>
    private sealed class PixelBufferSampler : IShaderSampler
    {
        private readonly PixelBuffer _buffer;
        private readonly float[] _sample = new float[4];

        /// <summary>Creates the sampler.</summary>
        /// <param name="buffer">Frame to sample.</param>
        public PixelBufferSampler(PixelBuffer buffer) => _buffer = buffer;

        /// <inheritdoc/>
        public ShaderValue Sample(string sampler, float u, float v)
        {
            _buffer.SampleBilinear(u, v, _sample);
            return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
        }

        /// <inheritdoc/>
        public ShaderValue SampleBlur(int level, float u, float v) => Sample("sampler_blur", u, v);

        /// <inheritdoc/>
        public ShaderValue SampleVolume(string sampler, float x, float y, float z) => Sample(sampler, x, y);

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y)
        {
            var column = Math.Clamp(x, 0, _buffer.Width - 1);
            var row = Math.Clamp(y, 0, _buffer.Height - 1);
            var offset = (((row * _buffer.Width) + column) * 4);
            return ShaderValue.Vector(
                _buffer.Pixels[offset],
                _buffer.Pixels[offset + 1],
                _buffer.Pixels[offset + 2],
                _buffer.Pixels[offset + 3],
                4);
        }
    }

    /// <summary>A sampler that returns a different colour per sampler name.</summary>
    private sealed class NamedSampler : IShaderSampler
    {
        private readonly Dictionary<string, ShaderValue> _colours;

        /// <summary>Creates the sampler.</summary>
        /// <param name="colours">Colour per sampler name.</param>
        public NamedSampler(Dictionary<string, ShaderValue> colours) => _colours = colours;

        /// <inheritdoc/>
        public ShaderValue Sample(string sampler, float u, float v) =>
            _colours.TryGetValue(sampler, out var colour) ? colour : ShaderValue.Vector(0f, 0f, 0f, 1f, 4);

        /// <inheritdoc/>
        public ShaderValue SampleBlur(int level, float u, float v) => Sample($"sampler_blur{level}", u, v);

        /// <inheritdoc/>
        public ShaderValue SampleVolume(string sampler, float x, float y, float z) => Sample(sampler, x, y);

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y) => Sample("sampler_main", x, y);
    }
}
