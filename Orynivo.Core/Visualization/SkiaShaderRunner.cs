using SkiaSharp;

namespace Orynivo.Visualization;

/// <summary>
/// Runs a translated Milkdrop shader as a Skia runtime effect. It is the GPU counterpart of
/// <see cref="ShaderInterpreter"/>: both take the same parsed tree, so the same shader can be
/// rendered twice and the pixels compared, which is how the GPU path stays honest against the CPU
/// reference.
/// </summary>
public static class SkiaShaderRunner
{
    /// <summary>One frame a shader sampler reads, as RGBA components in the range zero to one.</summary>
    /// <param name="Pixels">Frame components, row by row, four floats per pixel.</param>
    /// <param name="Width">Frame width.</param>
    /// <param name="Height">Frame height.</param>
    public readonly record struct SamplerSource(float[] Pixels, int Width, int Height);

    /// <summary>
    /// Bilinear filtering for every child shader, which is how the interpreter samples. A nearest
    /// child would return a different texel between texel centres and break the comparison.
    /// </summary>
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>Renders a shader over one source frame.</summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <param name="source">Source frame as RGBA components in the range zero to one.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="uniforms">Extra scalar uniforms the shader reads, for example <c>bass</c>.</param>
    /// <param name="textures">Texture bank that supplies the noise and volume textures.</param>
    /// <returns>The rendered frame as RGBA components in the range zero to one.</returns>
    /// <exception cref="PresetExpressionException">The shader cannot be translated or Skia rejects it.</exception>
    public static float[] Render(
        ShaderNode program,
        float[] source,
        int width,
        int height,
        IReadOnlyDictionary<string, float>? uniforms = null,
        VisualizerTextureBank? textures = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Render(
            program,
            new SamplerSource(source, width, height),
            width,
            height,
            uniforms,
            textures);
    }

    /// <summary>
    /// Renders a shader as a Skia runtime effect. Each sampler may read its own frame, so a comp pass
    /// can hand the shader the composited frame, its blur levels, and the noise and volume textures the
    /// interpreter would read, which is what keeps the two paths on the same picture.
    /// </summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <param name="source">Frame the samplers fall back to.</param>
    /// <param name="width">Render width.</param>
    /// <param name="height">Render height.</param>
    /// <param name="uniforms">Extra scalar uniforms the shader reads, for example <c>bass</c>.</param>
    /// <param name="textures">Texture bank that supplies the noise and volume textures.</param>
    /// <param name="samplerSources">Frame each sampler reads, by sampler name.</param>
    /// <param name="vectorUniforms">Vector uniforms such as <c>rand_frame</c>.</param>
    /// <returns>The rendered frame as RGBA components in the range zero to one.</returns>
    /// <exception cref="PresetExpressionException">The shader cannot be translated or Skia rejects it.</exception>
    public static float[] Render(
        ShaderNode program,
        SamplerSource source,
        int width,
        int height,
        IReadOnlyDictionary<string, float>? uniforms = null,
        VisualizerTextureBank? textures = null,
        IReadOnlyDictionary<string, SamplerSource>? samplerSources = null,
        IReadOnlyDictionary<string, float[]>? vectorUniforms = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(source.Pixels);

        var sksl = ShaderTranspiler.Transpile(program, out var samplers);
        using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);

        using var sourceBitmap = CreateBitmap(source.Pixels, source.Width, source.Height);
        using var sourceShader = sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        textures ??= new VisualizerTextureBank();

