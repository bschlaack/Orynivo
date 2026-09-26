namespace Orynivo.Visualization;

/// <summary>The textures a Milkdrop preset can sample.</summary>
public enum VisualizerTexture
{
    /// <summary>The 256 x 256 noise texture, <c>noise_lq</c> (Milkdrop's low-quality noise).</summary>
    NoiseLow,

    /// <summary>The small 32 x 32 noise texture, <c>noise_lq_lite</c>.</summary>
    NoiseLowLite,

    /// <summary>The 256 x 256 noise texture, <c>noise_mq</c>.</summary>
    NoiseMedium,

    /// <summary>The 256 x 256 noise texture, <c>noise_hq</c>.</summary>
    NoiseHigh,

    /// <summary>The first of the sixteen 32 x 32 random textures, <c>rand00</c>.</summary>
    Random00,

    /// <summary>The sixteenth of the sixteen 32 x 32 random textures, <c>rand15</c>.</summary>
    Random15 = Random00 + 15,

    /// <summary>The small 32 x 32 x 32 volume noise, <c>noisevol_lq</c>, sampled by <c>tex3D</c>.</summary>
    NoiseVolumeLow,

    /// <summary>The high quality 32 x 32 x 32 volume noise, <c>noisevol_hq</c>, sampled by <c>tex3D</c>.</summary>
    NoiseVolumeHigh
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
    /// <summary>Edge length of the small random and <c>noise_lq_lite</c> textures.</summary>
    public const int SmallSize = 32;

    /// <summary>
    /// Edge length of the noise textures Milkdrop generates: <c>noise_lq</c>, <c>noise_mq</c>, and
    /// <c>noise_hq</c> are all 256 (only <c>noise_lq_lite</c> is 32).
    /// </summary>
    public const int MediumSize = 256;

    /// <summary>Edge length of every axis of the cubic volume noise textures.</summary>
    public const int VolumeSize = 32;

    /// <summary>How many volume slices sit next to each other in one atlas row.</summary>
    public const int VolumeAtlasColumns = 8;

    /// <summary>How many atlas rows hold the volume slices.</summary>
    public const int VolumeAtlasRows = VolumeSize / VolumeAtlasColumns;

    /// <summary>Width of the two dimensional atlas that carries a volume texture.</summary>
    public const int VolumeAtlasWidth = VolumeSize * VolumeAtlasColumns;

    /// <summary>Height of the two dimensional atlas that carries a volume texture.</summary>
    public const int VolumeAtlasHeight = VolumeSize * VolumeAtlasRows;

    private readonly Dictionary<VisualizerTexture, float[]> _textures = [];
    private readonly Dictionary<VisualizerTexture, float[]> _volumes = [];
    private readonly float[] _sample = new float[4];

    /// <summary>Resolves a Milkdrop sampler name to one of the generated textures.</summary>
    /// <param name="sampler">Sampler name, for example <c>sampler_noise_lq</c>.</param>
    /// <param name="texture">The resolved texture.</param>
    /// <returns><see langword="true"/> when the name refers to a generated texture.</returns>
    public static bool TryResolve(string sampler, out VisualizerTexture texture)
    {
        texture = VisualizerTexture.NoiseLow;
        switch (sampler)
        {
            case "sampler_noise_lq":
                texture = VisualizerTexture.NoiseLow;
                return true;
            case "sampler_noise_lq_lite":
                texture = VisualizerTexture.NoiseLowLite;
                return true;
            case "sampler_noise_mq":
                texture = VisualizerTexture.NoiseMedium;
                return true;
            case "sampler_noise_hq":
                texture = VisualizerTexture.NoiseHigh;
                return true;
            case "sampler_noisevol_lq":
                texture = VisualizerTexture.NoiseVolumeLow;
                return true;
            case "sampler_noisevol_hq":
                texture = VisualizerTexture.NoiseVolumeHigh;
                return true;
        }

        if (sampler.Length == "sampler_rand00".Length &&
            sampler.StartsWith("sampler_rand", StringComparison.Ordinal) &&
            int.TryParse(sampler.AsSpan("sampler_rand".Length), out var index) &&
            index is >= 0 and <= 15)
        {
            texture = VisualizerTexture.Random00 + index;
            return true;
        }

        return false;
    }

