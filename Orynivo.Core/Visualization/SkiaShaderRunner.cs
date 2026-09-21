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

    /// <summary>
    /// Blurs a frame in place with the same 3x3 box filter <see cref="PixelBuffer.Blur"/> applies, run
    /// as a Skia runtime effect. The frame travels through an eight-bit bitmap, so the result differs
    /// from the CPU blur by at most a level or two.
    /// </summary>
    /// <param name="frame">Frame to blur in place.</param>
    /// <param name="passes">Number of box-blur passes.</param>
    /// <exception cref="PresetExpressionException">Skia rejects the blur effect.</exception>
    public static void BlurFrame(PixelBuffer frame, int passes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        passes = Math.Clamp(passes, 0, 8);
        if (passes == 0)
            return;

        using var effect = SKRuntimeEffect.CreateShader(CompPass.BlurSkSL, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);
        using var sourceBitmap = CreateBitmap(frame.Pixels, frame.Width, frame.Height);
        using var sourceShader = sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);

        var owned = new List<SKShader>();
        try
        {
            SKShader current = sourceShader;
            for (var pass = 0; pass < passes; pass++)
            {
                var uniforms = new SKRuntimeEffectUniforms(effect);
                uniforms["size"] = new float[] { frame.Width, frame.Height };
                var children = new SKRuntimeEffectChildren(effect) { ["source"] = current };
                var next = effect.ToShader(uniforms, children);
                owned.Add(next);
                current = next;
            }

            using var target = CreateBitmap(new float[frame.Width * frame.Height * 4], frame.Width, frame.Height);
            using var surface = SKSurface.Create(target.Info, target.GetPixels(), target.RowBytes);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            using (var paint = new SKPaint { Shader = current })
                canvas.DrawRect(new SKRect(0, 0, frame.Width, frame.Height), paint);

            canvas.Flush();
            ReadPixels(target, frame.Width, frame.Height).AsSpan().CopyTo(frame.Pixels);
        }
        finally
        {
            foreach (var shader in owned)
                shader.Dispose();
        }
    }

    /// <summary>
    /// Applies Milkdrop's video echo in place: the frame is sampled through a zoom and an optional
    /// horizontal or vertical flip and blended back over itself. It reproduces
    /// <c>PresetRenderer.ApplyVideoEcho</c>, including leaving a pixel untouched when the sample falls
    /// outside the frame.
    /// </summary>
    /// <param name="frame">Frame to modify in place.</param>
    /// <param name="alpha">Echo blend amount from zero to one.</param>
    /// <param name="zoom">Echo zoom.</param>
    /// <param name="orientation">Zero to three: horizontal, vertical, or both flipped.</param>
    /// <exception cref="PresetExpressionException">Skia rejects the echo effect.</exception>
    public static void VideoEcho(PixelBuffer frame, float alpha, float zoom, int orientation)
    {
        ArgumentNullException.ThrowIfNull(frame);
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (alpha <= 0f)
            return;

        zoom = Math.Clamp(zoom, 0.1f, 4f);
        orientation = Math.Clamp(orientation, 0, 3);

        using var effect = SKRuntimeEffect.CreateShader(EchoSkSL, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);
        using var sourceBitmap = CreateBitmap(frame.Pixels, frame.Width, frame.Height);
        using var sourceShader = sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["size"] = new float[] { frame.Width, frame.Height },
            ["zoom"] = zoom,
            ["alpha"] = alpha,
            ["orientation"] = (float)orientation
        };
        var children = new SKRuntimeEffectChildren(effect) { ["frame"] = sourceShader };
        using var shader = effect.ToShader(uniforms, children);
        using var target = CreateBitmap(new float[frame.Width * frame.Height * 4], frame.Width, frame.Height);
        using var surface = SKSurface.Create(target.Info, target.GetPixels(), target.RowBytes);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(new SKRect(0, 0, frame.Width, frame.Height), paint);

        canvas.Flush();
        ReadPixels(target, frame.Width, frame.Height).AsSpan().CopyTo(frame.Pixels);
    }

    /// <summary>
    /// The SkSL of the video echo. The frame is sampled through the zoom and the orientation; a sample
    /// outside the frame leaves the pixel as it was, which is what the CPU pass does.
    /// </summary>
    private const string EchoSkSL = """
        uniform shader frame;
        uniform float2 size;
        uniform float zoom;
        uniform float alpha;
        uniform float orientation;
        half4 main(float2 coord) {
            float4 original = float4(frame.eval(coord));
            float2 uv = coord / size;
            float2 s = ((uv - 0.5) / zoom) + 0.5;
            if (orientation == 1.0 || orientation == 3.0) s.x = 1.0 - s.x;
            if (orientation == 2.0 || orientation == 3.0) s.y = 1.0 - s.y;
            if (s.x < 0.0 || s.x > 1.0 || s.y < 0.0 || s.y > 1.0) return half4(original);
            float4 sampled = float4(frame.eval((s * (size - 1.0)) + 0.5));
            return half4(mix(original, sampled, alpha));
        }
        """;

    /// <summary>
    /// Adds a frame onto another in place, the way Milkdrop composites the warped frame with the
    /// overlay: <paramref name="target"/> becomes <c>clamp(target + overlay, 0, 1)</c> per channel.
    /// </summary>
    /// <param name="target">Frame to add into and write.</param>
    /// <param name="overlay">Frame to add.</param>
    /// <exception cref="ArgumentException">The frames have different sizes.</exception>
    /// <exception cref="PresetExpressionException">Skia rejects the composite effect.</exception>
    public static void Composite(PixelBuffer target, PixelBuffer overlay)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(overlay);
        if (target.Width != overlay.Width || target.Height != overlay.Height)
            throw new ArgumentException("The frames have different sizes.", nameof(overlay));

        using var effect = SKRuntimeEffect.CreateShader(CompositeSkSL, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);
        using var targetBitmap = CreateBitmap(target.Pixels, target.Width, target.Height);
        using var targetShader = targetBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        using var overlayBitmap = CreateBitmap(overlay.Pixels, overlay.Width, overlay.Height);
        using var overlayShader = overlayBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        var children = new SKRuntimeEffectChildren(effect)
        {
            ["base"] = targetShader,
            ["overlay"] = overlayShader
        };
        using var shader = effect.ToShader(new SKRuntimeEffectUniforms(effect), children);
        using var result = CreateBitmap(new float[target.Width * target.Height * 4], target.Width, target.Height);
        using var surface = SKSurface.Create(result.Info, result.GetPixels(), result.RowBytes);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(new SKRect(0, 0, target.Width, target.Height), paint);

        canvas.Flush();
        ReadPixels(result, target.Width, target.Height).AsSpan().CopyTo(target.Pixels);
    }

    /// <summary>The SkSL of the additive composite.</summary>
    private const string CompositeSkSL = """
        uniform shader base;
        uniform shader overlay;
        half4 main(float2 coord) {
            return half4(clamp(float4(base.eval(coord)) + float4(overlay.eval(coord)), 0.0, 1.0));
        }
        """;

    /// <summary>One rectangular border band of the Milkdrop border pass.</summary>
    /// <param name="Inset">Inset as a fraction of the smaller dimension.</param>
    /// <param name="Thickness">Band thickness as a fraction of the smaller dimension.</param>
    /// <param name="Red">Red component.</param>
    /// <param name="Green">Green component.</param>
    /// <param name="Blue">Blue component.</param>
    /// <param name="Alpha">Blend amount from zero to one.</param>
    public readonly record struct BorderBand(
        float Inset,
        float Thickness,
        float Red,
        float Green,
        float Blue,
        float Alpha);

    /// <summary>
    /// Draws Milkdrop's outer and inner border bands over a frame in place, reproducing
    /// <c>PresetRenderer.DrawBorderFrame</c>: a band is the ring whose distance from the inset
    /// rectangle is below the band width, and a pixel on it is blended towards the band colour.
    /// </summary>
    /// <param name="frame">Frame to draw on in place.</param>
    /// <param name="outer">Outer band.</param>
    /// <param name="inner">Inner band.</param>
    /// <exception cref="PresetExpressionException">Skia rejects the border effect.</exception>
    public static void Borders(PixelBuffer frame, BorderBand outer, BorderBand inner)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var effect = SKRuntimeEffect.CreateShader(BorderSkSL, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);
        using var bitmap = CreateBitmap(frame.Pixels, frame.Width, frame.Height);
        using var source = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["size"] = new float[] { frame.Width, frame.Height },
            ["outerShape"] = new float[] { outer.Inset, outer.Thickness },
            ["outerColor"] = new float[] { outer.Red, outer.Green, outer.Blue, outer.Alpha },
            ["innerShape"] = new float[] { inner.Inset, inner.Thickness },
            ["innerColor"] = new float[] { inner.Red, inner.Green, inner.Blue, inner.Alpha }
        };
        var children = new SKRuntimeEffectChildren(effect) { ["frame"] = source };
        using var shader = effect.ToShader(uniforms, children);
        using var target = CreateBitmap(new float[frame.Width * frame.Height * 4], frame.Width, frame.Height);
        using var surface = SKSurface.Create(target.Info, target.GetPixels(), target.RowBytes);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(new SKRect(0, 0, frame.Width, frame.Height), paint);

        canvas.Flush();
        ReadPixels(target, frame.Width, frame.Height).AsSpan().CopyTo(frame.Pixels);
    }

    /// <summary>
    /// The SkSL of the border pass. A pixel is on a band when its distance to the inset rectangle is
    /// inside the band width, which is exactly the ring <c>DrawBorderFrame</c> paints.
    /// </summary>
    private const string BorderSkSL = """
        uniform shader frame;
        uniform float2 size;
        uniform float2 outerShape;
        uniform float4 outerColor;
        uniform float2 innerShape;
        uniform float4 innerColor;
        float4 applyBorder(float4 c, float2 coord, float2 shape, float4 colour) {
            float smallest = min(size.x, size.y);
            float margin = floor(smallest * shape.x);
            float band = max(1.0, floor(smallest * shape.y));
            float x = floor(coord.x);
            float y = floor(coord.y);
            float ring = min(min(x - margin, y - margin), min((size.x - 1.0 - margin) - x, (size.y - 1.0 - margin) - y));
            float painted = ((ring >= 0.0) && (ring < band)) ? 1.0 : 0.0;
            return float4(mix(c.rgb, colour.rgb, painted * colour.a), c.a);
        }
        half4 main(float2 coord) {
            float4 c = float4(frame.eval(coord));
            c = applyBorder(c, coord, outerShape, outerColor);
            c = applyBorder(c, coord, innerShape, innerColor);
            return half4(c);
        }
        """;

    /// <summary>The motion parameters of one warp pass.</summary>
    /// <param name="Zoom">Zoom factor, already bounded below by 0.01.</param>
    /// <param name="ZoomExp">Zoom exponent, one for a plain zoom.</param>
    /// <param name="Rotation">Rotation in radians.</param>
    /// <param name="CentreX">Rotation and zoom centre x.</param>
    /// <param name="CentreY">Rotation and zoom centre y.</param>
    /// <param name="OffsetX">Translation x.</param>
    /// <param name="OffsetY">Translation y.</param>
    /// <param name="StretchX">Horizontal stretch.</param>
    /// <param name="StretchY">Vertical stretch.</param>
    public readonly record struct WarpParameters(
        float Zoom,
        float ZoomExp,
        float Rotation,
        float CentreX,
        float CentreY,
        float OffsetX,
        float OffsetY,
        float StretchX,
        float StretchY);

    /// <summary>
    /// Warps the previous frame into the target with the Milkdrop motion transform. It reproduces the
    /// geometric part of <c>PresetRenderer.Warp</c> for a preset whose per-pixel block and warp shader
    /// are empty: centre, stretch, rotate, and zoom the sampling position, then read the previous frame
    /// bilinearly, leaving a sample outside the frame black the way <see cref="PixelBuffer.SampleBilinear"/>
    /// does.
    /// </summary>
    /// <param name="previous">Frame to sample.</param>
    /// <param name="target">Frame to write; must have the same size.</param>
    /// <param name="parameters">Motion parameters.</param>
    /// <exception cref="ArgumentException">The frames have different sizes.</exception>
    /// <exception cref="PresetExpressionException">Skia rejects the warp effect.</exception>
    public static void Warp(PixelBuffer previous, PixelBuffer target, WarpParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(target);
        if (previous.Width != target.Width || previous.Height != target.Height)
            throw new ArgumentException("The frames have different sizes.", nameof(target));

        using var effect = SKRuntimeEffect.CreateShader(WarpSkSL, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);
        using var bitmap = CreateBitmap(previous.Pixels, previous.Width, previous.Height);
        using var source = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["size"] = new float[] { target.Width, target.Height },
            ["zoom"] = parameters.Zoom,
            ["zoomExp"] = parameters.ZoomExp,
            ["rotation"] = parameters.Rotation,
            ["centre"] = new float[] { parameters.CentreX, parameters.CentreY },
            ["offset"] = new float[] { parameters.OffsetX, parameters.OffsetY },
            ["stretch"] = new float[] { parameters.StretchX, parameters.StretchY }
        };
        var children = new SKRuntimeEffectChildren(effect) { ["frame"] = source };
        using var shader = effect.ToShader(uniforms, children);
        using var result = CreateBitmap(new float[target.Width * target.Height * 4], target.Width, target.Height);
        using var surface = SKSurface.Create(result.Info, result.GetPixels(), result.RowBytes);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(new SKRect(0, 0, target.Width, target.Height), paint);

        canvas.Flush();
        ReadPixels(result, target.Width, target.Height).AsSpan().CopyTo(target.Pixels);
    }

    /// <summary>
    /// The SkSL of the geometric warp. A sample outside the frame is black, because
    /// <see cref="PixelBuffer.SampleBilinear"/> returns zero there rather than clamping.
    /// </summary>
    private const string WarpSkSL = """
        uniform shader frame;
        uniform float2 size;
        uniform float zoom;
        uniform float zoomExp;
        uniform float rotation;
        uniform float2 centre;
        uniform float2 offset;
        uniform float2 stretch;
        half4 main(float2 coord) {
            float2 normalized = ((coord - 0.5) / (size - 1.0) * 2.0) - 1.0;
            float2 warped = (normalized - centre) * stretch;
            float c = cos(rotation);
            float s = sin(rotation);
            float2 rotated = float2((warped.x * c) - (warped.y * s), (warped.x * s) + (warped.y * c));
            float2 sample;
            if (zoomExp != 1.0) {
                float radius = length(rotated);
                float pixelZoom = pow(zoom, 1.0 + (zoomExp * radius * 2.0));
                sample = (rotated * pixelZoom) + centre + offset;
            } else {
                sample = (rotated * zoom) + centre + offset;
            }
            float2 uv = (sample * 0.5) + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return half4(0.0);
            return half4(frame.eval((uv * (size - 1.0)) + 0.5));
        }
        """;

    /// <summary>Converts a zero-to-one component into a byte.</summary>
    /// <param name="value">Component value.</param>
    /// <returns>The byte value.</returns>
    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>
    /// A compiled warp pass that runs as a Skia runtime effect over the previous frame. It is the
    /// GPU counterpart of the CPU warp stage: the entry point builds the sampling coordinate from
    /// the Milkdrop motion transform, runs the preset's per-pixel expression block, and then either
    /// runs the warp shader or samples the previous frame directly. The runtime effect, the static
    /// noise and volume child shaders, and the per-pixel uniforms are built once.
    /// </summary>
    public sealed class WarpPass : IDisposable
    {
        /// <summary>The motion uniform names, which have to match <c>ShaderTranspiler.WarpUniforms</c>.</summary>
        private const string SizeUniform = "_orynivo_size";
        private const string ZoomUniform = "_orynivo_zoom";
        private const string ZoomExpUniform = "_orynivo_zoomExp";
        private const string RotationUniform = "_orynivo_rotation";
        private const string CentreUniform = "_orynivo_centre";
        private const string OffsetUniform = "_orynivo_offset";
        private const string StretchUniform = "_orynivo_stretch";

        private readonly SKRuntimeEffect _effect;
        private readonly IReadOnlyList<string> _samplers;
        private readonly IReadOnlyList<string> _perPixelUniforms;
        private readonly Dictionary<string, SKShader> _static = new(StringComparer.Ordinal);
        private readonly List<SKBitmap> _bitmaps = [];
        private readonly List<SKShader> _shaders = [];

        /// <summary>Creates the pass.</summary>
        /// <param name="effect">Runtime effect to draw with.</param>
        /// <param name="samplers">Samplers the effect declares.</param>
        /// <param name="perPixelUniforms">Preset variables the per-pixel block reads.</param>
        /// <param name="textures">Texture bank for the static noise and volume samplers.</param>
        private WarpPass(
            SKRuntimeEffect effect,
            IReadOnlyList<string> samplers,
            IReadOnlyList<string> perPixelUniforms,
            VisualizerTextureBank textures)
        {
            _effect = effect;
            _samplers = samplers;
            _perPixelUniforms = perPixelUniforms;

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

        /// <summary>Gets the preset variables the per-pixel block reads, which the caller has to seed.</summary>
        public IReadOnlyList<string> PerPixelUniforms => _perPixelUniforms;

        /// <summary>Compiles a warp pass, or reports why it cannot run on the GPU.</summary>
        /// <param name="program">Parsed warp shader body, or <see langword="null"/> for a warp without a shader.</param>
        /// <param name="perPixel">Per-pixel expression block, or <see langword="null"/>.</param>
        /// <param name="textures">Texture bank for the noise and volume samplers.</param>
        /// <param name="error">Failure reason, or <see langword="null"/> on success.</param>
        /// <returns>The pass, or <see langword="null"/> when the warp stays on the CPU.</returns>
        public static WarpPass? TryCreate(
            ShaderNode? program,
            PresetProgram? perPixel,
            VisualizerTextureBank textures,
            out string? error)
        {
            try
            {
                var sksl = ShaderTranspiler.TranspileWarp(program, perPixel, out var samplers, out var uniforms);
                var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
                if (effect is null)
                {
                    error = errors;
                    return null;
                }

                error = null;
                return new WarpPass(effect, samplers, uniforms, textures);
            }
            catch (PresetExpressionException exception)
            {
                error = exception.Message;
                return null;
            }
        }

        /// <summary>Draws the warp pass over the previous frame.</summary>
        /// <param name="previous">Frame to sample.</param>
        /// <param name="target">Frame to write; must have the same size.</param>
        /// <param name="parameters">Motion parameters.</param>
        /// <param name="scalars">Scalar uniforms the shader and per-pixel block read.</param>
        /// <param name="vectors">Vector uniforms such as <c>rand_frame</c>.</param>
        /// <exception cref="ArgumentException">The frames have different sizes.</exception>
        public void Render(
            PixelBuffer previous,
            PixelBuffer target,
            WarpParameters parameters,
            IReadOnlyDictionary<string, float> scalars,
            IReadOnlyDictionary<string, float[]> vectors)
        {
            ArgumentNullException.ThrowIfNull(previous);
            ArgumentNullException.ThrowIfNull(target);
            if (previous.Width != target.Width || previous.Height != target.Height)
                throw new ArgumentException("The frames have different sizes.", nameof(target));

            var width = target.Width;
            var height = target.Height;
            using var sourceBitmap = CreateBitmap(previous.Pixels, width, height);
            using var sourceShader = sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
            var children = new SKRuntimeEffectChildren(_effect);
            foreach (var name in _samplers)
            {
                // A static noise or volume texture keeps its own shader; every frame sampler reads
                // the previous frame, which is what the CPU warp samples for sampler_main.
                children[name] = new SKRuntimeEffectChild(
                    _static.TryGetValue(name, out var cached) ? cached : sourceShader);
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
            uniforms[SizeUniform] = new float[] { width, height };
            uniforms[ZoomUniform] = parameters.Zoom;
            uniforms[ZoomExpUniform] = parameters.ZoomExp;
            uniforms[RotationUniform] = parameters.Rotation;
            uniforms[CentreUniform] = new float[] { parameters.CentreX, parameters.CentreY };
            uniforms[OffsetUniform] = new float[] { parameters.OffsetX, parameters.OffsetY };
            uniforms[StretchUniform] = new float[] { parameters.StretchX, parameters.StretchY };

            // Every declared uniform has to be set, so the per-pixel variables default to zero
            // and the caller fills in the slots it knows.
            foreach (var name in _perPixelUniforms)
            {
                var emitted = PresetExpressionTranspiler.UniformName(name);
                uniforms[emitted] = scalars.TryGetValue(emitted, out var value) ? value : 0f;
            }

            foreach (var (name, value) in scalars)
                uniforms[name] = value;
            foreach (var (name, value) in vectors)
                uniforms[name] = value;

            using var shader = _effect.ToShader(uniforms, children);
            using var result = CreateBitmap(new float[width * height * 4], width, height);
            using var surface = SKSurface.Create(result.Info, result.GetPixels(), result.RowBytes);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            using (var paint = new SKPaint { Shader = shader })
                canvas.DrawRect(new SKRect(0, 0, width, height), paint);

            canvas.Flush();
            ReadPixels(result, width, height).AsSpan().CopyTo(target.Pixels);
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
        private readonly IReadOnlyList<string> _perPixelUniforms;
        private readonly SKRuntimeEffect? _blurEffect;
        private readonly Dictionary<string, SKShader> _static = new(StringComparer.Ordinal);
        private readonly List<SKBitmap> _bitmaps = [];
        private readonly List<SKShader> _shaders = [];

        /// <summary>Creates the pass.</summary>
        /// <param name="effect">Runtime effect to draw with.</param>
        /// <param name="samplers">Samplers the effect declares.</param>
        /// <param name="perPixelUniforms">Preset variables the per-pixel block reads.</param>
        /// <param name="textures">Texture bank for the static noise and volume samplers.</param>
        private CompPass(
            SKRuntimeEffect effect,
            IReadOnlyList<string> samplers,
            IReadOnlyList<string> perPixelUniforms,
            VisualizerTextureBank textures)
        {
            _effect = effect;
            _samplers = samplers;
            _perPixelUniforms = perPixelUniforms;
            _blurEffect = SKRuntimeEffect.CreateShader(BlurSkSL, out _);

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

        /// <summary>Gets the preset variables the per-pixel block reads, which the caller has to seed.</summary>
        public IReadOnlyList<string> PerPixelUniforms => _perPixelUniforms;

        /// <summary>
        /// Gets or sets a value indicating whether the blur levels are built on the GPU as a chain of
        /// box-blur passes over the composited frame instead of being supplied as pre-blurred frames.
        /// </summary>
        public bool GpuBlur { get; set; }

        /// <summary>
        /// The SkSL of one 3x3 box-blur pass. It reproduces <see cref="PixelBuffer.Blur"/> exactly:
        /// nine clamped taps averaged per channel.
        /// </summary>
        internal const string BlurSkSL = """
            uniform shader source;
            uniform float2 size;
            half4 main(float2 coord) {
                float4 total = float4(0.0);
                for (int dy = 0; dy < 3; dy++) {
                    for (int dx = 0; dx < 3; dx++) {
                        float2 offset = float2(float(dx) - 1.0, float(dy) - 1.0);
                        float2 p = clamp(coord + offset, float2(0.0), size - 1.0);
                        total += float4(source.eval(p));
                    }
                }
                return half4(total * (1.0 / 9.0));
            }
            """;

        /// <summary>Compiles a comp shader, or reports why it cannot run on the GPU.</summary>
        /// <param name="program">Parsed comp shader body.</param>
        /// <param name="perPixel">Per-pixel expression block that runs alongside the shader, or <see langword="null"/>.</param>
        /// <param name="textures">Texture bank for the noise and volume samplers.</param>
        /// <param name="error">Failure reason, or <see langword="null"/> on success.</param>
        /// <returns>The pass, or <see langword="null"/> when the shader stays on the CPU.</returns>
        public static CompPass? TryCreate(
            ShaderNode program,
            PresetProgram? perPixel,
            VisualizerTextureBank textures,
            out string? error)
        {
            try
            {
                var sksl = ShaderTranspiler.TranspileComp(program, perPixel, out var samplers, out var perPixelUniforms);
                var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
                if (effect is null)
                {
                    error = errors;
                    return null;
                }

                error = null;
                return new CompPass(effect, samplers, perPixelUniforms, textures);
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

                // The blur levels are blurred copies of the composited frame, which is sampler_main.
                var mainShader = sourceShader;
                if (samplerSources.TryGetValue("sampler_main", out var mainSource))
                {
                    var mainBitmap = CreateBitmap(mainSource.Pixels, mainSource.Width, mainSource.Height);
                    ownedBitmaps.Add(mainBitmap);
                    mainShader = mainBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
                    ownedShaders.Add(mainShader);
                }

                var blurCache = new Dictionary<int, SKShader>();
                var children = new SKRuntimeEffectChildren(_effect);
                foreach (var name in _samplers)
                {
                    if (_static.TryGetValue(name, out var cached))
                    {
                        children[name] = new SKRuntimeEffectChild(cached);
                        continue;
                    }

                    if (GpuBlur && _blurEffect is not null &&
                        name.StartsWith("sampler_blur", StringComparison.Ordinal) &&
                        int.TryParse(name.AsSpan("sampler_blur".Length), out var level) &&
                        level is >= 1 and <= 3)
                    {
                        children[name] = new SKRuntimeEffectChild(
                            BuildBlurShader(blurCache, mainShader, width, height, level, ownedShaders));
                        continue;
                    }

                    SKShader child;
                    if (name == "sampler_main")
                    {
                        child = mainShader;
                    }
                    else if (samplerSources.TryGetValue(name, out var source))
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

                // Every declared uniform has to be set, so the per-pixel variables default to zero
                // and the caller fills in the slots it knows.
                foreach (var name in _perPixelUniforms)
                {
                    var emitted = PresetExpressionTranspiler.UniformName(name);
                    uniforms[emitted] = scalars.TryGetValue(emitted, out var value) ? value : 0f;
                }

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

        /// <summary>Builds the blur shader for one level, reusing the previous level as its input.</summary>
        /// <param name="cache">Blur shaders built for this draw, by level.</param>
        /// <param name="source">Unblurred frame shader.</param>
        /// <param name="width">Frame width.</param>
        /// <param name="height">Frame height.</param>
        /// <param name="level">Blur level from one to three.</param>
        /// <param name="owned">Receives the shaders the caller has to dispose.</param>
        /// <returns>The blurred shader.</returns>
        private SKShader BuildBlurShader(
            Dictionary<int, SKShader> cache,
            SKShader source,
            int width,
            int height,
            int level,
            List<SKShader> owned)
        {
            if (cache.TryGetValue(level, out var existing))
                return existing;

            var input = level == 1 ? source : BuildBlurShader(cache, source, width, height, level - 1, owned);
            var uniforms = new SKRuntimeEffectUniforms(_blurEffect!);
            uniforms["size"] = new float[] { width, height };
            var children = new SKRuntimeEffectChildren(_blurEffect!) { ["source"] = input };
            var blurred = _blurEffect!.ToShader(uniforms, children);
            owned.Add(blurred);
            cache[level] = blurred;
            return blurred;
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
            _blurEffect?.Dispose();
            _effect.Dispose();
        }
    }
}
