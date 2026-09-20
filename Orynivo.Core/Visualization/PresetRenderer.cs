using Orynivo.Audio;

namespace Orynivo.Visualization;

/// <summary>Audio values a visualizer frame reacts to.</summary>
public interface IVisualizerAudioSource
{
    /// <summary>Gets the smoothed logarithmic band levels in the range zero to one.</summary>
    ReadOnlySpan<float> Bands { get; }

    /// <summary>Gets the recent mono samples in the range -1 to 1 for the waveform overlay.</summary>
    ReadOnlySpan<float> Waveform { get; }

    /// <summary>Gets the bass energy in the range zero to one.</summary>
    float Bass { get; }

    /// <summary>Gets the mid energy in the range zero to one.</summary>
    float Mid { get; }

    /// <summary>Gets the treble energy in the range zero to one.</summary>
    float Treble { get; }

    /// <summary>Gets the overall level in the range zero to one.</summary>
    float Volume { get; }
}

/// <summary>
/// Runs one preset frame by frame. The stages follow Milkdrop: a one-time initialisation,
/// the per-frame block that also adjusts <c>decay</c>, <c>zoom</c>, and <c>warp</c>, the
/// per-pixel block that chooses where the previous frame is sampled from, optional blur
/// passes, the feedback fade, and finally the freshly drawn waveform and spectrum on top.
/// Everything runs on the calling thread and only touches this renderer's own buffers, so a
/// visualization can never interfere with audio output.
/// </summary>
public sealed class PresetRenderer : IVisualizerAudioSource
{
    private readonly PixelBuffer _previous;
    private readonly PixelBuffer _warped;
    private readonly PixelBuffer _fresh;
    private readonly float[] _slots;
    private readonly float[] _sample = new float[4];
    private IVisualizerAudioSource? _audio;
    private bool _initialized;
    private long _frame;
    private double _elapsed;

    /// <summary>Creates a renderer for one preset.</summary>
    /// <param name="preset">Preset to run.</param>
    /// <param name="width">Render width; the presenter scales the result up.</param>
    /// <param name="height">Render height.</param>
    public PresetRenderer(VisualizerPreset preset, int width = 480, int height = 270)
    {
        Preset = preset ?? throw new ArgumentNullException(nameof(preset));
        _previous = new PixelBuffer(width, height);
        _warped = new PixelBuffer(width, height);
        _fresh = new PixelBuffer(width, height);
        _slots = new float[preset.Layout.Count];
    }

    /// <summary>Gets the preset being rendered.</summary>
    public VisualizerPreset Preset { get; }

    /// <summary>Gets the frame the presenter should show.</summary>
    public PixelBuffer Output => _previous;

    /// <summary>Gets the number of rendered frames.</summary>
    public long FrameCount => _frame;

    /// <summary>Gets the band levels of the last rendered frame.</summary>
    public ReadOnlySpan<float> Bands => _audio is null ? default : _audio.Bands;

    /// <summary>Gets the waveform of the last rendered frame.</summary>
    public ReadOnlySpan<float> Waveform => _audio is null ? default : _audio.Waveform;

    /// <summary>Gets the bass energy of the last rendered frame.</summary>
    public float Bass { get; private set; }

    /// <summary>Gets the mid energy of the last rendered frame.</summary>
    public float Mid { get; private set; }

    /// <summary>Gets the treble energy of the last rendered frame.</summary>
    public float Treble { get; private set; }

    /// <summary>Gets the overall level of the last rendered frame.</summary>
    public float Volume { get; private set; }

    /// <summary>Renders one frame.</summary>
    /// <param name="audio">Audio values to react to.</param>
    /// <param name="deltaSeconds">Seconds since the previous frame.</param>
    public void RenderFrame(IVisualizerAudioSource audio, double deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        Bass = audio.Bass;
        Mid = audio.Mid;
        Treble = audio.Treble;
        Volume = audio.Volume;
        _elapsed += Math.Clamp(deltaSeconds, 0d, 0.25d);

        SeedFrameVariables();
        if (!_initialized)
        {
            Preset.PerFrameInit.Execute(_slots);
            _initialized = true;
        }

        Preset.PerFrame.Execute(_slots);

        var decay = Math.Clamp(Read("decay", Preset.Decay), 0f, 1f);
        var zoom = Math.Max(0.05f, Read("zoom", Preset.Zoom));
        Warp();

        for (var pass = 0; pass < Preset.BlurLevel; pass++)
            _warped.Blur();

        _warped.Scale(decay);
        DrawOverlay();
        Composite();
        _previous.CopyFrom(_fresh);
        _frame++;
    }