    /// <summary>Returns the edge length of one texture.</summary>
    /// <param name="texture">Texture to describe.</param>
    /// <returns>The edge length in pixels.</returns>
    public static int GetSize(VisualizerTexture texture) => texture switch
    {
        VisualizerTexture.NoiseLowLite => SmallSize,
        >= VisualizerTexture.Random00 and <= VisualizerTexture.Random15 => SmallSize,
        VisualizerTexture.NoiseVolumeLow or VisualizerTexture.NoiseVolumeHigh => VolumeSize,
        _ => MediumSize
    };

    /// <summary>Reports whether a texture is one of the cubic volume noises.</summary>
    /// <param name="texture">Texture to test.</param>
    /// <returns><see langword="true"/> when the texture is a volume.</returns>
    public static bool IsVolume(VisualizerTexture texture) =>
        texture is VisualizerTexture.NoiseVolumeLow or VisualizerTexture.NoiseVolumeHigh;

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

    /// <summary>
    /// Returns one cubic volume texture as interleaved RGBA floats in the range zero to one,
    /// generating it on first use. The values are quantised to eight bits because the GPU carries the
    /// volume in an eight-bit atlas, and both execution paths have to start from exactly the same
    /// texels. The returned span stays valid for the lifetime of the bank.
    /// </summary>
    /// <param name="texture">Volume texture to return.</param>
    /// <returns>The volume voxels, x fastest, then y, then z, four floats per voxel.</returns>
    public ReadOnlySpan<float> GetVolumePixels(VisualizerTexture texture)
    {
        if (_volumes.TryGetValue(texture, out var cached))
            return cached;

        var size = VolumeSize;
        var pixels = new float[size * size * size * 4];
        // A fixed seed per texture keeps the whole bank reproducible between runs and platforms.
        var state = (uint)(0x51ED270Bu + (uint)texture * 0xC2B2AE35u);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = NextFloat(ref state);
            pixels[index + 1] = NextFloat(ref state);
            pixels[index + 2] = NextFloat(ref state);
            pixels[index + 3] = 1f;
        }

