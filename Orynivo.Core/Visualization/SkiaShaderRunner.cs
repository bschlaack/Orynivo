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
    /// <summary>
    /// Renders a shader over a source frame on the GPU.
    /// </summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <param name="source">Source frame as RGBA components in the range zero to one.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="uniforms">Extra scalar uniforms the shader reads, for example <c>bass</c>.</param>
    /// <returns>The rendered frame as RGBA components in the range zero to one.</returns>
    /// <exception cref="PresetExpressionException">The shader cannot be translated or Skia rejects it.</exception>
    public static float[] Render(
        ShaderNode program,
        float[] source,
        int width,
        int height,
        IReadOnlyDictionary<string, float>? uniforms = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(source);

        var sksl = ShaderTranspiler.Transpile(program);
        using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors)
            ?? throw new PresetExpressionException($"SkSL was rejected: {errors}", 0);

        using var sourceBitmap = CreateBitmap(source, width, height);
        using var sourceShader = sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

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

        effectUniforms["texsize"] = new float[] { width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height) };
        if (uniforms is not null)
        {
            foreach (var (name, value) in uniforms)
                effectUniforms[name] = value;
        }

        // Every sampler the prelude declares needs a child shader, even when the body only reads
        // sampler_main; the frame stands in for the blur levels and the texture bank here.
        var children = new SKRuntimeEffectChildren(effect);
        foreach (var name in SamplerNames)
            children[name] = new SKRuntimeEffectChild(sourceShader);

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

    /// <summary>The samplers the transpiler's prelude declares.</summary>
    private static readonly string[] SamplerNames =
    [
        "sampler_main", "sampler_blur1", "sampler_blur2", "sampler_blur3",
        "sampler_noise_lq", "sampler_noise_mq", "sampler_noise_hq"
    ];

    /// <summary>Creates an eight-bit bitmap from a frame of zero-to-one components.</summary>
    /// <param name="pixels">Frame components.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns>The bitmap.</returns>
    private static SKBitmap CreateBitmap(float[] pixels, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                bitmap.SetPixel(
                    x,
                    y,
                    new SKColor(
                        ToByte(pixels[offset]),
                        ToByte(pixels[offset + 1]),
                        ToByte(pixels[offset + 2]),
                        ToByte(pixels[offset + 3])));
            }
        }

        return bitmap;
    }

    /// <summary>Reads a bitmap back into a frame of zero-to-one components.</summary>
    /// <param name="bitmap">Bitmap to read.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns>The frame components.</returns>
    private static float[] ReadPixels(SKBitmap bitmap, int width, int height)
    {
        var pixels = new float[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var colour = bitmap.GetPixel(x, y);
                var offset = ((y * width) + x) * 4;
                pixels[offset] = colour.Red / 255f;
                pixels[offset + 1] = colour.Green / 255f;
                pixels[offset + 2] = colour.Blue / 255f;
                pixels[offset + 3] = colour.Alpha / 255f;
            }
        }

        return pixels;
    }

    /// <summary>Converts a zero-to-one component into a byte.</summary>
    /// <param name="value">Component value.</param>
    /// <returns>The byte value.</returns>
    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