    /// <summary>
    /// Draws only the waveform and spectrum overlay, without the feedback warp. This is the
    /// reduce-motion path: the picture still shows the music but nothing moves.
    /// </summary>
    /// <param name="audio">Audio values to draw.</param>
    public void RenderOverlayOnly(IVisualizerAudioSource audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        Bass = audio.Bass;
        Mid = audio.Mid;
        Treble = audio.Treble;
        Volume = audio.Volume;
        DrawOverlay();
        _previous.CopyFrom(_fresh);
        _frame++;
    }

    /// <summary>Clears every buffer and restarts the preset on the next frame.</summary>
    public void Reset()
    {
        _previous.Clear();
        _warped.Clear();
        _fresh.Clear();
        Array.Clear(_slots);
        _audio = null;
        _initialized = false;
        _frame = 0;
        _elapsed = 0;
        Bass = Mid = Treble = Volume = 0f;
    }

    /// <summary>Seeds the built-in variables a preset expects for this frame.</summary>
    private void SeedFrameVariables()
    {
        Write("time", (float)_elapsed);
        Write("fps", _frame > 0 ? 1f / Math.Max(0.001f, (float)(_elapsed / Math.Max(1, _frame))) : 0f);
        Write("frame", _frame);
        Write("bass", Bass);
        Write("mid", Mid);
        Write("treb", Treble);
        Write("vol", Volume);
        Write("decay", Preset.Decay);
        Write("zoom", Preset.Zoom);
        Write("warp", Preset.Warp);
    }

