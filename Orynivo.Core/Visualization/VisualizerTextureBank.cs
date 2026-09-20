namespace Orynivo.Visualization;

/// <summary>The textures a Milkdrop preset can sample.</summary>
public enum VisualizerTexture
{
    /// <summary>The small 32 x 32 noise texture, <c>noise_lq</c>.</summary>
    NoiseLow,

    /// <summary>The medium 256 x 256 noise texture, <c>noise_mq</c>.</summary>
    NoiseMedium,

    /// <summary>The large 512 x 512 noise texture, <c>noise_hq</c>.</summary>
    NoiseHigh,

    /// <summary>The first of the sixteen 32 x 32 random textures, <c>rand00</c>.</summary>
    Random00,

    /// <summary>The sixteenth of the sixteen 32 x 32 random textures, <c>rand15</c>.</summary>
    Random15 = Random00 + 15
}

/// <summary>How a sampled texture behaves outside its zero-to-one range.</summary>
public enum VisualizerTextureWrap
{
    /// <summary>Repeats the texture.</summary>
    Repeat,

    /// <summary>Clamps to the edge pixel.</summary>
    Clamp,

    /// <summary>Mirrors the texture at every edge.</summary>
    Mirror
}

/// <summary>
/// Provides the noise and random textures Milkdrop shaders sample. Milkdrop ships these as
/// image files; here they are generated deterministically from a fixed seed, so no third party
/// asset is bundled and every run produces exactly the same textures. Generation is lazy, so a
/// session that never opens the visualizer allocates nothing.
/// </summary>
public sealed class VisualizerTextureBank
{
    /// <summary>Edge length of the small and random textures.</summary>
    public const int SmallSize = 32;

    /// <summary>Edge length of the medium noise texture.</summary>
    public const int MediumSize = 256;

    /// <summary>Edge length of the large noise texture.</summary>
    public const int LargeSize = 512;

    private readonly Dictionary<VisualizerTexture, float[]> _textures = [];
    private readonly float[] _sample = new float[4];

    /// <summary>Returns the edge length of one texture.</summary>
    /// <param name="texture">Texture to describe.</param>
    /// <returns>The edge length in pixels.</returns>
    public static int GetSize(VisualizerTexture texture) => texture switch
    {
        VisualizerTexture.NoiseMedium => MediumSize,
        VisualizerTexture.NoiseHigh => LargeSize,
        _ => SmallSize
    };

    /// <summary>
    /// Returns one texture as interleaved RGBA floats in the range zero to one, generating it on
    /// first use. The returned span stays valid for the lifetime of the bank.
    /// </summary>
    /// <param name="texture">Texture to return.</param>
    /// <returns>The texture pixels, row by row, four floats per pixel.</returns>
    public ReadOnlySpan<float> GetPixels(VisualizerTexture texture)
    {
        if (_textures.TryGetValue(texture, out var cached))
            return cached;

        var size = GetSize(texture);
        var pixels = new float[size * size * 4];
        // A fixed seed per texture keeps the whole bank reproducible between runs and platforms.
        var state = (uint)(0x9E3779B9u + (uint)texture * 0x85EBCA6Bu);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = NextFloat(ref state);
            pixels[index + 1] = NextFloat(ref state);
            pixels[index + 2] = NextFloat(ref state);
            pixels[index + 3] = 1f;
        }

        // A few smoothing passes turn white noise into the soft clouds the presets expect,
        // except for the random textures, which Milkdrop keeps as hard noise.
        if (texture is not (>= VisualizerTexture.Random00 and <= VisualizerTexture.Random15))
        {
            for (var pass = 0; pass < 3; pass++)
                Smooth(pixels, size);
        }

        _textures[texture] = pixels;
        return pixels;
    }

    /// <summary>Samples a texture with bilinear filtering and the requested wrap mode.</summary>
    /// <param name="texture">Texture to sample.</param>
    /// <param name="u">Horizontal coordinate; whole numbers repeat the texture.</param>
    /// <param name="v">Vertical coordinate.</param>
    /// <param name="wrap">Behaviour outside the zero-to-one range.</param>
    /// <returns>The sampled RGBA values, reused between calls.</returns>
    public ReadOnlySpan<float> Sample(VisualizerTexture texture, float u, float v, VisualizerTextureWrap wrap)
    {
        var pixels = GetPixels(texture);
        var size = GetSize(texture);
        var x = (WrapCoordinate(u, wrap, out var clampX) * size) - 0.5f;
        var y = (WrapCoordinate(v, wrap, out var clampY) * size) - 0.5f;
        if (clampX || clampY)
        {
            x = Math.Clamp(x, 0f, size - 1f);
            y = Math.Clamp(y, 0f, size - 1f);
        }

        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        for (var channel = 0; channel < 4; channel++)
        {
            var topLeft = PixelsAt(pixels, size, x0, y0, channel);
            var topRight = PixelsAt(pixels, size, x0 + 1, y0, channel);
            var bottomLeft = PixelsAt(pixels, size, x0, y0 + 1, channel);
            var bottomRight = PixelsAt(pixels, size, x0 + 1, y0 + 1, channel);
            var top = topLeft + ((topRight - topLeft) * fx);
            var bottom = bottomLeft + ((bottomRight - bottomLeft) * fx);
            _sample[channel] = top + ((bottom - top) * fy);
        }

        return _sample;
    }

    /// <summary>Frees every generated texture.</summary>
    public void Clear() => _textures.Clear();

    /// <summary>Maps a coordinate into the zero-to-one range for the requested wrap mode.</summary>
    /// <param name="value">Coordinate to map.</param>
    /// <param name="wrap">Wrap mode.</param>
    /// <param name="clamp">Set when the caller has to clamp the pixel index afterwards.</param>
    /// <returns>The mapped coordinate.</returns>
    private static float WrapCoordinate(float value, VisualizerTextureWrap wrap, out bool clamp)
    {
        clamp = false;
        switch (wrap)
        {
            case VisualizerTextureWrap.Clamp:
                clamp = true;
                return Math.Clamp(value, 0f, 1f);
            case VisualizerTextureWrap.Mirror:
                var mirrored = MathF.Abs(value);
                var period = mirrored % 2f;
                return period > 1f ? 2f - period : period;
            default:
                return value - MathF.Floor(value);
        }
    }

    /// <summary>Reads one channel of one texel, wrapping the indices.</summary>
    private static float PixelsAt(ReadOnlySpan<float> pixels, int size, int x, int y, int channel)
    {
        var wrappedX = ((x % size) + size) % size;
        var wrappedY = ((y % size) + size) % size;
        return pixels[(((wrappedY * size) + wrappedX) * 4) + channel];
    }

    /// <summary>Smooths a texture with a wrap-around box blur.</summary>
    /// <param name="pixels">Texture pixels to smooth in place.</param>
    /// <param name="size">Edge length.</param>
    private static void Smooth(float[] pixels, int size)
    {
        var copy = (float[])pixels.Clone();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = (((y * size) + x) * 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    var total = 0f;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                            total += PixelsAt(copy, size, x + dx, y + dy, channel);
                    }

                    pixels[offset + channel] = total / 9f;
                }
            }
        }
    }

    /// <summary>Advances a xorshift generator and returns the next value in the range zero to one.</summary>
    /// <param name="state">Generator state.</param>
    /// <returns>The next value.</returns>
    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (float)0xFFFFFF;
    }
}
