using System.Diagnostics;
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
/// Runs one preset frame by frame through the Milkdrop stage order: the one-time
/// initialisation blocks, the per-frame block that also adjusts the motion parameters, the
/// per-pixel block that chooses where the previous frame is sampled from, the blur passes,
/// the feedback fade with the centre darkening and gamma, and finally the freshly drawn
/// waveforms, spectrum, and shapes on top. Everything runs on the calling thread and only
/// touches this renderer's own buffers, so a visualization can never interfere with audio
/// output.
/// </summary>
public sealed class PresetRenderer : IVisualizerAudioSource, IShaderSampler
{
    private readonly PixelBuffer _previous;
    private readonly PixelBuffer _warped;
    private readonly PixelBuffer _fresh;
    private readonly PixelBuffer _frameCopy;
    private readonly PixelBuffer _blurred;
    private readonly Stopwatch _shaderClock = new();
    private readonly List<(ShaderInterpreter Interpreter, VisualizerShader Shader)> _warpShaders = [];
    private readonly List<(ShaderInterpreter Interpreter, VisualizerShader Shader)> _compShaders = [];
    private bool _shadersSkipped;
    private int _framesSinceSkip;
    private int _blurLevel;
    private bool _samplerMainIsWarped;
    private readonly float[] _slots;
    private readonly float[] _sample = new float[4];
    private IVisualizerAudioSource? _audio;
    private bool _initialized;
    private long _frame;
    private double _elapsed;
    private readonly float[] _motionX = new float[MotionColumns * MotionRows];
    private readonly float[] _motionY = new float[MotionColumns * MotionRows];
    private readonly int[] _motionCount = new int[MotionColumns * MotionRows];
    private float _attBass;
    private float _attMid;
    private float _attTreble;
    private float _attVolume;

    /// <summary>Motion-vector grid columns.</summary>
    private const int MotionColumns = 8;