    /// <summary>Warps the previous frame through the per-pixel block into the warped buffer.</summary>
    private void Warp()
    {
        var zoom = Math.Max(0.05f, Read("zoom", Preset.Zoom));
        var width = _warped.Width;
        var height = _warped.Height;
        for (var y = 0; y < height; y++)
        {
            var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
            for (var x = 0; x < width; x++)
            {
                var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                Write("x", normalizedX);
                Write("y", normalizedY);
                Write("rad", MathF.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY)));
                Write("ang", MathF.Atan2(normalizedY, normalizedX));
                Preset.PerPixel.Execute(_slots);

                var sampleX = Read("x", normalizedX) * zoom;
                var sampleY = Read("y", normalizedY) * zoom;
                _previous.SampleBilinear((sampleX * 0.5f) + 0.5f, (sampleY * 0.5f) + 0.5f, _sample);
                var offset = (((y * width) + x) * 4);
                _warped.Pixels[offset] = _sample[0];
                _warped.Pixels[offset + 1] = _sample[1];
                _warped.Pixels[offset + 2] = _sample[2];
                _warped.Pixels[offset + 3] = _sample[3];
            }
        }
    }

    /// <summary>Draws the waveform, the spectrum bars, and the custom shapes into the fresh buffer.</summary>
    private void DrawOverlay()
    {
        _fresh.Clear();
        DrawWaveform();
        DrawSpectrum();
        DrawShapes();
    }

    /// <summary>Draws the waveform, honouring a preset's per-point program.</summary>
    private void DrawWaveform()
    {
        var waveform = Waveform;
        if (waveform.Length < 2)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        var alpha = Preset.WaveAlpha;
        var amplitude = height * Preset.WaveScale * 0.5f;
        var centre = height * 0.5f;
        var perPoint = Preset.WavePerPoint;

        for (var column = 0; column < width; column++)
        {
            var t = width > 1 ? column / (float)(width - 1) : 0f;
            var point = (int)((long)column * (waveform.Length - 1) / Math.Max(1, width - 1));
            var sample = Math.Clamp(waveform[point], -1f, 1f);

            var normalizedX = (t * 2f) - 1f;
            var normalizedY = sample;
            if (!perPoint.IsEmpty)
            {
                Write("t", t);
                Write("i", column);
                Write("sample", sample);
                Write("x", normalizedX);
                Write("y", normalizedY);
                perPoint.Execute(_slots);
                normalizedX = Read("x", normalizedX);
                normalizedY = Read("y", normalizedY);
            }

            var x = (int)Math.Clamp((normalizedX * 0.5f + 0.5f) * (width - 1), 0f, width - 1);
            var y = (int)Math.Clamp(centre + (normalizedY * amplitude), 0f, height - 1);
            _fresh.AddPixel(x, y, alpha * 0.35f, alpha, alpha);
            _fresh.AddPixel(x, y + 1, alpha * 0.15f, alpha * 0.4f, alpha * 0.5f);
        }
    }

    /// <summary>Draws the spectrum bars.</summary>
    private void DrawSpectrum()
    {
        var bands = Bands;
        if (bands.Length == 0)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        var alpha = Preset.WaveAlpha;
        var barWidth = Math.Max(1, width / bands.Length);
        for (var band = 0; band < bands.Length; band++)
        {
            var barHeight = (int)Math.Clamp(bands[band] * height * 0.6f, 0f, height - 1);
            for (var row = 0; row < barHeight; row++)
            {
                var y = height - 1 - row;
                for (var column = 0; column < barWidth; column++)
                {
                    var x = (band * barWidth) + column;
                    if (x < width)
                        _fresh.AddPixel(x, y, alpha * 0.25f, alpha * 0.6f, alpha);
                }
            }
        }
    }

    /// <summary>Draws every custom shape of the preset.</summary>
    private void DrawShapes()
    {
        foreach (var shape in Preset.Shapes)
        {
            SeedShape(shape);
            shape.PerFrame.Execute(_slots);

            var centreX = Read("x", shape.X);
            var centreY = Read("y", shape.Y);
            var radius = Math.Max(0f, Read("rad", shape.Radius));
            var angle = Read("ang", shape.Angle);
            var sides = (int)Math.Clamp(Read("sides", shape.Sides), 0f, 64f);
            var red = Math.Clamp(Read("r", shape.Red), 0f, 1f);
            var green = Math.Clamp(Read("g", shape.Green), 0f, 1f);
            var blue = Math.Clamp(Read("b", shape.Blue), 0f, 1f);
            var alpha = Math.Clamp(Read("a", shape.Alpha), 0f, 1f);

            var vertices = BuildVertices(shape, sides, centreX, centreY, radius, angle);
            FillPolygon(vertices, red, green, blue, alpha, shape.Additive);
            DrawPolygonBorder(vertices, shape);
        }
    }

    /// <summary>Builds the vertex list of a shape, running its per-point program.</summary>
    /// <param name="shape">Shape being drawn.</param>
    /// <param name="sides">Vertex count; below three draws a circle approximation.</param>
    /// <param name="centreX">Centre column in the range -1 to 1.</param>
    /// <param name="centreY">Centre row in the range -1 to 1.</param>
    /// <param name="radius">Radius in the range 0 to 1.</param>
    /// <param name="angle">Base rotation in radians.</param>
    /// <returns>Vertex positions in the range -1 to 1.</returns>
    private (float X, float Y)[] BuildVertices(
        VisualizerShape shape,
        int sides,
        float centreX,
        float centreY,
        float radius,
        float angle)
    {
        var count = sides >= 3 ? sides : 24;
        var vertices = new (float X, float Y)[count];
        for (var index = 0; index < count; index++)
        {
            var t = index / (float)count;
            var vertexAngle = angle + (t * 2f * MathF.PI);
            var x = centreX + (MathF.Cos(vertexAngle) * radius);
            var y = centreY + (MathF.Sin(vertexAngle) * radius);

            if (!shape.PerPoint.IsEmpty)
            {
                Write("t", t);
                Write("i", index);
                Write("x", x);
                Write("y", y);
                Write("rad", radius);
                Write("ang", vertexAngle);
                Write("sides", sides);
                Write("r", shape.Red);
                Write("g", shape.Green);
                Write("b", shape.Blue);
                Write("a", shape.Alpha);
                shape.PerPoint.Execute(_slots);
                x = Read("x", x);
                y = Read("y", y);
            }

            vertices[index] = (x, y);
        }

        return vertices;
    }

    /// <summary>Fills a convex polygon with a scanline pass.</summary>
    private void FillPolygon(
        (float X, float Y)[] vertices,
        float red,
        float green,
        float blue,
        float alpha,
        bool additive)
    {
        if (vertices.Length < 3 || alpha <= 0f)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        for (var row = 0; row < height; row++)
        {
            var y = height > 1 ? (row / (float)(height - 1) * 2f) - 1f : 0f;
            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            for (var index = 0; index < vertices.Length; index++)
            {
                var a = vertices[index];
                var b = vertices[(index + 1) % vertices.Length];
                if ((a.Y > y) == (b.Y > y))
                    continue;

                var x = a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                minimum = Math.Min(minimum, x);
                maximum = Math.Max(maximum, x);
            }

            if (minimum > maximum)
                continue;

            var from = (int)Math.Clamp((minimum * 0.5f + 0.5f) * (width - 1), 0f, width - 1);
            var to = (int)Math.Clamp((maximum * 0.5f + 0.5f) * (width - 1), 0f, width - 1);
            for (var column = from; column <= to; column++)
                PaintPixel(column, row, red, green, blue, alpha, additive);
        }
    }

    /// <summary>Draws the border of a shape with its border colour.</summary>
    /// <param name="vertices">Vertex positions in the range -1 to 1.</param>
    /// <param name="shape">Shape whose border colour is used.</param>
    private void DrawPolygonBorder((float X, float Y)[] vertices, VisualizerShape shape)
    {
        if (vertices.Length < 2 || shape.BorderAlpha <= 0f)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        for (var index = 0; index < vertices.Length; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % vertices.Length];
            var steps = Math.Max(2, (int)(MathF.Abs(b.X - a.X) * width));
            for (var step = 0; step <= steps; step++)
            {
                var t = step / (float)steps;
                var x = a.X + ((b.X - a.X) * t);
                var y = a.Y + ((b.Y - a.Y) * t);
                var column = (int)Math.Clamp((x * 0.5f + 0.5f) * (width - 1), 0f, width - 1);
                var row = (int)Math.Clamp((y * 0.5f + 0.5f) * (height - 1), 0f, height - 1);
                PaintPixel(
                    column,
                    row,
                    shape.BorderRed,
                    shape.BorderGreen,
                    shape.BorderBlue,
                    shape.BorderAlpha,
                    shape.Additive);
            }
        }
    }

    /// <summary>Writes one overlay pixel, either adding it or replacing the pixel.</summary>
    private void PaintPixel(
        int x,
        int y,
        float red,
        float green,
        float blue,
        float alpha,
        bool additive)
    {
        if (x < 0 || y < 0 || x >= _fresh.Width || y >= _fresh.Height)
            return;

        var offset = (((y * _fresh.Width) + x) * 4);
        if (additive)
        {
            _fresh.Pixels[offset] = Math.Clamp(_fresh.Pixels[offset] + (red * alpha), 0f, 1f);
            _fresh.Pixels[offset + 1] = Math.Clamp(_fresh.Pixels[offset + 1] + (green * alpha), 0f, 1f);
            _fresh.Pixels[offset + 2] = Math.Clamp(_fresh.Pixels[offset + 2] + (blue * alpha), 0f, 1f);
        }
        else
        {
            _fresh.Pixels[offset] = Math.Clamp((_fresh.Pixels[offset] * (1f - alpha)) + (red * alpha), 0f, 1f);
            _fresh.Pixels[offset + 1] = Math.Clamp((_fresh.Pixels[offset + 1] * (1f - alpha)) + (green * alpha), 0f, 1f);
            _fresh.Pixels[offset + 2] = Math.Clamp((_fresh.Pixels[offset + 2] * (1f - alpha)) + (blue * alpha), 0f, 1f);
        }

        _fresh.Pixels[offset + 3] = 1f;
    }

    /// <summary>Seeds a shape's default values into the shared slots.</summary>
    /// <param name="shape">Shape about to be drawn.</param>
    private void SeedShape(VisualizerShape shape)
    {
        Write("sides", shape.Sides);
        Write("x", shape.X);
        Write("y", shape.Y);
        Write("rad", shape.Radius);
        Write("ang", shape.Angle);
        Write("r", shape.Red);
        Write("g", shape.Green);
        Write("b", shape.Blue);
        Write("a", shape.Alpha);
    }
    /// <summary>Adds the freshly drawn overlay on top of the faded feedback image.</summary>
    private void Composite()
    {
        var pixels = _fresh.Pixels;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = Math.Clamp(_warped.Pixels[index] + pixels[index], 0f, 1f);
            pixels[index + 1] = Math.Clamp(_warped.Pixels[index + 1] + pixels[index + 1], 0f, 1f);
            pixels[index + 2] = Math.Clamp(_warped.Pixels[index + 2] + pixels[index + 2], 0f, 1f);
            pixels[index + 3] = 1f;
        }
    }

    private float Read(string name, float fallback)
    {
        var slot = Preset.Layout.IndexOf(name);
        return slot < 0 ? fallback : _slots[slot];
    }

    private void Write(string name, float value)
    {
        var slot = Preset.Layout.IndexOf(name);
        if (slot >= 0)
            _slots[slot] = value;
    }
}
