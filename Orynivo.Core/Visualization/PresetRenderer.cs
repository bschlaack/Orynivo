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

    /// <summary>Draws the waveform and the spectrum bars into the fresh buffer.</summary>
    private void DrawOverlay()
    {
        _fresh.Clear();
        var width = _fresh.Width;
        var height = _fresh.Height;
        var alpha = Preset.WaveAlpha;

        var waveform = Waveform;
        if (waveform.Length > 1)
        {
            var centre = height * 0.5f;
            var amplitude = height * Preset.WaveScale * 0.5f;
            for (var x = 0; x < width; x++)
            {
                var point = (int)((long)x * (waveform.Length - 1) / Math.Max(1, width - 1));
                var value = Math.Clamp(waveform[point], -1f, 1f);
                var y = (int)Math.Clamp(centre + (value * amplitude), 0f, height - 1);
                _fresh.AddPixel(x, y, alpha * 0.35f, alpha, alpha);
                _fresh.AddPixel(x, y + 1, alpha * 0.15f, alpha * 0.4f, alpha * 0.5f);
            }
        }

        var bands = Bands;
        if (bands.Length > 0)
        {
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