    /// <summary>Motion-vector grid rows.</summary>
    private const int MotionRows = 6;

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
        _frameCopy = new PixelBuffer(width, height);
        _blurred = new PixelBuffer(width, height);
        _slots = new float[preset.Layout.Count];
        foreach (var shader in preset.WarpShaders)
            _warpShaders.Add((new ShaderInterpreter(shader.Program, this), shader));
        foreach (var shader in preset.CompShaders)
            _compShaders.Add((new ShaderInterpreter(shader.Program, this), shader));
    }

    /// <summary>Gets the preset being rendered.</summary>
    public VisualizerPreset Preset { get; }

    /// <summary>Gets the frame the presenter should show.</summary>
    public PixelBuffer Output => _previous;

    /// <summary>Gets the number of rendered frames.</summary>
    public long FrameCount => _frame;

    /// <summary>
    /// Gets or sets how long the shaders may take per frame, in milliseconds. A frame that
    /// exceeds the budget disables the shaders for a while, so a heavy preset keeps a smooth
    /// picture instead of stalling playback.
    /// </summary>
    public double ShaderTimeBudgetMilliseconds { get; set; } = 20d;

    /// <summary>Gets how long the shaders took on the last rendered frame, in milliseconds.</summary>
    public double LastShaderMilliseconds { get; private set; }

    /// <summary>Gets a value indicating whether the shaders are currently being skipped.</summary>
    public bool ShadersSkipped => _shadersSkipped;

    /// <summary>Gets a value indicating whether the preset carries any shader.</summary>
    public bool HasShaders => _warpShaders.Count > 0 || _compShaders.Count > 0;

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
        SmoothBands();

        SeedFrameVariables();
        if (!_initialized)
        {
            Preset.PerFrameInit.Execute(_slots);
            Preset.PerPixelInit.Execute(_slots);
            foreach (var wave in Preset.Waves)
                wave.Init.Execute(_slots);
            foreach (var shape in Preset.Shapes)
                shape.Init.Execute(_slots);
            _initialized = true;
        }

        Preset.PerFrame.Execute(_slots);
        foreach (var wave in Preset.Waves)
            wave.PerFrame.Execute(_slots);

        var decay = Math.Clamp(Read("decay", Preset.Decay), 0f, 1f);
        var useShaders = BeginShaderFrame();
        _shaderClock.Restart();
        Warp(useShaders);

        for (var pass = 0; pass < BlurPasses(); pass++)
            _warped.Blur();

        _warped.Scale(decay);
        ApplyVideoEcho();
        DarkenCenter();
        DrawBorders();
        ApplyGamma();
        DrawOverlay();
        Composite();
        if (useShaders)
            ApplyCompShaders();
        MeasureShaderTime();
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
        _frameCopy.Clear();
        _blurred.Clear();
        _blurLevel = 0;
        _shadersSkipped = false;
        _framesSinceSkip = 0;
        Array.Clear(_slots);
        _audio = null;
        _initialized = false;
        _frame = 0;
        _elapsed = 0;
        Array.Clear(_motionX);
        Array.Clear(_motionY);
        Array.Clear(_motionCount);
        _attBass = _attMid = _attTreble = _attVolume = 0f;
        Bass = Mid = Treble = Volume = 0f;
    }

    /// <summary>Keeps the smoothed <c>*_att</c> bands the presets read alongside the raw bands.</summary>
    private void SmoothBands()
    {
        _attBass = (_attBass * 0.8f) + (Bass * 0.2f);
        _attMid = (_attMid * 0.8f) + (Mid * 0.2f);
        _attTreble = (_attTreble * 0.8f) + (Treble * 0.2f);
        _attVolume = (_attVolume * 0.8f) + (Volume * 0.2f);
    }

    /// <summary>Seeds the standard variables a preset expects for this frame.</summary>
    private void SeedFrameVariables()
    {
        var width = _previous.Width;
        var height = _previous.Height;
        Write("time", (float)_elapsed);
        Write("fps", _frame > 0 ? 1f / Math.Max(0.001f, (float)(_elapsed / Math.Max(1, _frame))) : 0f);
        Write("frame", _frame);
        Write("monitor", 1f);
        Write("bass", Bass);
        Write("mid", Mid);
        Write("treb", Treble);
        Write("vol", Volume);
        Write("bass_att", _attBass);
        Write("mid_att", _attMid);
        Write("treb_att", _attTreble);
        Write("aspectx", height > 0 ? width / (float)height : 1f);
        Write("aspecty", 1f);
        Write("pixelsx", width);
        Write("pixelsy", height);
        // The per-frame defaults a preset can override before the warp reads them back.
        Write("decay", Preset.Decay);
        Write("fDecay", Preset.Decay);
        Write("fGammaAdj", 1f);
        Write("zoom", Preset.Zoom);
        Write("zoomexp", 1f);
        Write("rot", 0f);
        Write("cx", 0f);
        Write("cy", 0f);
        Write("dx", 0f);
        Write("dy", 0f);
        Write("warp", Preset.Warp);
        Write("sx", 1f);
        Write("sy", 1f);
        Write("blur1", Preset.BlurLevel);
        Write("blur2", 0f);
        Write("blur3", 0f);
        Write("darken_center", 0f);
        Write("wave_mode", 3f);
        Write("wave_r", 1f);
        Write("wave_g", 1f);
        Write("wave_b", 1f);
        Write("wave_a", Preset.WaveAlpha);
        Write("wave_x", 0.5f);
        Write("wave_y", 0.5f);
        Write("wave_mystery", 0f);
        Write("wave_dots", 0f);
        Write("wave_thick", 0f);
        Write("wave_additive", 1f);
        Write("wave_brighten", 0f);
        Write("ob_r", 0f);
        Write("ob_g", 0f);
        Write("ob_b", 0f);
        Write("ob_a", 0f);
        Write("ib_r", 0f);
        Write("ib_g", 0f);
        Write("ib_b", 0f);
        Write("ib_a", 0f);
        Write("echo_zoom", 1f);
        Write("echo_alpha", 0f);
        Write("echo_orient", 0f);
        Write("fVideoEchoZoom", 1f);
        Write("fVideoEchoAlpha", 0f);
        Write("nVideoEchoOrientation", 0f);
        // Preset keys are the per-frame starting values; the per-frame block may still override
        // them, and they are restored on the next frame just like in Milkdrop.
        foreach (var (name, value) in Preset.Defaults)
            Write(name, value);
    }

    /// <summary>How many box-blur passes the preset asked for across <c>blur1</c> to <c>blur3</c>.</summary>
    /// <returns>The bounded pass count.</returns>
    private int BlurPasses()
    {
        var passes = (int)Math.Clamp(Read("blur1", Preset.BlurLevel), 0f, 3f) +
                     (int)Math.Clamp(Read("blur2", 0f), 0f, 3f) +
                     (int)Math.Clamp(Read("blur3", 0f), 0f, 3f);
        return Math.Clamp(passes, 0, 8);
    }

    /// <summary>
    /// Warps the previous frame into the warped buffer. The built-in motion parameters run
    /// first, then the preset's per-pixel block sees that warped position in <c>x</c>, <c>y</c>,
    /// <c>rad</c>, and <c>ang</c> and may offset or replace it before the sample is taken.
    /// </summary>
    private void Warp(bool useShaders)
    {
        var zoom = Math.Max(0.01f, Read("zoom", Preset.Zoom));
        var zoomExp = Read("zoomexp", 1f);
        var rotation = Read("rot", 0f);
        var centreX = Read("cx", 0f);
        var centreY = Read("cy", 0f);
        var offsetX = Read("dx", 0f);
        var offsetY = Read("dy", 0f);
        var stretchX = Read("sx", 1f);
        var stretchY = Read("sy", 1f);
        var cosRotation = MathF.Cos(rotation);
        var sinRotation = MathF.Sin(rotation);

        var width = _warped.Width;
        var height = _warped.Height;
        for (var y = 0; y < height; y++)
        {
            var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
            for (var x = 0; x < width; x++)
            {
                var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;

                // Centre, stretch, rotate, and zoom the sampling position.
                var warpedX = (normalizedX - centreX) * stretchX;
                var warpedY = (normalizedY - centreY) * stretchY;
                var rotatedX = (warpedX * cosRotation) - (warpedY * sinRotation);
                var rotatedY = (warpedX * sinRotation) + (warpedY * cosRotation);
                var radius = MathF.Sqrt((rotatedX * rotatedX) + (rotatedY * rotatedY));
                var pixelZoom = zoomExp == 1f
                    ? zoom
                    : MathF.Pow(zoom, 1f + (zoomExp * radius * 2f));
                warpedX = (rotatedX * pixelZoom) + centreX + offsetX;
                warpedY = (rotatedY * pixelZoom) + centreY + offsetY;

                Write("x", warpedX);
                Write("y", warpedY);
                Write("rad", radius);
                Write("ang", MathF.Atan2(rotatedY, rotatedX));
                Preset.PerPixel.Execute(_slots);

                var sampleX = Read("x", warpedX);
                var sampleY = Read("y", warpedY);
                RecordMotion(normalizedX, normalizedY, sampleX, sampleY);
                var sampleU = (sampleX * 0.5f) + 0.5f;
                var sampleV = (sampleY * 0.5f) + 0.5f;
                if (useShaders)
                {
                    RunWarpShaders(sampleU, sampleV, (normalizedX * 0.5f) + 0.5f, (normalizedY * 0.5f) + 0.5f);
                }
                else
                {
                    _previous.SampleBilinear(sampleU, sampleV, _sample);
                }

                var offset = (((y * width) + x) * 4);
                _warped.Pixels[offset] = _sample[0];
                _warped.Pixels[offset + 1] = _sample[1];
                _warped.Pixels[offset + 2] = _sample[2];
                _warped.Pixels[offset + 3] = _sample[3];
            }
        }
    }

    /// <summary>Decides whether the shaders run on this frame, honouring the time budget.</summary>
    /// <returns><see langword="true"/> when the shaders should run.</returns>
    private bool BeginShaderFrame()
    {
        if (!HasShaders)
            return false;

        if (!_shadersSkipped)
            return true;

        // Retry periodically so a preset that became affordable is picked up again.
        if (++_framesSinceSkip < 120)
            return false;

        _framesSinceSkip = 0;
        _shadersSkipped = false;
        return true;
    }

    /// <summary>Measures the shader cost of this frame and skips them when they exceed the budget.</summary>
    private void MeasureShaderTime()
    {
        if (!HasShaders)
        {
            LastShaderMilliseconds = 0d;
            return;
        }

        _shaderClock.Stop();
        LastShaderMilliseconds = _shaderClock.Elapsed.TotalMilliseconds;
        if (LastShaderMilliseconds > ShaderTimeBudgetMilliseconds)
            _shadersSkipped = true;
    }

    /// <summary>Runs the warp shaders for one pixel and stores the resulting colour.</summary>
    /// <param name="u">Sampling coordinate of the current pixel.</param>
    /// <param name="v">Sampling row of the current pixel.</param>
    /// <param name="originalU">Coordinate of the pixel itself.</param>
    /// <param name="originalV">Row of the pixel itself.</param>
    private void RunWarpShaders(float u, float v, float originalU, float originalV)
    {
        _samplerMainIsWarped = false;
        foreach (var (interpreter, shader) in _warpShaders)
        {
            shader.PerFrame.Execute(_slots);
            BindShaderVariables(interpreter, u, v, originalU, originalV);
            var colour = interpreter.Run();
            _sample[0] = colour.X;
            _sample[1] = colour.Y;
            _sample[2] = colour.Z;
            _sample[3] = 1f;
        }

        if (_warpShaders.Count == 0)
            _previous.SampleBilinear(u, v, _sample);
    }

    /// <summary>Runs the comp shaders over the composited frame.</summary>
    private void ApplyCompShaders()
    {
        if (_compShaders.Count == 0)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        _frameCopy.CopyFrom(_fresh);
        _samplerMainIsWarped = true;
        for (var y = 0; y < height; y++)
        {
            var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
            var v = (y + 0.5f) / height;
            for (var x = 0; x < width; x++)
            {
                var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                var u = (x + 0.5f) / width;
                foreach (var (interpreter, shader) in _compShaders)
                {
                    shader.PerPixel.Execute(_slots);
                    BindShaderVariables(interpreter, u, v, u, v);
                    var colour = interpreter.Run();
                    var offset = (((y * width) + x) * 4);
                    _fresh.Pixels[offset] = Math.Clamp(colour.X, 0f, 1f);
                    _fresh.Pixels[offset + 1] = Math.Clamp(colour.Y, 0f, 1f);
                    _fresh.Pixels[offset + 2] = Math.Clamp(colour.Z, 0f, 1f);
                }
            }
        }
    }

    /// <summary>Writes the variables every shader can read for this pixel.</summary>
    /// <param name="interpreter">Shader about to run.</param>
    /// <param name="u">Sampling coordinate in the range zero to one.</param>
    /// <param name="v">Sampling row in the range zero to one.</param>
    /// <param name="originalU">Coordinate of the pixel itself.</param>
    /// <param name="originalV">Row of the pixel itself.</param>
    private void BindShaderVariables(ShaderInterpreter interpreter, float u, float v, float originalU, float originalV)
    {
        var width = _previous.Width;
        var height = _previous.Height;
        interpreter.SetVariable("uv", ShaderValue.Vector(u, v, 0f, 0f, 2));
        interpreter.SetVariable("uv_orig", ShaderValue.Vector(originalU, originalV, 0f, 0f, 2));
        interpreter.SetVariable("texsize", ShaderValue.Vector(width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height), 4));
        interpreter.SetVariable("time", Read("time", 0f));
        interpreter.SetVariable("frame", Read("frame", 0f));
        interpreter.SetVariable("fps", Read("fps", 0f));
        interpreter.SetVariable("bass", Bass);
        interpreter.SetVariable("mid", Mid);
        interpreter.SetVariable("treb", Treble);
        interpreter.SetVariable("vol", Volume);
        interpreter.SetVariable("bass_att", Read("bass_att", 0f));
        interpreter.SetVariable("mid_att", Read("mid_att", 0f));
        interpreter.SetVariable("treb_att", Read("treb_att", 0f));
        interpreter.SetVariable("aspectx", Read("aspectx", 1f));
        interpreter.SetVariable("aspecty", Read("aspecty", 1f));
        var x = (u * 2f) - 1f;
        var y = (v * 2f) - 1f;
        interpreter.SetVariable("rad", MathF.Sqrt((x * x) + (y * y)));
        interpreter.SetVariable("ang", MathF.Atan2(y, x));
    }

    /// <inheritdoc/>
    public ShaderValue Sample(string sampler, float u, float v)
    {
        var source = sampler switch
        {
            "sampler_fc_main" => _frameCopy,
            "sampler_pc_main" => _previous,
            _ => _samplerMainIsWarped ? _warped : _previous
        };
        source.SampleBilinear(u, v, _sample);
        return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
    }

    /// <inheritdoc/>
    public ShaderValue SampleBlur(int level, float u, float v)
    {
        level = Math.Clamp(level, 1, 3);
        if (_blurLevel != level)
        {
            _blurred.CopyFrom(_warped);
            for (var pass = 0; pass < level; pass++)
                _blurred.Blur();
            _blurLevel = level;
        }

        _blurred.SampleBilinear(u, v, _sample);
        return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
    }

    /// <inheritdoc/>
    public ShaderValue SamplePixel(int x, int y)
    {
        var column = Math.Clamp(x, 0, _warped.Width - 1);
        var row = Math.Clamp(y, 0, _warped.Height - 1);
        var offset = (((row * _warped.Width) + column) * 4);
        return ShaderValue.Vector(
            _warped.Pixels[offset],
            _warped.Pixels[offset + 1],
            _warped.Pixels[offset + 2],
            _warped.Pixels[offset + 3],
            4);
    }

    /// <summary>Darkens the centre of the frame by the amount the preset asked for.</summary>
    private void DarkenCenter()
    {
        var amount = Math.Clamp(Read("darken_center", 0f), 0f, 1f);
        if (amount <= 0f)
            return;

        var width = _warped.Width;
        var height = _warped.Height;
        var pixels = _warped.Pixels;
        for (var y = 0; y < height; y++)
        {
            var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
            for (var x = 0; x < width; x++)
            {
                var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                var distance = MathF.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
                var factor = 1f - (amount * Math.Clamp(1f - distance, 0f, 1f));
                var offset = (((y * width) + x) * 4);
                pixels[offset] *= factor;
                pixels[offset + 1] *= factor;
                pixels[offset + 2] *= factor;
            }
        }
    }

    /// <summary>Applies the preset's gamma adjustment to the warped frame.</summary>
    private void ApplyGamma()
    {
        var gamma = Read("fGammaAdj", 1f);
        if (MathF.Abs(gamma - 1f) < 0.001f)
            return;

        gamma = Math.Clamp(gamma, 0.1f, 10f);
        var pixels = _warped.Pixels;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = MathF.Pow(Math.Clamp(pixels[index], 0f, 1f), gamma);
            pixels[index + 1] = MathF.Pow(Math.Clamp(pixels[index + 1], 0f, 1f), gamma);
            pixels[index + 2] = MathF.Pow(Math.Clamp(pixels[index + 2], 0f, 1f), gamma);
        }
    }

    /// <summary>Draws the waveform, the spectrum bars, and the custom shapes into the fresh buffer.</summary>
    private void DrawOverlay()
    {
        _fresh.Clear();
        DrawWaves();
        DrawSpectrum();
        DrawMotionVectors();
        DrawShapes();
    }

    /// <summary>
    /// Draws the declared Milkdrop waveforms. The first slot is always drawn as the default
    /// wave; the other three are drawn when a preset declares a program for them. Every wave
    /// honours the global mode and its own per-point program.
    /// </summary>
    private void DrawWaves()
    {
        for (var index = 0; index < Preset.Waves.Count; index++)
        {
            var wave = Preset.Waves[index];
            if (index > 0 && wave.Init.IsEmpty && wave.PerFrame.IsEmpty && wave.PerPoint.IsEmpty)
                continue;

            DrawWave(wave, index);
        }
    }

    /// <summary>Draws one waveform with the global mode, colour, and modifiers.</summary>
    /// <param name="wave">Waveform programs to run.</param>
    /// <param name="index">Waveform slot, used to offset the default line.</param>
    private void DrawWave(VisualizerWave wave, int index)
    {
        var waveform = Waveform;
        if (waveform.Length < 2)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        var alpha = Math.Clamp(Read("wave_a", Preset.WaveAlpha), 0f, 1f);
        if (alpha <= 0f)
            return;

        var amplitude = height * Preset.WaveScale * 0.5f;
        var centre = height * Read("wave_y", 0.5f);
        var centreX = Read("wave_x", 0.5f) * (width - 1);
        var red = Math.Clamp(Read("wave_r", 1f), 0f, 1f);
        var green = Math.Clamp(Read("wave_g", 1f), 0f, 1f);
        var blue = Math.Clamp(Read("wave_b", 1f), 0f, 1f);
        var mystery = Read("wave_mystery", 0f);
        var mode = (int)Math.Clamp(Read("wave_mode", 3f), 0f, 7f);
        var circular = mode <= 1;
        var doubled = mode is 2 or 6 or 7;
        var dots = Read("wave_dots", 0f) >= 0.5f;
        var thick = Read("wave_thick", 0f) >= 0.5f;
        var additive = Read("wave_additive", 1f) >= 0.5f;
        var perPoint = wave.PerPoint;
        if (perPoint.IsEmpty && index == 0)
            perPoint = Preset.WavePerPoint;

        // The default line of the second slot sits slightly lower so two slots stay visible.
        var lineOffset = index == 0 ? 0f : (index - 1.5f) * height * 0.06f;
        var radius = height * 0.35f;
        for (var column = 0; column < width; column++)
        {
            var t = width > 1 ? column / (float)(width - 1) : 0f;
            var point = (int)((long)column * (waveform.Length - 1) / Math.Max(1, width - 1));
            var sample = Math.Clamp(waveform[point], -1f, 1f);

            float x;
            float y;
            if (circular)
            {
                var angle = (t * 2f * MathF.PI) + mystery;
                var currentRadius = radius + (sample * amplitude * 0.6f);
                x = (width * 0.5f) + (MathF.Cos(angle) * currentRadius);
                y = (height * 0.5f) + (MathF.Sin(angle) * currentRadius);
            }
            else
            {
                x = (t * (width - 1)) + ((centreX - ((width - 1) * 0.5f)) * 0.5f);
                y = centre + (sample * amplitude) + lineOffset + (mystery * amplitude);
            }

            if (!perPoint.IsEmpty)
            {
                // A line wave keeps the documented sample-unit mapping, so y = -1 still means
                // the top of the wave band; the circular modes work in frame coordinates.
                var normalizedX = circular
                    ? ((x / Math.Max(1f, width - 1)) * 2f) - 1f
                    : (t * 2f) - 1f;
                var normalizedY = circular
                    ? ((y / Math.Max(1f, height - 1)) * 2f) - 1f
                    : sample;
                Write("t", t);
                Write("i", column);
                Write("sample", sample);
                Write("x", normalizedX);
                Write("y", normalizedY);
                perPoint.Execute(_slots);
                normalizedX = Read("x", normalizedX);
                normalizedY = Read("y", normalizedY);
                if (circular)
                {
                    x = (normalizedX * 0.5f + 0.5f) * (width - 1);
                    y = (normalizedY * 0.5f + 0.5f) * (height - 1);
                }
                else
                {
                    x = (normalizedX * 0.5f + 0.5f) * (width - 1);
                    y = centre + (normalizedY * amplitude) + lineOffset + (mystery * amplitude);
                }
            }

            var pixelX = (int)Math.Clamp(x, 0f, width - 1);
            var pixelY = (int)Math.Clamp(y, 0f, height - 1);
            if (dots)
            {
                PaintPixel(pixelX, pixelY, red, green, blue, alpha, additive);
                if (thick)
                    PaintPixel(pixelX, pixelY + 1, red, green, blue, alpha * 0.6f, additive);
                continue;
            }

            PaintPixel(pixelX, pixelY, red, green, blue, alpha, additive);
            PaintPixel(pixelX, pixelY + 1, red, green, blue, alpha * 0.45f, additive);
            if (thick)
            {
                PaintPixel(pixelX, pixelY + 2, red, green, blue, alpha * 0.8f, additive);
                PaintPixel(pixelX, pixelY + 3, red, green, blue, alpha * 0.5f, additive);
            }

            if (doubled)
                PaintPixel(pixelX, (int)Math.Clamp((2 * centre) - pixelY, 0f, height - 1), red, green, blue, alpha, additive);
        }
    }

    /// <summary>Records the motion field into the grid the motion-vector overlay draws.</summary>
    /// <param name="pixelX">Pixel position in the range -1 to 1.</param>
    /// <param name="pixelY">Pixel row in the range -1 to 1.</param>
    /// <param name="sampleX">Sampled position in the range -1 to 1.</param>
    /// <param name="sampleY">Sampled row in the range -1 to 1.</param>
    private void RecordMotion(float pixelX, float pixelY, float sampleX, float sampleY)
    {
        var column = (int)Math.Clamp((pixelX * 0.5f + 0.5f) * (MotionColumns - 1), 0f, MotionColumns - 1);
        var row = (int)Math.Clamp((pixelY * 0.5f + 0.5f) * (MotionRows - 1), 0f, MotionRows - 1);
        var cell = (row * MotionColumns) + column;
        _motionX[cell] += sampleX - pixelX;
        _motionY[cell] += sampleY - pixelY;
        _motionCount[cell]++;
    }

    /// <summary>Draws the recorded motion field as a grid of vectors.</summary>
    private void DrawMotionVectors()
    {
        var length = Math.Clamp(Read("mv_l", 0f), 0f, 1f);
        if (length <= 0f)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        for (var row = 0; row < MotionRows; row++)
        {
            for (var column = 0; column < MotionColumns; column++)
            {
                var cell = (row * MotionColumns) + column;
                if (_motionCount[cell] == 0)
                    continue;

                var fromX = (column + 0.5f) / MotionColumns * (width - 1);
                var fromY = (row + 0.5f) / MotionRows * (height - 1);
                var deltaX = _motionX[cell] / _motionCount[cell] * width * length;
                var deltaY = _motionY[cell] / _motionCount[cell] * height * length;
                var steps = Math.Max(2, (int)MathF.Abs(deltaX));
                for (var step = 0; step <= steps; step++)
                {
                    var t = step / (float)steps;
                    PaintPixel(
                        (int)Math.Clamp(fromX + (deltaX * t), 0f, width - 1),
                        (int)Math.Clamp(fromY + (deltaY * t), 0f, height - 1),
                        1f,
                        1f,
                        1f,
                        length * 0.5f,
                        additive: true);
                }
            }
        }
    }

    /// <summary>Draws the outer and inner Milkdrop borders over the warped frame.</summary>
    private void DrawBorders()
    {
        DrawBorderFrame(0f, 0.02f);
        DrawBorderFrame(0.06f, 0.02f);
    }

    /// <summary>Draws one border frame with its own colour keys.</summary>
    /// <param name="inset">Inset as a fraction of the smaller dimension.</param>
    /// <param name="thickness">Frame thickness as a fraction of the smaller dimension.</param>
    private void DrawBorderFrame(float inset, float thickness)
    {
        var prefix = inset > 0f ? "ib_" : "ob_";
        var alpha = Math.Clamp(Read(prefix + "a", 0f), 0f, 1f);
        if (alpha <= 0f)
            return;

        var red = Math.Clamp(Read(prefix + "r", 1f), 0f, 1f);
        var green = Math.Clamp(Read(prefix + "g", 1f), 0f, 1f);
        var blue = Math.Clamp(Read(prefix + "b", 1f), 0f, 1f);
        var width = _warped.Width;
        var height = _warped.Height;
        var band = Math.Max(1, (int)(Math.Min(width, height) * thickness));
        var margin = (int)(Math.Min(width, height) * inset);
        for (var offset = 0; offset < band; offset++)
        {
            var left = margin + offset;
            var top = margin + offset;
            var right = width - 1 - margin - offset;
            var bottom = height - 1 - margin - offset;
            if (left > right || top > bottom)
                break;

            for (var x = left; x <= right; x++)
            {
                PaintWarped(x, top, red, green, blue, alpha);
                PaintWarped(x, bottom, red, green, blue, alpha);
            }

            for (var y = top; y <= bottom; y++)
            {
                PaintWarped(left, y, red, green, blue, alpha);
                PaintWarped(right, y, red, green, blue, alpha);
            }
        }
    }

    /// <summary>Blends one pixel into the warped frame.</summary>
    private void PaintWarped(int x, int y, float red, float green, float blue, float alpha)
    {
        if (x < 0 || y < 0 || x >= _warped.Width || y >= _warped.Height)
            return;

        var offset = (((y * _warped.Width) + x) * 4);
        _warped.Pixels[offset] = Math.Clamp((_warped.Pixels[offset] * (1f - alpha)) + (red * alpha), 0f, 1f);
        _warped.Pixels[offset + 1] = Math.Clamp((_warped.Pixels[offset + 1] * (1f - alpha)) + (green * alpha), 0f, 1f);
        _warped.Pixels[offset + 2] = Math.Clamp((_warped.Pixels[offset + 2] * (1f - alpha)) + (blue * alpha), 0f, 1f);
    }

    /// <summary>
    /// Blends a scaled and optionally flipped copy of the frame back over itself. This is the
    /// Milkdrop video-echo stage, driven by the <c>echo_*</c> keys.
    /// </summary>
    private void ApplyVideoEcho()
    {
        var alpha = Math.Clamp(Read("echo_alpha", Read("fVideoEchoAlpha", 0f)), 0f, 1f);
        if (alpha <= 0f)
            return;

        var zoom = Math.Clamp(Read("echo_zoom", Read("fVideoEchoZoom", 1f)), 0.1f, 4f);
        var orientation = (int)Math.Clamp(
            Read("echo_orient", Read("nVideoEchoOrientation", 0f)),
            0f,
            3f);
        var width = _warped.Width;
        var height = _warped.Height;
        _fresh.CopyFrom(_warped);
        var source = _fresh.Pixels;
        var target = _warped.Pixels;
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) / height;
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width;
                var sampleU = ((u - 0.5f) / zoom) + 0.5f;
                var sampleV = ((v - 0.5f) / zoom) + 0.5f;
                if (orientation is 1 or 3)
                    sampleU = 1f - sampleU;
                if (orientation is 2 or 3)
                    sampleV = 1f - sampleV;

                if (sampleU < 0f || sampleU > 1f || sampleV < 0f || sampleV > 1f)
                    continue;

                _fresh.SampleBilinear(sampleU, sampleV, _sample);
                var offset = (((y * width) + x) * 4);
                target[offset] = Math.Clamp((target[offset] * (1f - alpha)) + (_sample[0] * alpha), 0f, 1f);
                target[offset + 1] = Math.Clamp((target[offset + 1] * (1f - alpha)) + (_sample[1] * alpha), 0f, 1f);
                target[offset + 2] = Math.Clamp((target[offset + 2] * (1f - alpha)) + (_sample[2] * alpha), 0f, 1f);
            }
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

    /// <summary>Reads a variable of the shared slot layout, for diagnostics and tests.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The current value, or zero when the layout does not contain the name.</returns>
    public float ReadVariable(string name) => Read(name, 0f);

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