        var ownedBitmaps = new List<SKBitmap>();
        var ownedShaders = new List<SKShader>();
        try
        {
            var children = BuildChildren(
                effect,
                samplers,
                sourceShader,
                textures,
                samplerSources,
                ownedBitmaps,
                ownedShaders);

            var effectUniforms = new SKRuntimeEffectUniforms(effect);

            // Skia requires every declared uniform to be set, so the whole shader vocabulary starts at
            // zero and the caller plus the frame size fill in what they know.
            foreach (var (name, count) in ShaderTranspiler.UniformComponents)
            {
                if (count == 1)
                    effectUniforms[name] = 0f;
                else
                    effectUniforms[name] = new float[count];
            }

            effectUniforms["texsize"] = TexSize(width, height);
            SetSamplerSizes(effectUniforms, width, height);
            if (uniforms is not null)
            {
                foreach (var (name, value) in uniforms)
                    effectUniforms[name] = value;
            }

            if (vectorUniforms is not null)
            {
                foreach (var (name, value) in vectorUniforms)
                    effectUniforms[name] = value;
            }

            using var shader = effect.ToShader(effectUniforms, children);
            using var target = CreateBitmap(new float[width * height * 4], width, height);
            using var surface = SKSurface.Create(target.Info, target.GetPixels(), target.RowBytes);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            using (var paint = new SKPaint { Shader = shader })
                canvas.DrawRect(new SKRect(0, 0, width, height), paint);

            canvas.Flush();
            return ReadPixels(target, width, height);
        }
        finally
        {
            foreach (var shader in ownedShaders)
                shader.Dispose();
            foreach (var bitmap in ownedBitmaps)
                bitmap.Dispose();
        }
    }

    /// <summary>Binds a child shader for every sampler the shader declares.</summary>
    /// <param name="effect">Runtime effect the children belong to.</param>
    /// <param name="samplers">Sampler names the generated SkSL declares.</param>
    /// <param name="sourceShader">Frame shader used for a sampler without its own source.</param>
    /// <param name="textures">Texture bank for the noise and volume textures.</param>
    /// <param name="samplerSources">Frame each sampler reads, by sampler name.</param>
    /// <param name="ownedBitmaps">Receives the bitmaps the caller has to dispose.</param>
    /// <param name="ownedShaders">Receives the shaders the caller has to dispose.</param>
    /// <returns>The children to bind.</returns>
    private static SKRuntimeEffectChildren BuildChildren(
        SKRuntimeEffect effect,
        IReadOnlyList<string> samplers,
        SKShader sourceShader,
        VisualizerTextureBank textures,
        IReadOnlyDictionary<string, SamplerSource>? samplerSources,
        List<SKBitmap> ownedBitmaps,
        List<SKShader> ownedShaders)
    {
        var children = new SKRuntimeEffectChildren(effect);
        foreach (var name in samplers)
        {
            SKShader child;
            if (name is "sampler_noisevol_lq" or "sampler_noisevol_hq")
            {
                child = CreateVolumeShader(
                    textures,
                    name.EndsWith("hq", StringComparison.Ordinal)
                        ? VisualizerTexture.NoiseVolumeHigh
                        : VisualizerTexture.NoiseVolumeLow);
                ownedShaders.Add(child);
            }
            else if (VisualizerTextureBank.TryResolve(name, out var texture))
            {
                // A generated noise or random texture; the interpreter samples it with repeat.
                var size = VisualizerTextureBank.GetSize(texture);
                var bitmap = CreateBitmap(textures.GetPixels(texture), size, size);
                ownedBitmaps.Add(bitmap);
                child = bitmap.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, LinearSampling);
                ownedShaders.Add(child);
            }
            else if (samplerSources is not null && samplerSources.TryGetValue(name, out var sampler))
            {
                var bitmap = CreateBitmap(sampler.Pixels, sampler.Width, sampler.Height);
                ownedBitmaps.Add(bitmap);
                child = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
                ownedShaders.Add(child);
            }
            else
            {
                child = sourceShader;
            }

            children[name] = new SKRuntimeEffectChild(child);
        }

        return children;
    }

    /// <summary>Sets the <c>texsize_*</c> uniforms to the sizes of the textures the runner binds.</summary>
    /// <param name="uniforms">Uniform block to fill.</param>
    /// <param name="width">Render width.</param>
    /// <param name="height">Render height.</param>
    private static void SetSamplerSizes(SKRuntimeEffectUniforms uniforms, int width, int height)
    {
        uniforms["texsize_main"] = TexSize(width, height);
        uniforms["texsize_fc_main"] = TexSize(width, height);
        uniforms["texsize_pc_main"] = TexSize(width, height);
        uniforms["texsize_noise_lq"] = TexSize(VisualizerTextureBank.SmallSize, VisualizerTextureBank.SmallSize);
        uniforms["texsize_noise_mq"] = TexSize(VisualizerTextureBank.MediumSize, VisualizerTextureBank.MediumSize);
        uniforms["texsize_noise_hq"] = TexSize(VisualizerTextureBank.LargeSize, VisualizerTextureBank.LargeSize);
        uniforms["texsize_noisevol_lq"] = TexSize(VisualizerTextureBank.VolumeSize, VisualizerTextureBank.VolumeSize);
        uniforms["texsize_noisevol_hq"] = TexSize(VisualizerTextureBank.VolumeSize, VisualizerTextureBank.VolumeSize);
    }

    /// <summary>Builds the texsize vector for one size.</summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <returns>The four components SkSL reads.</returns>
    private static float[] TexSize(int width, int height) =>
        [width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height)];

    /// <summary>Creates an eight-bit bitmap from a frame of zero-to-one components.</summary>
    /// <param name="pixels">Frame components.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns>The bitmap.</returns>
    private static SKBitmap CreateBitmap(ReadOnlySpan<float> pixels, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var bytes = bitmap.GetPixelSpan();
        var count = Math.Min(pixels.Length / 4, width * height);
        for (var pixel = 0; pixel < count; pixel++)
        {
            var source = pixel * 4;
            var target = pixel * 4;
            bytes[target] = ToByte(pixels[source]);
            bytes[target + 1] = ToByte(pixels[source + 1]);
            bytes[target + 2] = ToByte(pixels[source + 2]);
            bytes[target + 3] = ToByte(pixels[source + 3]);
        }

        return bitmap;
    }

    /// <summary>Builds the slice-atlas shader that carries one cubic volume for the GPU.</summary>
    /// <param name="textures">Texture bank holding the volume.</param>
    /// <param name="texture">Volume texture to lay out.</param>
    /// <returns>The atlas shader, owned by the caller.</returns>
    private static SKShader CreateVolumeShader(VisualizerTextureBank textures, VisualizerTexture texture)
    {
        var atlas = CreateBitmap(
            textures.GetVolumeAtlasPixels(texture),
            VisualizerTextureBank.VolumeAtlasWidth,
            VisualizerTextureBank.VolumeAtlasHeight);
        var shader = atlas.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        atlas.Dispose();
        return shader;
    }

    /// <summary>Reads a bitmap back into a frame of zero-to-one components.</summary>
    /// <param name="bitmap">Bitmap to read.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns>The frame components.</returns>
    private static float[] ReadPixels(SKBitmap bitmap, int width, int height)
    {
        var pixels = new float[width * height * 4];
        var bytes = bitmap.GetPixelSpan();
        var count = Math.Min(bytes.Length / 4, width * height);
        for (var pixel = 0; pixel < count; pixel++)
        {
            var source = pixel * 4;
            pixels[source] = bytes[source] / 255f;
            pixels[source + 1] = bytes[source + 1] / 255f;
            pixels[source + 2] = bytes[source + 2] / 255f;
            pixels[source + 3] = bytes[source + 3] / 255f;
        }

        return pixels;
    }

    /// <summary>Converts a zero-to-one component into a byte.</summary>
    /// <param name="value">Component value.</param>
    /// <returns>The byte value.</returns>
    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>
    /// A compiled comp shader that runs as a Skia runtime effect over the renderer's frames. The
    /// runtime effect and the static noise and volume child shaders are built once; each draw binds
    /// the composited frame, its blur levels, and the previous frame, which is what the interpreter's
    /// sampler reads for a comp pass.
    /// </summary>
    public sealed class CompPass : IDisposable
    {
        private readonly SKRuntimeEffect _effect;
        private readonly IReadOnlyList<string> _samplers;
        private readonly Dictionary<string, SKShader> _static = new(StringComparer.Ordinal);
        private readonly List<SKBitmap> _bitmaps = [];
        private readonly List<SKShader> _shaders = [];

        /// <summary>Creates the pass.</summary>
        /// <param name="effect">Runtime effect to draw with.</param>
        /// <param name="samplers">Samplers the effect declares.</param>
        /// <param name="textures">Texture bank for the static noise and volume samplers.</param>
        private CompPass(SKRuntimeEffect effect, IReadOnlyList<string> samplers, VisualizerTextureBank textures)
        {
            _effect = effect;
            _samplers = samplers;

            foreach (var name in samplers)
            {
                if (name is "sampler_noisevol_lq" or "sampler_noisevol_hq")
                {
                    var shader = CreateVolumeShader(
                        textures,
                        name.EndsWith("hq", StringComparison.Ordinal)
                            ? VisualizerTexture.NoiseVolumeHigh
                            : VisualizerTexture.NoiseVolumeLow);
                    _static[name] = shader;
                    _shaders.Add(shader);
                }
                else if (VisualizerTextureBank.TryResolve(name, out var texture))
                {
                    var size = VisualizerTextureBank.GetSize(texture);
                    var bitmap = CreateBitmap(textures.GetPixels(texture), size, size);
                    _bitmaps.Add(bitmap);
                    var shader = bitmap.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, LinearSampling);
                    _static[name] = shader;
                    _shaders.Add(shader);
                }
            }
        }

        /// <summary>Gets the samplers the effect declares.</summary>
        public IReadOnlyList<string> Samplers => _samplers;

        /// <summary>Compiles a comp shader, or reports why it cannot run on the GPU.</summary>
        /// <param name="program">Parsed comp shader body.</param>
        /// <param name="textures">Texture bank for the noise and volume samplers.</param>
        /// <param name="error">Failure reason, or <see langword="null"/> on success.</param>
        /// <returns>The pass, or <see langword="null"/> when the shader stays on the CPU.</returns>
        public static CompPass? TryCreate(ShaderNode program, VisualizerTextureBank textures, out string? error)
        {
            try
            {
                var sksl = ShaderTranspiler.Transpile(program, out var samplers);
                var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
                if (effect is null)
                {
                    error = errors;
                    return null;
                }

                error = null;
                return new CompPass(effect, samplers, textures);
            }
            catch (PresetExpressionException exception)
            {
                error = exception.Message;
                return null;
            }
        }

        /// <summary>Draws the comp pass over the renderer's frames.</summary>
        /// <param name="output">Frame to write, which is also the fallback sampler source.</param>
        /// <param name="width">Frame width.</param>
        /// <param name="height">Frame height.</param>
        /// <param name="samplerSources">Frame each sampler reads, by sampler name.</param>
        /// <param name="scalars">Scalar uniforms the shader reads.</param>
        /// <param name="vectors">Vector uniforms the shader reads.</param>
        public void Render(
            PixelBuffer output,
            int width,
            int height,
            IReadOnlyDictionary<string, SamplerSource> samplerSources,
            IReadOnlyDictionary<string, float> scalars,
            IReadOnlyDictionary<string, float[]> vectors)
        {
            var ownedBitmaps = new List<SKBitmap>();
            var ownedShaders = new List<SKShader>();
            try
            {
                using var sourceBitmap = CreateBitmap(output.Pixels, width, height);
                using var sourceShader = sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
                var children = new SKRuntimeEffectChildren(_effect);
                foreach (var name in _samplers)
                {
                    if (_static.TryGetValue(name, out var cached))
                    {
                        children[name] = new SKRuntimeEffectChild(cached);
                        continue;
                    }

                    SKShader child;
                    if (samplerSources.TryGetValue(name, out var source))
                    {
                        var bitmap = CreateBitmap(source.Pixels, source.Width, source.Height);
                        ownedBitmaps.Add(bitmap);
                        child = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
                        ownedShaders.Add(child);
                    }
                    else
                    {
                        child = sourceShader;
                    }

                    children[name] = new SKRuntimeEffectChild(child);
                }

                var uniforms = new SKRuntimeEffectUniforms(_effect);
                foreach (var (name, count) in ShaderTranspiler.UniformComponents)
                {
                    if (count == 1)
                        uniforms[name] = 0f;
                    else
                        uniforms[name] = new float[count];
                }

                uniforms["texsize"] = TexSize(width, height);
                SetSamplerSizes(uniforms, width, height);
                foreach (var (name, value) in scalars)
                    uniforms[name] = value;
                foreach (var (name, value) in vectors)
                    uniforms[name] = value;

                using var shader = _effect.ToShader(uniforms, children);
                using var target = CreateBitmap(new float[width * height * 4], width, height);
                using var surface = SKSurface.Create(target.Info, target.GetPixels(), target.RowBytes);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Black);
                using (var paint = new SKPaint { Shader = shader })
                    canvas.DrawRect(new SKRect(0, 0, width, height), paint);

                canvas.Flush();
                var pixels = ReadPixels(target, width, height);
                pixels.AsSpan(0, Math.Min(pixels.Length, output.Pixels.Length)).CopyTo(output.Pixels);
            }
            finally
            {
                foreach (var shader in ownedShaders)
                    shader.Dispose();
                foreach (var bitmap in ownedBitmaps)
                    bitmap.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var shader in _shaders)
                shader.Dispose();
            foreach (var bitmap in _bitmaps)
                bitmap.Dispose();
            _static.Clear();
            _shaders.Clear();
            _bitmaps.Clear();
            _effect.Dispose();
        }
    }
}