        // A few three dimensional box passes turn the white noise into the soft clouds Milkdrop's
        // volume textures hold.
        for (var pass = 0; pass < 3; pass++)
            SmoothVolume(pixels, size);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = Quantise(pixels[index]);
            pixels[index + 1] = Quantise(pixels[index + 1]);
            pixels[index + 2] = Quantise(pixels[index + 2]);
        }

        _volumes[texture] = pixels;
        return pixels;
    }

    /// <summary>
    /// Lays a volume texture out as a two dimensional slice atlas, which is how the GPU carries a
    /// three dimensional texture because Skia's runtime effects only sample two dimensional shaders.
    /// Slice <c>z</c> sits at column <c>z % VolumeAtlasColumns</c> and row
    /// <c>z / VolumeAtlasColumns</c>; the layout matches the SkSL helper the transpiler emits.
    /// </summary>
    /// <param name="texture">Volume texture to lay out.</param>
    /// <returns>The atlas pixels, row by row, four floats per pixel.</returns>
    public ReadOnlySpan<float> GetVolumeAtlasPixels(VisualizerTexture texture)
    {
        var volume = GetVolumePixels(texture);
        var size = VolumeSize;
        var atlas = new float[VolumeAtlasWidth * VolumeAtlasHeight * 4];
        for (var z = 0; z < size; z++)
        {
            var column = z % VolumeAtlasColumns;
            var row = z / VolumeAtlasColumns;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var source = ((((z * size) + y) * size) + x) * 4;
                    var targetX = (column * size) + x;
                    var targetY = (row * size) + y;
                    var target = (((targetY * VolumeAtlasWidth) + targetX) * 4);
                    atlas[target] = volume[source];
                    atlas[target + 1] = volume[source + 1];
                    atlas[target + 2] = volume[source + 2];
                    atlas[target + 3] = volume[source + 3];
                }
            }
        }

        return atlas;
    }

    /// <summary>Samples a volume texture with trilinear filtering and the requested wrap mode.</summary>
    /// <param name="texture">Volume texture to sample.</param>
    /// <param name="u">X coordinate; whole numbers repeat the volume.</param>
    /// <param name="v">Y coordinate.</param>
    /// <param name="w">Z coordinate.</param>
    /// <param name="wrap">Behaviour outside the zero-to-one range.</param>
    /// <returns>The sampled RGBA values, reused between calls.</returns>
    public ReadOnlySpan<float> SampleVolume(
        VisualizerTexture texture,
        float u,
        float v,
        float w,
        VisualizerTextureWrap wrap)
    {
        var volume = GetVolumePixels(texture);
        var size = VolumeSize;
        var x = (WrapCoordinate(u, wrap, out var clampX) * size) - 0.5f;
        var y = (WrapCoordinate(v, wrap, out var clampY) * size) - 0.5f;
        var z = (WrapCoordinate(w, wrap, out var clampZ) * size) - 0.5f;
        if (clampX || clampY || clampZ)
        {
            x = Math.Clamp(x, 0f, size - 1f);
            y = Math.Clamp(y, 0f, size - 1f);
            z = Math.Clamp(z, 0f, size - 1f);
        }

        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var z0 = (int)MathF.Floor(z);
        var fx = x - x0;
        var fy = y - y0;
        var fz = z - z0;
        for (var channel = 0; channel < 4; channel++)
        {
            var c000 = VoxelAt(volume, size, x0, y0, z0, channel);
            var c100 = VoxelAt(volume, size, x0 + 1, y0, z0, channel);
            var c010 = VoxelAt(volume, size, x0, y0 + 1, z0, channel);
            var c110 = VoxelAt(volume, size, x0 + 1, y0 + 1, z0, channel);
            var c001 = VoxelAt(volume, size, x0, y0, z0 + 1, channel);
            var c101 = VoxelAt(volume, size, x0 + 1, y0, z0 + 1, channel);
            var c011 = VoxelAt(volume, size, x0, y0 + 1, z0 + 1, channel);
            var c111 = VoxelAt(volume, size, x0 + 1, y0 + 1, z0 + 1, channel);
            var bottom = Lerp(c000, c100, fx) + ((Lerp(c010, c110, fx) - Lerp(c000, c100, fx)) * fy);
            var top = Lerp(c001, c101, fx) + ((Lerp(c011, c111, fx) - Lerp(c001, c101, fx)) * fy);
            _sample[channel] = bottom + ((top - bottom) * fz);
        }

        return _sample;
    }

    /// <summary>Frees every generated texture.</summary>
    public void Clear()
    {
        _textures.Clear();
        _volumes.Clear();
    }

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

    /// <summary>Smooths a volume with a wrap-around box blur over all three axes.</summary>
    /// <param name="pixels">Volume voxels to smooth in place.</param>
    /// <param name="size">Edge length.</param>
    private static void SmoothVolume(float[] pixels, int size)
    {
        var copy = (float[])pixels.Clone();
        for (var z = 0; z < size; z++)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var offset = (((((z * size) + y) * size) + x) * 4);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var total = 0f;
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            for (var dy = -1; dy <= 1; dy++)
                            {
                                for (var dx = -1; dx <= 1; dx++)
                                    total += VoxelAt(copy, size, x + dx, y + dy, z + dz, channel);
                            }
                        }

                        pixels[offset + channel] = total / 27f;
                    }
                }
            }
        }
    }

    /// <summary>Reads one channel of one voxel, wrapping the indices on all three axes.</summary>
    /// <param name="pixels">Volume voxels.</param>
    /// <param name="size">Edge length.</param>
    /// <param name="x">X index.</param>
    /// <param name="y">Y index.</param>
    /// <param name="z">Z index.</param>
    /// <param name="channel">Channel index.</param>
    /// <returns>The voxel channel.</returns>
    private static float VoxelAt(ReadOnlySpan<float> pixels, int size, int x, int y, int z, int channel)
    {
        var wrappedX = ((x % size) + size) % size;
        var wrappedY = ((y % size) + size) % size;
        var wrappedZ = ((z % size) + size) % size;
        return pixels[(((((wrappedZ * size) + wrappedY) * size) + wrappedX) * 4) + channel];
    }

    /// <summary>Interpolates between two values.</summary>
    /// <param name="from">Value at zero.</param>
    /// <param name="to">Value at one.</param>
    /// <param name="amount">Blend amount.</param>
    /// <returns>The interpolated value.</returns>
    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);

    /// <summary>Rounds a zero-to-one value to the eight-bit steps of the GPU atlas.</summary>
    /// <param name="value">Value to quantise.</param>
    /// <returns>The quantised value.</returns>
    private static float Quantise(float value) => MathF.Round(Math.Clamp(value, 0f, 1f) * 255f) / 255f;
}
