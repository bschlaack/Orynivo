namespace Orynivo.Visualization;

/// <summary>
/// A floating-point RGBA framebuffer. The visualizer works in floats because the feedback
/// loop samples and re-blends the previous frame every frame; rounding to bytes each pass
/// would visibly band the picture. The buffer is rendered at a low resolution and scaled up
/// by the presenter, which is what keeps CPU rendering viable.
/// </summary>
public sealed class PixelBuffer
{
    private readonly float[] _pixels;

    /// <summary>Creates a buffer of the given size.</summary>
    /// <param name="width">Buffer width in pixels.</param>
    /// <param name="height">Buffer height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    public PixelBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _pixels = new float[width * height * 4];
    }

    /// <summary>Gets the buffer width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the buffer height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the raw RGBA samples in the range zero to one.</summary>
    public Span<float> Pixels => _pixels;

    /// <summary>Sets every sample to zero.</summary>
    public void Clear() => Array.Clear(_pixels);

    /// <summary>Copies another buffer of the same size into this one.</summary>
    /// <param name="source">Source buffer.</param>
    /// <exception cref="ArgumentException">The sizes differ.</exception>
    public void CopyFrom(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("The buffers must have the same size.", nameof(source));

        source._pixels.CopyTo(_pixels, 0);
    }

    /// <summary>Multiplies every sample by a factor, which fades the feedback image.</summary>
    /// <param name="factor">Factor in the range zero to one.</param>
    public void Scale(float factor)
    {
        for (var index = 0; index < _pixels.Length; index++)
            _pixels[index] *= factor;
    }

    /// <summary>Reads one channel of one pixel.</summary>
    /// <param name="x">Pixel column; out-of-range coordinates are clamped.</param>
    /// <param name="y">Pixel row; out-of-range coordinates are clamped.</param>
    /// <param name="channel">Channel index, zero to three.</param>
    /// <returns>The channel value.</returns>
    public float GetPixel(int x, int y, int channel)
    {
        var clampedX = Math.Clamp(x, 0, Width - 1);
        var clampedY = Math.Clamp(y, 0, Height - 1);
        return _pixels[(((clampedY * Width) + clampedX) * 4) + Math.Clamp(channel, 0, 3)];
    }

    /// <summary>Adds a colour to one pixel, saturating at one.</summary>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    /// <param name="red">Red amount.</param>
    /// <param name="green">Green amount.</param>
    /// <param name="blue">Blue amount.</param>
    public void AddPixel(int x, int y, float red, float green, float blue)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return;

        var offset = ((y * Width) + x) * 4;
        _pixels[offset] = Math.Clamp(_pixels[offset] + red, 0f, 1f);
        _pixels[offset + 1] = Math.Clamp(_pixels[offset + 1] + green, 0f, 1f);
        _pixels[offset + 2] = Math.Clamp(_pixels[offset + 2] + blue, 0f, 1f);
        _pixels[offset + 3] = 1f;
    }

    /// <summary>
    /// Samples a pixel at normalized coordinates with bilinear filtering. Coordinates outside
    /// the frame return transparent black instead of clamping to the edge, because a clamped
    /// edge smears the border colour into long streaks when a preset zooms or warps outwards.
    /// </summary>
    /// <param name="u">Horizontal coordinate, where zero is the left edge and one the right.</param>
    /// <param name="v">Vertical coordinate, where zero is the top edge and one the bottom.</param>
    /// <param name="destination">Destination for the four channel values.</param>
    public void SampleBilinear(float u, float v, Span<float> destination)
    {
        if (destination.Length < 4)
            throw new ArgumentException("The destination must hold four channels.", nameof(destination));

        if (u is < 0f or > 1f || v is < 0f or > 1f || float.IsNaN(u) || float.IsNaN(v))
        {
            destination[0] = destination[1] = destination[2] = destination[3] = 0f;
            return;
        }

        var x = (u * (Width - 1));
        var y = (v * (Height - 1));
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var fx = x - x0;
        var fy = y - y0;

        for (var channel = 0; channel < 4; channel++)
        {
            var topLeft = _pixels[(((y0 * Width) + x0) * 4) + channel];
            var topRight = _pixels[(((y0 * Width) + x1) * 4) + channel];
            var bottomLeft = _pixels[(((y1 * Width) + x0) * 4) + channel];
            var bottomRight = _pixels[(((y1 * Width) + x1) * 4) + channel];
            var top = topLeft + ((topRight - topLeft) * fx);
            var bottom = bottomLeft + ((bottomRight - bottomLeft) * fx);
            destination[channel] = top + ((bottom - top) * fy);
        }
    }

    /// <summary>Applies one three-by-three box blur pass, softening the feedback image.</summary>
    public void Blur()
    {
        var copy = new float[_pixels.Length];
        _pixels.CopyTo(copy, 0);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0f;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var sampleY = Math.Clamp(y + dy, 0, Height - 1);
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var sampleX = Math.Clamp(x + dx, 0, Width - 1);
                            sum += copy[(((sampleY * Width) + sampleX) * 4) + channel];
                        }
                    }

                    _pixels[offset + channel] = sum / 9f;
                }
            }
        }
    }

    /// <summary>Writes the buffer as BGRA bytes for a presentation bitmap.</summary>
    /// <param name="destination">Destination of at least <c>width * height * 4</c> bytes.</param>
    public void WriteBgra(Span<byte> destination)
    {
        var needed = Width * Height * 4;
        if (destination.Length < needed)
            throw new ArgumentException("The destination is too small for the buffer.", nameof(destination));

        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            var source = pixel * 4;
            var target = pixel * 4;
            destination[target] = ToByte(_pixels[source + 2]);
            destination[target + 1] = ToByte(_pixels[source + 1]);
            destination[target + 2] = ToByte(_pixels[source]);
            destination[target + 3] = ToByte(Math.Max(_pixels[source + 3], 1f));
        }
    }

    /// <summary>Writes one row as BGRA bytes, for presenters whose row stride is padded.</summary>
    /// <param name="y">Row index.</param>
    /// <param name="destination">Destination of at least <c>width * 4</c> bytes.</param>
    public void WriteRowBgra(int y, Span<byte> destination)
    {
        var needed = Width * 4;
        if (destination.Length < needed)
            throw new ArgumentException("The destination is too small for one row.", nameof(destination));
        if (y < 0 || y >= Height)
            return;

        for (var x = 0; x < Width; x++)
        {
            var source = (((y * Width) + x) * 4);
            var target = x * 4;
            destination[target] = ToByte(_pixels[source + 2]);
            destination[target + 1] = ToByte(_pixels[source + 1]);
            destination[target + 2] = ToByte(_pixels[source]);
            destination[target + 3] = ToByte(Math.Max(_pixels[source + 3], 1f));
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(value * 255f + 0.5f, 0f, 255f);
}
