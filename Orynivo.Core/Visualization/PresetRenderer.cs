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

    /// <summary>
    /// Gets the recent spectrum magnitudes in the range zero to one, oldest bin first, for a custom
    /// waveform that reads the spectrum. The default is empty, so a source that has no spectrum makes
    /// such a waveform draw nothing instead of a wrong picture.
    /// </summary>
    ReadOnlySpan<float> Spectrum => default;

    /// <summary>
    /// Gets the bass level relative to its long-term average, where one is neutral and a value above
    /// one means the band is louder than usual. Milkdrop's <c>bass</c> variable is this value, so a
    /// preset condition such as <c>above(bass, 1.2)</c> can fire; the normalized <see cref="Bass"/> is
    /// a different contract and stays unchanged.
    /// </summary>
    float BassRelative => 1f;

    /// <summary>Gets the mid level relative to its long-term average.</summary>
    float MidRelative => 1f;

    /// <summary>Gets the treble level relative to its long-term average.</summary>
    float TrebleRelative => 1f;

    /// <summary>Gets the attenuated (smoothed) bass level relative to its long-term average.</summary>
    float BassAttRelative => 1f;

    /// <summary>Gets the attenuated (smoothed) mid level relative to its long-term average.</summary>
    float MidAttRelative => 1f;

    /// <summary>Gets the attenuated (smoothed) treble level relative to its long-term average.</summary>
    float TrebleAttRelative => 1f;

    /// <summary>Gets the left channel's waveform samples, or the mono waveform when the source is mono.</summary>
    ReadOnlySpan<float> WaveformLeft => Waveform;

    /// <summary>Gets the right channel's waveform samples, or the mono waveform when the source is mono.</summary>
    ReadOnlySpan<float> WaveformRight => Waveform;

    /// <summary>Gets the left channel's spectrum, or the mono spectrum when the source is mono.</summary>
    ReadOnlySpan<float> SpectrumLeft => Spectrum;

    /// <summary>Gets the right channel's spectrum, or the mono spectrum when the source is mono.</summary>
    ReadOnlySpan<float> SpectrumRight => Spectrum;

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
public sealed class PresetRenderer : IVisualizerAudioSource, IShaderSampler, IDisposable
{
    private readonly PixelBuffer _previous;
    private readonly PixelBuffer _warped;
    private readonly PixelBuffer _fresh;
    private readonly PixelBuffer _frameCopy;
    private readonly PixelBuffer[] _blurLevels;
    private readonly Stopwatch _shaderClock = new();
    private readonly Stopwatch _warpShaderClock = new();
    private readonly Stopwatch _warpClock = new();
    private double _warpShaderMilliseconds;
    private readonly Stopwatch _clock = new();
    private double _mark;
    private double _sumWarp;
    private double _sumBlur;
    private double _sumPostProcess;
    private double _sumOverlay;
    private double _sumComposite;
    private double _sumShader;
    private double _sumTotal;
    private int _timingFrames;
    private readonly List<(ShaderInterpreter Interpreter, VisualizerShader Shader)> _warpShaders = [];
    private readonly List<(ShaderInterpreter Interpreter, VisualizerShader Shader)> _compShaders = [];
    private readonly List<CompiledShader> _compiledWarp = [];
    private readonly List<CompiledShader> _compiledComp = [];
    private bool _shaderGridReduced;
    /// <summary>Whether each blur level's buffer holds this stage's picture.</summary>
    private readonly bool[] _blurLevelReady = new bool[3];
    /// <summary>Times one Skia comp pass, so an overrunning one hands the preset to the interpreter.</summary>
    private readonly System.Diagnostics.Stopwatch _skiaCompClock = new();
    /// <summary>
    /// Upper bound on the pixels a shader pass may cost. Both the warp and the comp shader run on
    /// a grid at or below this size and are scaled back up, because a per-pixel shader on the CPU
    /// cannot afford the full frame at a high resolution.
    /// </summary>
    private const int ShaderPixelBudget = 10_000;

    /// <summary>
    /// Lower bound on the shader grid. The grid shrinks towards this when a shader is too slow, so
    /// a heavy preset degrades to a coarse picture instead of losing its shaders completely.
    /// </summary>
    private const int ShaderPixelFloor = 1_024;

    /// <summary>Pixels the shader grid currently aims for, adapted from the measured shader cost.</summary>
    private int _shaderPixelTarget = ShaderPixelBudget;

    /// <summary>Pixels the shader grid used on the last frame that ran shaders.</summary>
    private int _shaderPixelsUsed;

    private bool _perPixelSuspended;
    private bool _skipPerPixelThisFrame;
    private bool _presetProgramsFailed;
    private int _framesSinceSuspend;

    private readonly VisualizerTextureBank _textures = new();
    private readonly float[] _randFrame = new float[4];

    /// <summary>The four roam vectors for the current frame: cos, sin, slow cos, slow sin.</summary>
    private readonly ShaderValue[] _roam = new ShaderValue[4];
    private PixelBuffer? _shaderOutput;
    private SkiaShaderRunner.CompPass? _skiaComp;
    private bool _skiaCompTried;
    private SkiaShaderRunner.WarpPass? _skiaWarp;
    private bool _skiaWarpTried;
    private bool _warpShadersFailed;
    private bool _compShadersFailed;

    /// <summary>
    /// Whether the comp stage ran on the last frame, so its input is the pre-comp composite. The
    /// feedback has to be that pre-comp frame, not the comp output: the comp shader is a display
    /// pass in the reference implementation, and feeding its output back lets a preset that
    /// amplifies inside the comp shader diverge to white.
    /// </summary>
    private bool _compStageRan;
    private double[] _slots;
    // The per-pixel stage runs against its own copy of the slots, so the user variables its block
    // writes (a preset may reuse a name such as x2 as a spring state in per-frame and a vortex point
    // in per-pixel) cannot overwrite the per-frame state. Milkdrop keeps the two stages separate;
    // only q1-q8 carry from per-frame to per-pixel.
    private double[] _pixelSlots;
    private double _frameDelta = 1d / 60d;
    private static readonly string[] QNames = Enumerable.Range(1, 32).Select(i => "q" + i).ToArray();
    private static readonly string[] TNames = Enumerable.Range(1, 8).Select(i => "t" + i).ToArray();
    private readonly Dictionary<object, double[]> _elementFrames = [];
    private readonly Dictionary<object, double[]> _elementPoints = [];
    private readonly Dictionary<object, double[]> _elementInitT = [];
    private static readonly string[] ElementInputs = ["time", "fps", "frame", "progress", "bass", "mid", "treb", "bass_att", "mid_att", "treb_att"];

    // Resolved once so the per-pixel loop never looks a name up in the layout again.
    private readonly int _slotX;
    private readonly int _slotY;
    private readonly int _slotRad;
    private readonly int _slotAng;
    private readonly bool _perPixelUsesX;
    private readonly bool _perPixelUsesY;
    private readonly bool _perPixelUsesRadius;
    private readonly bool _perPixelUsesAngle;
    // Milkdrop treats the motion variables as per-vertex values that a per-pixel program may
    // change, and the change carries into the next pixel. That costs a read per pixel, so it is
    // only done for the presets that actually write one of them.
    private readonly bool _perPixelWritesMotion;
    // Milkdrop evaluates the per-pixel program once per mesh vertex, so a program that writes the
    // sample position itself cannot be represented by the mesh and keeps the per-pixel path.
    private readonly bool _perPixelWritesPosition;
    private readonly int _slotZoom;
    private readonly int _slotZoomExp;
    private readonly int _slotRot;
    private readonly int _slotCx;
    private readonly int _slotCy;
    private readonly int _slotDx;
    private readonly int _slotDy;
    private readonly int _slotSx;
    private readonly int _slotSy;
    private readonly int _slotWarp;
    private readonly double[][] _workerSlots;
    private readonly double[][] _workerPixelSlots;
    private readonly float[][] _workerSample;
    private readonly bool _canParallelizeWarp;
    // Resolved once: whether the per-pixel program may run on the GPU, which evaluates every pixel
    // independently and therefore cannot reproduce a value carried from the previous pixel.
    private readonly bool _perPixelGpuSafe;
    private readonly float[] _sample = new float[4];
    // Scratch for the custom waveforms: the smoothed sample data, the per-point values, and the
    // smoothed polyline. They are reused every frame so drawing an overlay stays allocation-free.
    private readonly float[] _waveSamples = new float[512];
    private readonly float[] _waveSamplesRight = new float[512];
    private readonly float[] _wavePcmLeft = new float[MilkdropWaveform.SampleCount];
    private readonly float[] _wavePcmRight = new float[MilkdropWaveform.SampleCount];
    private readonly float[] _waveSecondX = new float[512];
    private readonly float[] _waveSecondY = new float[512];
    // Texture coordinates of one shape's triangle fan, reused every shape so the overlay stays
    // allocation-free.
    private readonly float[] _shapeUvX = new float[128];
    private readonly float[] _shapeUvY = new float[128];
    private readonly float[] _shapeSample = new float[4];
    // The reference's per-preset hue offsets. It seeds them randomly on every load; a stable seed
    // from the preset name keeps a preset's look reproducible across runs.
    private readonly float[] _hueOffsets = new float[4];
    private readonly float[] _hueShadeR = new float[4];
    private readonly float[] _hueShadeG = new float[4];
    private readonly float[] _hueShadeB = new float[4];
    private readonly float[] _wavePointX = new float[512];
    private readonly float[] _wavePointY = new float[512];
    private readonly float[] _waveRed = new float[512];
    private readonly float[] _waveGreen = new float[512];
    private readonly float[] _waveBlue = new float[512];
    private readonly float[] _waveAlpha = new float[512];
    private readonly float[] _waveOutX = new float[1024];
    private readonly float[] _waveOutY = new float[1024];
    private readonly float[] _waveOutRed = new float[1024];
    private readonly float[] _waveOutGreen = new float[1024];
    private readonly float[] _waveOutBlue = new float[1024];
    private readonly float[] _waveOutAlpha = new float[1024];
    private IVisualizerAudioSource? _audio;
    private bool _initialized;
    private long _frame;
    private double _elapsed;
    private readonly float[] _motionX = new float[MotionColumns * MotionRows];
    private readonly float[] _motionY = new float[MotionColumns * MotionRows];
    private readonly int[] _motionCount = new int[MotionColumns * MotionRows];

    /// <summary>Motion-vector grid columns.</summary>
    private const int MotionColumns = 8;

    /// <summary>Motion-vector grid rows.</summary>
    private const int MotionRows = 6;

    /// <summary>Mesh grid columns, matching the reference implementation's default.</summary>
    public const int MeshGridX = 64;

    /// <summary>Mesh grid rows, matching the reference implementation's default.</summary>
    public const int MeshGridY = 48;

    /// <summary>
    /// Motion values the mesh carries per vertex, in this order: zoom, zoomexp, rot, cx, cy, dx, dy,
    /// sx, sy, warp. A GPU warp reads them as vertex attributes, so the order is part of the
    /// contract.
    /// </summary>
    public const int MeshValues = 10;

    /// <summary>The interpolated motion the per-pixel program produced per mesh vertex.</summary>
    private readonly float[] _meshMotion = new float[(MeshGridX + 1) * (MeshGridY + 1) * MeshValues];

    /// <summary>
    /// The sampling position each mesh vertex transforms to, in minus-one-to-one space. The warp
    /// interpolates this pair, not the motion, which is what the reference warp vertex shader does.
    /// </summary>
    private readonly float[] _meshUv = new float[(MeshGridX + 1) * (MeshGridY + 1) * 2];

    /// <summary>Whether <see cref="_meshMotion"/> holds the current frame's mesh.</summary>
    private bool _meshBuiltThisFrame;

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
        // The reference implementation builds each blur level from a downscaled copy of the frame:
        // blur1 is a quarter, blur2 an eighth, and blur3 a sixteenth of the size. Blurring at full
        // resolution keeps far more detail than the reference, and a preset that feeds the blur into
        // its own maths (a `tan` term) turns that detail into hard edges instead of soft rings.
        _blurLevels =
        [
            new PixelBuffer(Math.Max(16, width / 4), Math.Max(16, height / 4)),
            new PixelBuffer(Math.Max(16, width / 8), Math.Max(16, height / 8)),
            new PixelBuffer(Math.Max(16, width / 16), Math.Max(16, height / 16))
        ];
        _slots = new double[preset.Layout.Count];
        _pixelSlots = new double[preset.Layout.Count];
        _slotX = preset.Layout.IndexOf("x");
        _slotY = preset.Layout.IndexOf("y");
        _slotRad = preset.Layout.IndexOf("rad");
        _slotAng = preset.Layout.IndexOf("ang");
        _perPixelUsesX = preset.PerPixel.Uses("x");
        _perPixelUsesY = preset.PerPixel.Uses("y");
        _perPixelUsesRadius = preset.PerPixel.Uses("rad");
        _perPixelUsesAngle = preset.PerPixel.Uses("ang");
        _perPixelWritesMotion = MotionVariables.Any(preset.PerPixel.Writes);
        _perPixelWritesPosition = preset.PerPixel.Writes("x") || preset.PerPixel.Writes("y");
        _slotZoom = preset.Layout.IndexOf("zoom");
        _slotZoomExp = preset.Layout.IndexOf("zoomexp");
        _slotRot = preset.Layout.IndexOf("rot");
        _slotCx = preset.Layout.IndexOf("cx");
        _slotCy = preset.Layout.IndexOf("cy");
        _slotDx = preset.Layout.IndexOf("dx");
        _slotDy = preset.Layout.IndexOf("dy");
        _slotSx = preset.Layout.IndexOf("sx");
        _slotSy = preset.Layout.IndexOf("sy");
        _slotWarp = preset.Layout.IndexOf("warp");
        // The reference's hue offsets are random per load; a stable seed from the preset name keeps
        // the same preset looking the same across runs while still varying between presets.
        var hueSeed = 2166136261u;
        foreach (var character in preset.Name)
        {
            hueSeed ^= character;
            hueSeed *= 16777619u;
        }

        _hueOffsets[0] = (hueSeed % 64841u) * 0.01f;
        _hueOffsets[1] = ((hueSeed >> 8) % 53751u) * 0.01f;
        _hueOffsets[2] = ((hueSeed >> 16) % 42661u) * 0.01f;
        _hueOffsets[3] = ((hueSeed >> 24) % 31571u) * 0.01f;
        // A per-pixel pass may run in parallel when nothing it writes can be seen by another pixel
        // or by another stage of the preset. A value the engine re-seeds for every pixel is always
        // safe; a pixel-local temporary is safe when the block assigns it before it reads it and no
        // other stage reads it, because then it never leaves the pixel that wrote it. The engine's
        // own motion values stay shared even when nothing else reads them, because the mesh does.
        var shared = new HashSet<string>(MotionVariables, StringComparer.Ordinal);
        AddReferencedVariables(shared, preset.PerFrameInit);
        AddReferencedVariables(shared, preset.PerFrame);
        AddReferencedVariables(shared, preset.PerPixelInit);
        AddReferencedVariables(shared, preset.WavePerPoint);
        foreach (var shape in preset.Shapes)
        {
            AddReferencedVariables(shared, shape.Init);
            AddReferencedVariables(shared, shape.PerFrame);
            AddReferencedVariables(shared, shape.PerPoint);
        }

        foreach (var wave in preset.Waves)
        {
            AddReferencedVariables(shared, wave.Init);
            AddReferencedVariables(shared, wave.PerFrame);
            AddReferencedVariables(shared, wave.PerPoint);
        }

        _canParallelizeWarp = preset.WarpShaders.Count == 0 &&
                              PresetExpressionTranspiler.CanRunInParallel(preset.PerPixel) &&
                              preset.PerPixel.WrittenVariables.All(name =>
                                  IsSeededPerPixel(name) ||
                                  (!PresetVariableLayout.Standard.Contains(name) && !shared.Contains(name)));
        _perPixelGpuSafe = PresetExpressionTranspiler.CanRunInParallel(preset.PerPixel);
        var workers = ParallelRows.WorkerCount;
        _workerSlots = new double[workers][];
        _workerPixelSlots = new double[workers][];
        _workerSample = new float[workers][];
        for (var worker = 0; worker < workers; worker++)
        {
            _workerSlots[worker] = new double[preset.Layout.Count];
            _workerPixelSlots[worker] = new double[preset.Layout.Count];
            _workerSample[worker] = new float[4];
        }

        foreach (var shader in preset.WarpShaders)
        {
            _warpShaders.Add((new ShaderInterpreter(shader.Program, this), shader));
            _compiledWarp.Add(CompiledShader.Create(shader.Program));
        }

        foreach (var shader in preset.CompShaders)
        {
            _compShaders.Add((new ShaderInterpreter(shader.Program, this), shader));
            _compiledComp.Add(CompiledShader.Create(shader.Program));
        }
    }

    /// <summary>Gets the preset being rendered.</summary>
    public VisualizerPreset Preset { get; }

    /// <summary>
    /// Gets the frame the presenter should show. It is the post-comp frame, because the comp shader
    /// is the display pass; the feedback the next frame warps from is the separate pre-comp frame
    /// <see cref="MeshSource"/> exposes. Presenting <see cref="MeshSource"/> instead would drop the
    /// comp shader's picture.
    /// </summary>
    public PixelBuffer Output => _fresh;

    /// <summary>Gets the number of rendered frames.</summary>
    public long FrameCount => _frame;

    /// <summary>
    /// Gets or sets how long a complete frame may take, in milliseconds, before the shaders are
    /// treated as the optional work and skipped for a while. Skipping them keeps a heavy preset
    /// smooth instead of stalling playback.
    /// </summary>
    public double ShaderTimeBudgetMilliseconds { get; set; } = 30d;

    /// <summary>Gets how long the comp shaders took on the last rendered frame, in milliseconds.</summary>
    public double LastShaderMilliseconds { get; private set; }

    /// <summary>Gets the stage timings of the last rendered frame.</summary>
    public RenderTimings Timings { get; private set; }

    /// <summary>
    /// Gets the stage timings averaged over every frame since the renderer was created or since
    /// <see cref="ResetTimings"/> was called. Averaging keeps a single jittery frame from
    /// misrepresenting where the cost is.
    /// </summary>
    public RenderTimings AverageTimings { get; private set; }

    /// <summary>Restarts the averaging window used by <see cref="AverageTimings"/>.</summary>
    public void ResetTimings()
    {
        _sumWarp = 0d;
        _sumBlur = 0d;
        _sumPostProcess = 0d;
        _sumOverlay = 0d;
        _sumComposite = 0d;
        _sumShader = 0d;
        _sumTotal = 0d;
        _timingFrames = 0;
        AverageTimings = Timings;
    }

    /// <summary>
    /// Gets or sets the wall-clock ceiling for the warp stage. A per-pixel program runs once per
    /// screen pixel, so a preset that loops inside it can cost seconds for a single frame and
    /// freeze the picture; when the stage passes this ceiling the per-pixel program is left out for
    /// the rest of the frame and retried a moment later. The frame still renders with the per-frame
    /// motion values, so the preset stays visible instead of the window hanging.
    /// </summary>
    public double WarpStageBudgetMilliseconds { get; set; } = 1000d;

    /// <summary>
    /// Gets a value indicating whether the per-pixel program was left out of the last frame because
    /// the warp stage passed <see cref="WarpStageBudgetMilliseconds"/>.
    /// </summary>
    public bool PerPixelSuspended => _perPixelSuspended;

    /// <summary>
    /// Gets a value indicating whether the shader grid is below its full size because the shaders
    /// were too slow. The shaders keep running either way; only their resolution drops.
    /// </summary>
    public bool ShaderGridReduced => _shaderGridReduced;

    /// <summary>
    /// Gets the reason a shader was disabled, or <see langword="null"/> while every shader runs.
    /// A disabled shader silently costs a preset its picture, so the reason stays readable instead
    /// of only being swallowed.
    /// </summary>
    public string? ShaderError { get; private set; }

    /// <summary>
    /// Gets the reason the preset's own expression programs were stopped, or <see langword="null"/>
    /// while they run. A preset program that throws would otherwise fail every single frame, which
    /// looks like a frozen picture.
    /// </summary>
    public string? PresetError { get; private set; }

    /// <summary>
    /// Gets or sets the wall-clock ceiling for one shader pass. A shader that costs milliseconds per
    /// pixel needs minutes for a whole grid, and the adaptive grid can only react once a frame has
    /// finished, so a pass that overruns is abandoned here and the grid shrinks immediately.
    /// </summary>
    public double ShaderPassBudgetMilliseconds { get; set; } = 150d;

    /// <summary>
    /// Gets or sets the sink for the stage trace of a preset's first frames. A stage that never
    /// returns leaves its own line as the last one, which is how a frozen frame is located.
    /// </summary>
    public Action<string>? StageLogger { get; set; }

    /// <summary>
    /// Gets or sets the sink for the frame's brightness as it enters each stage, which is what lets a
    /// diagnostic attribute a white frame to the stage that turned it white. It is invoked once per
    /// stage on every frame while it is set, so the values track the current frame instead of the
    /// first frame the stage trace happens to cover. Leave it <see langword="null"/> to skip the
    /// probes.
    /// </summary>
    public Action<string, float>? StageBrightnessLogger { get; set; }

    /// <summary>The motion variables a per-pixel program may change for the following pixel.</summary>
    private static readonly string[] MotionVariables =
        ["zoom", "zoomexp", "rot", "cx", "cy", "dx", "dy", "sx", "sy"];

    /// <summary>Gets a value indicating whether the preset carries any shader.</summary>
    public bool HasShaders => _warpShaders.Count > 0 || _compShaders.Count > 0;

    /// <summary>
    /// Gets or sets a value indicating whether the comp shader and a warp shader run as Skia runtime
    /// effects instead of the interpreter. It is off by default because the Skia path carries the
    /// frame through eight-bit textures, so its picture differs from the interpreter by up to one
    /// level. The visualizer enables it: the compiled SkSL is faster than the interpreter for those
    /// per-pixel passes.
    /// </summary>
    public bool UseSkiaPasses { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the full-frame passes (the geometric warp, the video
    /// echo, the borders, and the composite) run as Skia runtime effects. It is off by default
    /// because those passes measured slower on the raster Skia surface than the interpreter's
    /// in-place float passes, which have no per-frame bitmap conversion.
    /// </summary>
    public bool UseSkiaFramePasses { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether full-frame passes may use more than one thread.
    /// It exists so a test can compare both paths and prove they render identical frames.
    /// </summary>
    public bool ParallelismEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the per-pixel program runs once per mesh vertex and
    /// its motion is interpolated across the frame, the way Milkdrop evaluates its per-vertex
    /// program, instead of running for every pixel. It applies only when the program writes motion
    /// and no sample position; a program that writes <c>x</c> or <c>y</c>, records motion vectors,
    /// or feeds a warp shader keeps the per-pixel path. The interpolated result equals the
    /// per-pixel result when the program writes a constant, which is what the identical-frame test
    /// asserts.
    /// <para>
    /// It is off by default: the mesh is the reference's geometry, but the engine's per-pixel
    /// <c>x</c>/<c>y</c> are the warped position in minus-one-to-one space rather than Milkdrop's
    /// aspect-scaled zero-to-one vertex position, so a preset that derives an offset from them
    /// renders visibly differently once the offset is interpolated. Turn it on to compare a preset
    /// against the reference (see <c>scripts/projectm-oracle</c>) before changing the default.
    /// </para>
    /// </summary>
    public bool MeshPerPixelEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the per-vertex mesh is evaluated even when the CPU
    /// warp does not interpolate it. A GPU warp needs the mesh values as vertex attributes, so it
    /// sets this while the CPU path keeps evaluating the per-pixel program; the two together are the
    /// transitional state, and a GPU warp that replaces the CPU warp clears it again. It has no
    /// effect when the per-pixel program writes no motion.
    /// </summary>
    public bool MeshRequested { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the mesh is built even when the per-pixel program
    /// writes the sample position <c>x</c> or <c>y</c>. A GPU warp that evaluates that block in its
    /// fragment stage still needs the mesh for the frame motion and the geometry, so the caller sets
    /// this while it owns the frame. The CPU path is unaffected: the mesh is an extra product, not a
    /// replacement, and the per-pixel warp still runs unless <see cref="ExpressionsOnly"/> is set.
    /// </summary>
    public bool MeshForPixelWarp { get; set; }

    /// <summary>
    /// Gets a value indicating whether the per-pixel program writes a motion value the warp reads
    /// (<c>zoom</c>, <c>zoomexp</c>, <c>rot</c>, <c>cx</c>/<c>cy</c>, <c>dx</c>/<c>dy</c>,
    /// <c>sx</c>/<c>sy</c>, or <c>warp</c>). A GPU pipeline must draw the CPU-evaluated motion mesh
    /// for such a program instead of a full-screen fragment warp, because the mesh carries the
    /// per-vertex motion the preset computed.
    /// </summary>
    public bool PerPixelWritesMotion => _perPixelWritesMotion;

    /// <summary>
    /// Gets or sets a value indicating whether the renderer runs only the preset's expressions and
    /// draws the overlay, leaving the frame itself to a GPU pipeline. The mesh is still built and the
    /// overlay is still drawn into <see cref="OverlayFrame"/>; every pixel pass is skipped, so this
    /// is the CPU half of the GPU split.
    /// </summary>
    public bool ExpressionsOnly { get; set; }

    /// <summary>
    /// Builds the per-vertex mesh for a GPU warp, which owns the frame itself. It is the mesh half of
    /// <see cref="Warp"/> without any pixel work, so <see cref="ExpressionsOnly"/> can call it.
    /// </summary>
    public void PrepareMeshForGpu()
    {
        _meshBuiltThisFrame = false;
        // A per-pixel block that writes the sample position cannot be interpolated over a mesh, so
        // the mesh is skipped for it. MeshForPixelWarp is the exception: the GPU evaluates that
        // block per pixel in a fragment shader and reads only the mesh's motion values, so the
        // mesh is still built as the source of those values and the CPU can stop evaluating the
        // block itself.
        if ((_perPixelWritesPosition && !MeshForPixelWarp) || Read("mv_enabled", 0f) >= 0.5f)
            return;
        if (!_perPixelWritesMotion && !MeshRequested)
            return;

        // The per-pixel stage starts from the per-frame state but writes to its own slots.
        Array.Copy(_slots, _pixelSlots, _slots.Length);
        BuildMesh(
            Math.Max(0.01f, Read("zoom", Preset.Zoom)),
            Read("zoomexp", 1f),
            Read("rot", 0f),
            Read("cx", 0f),
            Read("cy", 0f),
            Read("dx", 0f),
            Read("dy", 0f),
            Read("sx", 1f),
            Read("sy", 1f),
            Read("warp", Preset.Warp));
        _meshBuiltThisFrame = true;
    }

    /// <summary>
    /// Gets the frame the mesh warp samples: the feedback the CPU warp reads, which is the previous
    /// frame's pre-comp composite. A GPU warp binds it as its source texture, so it must stay valid
    /// until the next frame is rendered.
    /// </summary>
    public PixelBuffer MeshSource => _previous;

    /// <summary>
    /// Gets the overlay-only frame the last <see cref="RenderOverlayFrame"/> drew: the waveform,
    /// spectrum, motion vectors, and shapes without the feedback warp. A GPU pipeline composites it
    /// over its own warped frame, so the overlay stays the CPU's vector drawing.
    /// </summary>
    public PixelBuffer OverlayFrame => _fresh;

    /// <summary>
    /// Gets or sets a value indicating whether the renderer publishes the Milkdrop shape fills as
    /// geometry instead of rasterizing them into <see cref="OverlayFrame"/>. A GPU overlay draws
    /// <see cref="ShapeFills"/> and blends the overlay bitmap over it, which still carries the shape
    /// borders and the waves. The rasterized fill is skipped while this is set, so a caller that
    /// publishes the fills must draw them.
    /// </summary>
    public bool CollectShapeFills { get; set; }

    /// <summary>
    /// Gets the shape fills collected for the last frame, in draw order. The list is empty unless
    /// <see cref="CollectShapeFills"/> is set.
    /// </summary>
    public IReadOnlyList<ShapeFill> ShapeFills => _shapeFills;

    private readonly List<ShapeFill> _shapeFills = [];

    /// <summary>
    /// Draws only the waveform and spectrum overlay, without touching the feedback buffers, and
    /// leaves it in <see cref="OverlayFrame"/>. It is the overlay half of the frame for a GPU
    /// pipeline, which owns the warp and the frame passes itself.
    /// </summary>
    /// <param name="audio">Audio values to draw.</param>
    public void RenderOverlayFrame(IVisualizerAudioSource audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        CopyAudio(audio);
        // The overlay reads the live wave and shape variables, so they have to be seeded here too;
        // without it the overlay would draw from a stale slot array.
        SeedFrameVariables();
        DrawOverlay();
    }

    /// <summary>
    /// Copies the audio levels and the Milkdrop-relative loudness the preset expressions read. The
    /// normalized <see cref="Bass"/> and the relative <see cref="BassRelative"/> are different
    /// contracts: the preset's <c>bass</c> variable is the relative one, which can exceed one, so a
    /// condition such as <c>above(bass, 1.2)</c> can fire.
    /// </summary>
    /// <param name="audio">Audio source of the frame.</param>
    private void CopyAudio(IVisualizerAudioSource audio)
    {
        Bass = audio.Bass;
        Mid = audio.Mid;
        Treble = audio.Treble;
        Volume = audio.Volume;
        BassRelative = audio.BassRelative;
        MidRelative = audio.MidRelative;
        TrebleRelative = audio.TrebleRelative;
        BassAttRelative = audio.BassAttRelative;
        MidAttRelative = audio.MidAttRelative;
        TrebleAttRelative = audio.TrebleAttRelative;
    }

    /// <summary>
    /// Reads the per-frame values the frame passes use. It must be called after the per-frame block
    /// has run, which is the case once <see cref="RenderFrame"/> has returned.
    /// </summary>
    /// <returns>The frame's pass parameters.</returns>
    public VisualizerFrameParameters ReadFrameParameters() => new(
        Math.Clamp(Read("decay", Preset.Decay), 0f, 1f),
        BlurPasses(),
        Math.Clamp(Read("darken_center", 0f), 0f, 1f),
        Math.Clamp(Read("fGammaAdj", 1f), 0.1f, 10f),
        Math.Clamp(Read("echo_zoom", Read("fVideoEchoZoom", 1f)), 0.1f, 4f),
        Math.Clamp(Read("echo_alpha", Read("fVideoEchoAlpha", 0f)), 0f, 1f),
        (int)Math.Clamp(Read("echo_orient", Read("nVideoEchoOrientation", 0f)), 0f, 3f),
        ToPublicBand(ReadBand("ob_", 0f, 0.02f)),
        ToPublicBand(ReadBand("ib_", 0.06f, 0.02f)),
        (float)_elapsed * Read("fWarpAnimSpeed", 1f))
        {
            WarpScale = Read("fWarpScale", 1f),
            TextureWrap = Read("bTexWrap", 0f) != 0f,
            ShaderAmount = Math.Clamp(Read("shader", 1f), 0f, 1f),
            HueTime = (float)_elapsed * 30f,
            HueOffsets = (_hueOffsets[0], _hueOffsets[1], _hueOffsets[2], _hueOffsets[3]),
        };

    /// <summary>Publishes one border band with the public frame-parameter type.</summary>
    /// <param name="band">Band read from the preset's keys.</param>
    /// <returns>The same band as a public value.</returns>
    private static VisualizerBorderBand ToPublicBand(SkiaShaderRunner.BorderBand band) =>
        new(band.Inset, band.Thickness, band.Red, band.Green, band.Blue, band.Alpha);

    /// <summary>
    /// Copies the per-vertex motion the last rendered frame evaluated, so a GPU warp can upload it as
    /// vertex attributes. The values are ordered as <see cref="MeshValues"/> describes, row by row,
    /// with <see cref="MeshGridX"/> + 1 vertices per row.
    /// </summary>
    /// <param name="destination">Destination for the values.</param>
    /// <param name="meshX">Receives the mesh grid's column count.</param>
    /// <param name="meshY">Receives the mesh grid's row count.</param>
    /// <returns>
    /// <see langword="true"/> when the last frame built a mesh; <see langword="false"/> when the
    /// preset writes no per-pixel motion or the mesh was not requested, in which case the CPU warp is
    /// the only picture.
    /// </returns>
    /// <exception cref="ArgumentException">The destination is too small.</exception>
    public bool TryCopyMeshMotion(Span<float> destination, out int meshX, out int meshY)
    {
        meshX = MeshGridX;
        meshY = MeshGridY;
        if (!_meshBuiltThisFrame)
            return false;

        if (destination.Length < _meshMotion.Length)
            throw new ArgumentException("The destination is smaller than the mesh.", nameof(destination));

        _meshMotion.AsSpan().CopyTo(destination);
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether this preset's warp stage may run in parallel at all. It is
    /// <see langword="false"/> when the per-pixel code writes a value another pixel could read, or
    /// when a warp shader keeps interpreter state, so callers can report why a preset stays on one
    /// thread.
    /// </summary>
    public bool WarpParallelismAvailable => _canParallelizeWarp;

    /// <summary>Gets the band levels of the last rendered frame.</summary>
    public ReadOnlySpan<float> Bands => _audio is null ? default : _audio.Bands;

    /// <summary>Gets the waveform of the last rendered frame.</summary>
    public ReadOnlySpan<float> Waveform => _audio is null ? default : _audio.Waveform;

    /// <summary>Gets the spectrum of the last rendered frame.</summary>
    public ReadOnlySpan<float> Spectrum => _audio is null ? default : _audio.Spectrum;

    /// <summary>Gets the bass energy of the last rendered frame.</summary>
    public float Bass { get; private set; }

    /// <summary>Gets the mid energy of the last rendered frame.</summary>
    public float Mid { get; private set; }

    /// <summary>Gets the treble energy of the last rendered frame.</summary>
    public float Treble { get; private set; }

    /// <summary>Gets the overall level of the last rendered frame.</summary>
    public float Volume { get; private set; }

    /// <summary>Gets the bass level relative to its long-term average for the last rendered frame.</summary>
    public float BassRelative { get; private set; } = 1f;

    /// <summary>Gets the mid level relative to its long-term average for the last rendered frame.</summary>
    public float MidRelative { get; private set; } = 1f;

    /// <summary>Gets the treble level relative to its long-term average for the last rendered frame.</summary>
    public float TrebleRelative { get; private set; } = 1f;

    /// <summary>Gets the attenuated bass level relative to its long-term average.</summary>
    public float BassAttRelative { get; private set; } = 1f;

    /// <summary>Gets the attenuated mid level relative to its long-term average.</summary>
    public float MidAttRelative { get; private set; } = 1f;

    /// <summary>Gets the attenuated treble level relative to its long-term average.</summary>
    public float TrebleAttRelative { get; private set; } = 1f;

    /// <summary>Gets the left channel's waveform of the last rendered frame.</summary>
    public ReadOnlySpan<float> WaveformLeft => _audio is null ? default : _audio.WaveformLeft;

    /// <summary>Gets the right channel's waveform of the last rendered frame.</summary>
    public ReadOnlySpan<float> WaveformRight => _audio is null ? default : _audio.WaveformRight;

    /// <summary>Gets the left channel's spectrum of the last rendered frame.</summary>
    public ReadOnlySpan<float> SpectrumLeft => _audio is null ? default : _audio.SpectrumLeft;

    /// <summary>Gets the right channel's spectrum of the last rendered frame.</summary>
    public ReadOnlySpan<float> SpectrumRight => _audio is null ? default : _audio.SpectrumRight;

    /// <summary>Renders one frame.</summary>
    /// <param name="audio">Audio values to react to.</param>
    /// <param name="deltaSeconds">Seconds since the previous frame.</param>
    public void RenderFrame(IVisualizerAudioSource audio, double deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        CopyAudio(audio);
        _frameDelta = double.IsFinite(deltaSeconds) && deltaSeconds > 0d ? deltaSeconds : 1d / 60d;
        _elapsed += Math.Clamp(_frameDelta, 0d, 0.25d);

        _clock.Restart();
        _mark = 0d;
        SeedFrameVariables();
        if (!_initialized)
        {
            Preset.PerFrameInit.Execute(_slots);
            Preset.PerPixelInit.Execute(_slots);
            foreach (var wave in Preset.Waves)
                InitializeElement(wave, wave.Init);
            foreach (var shape in Preset.Shapes)
                InitializeElement(shape, shape.Init);
            _initialized = true;
        }

        // A stage that never returns leaves the last line of the trace as the stage it hung in, so
        // the first frames of a preset are traced stage by stage. Later frames are not, or a busy
        // visualizer would fill the log.
        var trace = _frame < 2 ? StageLogger : null;
        trace?.Invoke($"stage=perFrame begin frame={_frame} preset={Preset.Name}");
        ProbeStage("perFrame", _previous);
        try
        {
            Preset.PerFrame.Execute(_slots);
        }
        catch (Exception exception)
        {
            // A preset program that throws would fail every frame, so it is stopped once and the
            // reason kept, instead of the picture freezing with no explanation.
            _presetProgramsFailed = true;
            PresetError = "per_frame: " + exception.GetType().Name + ": " + exception.Message;
        }
        var decay = Math.Clamp(Read("decay", Preset.Decay), 0f, 1f);
        // The reference hands the animated hue shade to every shader as hue_shader, so it is computed
        // before anything can return early; a GPU pipeline reads the values through WriteShaderUniforms.
        ComputeHueShades();
        var useShaders = HasShaders;
        if (ExpressionsOnly)
        {
            // A GPU pipeline owns the frame, so the CPU runs the expressions and draws the overlay,
            // and nothing else. The overlay is drawn here rather than after the passes because the
            // passes are not the CPU's job in this mode.
            PrepareMeshForGpu();
            if (_meshBuiltThisFrame)
            {
                DrawOverlay();
                var overlayOnly = Mark();
                RecordTimings(0d, 0d, 0d, overlayOnly, 0d, 0d);
                _frame++;
                return;
            }

            // No mesh means the GPU cannot represent this preset's warp, so the frame falls through to
            // the full CPU path. The caller sees that through TryCopyMeshMotion returning false.
        }
        if (useShaders)
            SeedCompiledShaderFrame();
        trace?.Invoke($"stage=warp begin frame={_frame}");
        ProbeStage("warp", _previous);
        Warp(useShaders);
        var warp = Mark();
        trace?.Invoke($"stage=blur begin frame={_frame}");
        ProbeStage("blur", _warped);

        // The reference's warp fragment shader multiplies the sampled colour by the decay
        // (frag_COLOR = vec4(decay, ...)), so the decay belongs to the warp and the blur passes that
        // follow see the faded frame.
        _warped.Scale(decay);

        for (var pass = 0; pass < BlurPasses(); pass++)
            _warped.Blur();
        var blur = Mark();

        // The reference draws the shapes and waves onto the warped frame before the centre darkening
        // and the border, so those later passes cover the overlay instead of being covered by it.
        trace?.Invoke($"stage=overlay begin frame={_frame}");
        ProbeStage("overlay", _warped);
        DrawOverlay();
        Composite();
        ProbeStage("overlayDone", _warped);
        var overlay = Mark();

        DarkenCenter();
        ProbeStage("darken", _warped);
        DrawBorders();
        ProbeStage("borders", _warped);

        // The reference's final composite is either the custom comp shader or the legacy video echo
        // and gamma adjustment, never both, so a preset with a comp shader does not get the legacy
        // effects applied to the input the comp shader reads.
        var hasCompShader = useShaders && _compShaders.Count > 0;
        if (!hasCompShader)
        {
            ApplyVideoEcho();
            ApplyHueShadeAndGamma();
        }

        var postProcess = Mark();
        Publish();
        var composite = Mark();

        var shader = 0d;
        _compStageRan = false;
        if (useShaders)
        {
            trace?.Invoke($"stage=compShader begin frame={_frame}");
            ProbeStage("compShader", _fresh);
            _shaderClock.Restart();
            ApplyCompShaders();
            _shaderClock.Stop();
            shader = _shaderClock.Elapsed.TotalMilliseconds;
            ProbeStage("compDone", _fresh);
        }

        LastShaderMilliseconds = shader + _warpShaderMilliseconds;
        trace?.Invoke($"stage=done frame={_frame}");
        ProbeStage("done", _fresh);
        // The feedback is the pre-comp composite, exactly like the reference: the comp shader is a
        // display pass and must not feed its own output back. Without a comp stage the composite is
        // already the pre-comp frame, so it is the feedback too.
        _previous.CopyFrom(_compStageRan ? _frameCopy : _fresh);
        ProbeStage("feedback", _previous);
        _frame++;
        RecordTimings(warp, blur, postProcess, overlay, composite, LastShaderMilliseconds);
    }

    /// <summary>
    /// Reports the frame's mean brightness as it enters a named stage by sampling the buffer that
    /// stage reads. The value is what the previous stage produced, which is what lets a diagnostic
    /// attribute a white frame to the stage that turned it white.
    /// </summary>
    /// <param name="stage">Stage name, matching the stage trace.</param>
    /// <param name="buffer">Buffer the stage reads.</param>
    private void ProbeStage(string stage, PixelBuffer buffer) =>
        StageBrightnessLogger?.Invoke(stage, buffer.MeanBrightness());

    /// <summary>
    /// Draws only the waveform and spectrum overlay, without the feedback warp. This is the
    /// reduce-motion path: the picture still shows the music but nothing moves.
    /// </summary>
    /// <param name="audio">Audio values to draw.</param>
    public void RenderOverlayOnly(IVisualizerAudioSource audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        CopyAudio(audio);
        _clock.Restart();
        _mark = 0d;
        DrawOverlay();
        var overlay = Mark();
        _previous.CopyFrom(_fresh);
        _frame++;
        RecordTimings(0d, 0d, 0d, overlay, 0d, 0d);
    }

    /// <summary>Clears every buffer and restarts the preset on the next frame.</summary>
    public void Reset()
    {
        _previous.Clear();
        _warped.Clear();
        _fresh.Clear();
        _frameCopy.Clear();
        foreach (var blurLevel in _blurLevels) blurLevel.Clear();
        Array.Clear(_blurLevelReady);
        _shaderPixelTarget = ShaderPixelBudget;
        _shaderPixelsUsed = 0;
        _shaderGridReduced = false;
        _perPixelSuspended = false;
        _skipPerPixelThisFrame = false;
        _framesSinceSuspend = 0;
        Array.Clear(_slots);
        _elementFrames.Clear();
        _elementPoints.Clear();
        _elementInitT.Clear();
        _audio = null;
        _initialized = false;
        _frame = 0;
        _elapsed = 0;
        Array.Clear(_motionX);
        Array.Clear(_motionY);
        Array.Clear(_motionCount);
        Bass = Mid = Treble = Volume = 0f;
        BassRelative = MidRelative = TrebleRelative = 1f;
        BassAttRelative = MidAttRelative = TrebleAttRelative = 1f;
        Timings = default;
        ResetTimings();
    }

    /// <summary>Releases the native resources the Skia comp pass holds.</summary>
    public void Dispose()
    {
        _skiaComp?.Dispose();
        _skiaComp = null;
        _skiaWarp?.Dispose();
        _skiaWarp = null;
    }

    /// <summary>Seeds the standard variables a preset expects for this frame.</summary>
    private void SeedFrameVariables()
    {
        var width = _previous.Width;
        var height = _previous.Height;
        Write("time", (float)_elapsed);
        // The roam vectors only depend on the time, so they are computed once per frame rather than
        // once per pixel.
        var roamTime = (float)_elapsed;
        _roam[0] = RoamVector(roamTime, sine: false, slow: false);
        _roam[1] = RoamVector(roamTime, sine: true, slow: false);
        _roam[2] = RoamVector(roamTime, sine: false, slow: true);
        _roam[3] = RoamVector(roamTime, sine: true, slow: true);
        // A first-frame fps of zero makes a preset that divides by fps produce an infinity that
        // sticks in its accumulators, so the measured rate is used from the first frame.
        Write("fps", (float)(1d / _frameDelta));
        Write("frame", _frame);
        Write("monitor", 1f);
        // Milkdrop's band variables are relative to their long-term average and can exceed one, so a
        // condition such as above(bass, 1.2) fires; the attenuated values are the smoothed relatives.
        Write("bass", BassRelative);
        Write("mid", MidRelative);
        Write("treb", TrebleRelative);
        Write("vol", (BassRelative + MidRelative + TrebleRelative) * 0.333f);
        Write("bass_att", BassAttRelative);
        Write("mid_att", MidAttRelative);
        Write("treb_att", TrebleAttRelative);
        WarpSampling.GetAspect(width, height, out var frameAspectX, out var frameAspectY);
        // Milkdrop binds the preset's aspectx/aspecty to the *inverse* aspect factors
        // (var_pf_aspectx = m_fInvAspectX, plugin.cpp), so a landscape frame is (1, width/height):
        // Petal/Mashup presets multiply their per-vertex position delta by aspecty, and the
        // non-inverse value compressed that delta a second time and flattened their warp.
        Write("aspectx", 1f / frameAspectX);
        Write("aspecty", 1f / frameAspectY);
        Write("pixelsx", width);
        Write("pixelsy", height);
        // Milkdrop's mesh is the sampling grid, and the progress through a preset's playlist time.
        // There is no playlist time here, so progress stays zero and the grid is the frame.
        Write("meshx", width);
        Write("meshy", height);
        Write("progress", 0f);
        // Milkdrop keeps a random vector per frame; presets use it to vary a shader without
        // changing it every pixel.
        for (var index = 0; index < _randFrame.Length; index++)
            _randFrame[index] = Random.Shared.NextSingle();
        // The per-frame defaults a preset can override before the warp reads them back.
        Write("decay", Preset.Decay);
        Write("fDecay", Preset.Decay);
        Write("fGammaAdj", 1f);
        Write("fWarpAnimSpeed", 1f);
        Write("fWarpScale", 1f);
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
        Write("blur1_min", 0f); Write("blur1_max", 1f);
        Write("blur2_min", 0f); Write("blur2_max", 1f);
        Write("blur3_min", 0f); Write("blur3_max", 1f);
        Write("blur1", Preset.BlurLevel);
        Write("blur2", 0f);
        Write("blur3", 0f);
        Write("darken_center", 0f);
        // Milkdrop's default wave mode is the single line (six), which its idle preset confirms.
        Write("wave_mode", 6f);
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
        // A warp shader's blur levels are rebuilt for this frame; the cached buffer would otherwise
        // keep the previous frame's picture, because the buffer object is reused.
        Array.Clear(_blurLevelReady);
        var zoom = Math.Max(0.01f, Read("zoom", Preset.Zoom));
        var zoomExp = Read("zoomexp", 1f);
        var rotation = Read("rot", 0f);
        var centreX = Read("cx", 0f);
        var centreY = Read("cy", 0f);
        var offsetX = Read("dx", 0f);
        var offsetY = Read("dy", 0f);
        var stretchX = Read("sx", 1f);
        var stretchY = Read("sy", 1f);
        var warpAmount = Read("warp", Preset.Warp);
        // The reference's warp animation time is the preset time times the animation speed (whose
        // default is one) and its warp scale defaults to one.
        var warpTime = (float)_elapsed * Read("fWarpAnimSpeed", 1f);
        var warpScale = Read("fWarpScale", 1f);
        // The polar pair costs a square root and an arctangent per pixel, so it is only computed
        // when the preset's own code or the zoom exponent actually needs it.
        var needsRadius = _perPixelUsesRadius || zoomExp != 1f;
        var needsAngle = _perPixelUsesAngle;
        var recordMotion = Read("mv_enabled", 0f) >= 0.5f;
        if (!recordMotion)
        {
            // Stale samples would otherwise survive into the next time the grid is drawn.
            Array.Clear(_motionX);
            Array.Clear(_motionY);
            Array.Clear(_motionCount);
        }

        // The Skia path runs the warp stage when it can: the geometric warp for a preset with no
        // per-pixel code and no warp shader, and a warp shader (with or without the per-pixel
        // expression block) as a runtime effect. The geometric warp is a frame pass and stays on the
        // interpreter unless frame passes are enabled; the warp shader is a per-pixel pass and uses
        // the compiled SkSL. A per-pixel program that changes a value another pixel could read, or
        // that records motion vectors, stays on the interpreter, which is the reference.
        if (!_perPixelWritesMotion && !recordMotion)
        {
            if (_warpShaders.Count == 0 && Preset.PerPixel.IsEmpty)
            {
                if (!UseSkiaFramePasses)
                {
                    // Fall through to the interpreter's geometric warp.
                }
                else
                {
                    try
                    {
                        SkiaShaderRunner.Warp(
                            _previous,
                            _warped,
                            new SkiaShaderRunner.WarpParameters(
                                zoom,
                                zoomExp,
                                rotation,
                                centreX,
                                centreY,
                                offsetX,
                                offsetY,
                                stretchX,
                                stretchY));
                        _warpShaderMilliseconds = 0d;
                        return;
                    }
                    catch (Exception exception)
                    {
                        ShaderError = "warp (skia): " + exception.GetType().Name + ": " + exception.Message;
                    }
                }
            }
            // The Skia warp pass rasterises a runtime effect on one thread, while the interpreter
            // splits the rows across every core when nothing the program writes can leave its pixel.
            // Measured at 960 x 540, a parallelizable per-pixel program costs about 18 ms through
            // the interpreter against 65 ms through Skia, so the parallel interpreter wins whenever
            // it is available; the Skia pass only pays off for a program that cannot be split.
            else if (UseSkiaPasses && !_canParallelizeWarp && TrySkiaWarpPass(
                zoom,
                zoomExp,
                rotation,
                centreX,
                centreY,
                offsetX,
                offsetY,
                stretchX,
                stretchY))
            {
                return;
            }
        }

        var width = _warped.Width;
        var height = _warped.Height;
        var perPixel = Preset.PerPixel;

        var perPixelMotion = _perPixelWritesMotion && !recordMotion;

        // The per-pixel stage starts from the per-frame state but writes to its own slots, so its
        // user variables cannot leak back into the spring or any other per-frame variable.
        Array.Copy(_slots, _pixelSlots, _slots.Length);
        for (var worker = 0; worker < _workerPixelSlots.Length; worker++)
            Array.Copy(_slots, _workerPixelSlots[worker], _slots.Length);

        // Measured from here, not from the frame start: the ceiling guards a runaway per-pixel
        // program, and it must not trip on the one-off JIT cost of the earlier stages.
        _warpClock.Restart();

        // A per-pixel program that loops can cost seconds over a full frame. Once the stage passes
        // its ceiling the program is left out for the rest of the frame, so the window keeps
        // drawing instead of hanging; the next frames retry it. The flag has to be reset here,
        // otherwise one slow frame would leave the program out for good.
        _skipPerPixelThisFrame = false;
        if (_perPixelSuspended)
        {
            if (++_framesSinceSuspend < 60)
                _skipPerPixelThisFrame = true;
            else
            {
                _framesSinceSuspend = 0;
                _perPixelSuspended = false;
            }
        }

        // Centre, stretch, rotate, and zoom the sampling position of one point. A preset that
        // changes one of these inside per_pixel sees the change here, on the next point, the way
        // Milkdrop does it. Both the per-pixel fill and the shader grid go through this, so the
        // two paths cannot drift apart.
        void ComputeSample(double[] slots, float normalizedX, float normalizedY, out float sampleX, out float sampleY)
        {
            var zoomNow = zoom;
            var zoomExpNow = zoomExp;
            var rotationNow = rotation;
            var centreXNow = centreX;
            var centreYNow = centreY;
            var offsetXNow = offsetX;
            var offsetYNow = offsetY;
            var stretchXNow = stretchX;
            var stretchYNow = stretchY;
            var warpNow = warpAmount;
            if (perPixelMotion)
            {
                zoomNow = Math.Max(0.01f, Read(slots, _slotZoom, zoom));
                zoomExpNow = Read(slots, _slotZoomExp, zoomExp);
                rotationNow = Read(slots, _slotRot, rotation);
                centreXNow = Read(slots, _slotCx, centreX);
                centreYNow = Read(slots, _slotCy, centreY);
                offsetXNow = Read(slots, _slotDx, offsetX);
                offsetYNow = Read(slots, _slotDy, offsetY);
                stretchXNow = Read(slots, _slotSx, stretchX);
                stretchYNow = Read(slots, _slotSy, stretchY);
                warpNow = Read(slots, _slotWarp, warpAmount);
            }

            WarpSampling.GetAspect(width, height, out var aspectX, out var aspectY);
            WarpSampling.SamplePosition(
                normalizedX,
                normalizedY,
                zoomNow,
                zoomExpNow,
                rotationNow,
                centreXNow,
                centreYNow,
                offsetXNow,
                offsetYNow,
                stretchXNow,
                stretchYNow,
                aspectX,
                aspectY,
                needsRadius,
                warpNow,
                warpTime,
                warpScale,
                out var warpedX,
                out var warpedY);
            if (needsRadius || needsAngle)
            {
                var radius = MathF.Sqrt(
                    ((normalizedX * aspectX) * (normalizedX * aspectX)) + ((normalizedY * aspectY) * (normalizedY * aspectY)));
                if (needsRadius)
                    Write(slots, _slotRad, radius);
                if (needsAngle)
                    Write(slots, _slotAng, MathF.Atan2(normalizedY * aspectY, normalizedX * aspectX));
            }

            if (_perPixelUsesX)
                Write(slots, _slotX, warpedX);
            if (_perPixelUsesY)
                Write(slots, _slotY, warpedY);
            if (!_skipPerPixelThisFrame && !_presetProgramsFailed)
                perPixel.Execute(slots);

            sampleX = _perPixelUsesX ? Read(slots, _slotX, warpedX) : warpedX;
            sampleY = _perPixelUsesY ? Read(slots, _slotY, warpedY) : warpedY;
            if (recordMotion)
                RecordMotion(normalizedX, normalizedY, sampleX, sampleY);
        }

        void WarpRows(int worker, int from, int to)
        {
            // The span is taken inside the body, because a local function cannot capture one.
            var target = _warped.RawPixels;
            // A parallel worker gets its own slots and its own sample scratch, because the shared
            // ones would let two pixels race on the value a per-pixel program just wrote.
            var slots = worker < 0 ? _pixelSlots : _workerPixelSlots[worker];
            var sample = worker < 0 ? _sample : _workerSample[worker];
            for (var y = from; y < to; y++)
            {
                if (!_perPixelSuspended && _warpClock.Elapsed.TotalMilliseconds > WarpStageBudgetMilliseconds)
                {
                    // The program is the expensive part of the stage, so it is what gives way.
                    _perPixelSuspended = true;
                    _skipPerPixelThisFrame = true;
                }

                var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
                for (var x = 0; x < width; x++)
                {
                    var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                    ComputeSample(slots, normalizedX, normalizedY, out var sampleX, out var sampleY);

                    _previous.SampleBilinear((sampleX * 0.5f) + 0.5f, (sampleY * 0.5f) + 0.5f, sample);

                    var offset = (((y * width) + x) * 4);
                    target[offset] = sample[0];
                    target[offset + 1] = sample[1];
                    target[offset + 2] = sample[2];
                    target[offset + 3] = sample[3];
                }
            }
        }

        if (useShaders && _warpShaders.Count > 0)
        {
            // A warp shader is a per-pixel program, which the CPU cannot afford over a full frame
            // at a useful resolution. It runs on the same bounded grid as the comp pass and is
            // scaled back up, so the preset keeps its picture instead of losing the shader.
            _warpShaderClock.Restart();
            WarpShaderGrid(ComputeSample, width, height);
            _warpShaderClock.Stop();
            _warpShaderMilliseconds = _warpShaderClock.Elapsed.TotalMilliseconds;
            return;
        }

        _warpShaderMilliseconds = 0d;
        _meshBuiltThisFrame = false;

        // Milkdrop runs the per-pixel program once per mesh vertex and interpolates the motion it
        // produced across the quad. Our per-pixel path runs it for every pixel, which is finer than
        // the reference and costs more; the mesh is the faithful one. A program that writes the
        // sample position has no interpolated meaning, and one that records motion vectors keeps
        // the per-pixel path. A GPU warp needs a mesh for every preset, so requesting it without a
        // per-pixel motion program yields the uniform mesh the frame motion describes.
        if ((!_perPixelWritesPosition || MeshForPixelWarp) &&
            !recordMotion &&
            (_perPixelWritesMotion ? MeshPerPixelEnabled || MeshRequested : MeshRequested))
        {
            BuildMesh(zoom, zoomExp, rotation, centreX, centreY, offsetX, offsetY, stretchX, stretchY, warpAmount);
            _meshBuiltThisFrame = true;
            if (MeshPerPixelEnabled && _perPixelWritesMotion)
            {
                if (ParallelismEnabled)
                    ParallelRows.For(height, MeshRows);
                else
                    MeshRows(-1, 0, height);
                return;
            }
        }

        if (ParallelismEnabled && _canParallelizeWarp && !recordMotion)
        {
            // Each worker starts from the per-frame values, so a per-pixel program sees the same
            // frame variables it would on a single thread.
            for (var worker = 0; worker < _workerPixelSlots.Length; worker++)
                Array.Copy(_pixelSlots, _workerPixelSlots[worker], _slots.Length);
            ParallelRows.For(height, WarpRows);
        }
        else
        {
            WarpRows(-1, 0, height);
        }

        // Sample the transformed coordinates rather than interpolating the motion parameters.
        float MeshUvValue(int vertexX, int vertexY, int value) =>
            _meshUv[((((vertexY * (MeshGridX + 1)) + vertexX) * 2) + value)];

        float InterpolateUv(float meshX, float meshY, int value)
        {
            var x0 = (int)meshX;
            if (x0 >= MeshGridX)
                x0 = MeshGridX - 1;
            var y0 = (int)meshY;
            if (y0 >= MeshGridY)
                y0 = MeshGridY - 1;
            var fractionX = meshX - x0;
            var fractionY = meshY - y0;
            var top = MeshUvValue(x0, y0, value) +
                      ((MeshUvValue(x0 + 1, y0, value) - MeshUvValue(x0, y0, value)) * fractionX);
            var bottom = MeshUvValue(x0, y0 + 1, value) +
                         ((MeshUvValue(x0 + 1, y0 + 1, value) - MeshUvValue(x0, y0 + 1, value)) * fractionX);
            return top + ((bottom - top) * fractionY);
        }

        void MeshRows(int worker, int from, int to)
        {
            var target = _warped.RawPixels;
            var sample = worker < 0 ? _sample : _workerSample[worker];
            for (var y = from; y < to; y++)
            {
                var normalizedY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
                var meshY = Math.Clamp((normalizedY + 1f) * 0.5f * MeshGridY, 0f, MeshGridY - 0.0001f);
                for (var x = 0; x < width; x++)
                {
                    var normalizedX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                    var meshX = Math.Clamp((normalizedX + 1f) * 0.5f * MeshGridX, 0f, MeshGridX - 0.0001f);

                    // The reference transforms at the vertices, so the interpolated coordinate is
                    // what the warp samples with, not the interpolated motion.
                    var sampleX = InterpolateUv(meshX, meshY, 0);
                    var sampleY = InterpolateUv(meshX, meshY, 1);
                    _previous.SampleBilinear((sampleX * 0.5f) + 0.5f, (sampleY * 0.5f) + 0.5f, sample);

                    var offset = (((y * width) + x) * 4);
                    target[offset] = sample[0];
                    target[offset + 1] = sample[1];
                    target[offset + 2] = sample[2];
                    target[offset + 3] = sample[3];
                }
            }
        }
    }

    /// <summary>
    /// Evaluates the per-pixel program once per mesh vertex and keeps the motion it produced, so the
    /// warp can interpolate it across the frame the way Milkdrop does. The program shares the
    /// preset's slot array with the per-frame block, so it runs on the renderer's own slots and the
    /// per-frame motion values are put back afterwards; a later stage must see the frame's values,
    /// not the last vertex's. All ten motion variables are reseeded before each execution, so
    /// compound assignments cannot accumulate across vertices. Unvisited vertices retain frame
    /// motion if the warp budget is exceeded.
    /// </summary>
    /// <param name="zoom">Per-frame zoom factor.</param>
    /// <param name="zoomExp">Per-frame zoom exponent.</param>
    /// <param name="rotation">Per-frame rotation in radians.</param>
    /// <param name="centreX">Per-frame rotation and zoom centre x.</param>
    /// <param name="centreY">Per-frame rotation and zoom centre y.</param>
    /// <param name="offsetX">Per-frame translation x.</param>
    /// <param name="offsetY">Per-frame translation y.</param>
    /// <param name="stretchX">Per-frame horizontal stretch.</param>
    /// <param name="stretchY">Per-frame vertical stretch.</param>
    /// <param name="warp">Per-frame warp amount.</param>
    private void BuildMesh(
        float zoom,
        float zoomExp,
        float rotation,
        float centreX,
        float centreY,
        float offsetX,
        float offsetY,
        float stretchX,
        float stretchY,
        float warp)
    {
        // The reference's per-vertex position and polar pair use its own aspect, not the preset's
        // aspectx/aspecty variables, so the mesh must use the same factors the warp does.
        WarpSampling.GetAspect(_warped.Width, _warped.Height, out var aspectX, out var aspectY);
        for (var vertex = 0; vertex < (MeshGridX + 1) * (MeshGridY + 1); vertex++)
        {
            var index = vertex * MeshValues;
            _meshMotion[index] = zoom;
            _meshMotion[index + 1] = zoomExp;
            _meshMotion[index + 2] = rotation;
            _meshMotion[index + 3] = centreX;
            _meshMotion[index + 4] = centreY;
            _meshMotion[index + 5] = offsetX;
            _meshMotion[index + 6] = offsetY;
            _meshMotion[index + 7] = stretchX;
            _meshMotion[index + 8] = stretchY;
            _meshMotion[index + 9] = warp;
        }

        if (_perPixelSuspended)
        {
            // The program is still in its cool-down; the mesh keeps the frame values above. The
            // cool-down itself is counted where the per-pixel path counts it, earlier in the stage.
            return;
        }

        var frameZoom = Read(_slots, _slotZoom, zoom);
        var frameZoomExp = Read(_slots, _slotZoomExp, zoomExp);
        var frameRotation = Read(_slots, _slotRot, rotation);
        var frameCentreX = Read(_slots, _slotCx, centreX);
        var frameCentreY = Read(_slots, _slotCy, centreY);
        var frameOffsetX = Read(_slots, _slotDx, offsetX);
        var frameOffsetY = Read(_slots, _slotDy, offsetY);
        var frameStretchX = Read(_slots, _slotSx, stretchX);
        var frameStretchY = Read(_slots, _slotSy, stretchY);
        var frameWarp = Read(_slots, _slotWarp, warp);
        var suspended = false;
        for (var gridY = 0; gridY <= MeshGridY && !suspended; gridY++)
        {
            var normalizedY = (gridY / (float)MeshGridY * 2f) - 1f;
            for (var gridX = 0; gridX <= MeshGridX; gridX++)
            {
                if ((gridX & 63) == 0 && _warpClock.Elapsed.TotalMilliseconds > WarpStageBudgetMilliseconds)
                {
                    // A looping program can still be too expensive; the vertices left over keep the
                    // frame values, and the next frames skip the program until the cool-down ends.
                    _perPixelSuspended = true;
                    suspended = true;
                    break;
                }

                // Motion outputs are local to a vertex. In particular, zoom *= ... must
                // not multiply the preceding vertex's result across the entire mesh.
                Write(_pixelSlots, _slotZoom, frameZoom);
                Write(_pixelSlots, _slotZoomExp, frameZoomExp);
                Write(_pixelSlots, _slotRot, frameRotation);
                Write(_pixelSlots, _slotCx, frameCentreX);
                Write(_pixelSlots, _slotCy, frameCentreY);
                Write(_pixelSlots, _slotDx, frameOffsetX);
                Write(_pixelSlots, _slotDy, frameOffsetY);
                Write(_pixelSlots, _slotSx, frameStretchX);
                Write(_pixelSlots, _slotSy, frameStretchY);
                Write(_pixelSlots, _slotWarp, frameWarp);

                var normalizedX = (gridX / (float)MeshGridX * 2f) - 1f;
                // Milkdrop hands the program the vertex position and the aspect-scaled polar pair.
                var aspectVertexX = normalizedX * aspectX;
                var aspectVertexY = normalizedY * aspectY;
                // Milkdrop's per-vertex x/y are the aspect-scaled position in zero-to-one space,
                // which is the space a preset computes an offset like x - ox in.
                Write(_pixelSlots, _slotX, (normalizedX * 0.5f * aspectX) + 0.5f);
                Write(_pixelSlots, _slotY, (normalizedY * 0.5f * aspectY) + 0.5f);
                Write(_pixelSlots, _slotRad, MathF.Sqrt((aspectVertexX * aspectVertexX) + (aspectVertexY * aspectVertexY)));
                Write(_pixelSlots, _slotAng, -MathF.Atan2(aspectVertexY, aspectVertexX));
                Preset.PerPixel.Execute(_pixelSlots);

                var index = (((gridY * (MeshGridX + 1)) + gridX) * MeshValues);
                _meshMotion[index] = Math.Max(0.01f, Read(_pixelSlots, _slotZoom, zoom));
                _meshMotion[index + 1] = Read(_pixelSlots, _slotZoomExp, zoomExp);
                _meshMotion[index + 2] = Read(_pixelSlots, _slotRot, rotation);
                _meshMotion[index + 3] = Read(_pixelSlots, _slotCx, centreX);
                _meshMotion[index + 4] = Read(_pixelSlots, _slotCy, centreY);
                _meshMotion[index + 5] = Read(_pixelSlots, _slotDx, offsetX);
                _meshMotion[index + 6] = Read(_pixelSlots, _slotDy, offsetY);
                _meshMotion[index + 7] = Read(_pixelSlots, _slotSx, stretchX);
                _meshMotion[index + 8] = Read(_pixelSlots, _slotSy, stretchY);
                _meshMotion[index + 9] = Read(_pixelSlots, _slotWarp, warp);
            }
        }

        // The reference transforms the texture coordinate at each vertex and interpolates the
        // resulting coordinate, so the UV mesh is built here and the warp interpolates it.
        var uvNeedsRadius = _perPixelUsesRadius || zoomExp != 1f;
        var uvWarpTime = (float)_elapsed * Read("fWarpAnimSpeed", 1f);
        for (var gridY = 0; gridY <= MeshGridY; gridY++)
        {
            var normalizedY = (gridY / (float)MeshGridY * 2f) - 1f;
            for (var gridX = 0; gridX <= MeshGridX; gridX++)
            {
                var normalizedX = (gridX / (float)MeshGridX * 2f) - 1f;
                var motion = (((gridY * (MeshGridX + 1)) + gridX) * MeshValues);
                WarpSampling.SamplePosition(
                    normalizedX,
                    normalizedY,
                    _meshMotion[motion],
                    _meshMotion[motion + 1],
                    _meshMotion[motion + 2],
                    _meshMotion[motion + 3],
                    _meshMotion[motion + 4],
                    _meshMotion[motion + 5],
                    _meshMotion[motion + 6],
                    _meshMotion[motion + 7],
                    _meshMotion[motion + 8],
                    aspectX,
                    aspectY,
                    uvNeedsRadius,
                    _meshMotion[motion + 9],
                    uvWarpTime,
                    Read("fWarpScale", 1f),
                    out var sampleX,
                    out var sampleY);
                var uv = (((gridY * (MeshGridX + 1)) + gridX) * 2);
                _meshUv[uv] = sampleX;
                _meshUv[uv + 1] = sampleY;
            }
        }

        // Put the frame's motion values back: the comp shader and the post-processing stages read
        // them, and they mean the frame's values there, not the last vertex's.
        Write(_slots, _slotZoom, frameZoom);
        Write(_slots, _slotZoomExp, frameZoomExp);
        Write(_slots, _slotRot, frameRotation);
        Write(_slots, _slotCx, frameCentreX);
        Write(_slots, _slotCy, frameCentreY);
        Write(_slots, _slotDx, frameOffsetX);
        Write(_slots, _slotDy, frameOffsetY);
        Write(_slots, _slotSx, frameStretchX);
        Write(_slots, _slotSy, frameStretchY);
        Write("warp", frameWarp);
    }

    /// <summary>
    /// Runs the warp stage as a Skia runtime effect when the preset qualifies: at most one warp
    /// shader, no per-shader per-frame block, and a per-pixel program that only writes the values the
    /// engine re-seeds for every pixel. Anything else returns <see langword="false"/> so the caller
    /// keeps the interpreter, which is the reference implementation.
    /// </summary>
    /// <param name="zoom">Zoom factor.</param>
    /// <param name="zoomExp">Zoom exponent.</param>
    /// <param name="rotation">Rotation in radians.</param>
    /// <param name="centreX">Rotation and zoom centre x.</param>
    /// <param name="centreY">Rotation and zoom centre y.</param>
    /// <param name="offsetX">Translation x.</param>
    /// <param name="offsetY">Translation y.</param>
    /// <param name="stretchX">Horizontal stretch.</param>
    /// <param name="stretchY">Vertical stretch.</param>
    /// <returns><see langword="true"/> when the pass ran on the Skia path.</returns>
    private bool TrySkiaWarpPass(
        float zoom,
        float zoomExp,
        float rotation,
        float centreX,
        float centreY,
        float offsetX,
        float offsetY,
        float stretchX,
        float stretchY)
    {
        if (_warpShaders.Count > 1)
            return false;
        if (_warpShaders.Count == 1 && !_warpShaders[0].Shader.PerFrame.IsEmpty)
            return false;
        // The GPU evaluates every pixel independently, so a per-pixel program may only read a value
        // it writes after assigning it; a carried value would make the picture depend on the
        // execution order, which the interpreter has and the GPU does not.
        if (!_perPixelGpuSafe)
            return false;

        if (!_skiaWarpTried)
        {
            _skiaWarpTried = true;
            var program = _warpShaders.Count == 1 ? _warpShaders[0].Shader.Program : null;
            var perPixel = Preset.PerPixel.IsEmpty ? null : Preset.PerPixel;
            // An untranslatable block is expected, not an error: the interpreter renders it.
            _skiaWarp = SkiaShaderRunner.WarpPass.TryCreate(program, perPixel, _textures, out _);
        }

        if (_skiaWarp is null)
            return false;

        try
        {
            _skiaWarp.Render(
                _previous,
                _warped,
                new SkiaShaderRunner.WarpParameters(
                    zoom,
                    zoomExp,
                    rotation,
                    centreX,
                    centreY,
                    offsetX,
                    offsetY,
                    stretchX,
                    stretchY),
                BuildSkiaWarpScalars(_skiaWarp),
                BuildSkiaVectors());
            _warpShaderMilliseconds = 0d;
            return true;
        }
        catch (Exception exception)
        {
            // An unexpected failure must not take the frame with it, so the pass is dropped and the
            // interpreter takes over on the next frame.
            ShaderError = "warp (skia): " + exception.GetType().Name + ": " + exception.Message;
            _skiaWarp.Dispose();
            _skiaWarp = null;
            return false;
        }
    }

    /// <summary>Collects the scalar uniforms the warp shader and its per-pixel block read.</summary>
    /// <param name="pass">Warp pass whose per-pixel variables are seeded.</param>
    /// <returns>The scalar uniforms.</returns>
    private Dictionary<string, float> BuildSkiaWarpScalars(SkiaShaderRunner.WarpPass pass)
    {
        var scalars = BuildSkiaScalars(pass.PerPixelUniforms);
        // The GPU warp has to apply the same time-dependent displacement the CPU warp does.
        scalars["_orynivo_warp"] = Read("warp", Preset.Warp);
        scalars["_orynivo_warpTime"] = (float)_elapsed * Read("fWarpAnimSpeed", 1f);
        scalars["_orynivo_warpScale"] = Read("fWarpScale", 1f);
        return scalars;
    }

    /// <summary>Runs the warp shaders on the adaptive grid and scales the result over the frame.</summary>
    /// <param name="computeSample">Sampling-position evaluator shared with the per-pixel path.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void WarpShaderGrid(
        SamplePosition computeSample,
        int width,
        int height)
    {
        var (gridWidth, gridHeight) = ShaderGrid(width, height);
        if (_shaderOutput is null || _shaderOutput.Width != gridWidth || _shaderOutput.Height != gridHeight)
            _shaderOutput = new PixelBuffer(gridWidth, gridHeight);

        var output = _shaderOutput.Pixels;
        for (var gridY = 0; gridY < gridHeight; gridY++)
        {
            if (_warpShaderClock.Elapsed.TotalMilliseconds > ShaderPassBudgetMilliseconds)
            {
                // Same reason as the comp pass: an overrunning warp shader is abandoned and the
                // grid shrinks, so the frame keeps drawing.
                _shaderPixelTarget = Math.Max(ShaderPixelFloor, _shaderPixelTarget / 4);
                _shaderGridReduced = true;
                break;
            }

            if (!_perPixelSuspended && _warpClock.Elapsed.TotalMilliseconds > WarpStageBudgetMilliseconds)
            {
                _perPixelSuspended = true;
                _skipPerPixelThisFrame = true;
            }

            var normalizedY = gridHeight > 1 ? (gridY / (float)(gridHeight - 1) * 2f) - 1f : 0f;
            for (var gridX = 0; gridX < gridWidth; gridX++)
            {
                var normalizedX = gridWidth > 1 ? (gridX / (float)(gridWidth - 1) * 2f) - 1f : 0f;
                computeSample(_pixelSlots, normalizedX, normalizedY, out var sampleX, out var sampleY);
                RunWarpShaders(
                    (sampleX * 0.5f) + 0.5f,
                    (sampleY * 0.5f) + 0.5f,
                    (normalizedX * 0.5f) + 0.5f,
                    (normalizedY * 0.5f) + 0.5f);

                var offset = ((gridY * gridWidth) + gridX) * 4;
                output[offset] = _sample[0];
                output[offset + 1] = _sample[1];
                output[offset + 2] = _sample[2];
                output[offset + 3] = _sample[3];
            }
        }

        ScaleIntoWarped(_shaderOutput, width, height);
    }

    /// <summary>Scales a shader grid over the warped frame.</summary>
    /// <param name="source">Grid buffer.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void ScaleIntoWarped(PixelBuffer source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            _warped.CopyFrom(source);
            return;
        }

        // Bilinear on purpose: this grid is the base picture, so a nearest-neighbour scale would
        // show its blocks directly. The comp pass below can afford nearest because it is a soft
        // post-process result.
        var target = _warped.RawPixels;
        Span<float> sample = stackalloc float[4];
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) / height;
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                source.SampleBilinear((x + 0.5f) / width, v, sample);
                var offset = (row + x) * 4;
                target[offset] = sample[0];
                target[offset + 1] = sample[1];
                target[offset + 2] = sample[2];
                target[offset + 3] = sample[3];
            }
        }
    }

    /// <summary>Evaluates the sampling position of one point of the warp.</summary>
    /// <param name="slots">Variable slots of the stage.</param>
    /// <param name="normalizedX">Horizontal position in the range minus one to one.</param>
    /// <param name="normalizedY">Vertical position in the range minus one to one.</param>
    /// <param name="sampleX">Resulting horizontal sampling position.</param>
    /// <param name="sampleY">Resulting vertical sampling position.</param>
    private delegate void SamplePosition(double[] slots, float normalizedX, float normalizedY, out float sampleX, out float sampleY);

    /// <summary>
    /// Computes the grid a shader pass runs on. A frame larger than the current pixel target is
    /// reduced proportionally, which is how a shader that costs too much loses resolution instead
    /// of disappearing.
    /// </summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns>The grid size, equal to the frame size when no reduction is needed.</returns>
    private (int Width, int Height) ShaderGrid(int width, int height)
    {
        var pixels = (long)width * height;
        if (pixels <= _shaderPixelTarget)
        {
            _shaderPixelsUsed = width * height;
            return (width, height);
        }

        var scale = MathF.Sqrt(_shaderPixelTarget / (float)pixels);
        var gridWidth = Math.Max(1, (int)(width * scale));
        var gridHeight = Math.Max(1, (int)(height * scale));
        _shaderPixelsUsed = gridWidth * gridHeight;
        _shaderGridReduced = true;
        return (gridWidth, gridHeight);
    }

    /// <summary>
    /// Adapts the shader pixel target from the measured shader cost. A shader that overran the
    /// budget shrinks the grid, one with headroom grows it back, so a heavy preset settles at a
    /// resolution it can afford and keeps drawing.
    /// </summary>
    /// <param name="shaderMilliseconds">Measured shader cost of the frame.</param>
    private void AdaptShaderGrid(double shaderMilliseconds)
    {
        if (_shaderPixelsUsed <= 0 || shaderMilliseconds <= 0d)
            return;

        if (shaderMilliseconds > ShaderTimeBudgetMilliseconds)
        {
            var scaled = _shaderPixelsUsed * (ShaderTimeBudgetMilliseconds / shaderMilliseconds) * 0.9d;
            _shaderPixelTarget = (int)Math.Clamp(scaled, ShaderPixelFloor, ShaderPixelBudget);
        }
        else if (shaderMilliseconds < ShaderTimeBudgetMilliseconds * 0.5d)
        {
            _shaderPixelTarget = Math.Min(ShaderPixelBudget, _shaderPixelTarget * 2);
        }
    }

    /// <summary>Returns the milliseconds since the previous mark and moves the mark.</summary>
    /// <returns>Elapsed milliseconds of the stage that just finished.</returns>
    private double Mark()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        var delta = now - _mark;
        _mark = now;
        return delta;
    }

    /// <summary>Stores the stage timings of this frame and updates the averaging window.</summary>
    /// <param name="warp">Feedback warp stage.</param>
    /// <param name="blur">Blur passes.</param>
    /// <param name="postProcess">Fade, video echo, centre darkening, borders, and gamma.</param>
    /// <param name="overlay">Waveforms, spectrum, motion vectors, and shapes.</param>
    /// <param name="composite">Composite stage.</param>
    /// <param name="shader">Comp shader stage.</param>
    private void RecordTimings(
        double warp,
        double blur,
        double postProcess,
        double overlay,
        double composite,
        double shader)
    {
        var total = _clock.Elapsed.TotalMilliseconds;
        Timings = new RenderTimings(warp, blur, postProcess, overlay, composite, shader, total);
        _sumWarp += warp;
        _sumBlur += blur;
        _sumPostProcess += postProcess;
        _sumOverlay += overlay;
        _sumComposite += composite;
        _sumShader += shader;
        _sumTotal += total;
        _timingFrames++;
        AverageTimings = new RenderTimings(
            _sumWarp / _timingFrames,
            _sumBlur / _timingFrames,
            _sumPostProcess / _timingFrames,
            _sumOverlay / _timingFrames,
            _sumComposite / _timingFrames,
            _sumShader / _timingFrames,
            _sumTotal / _timingFrames);

        // A frame that costs more than the budget means the optional shader work is what has to
        // give, so it loses resolution rather than being dropped; a preset that never drew its
        // shader at all is exactly the empty picture a user reported.
        if (HasShaders)
            AdaptShaderGrid(shader);
    }

    /// <summary>Runs the warp shaders for one pixel and stores the resulting colour.</summary>
    /// <param name="u">Sampling coordinate of the current pixel.</param>
    /// <param name="v">Sampling row of the current pixel.</param>
    /// <param name="originalU">Coordinate of the pixel itself.</param>
    /// <param name="originalV">Row of the pixel itself.</param>
    private void RunWarpShaders(float u, float v, float originalU, float originalV)
    {
        if (_warpShadersFailed)
        {
            _previous.SampleBilinear(u, v, _sample);
            return;
        }

        try
        {
            RunWarpShadersCore(u, v, originalU, originalV);
        }
        catch (Exception exception)
        {
            // A preset shader may call something the engine does not implement, and an unexpected
            // failure must not take the frame with it. Disabling the shaders is the only safe answer.
            ShaderError = "warp: " + exception.GetType().Name + ": " + exception.Message;
            _warpShadersFailed = true;
            _warpShaders.Clear();
            _compiledWarp.Clear();
            _previous.SampleBilinear(u, v, _sample);
        }
    }

    /// <summary>Runs the enabled warp shaders for one pixel.</summary>
    /// <param name="u">Sampling coordinate.</param>
    /// <param name="v">Sampling row.</param>
    /// <param name="originalU">Coordinate of the pixel itself.</param>
    /// <param name="originalV">Row of the pixel itself.</param>
    private void RunWarpShadersCore(float u, float v, float originalU, float originalV)
    {
        for (var index = 0; index < _warpShaders.Count; index++)
        {
            var (interpreter, shader) = _warpShaders[index];
            shader.PerFrame.Execute(_slots);
            var compiled = _compiledWarp[index];
            ShaderValue colour;
            if (compiled.IsCompiled)
            {
                compiled.SetAt(compiled.UvIndex, ShaderValue.Vector(u, v, 0f, 0f, 2));
                compiled.SetAt(compiled.UvOrigIndex, ShaderValue.Vector(originalU, originalV, 0f, 0f, 2));
                SeedPolar(compiled, u, v, 1f, 1f, false);
                colour = compiled.Run(this);
            }
            else
            {
                BindShaderVariables(interpreter, u, v, originalU, originalV, false);
                colour = interpreter.Run();
                if (!interpreter.ReturnedValue && interpreter.Variables.TryGetValue("ret", out var written))
                    colour = written;
            }

            _sample[0] = colour.X;
            _sample[1] = colour.Y;
            _sample[2] = colour.Z;
            _sample[3] = 1f;
        }

        if (_warpShaders.Count == 0)
            _previous.SampleBilinear(u, v, _sample);
    }

    /// <summary>Writes the polar pair for one pixel, and only when the shader reads it.</summary>
    /// <param name="compiled">Compiled shader about to run.</param>
    /// <param name="u">Sampling coordinate in the range zero to one.</param>
    /// <param name="v">Sampling row in the range zero to one.</param>
    /// <param name="aspectX">Horizontal aspect factor (one on a landscape frame).</param>
    /// <param name="aspectY">Vertical aspect factor (at most one).</param>
    /// <param name="mathSpacePolar">
    /// Whether <c>rad</c>/<c>ang</c> use the reference's <c>UvToMathSpace</c> convention, matching
    /// the GPU emitter's comp entry point; the warp shader keeps its own polar pair.
    /// </param>
    private static void SeedPolar(CompiledShader compiled, float u, float v, float aspectX, float aspectY, bool mathSpacePolar)
    {
        if (compiled.RadIndex < 0 && compiled.AngIndex < 0)
            return;

        if (mathSpacePolar)
        {
            var px = ((u * 2f) - 1f) * aspectX;
            var py = ((v * 2f) - 1f) * aspectY;
            var corner = MathF.Sqrt((aspectX * aspectX) + (aspectY * aspectY));
            var angle = MathF.Atan2(py, px);
            if (angle < 0f)
                angle += 2f * MathF.PI;
            compiled.SetAt(compiled.RadIndex, ShaderValue.Scalar(MathF.Sqrt((px * px) + (py * py)) / corner));
            compiled.SetAt(compiled.AngIndex, ShaderValue.Scalar(angle));
            return;
        }

        var x = (u * 2f) - 1f;
        var y = (v * 2f) - 1f;
        compiled.SetAt(compiled.RadIndex, ShaderValue.Scalar(MathF.Sqrt((x * x) + (y * y))));
        compiled.SetAt(compiled.AngIndex, ShaderValue.Scalar(MathF.Atan2(y, x)));
    }

    /// <summary>
    /// Seeds the frame-constant variables of every compiled shader once per frame, so the per-pixel
    /// loop only writes what actually changes per pixel.
    /// </summary>
    private void SeedCompiledShaderFrame()
    {
        var width = _previous.Width;
        var height = _previous.Height;
        Span<ShaderValue> values = stackalloc ShaderValue[CompiledShader.FrameVariables.Length];
        values[0] = ShaderValue.Scalar(Read("time", 0f));
        values[1] = ShaderValue.Scalar(Read("frame", 0f));
        values[2] = ShaderValue.Scalar(Read("fps", 0f));
        values[3] = ShaderValue.Scalar(Bass);
        values[4] = ShaderValue.Scalar(Mid);
        values[5] = ShaderValue.Scalar(Treble);
        values[6] = ShaderValue.Scalar(Volume);
        values[7] = ShaderValue.Scalar(Read("bass_att", 0f));
        values[8] = ShaderValue.Scalar(Read("mid_att", 0f));
        values[9] = ShaderValue.Scalar(Read("treb_att", 0f));
        values[10] = ShaderValue.Scalar(Read("aspectx", 1f));
        values[11] = ShaderValue.Scalar(Read("aspecty", 1f));
        values[12] = ShaderValue.Vector(width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height), 4);
        values[13] = ShaderValue.Vector(_randFrame[0], _randFrame[1], _randFrame[2], _randFrame[3], 4);
        // Milkdrop shaders use "aspect" as the float4 pair, and a preset that swizzles it failed
        // outright when only the two scalars were bound, which disabled that shader. zw is the
        // reciprocal the presets read as aspect.zw.
        WarpSampling.GetAspect(width, height, out var aspectX, out var aspectY);
        values[14] = ShaderValue.Vector(
            aspectX,
            aspectY,
            1f / Math.Max(0.0001f, aspectX),
            1f / Math.Max(0.0001f, aspectY),
            4);
        foreach (var compiled in _compiledWarp)
            SeedCompiledShader(compiled, values);

        foreach (var compiled in _compiledComp)
            SeedCompiledShader(compiled, values);
    }

    /// <summary>
    /// Seeds one compiled shader's frame variables and the shared q and t values. The latter are
    /// read from the preset slots, because Milkdrop keeps one variable universe for the expression
    /// blocks and the shader; the GPU path seeds the same set.
    /// </summary>
    /// <param name="compiled">Shader to seed.</param>
    /// <param name="values">Frame-variable values, aligned with <see cref="CompiledShader.FrameVariables"/>.</param>
    private void SeedCompiledShader(CompiledShader compiled, ReadOnlySpan<ShaderValue> values)
    {
        if (!compiled.IsCompiled)
            return;

        for (var index = 0; index < compiled.FrameIndices.Length; index++)
            compiled.SetAt(compiled.FrameIndices[index], values[index]);
        for (var index = 0; index < compiled.PresetIndices.Length; index++)
            compiled.SetAt(compiled.PresetIndices[index], ShaderValue.Scalar(Read(CompiledShader.PresetVariables[index], 0f)));
    }

    /// <summary>Runs every comp shader over the shader grid.</summary>
    /// <param name="output">Buffer the shaders write into.</param>
    /// <param name="shaderWidth">Shader grid width.</param>
    /// <param name="shaderHeight">Shader grid height.</param>
    private bool RunCompShaders(PixelBuffer output, int shaderWidth, int shaderHeight)
    {
        // The comp shader's rad/ang use MilkDrop's UvToMathSpace convention, which is aspect scaled.
        var compAspectX = 1f / Math.Max(0.0001f, Read("aspectx", 1f));
        var compAspectY = 1f / Math.Max(0.0001f, Read("aspecty", 1f));
        for (var y = 0; y < shaderHeight; y++)
        {
            if (_shaderClock.Elapsed.TotalMilliseconds > ShaderPassBudgetMilliseconds)
            {
                // The grid can only shrink once a frame finishes, so an overrunning pass is
                // abandoned instead of freezing the picture for minutes.
                return false;
            }

            var v = (y + 0.5f) / shaderHeight;
            for (var x = 0; x < shaderWidth; x++)
            {
                var u = (x + 0.5f) / shaderWidth;
                for (var index = 0; index < _compShaders.Count; index++)
                {
                    var (interpreter, shader) = _compShaders[index];
                    // The comp stage is the one that can stall, so its first pixel is traced in
                    // three steps: the preset's own per-pixel block, the shader, and its sampling.
                    var trace = x == 0 && y == 0 && index == 0 && StageLogger is not null && _frame < 2;
                    if (trace)
                        StageLogger!($"stage=comp perPixel begin frame={_frame}");
                    shader.PerPixel.Execute(_slots);
                    if (trace)
                        StageLogger!($"stage=comp shader begin frame={_frame}");
                    var compiled = _compiledComp[index];
                    ShaderValue colour;
                    if (compiled.IsCompiled)
                    {
                        compiled.SetAt(compiled.UvIndex, ShaderValue.Vector(u, v, 0f, 0f, 2));
                        compiled.SetAt(compiled.UvOrigIndex, ShaderValue.Vector(u, v, 0f, 0f, 2));
                        SeedPolar(compiled, u, v, compAspectX, compAspectY, true);
                        colour = compiled.Run(this);
                    }
                    else
                    {
                        BindShaderVariables(interpreter, u, v, u, v, true);
                        colour = interpreter.Run();
                        if (!interpreter.ReturnedValue && interpreter.Variables.TryGetValue("ret", out var written))
                            colour = written;
                    }

                    if (trace)
                        StageLogger!($"stage=comp sampled frame={_frame}");
                    var offset = (((y * shaderWidth) + x) * 4);
                    output.Pixels[offset] = Math.Clamp(colour.X, 0f, 1f);
                    output.Pixels[offset + 1] = Math.Clamp(colour.Y, 0f, 1f);
                    output.Pixels[offset + 2] = Math.Clamp(colour.Z, 0f, 1f);
                }
            }
        }

        return true;
    }

    /// <summary>Runs the comp shaders over the composited frame.</summary>
    private void ApplyCompShaders()
    {
        if (_compShaders.Count == 0)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        _frameCopy.CopyFrom(_fresh);
        _compStageRan = true;
        // MilkDrop reruns BlurPasses after warp, but its first source is still VS[0]: previous
        // feedback. Rebuilding here follows that stage's cache lifetime.
        Array.Clear(_blurLevelReady);

        // A comp shader whose per-pixel block is empty is a pure post-process, so it can run as a
        // Skia runtime effect over the frame instead of the interpreter.
        if (TryApplySkiaCompShader(width, height))
            return;

        // A comp shader is a post-processing pass, so it may run on a smaller grid than the frame
        // and be scaled back up afterwards. That is what keeps a per-pixel shader inside the frame
        // budget on the CPU; sampling still reads the full-resolution frame, so the effect stays
        // where the preset put it.
        var (shaderWidth, shaderHeight) = ShaderGrid(width, height);
        if (_shaderOutput is null ||
            _shaderOutput.Width != shaderWidth ||
            _shaderOutput.Height != shaderHeight)
        {
            _shaderOutput = new PixelBuffer(shaderWidth, shaderHeight);
        }

        // When the grid matches the frame the shader writes into it directly, which keeps the
        // picture exact instead of resampling it through an identical-size copy.
        var scaled = shaderWidth != width || shaderHeight != height;
        var output = scaled ? _shaderOutput : _fresh;
        if (_compShadersFailed)
            return;
        try
        {
            if (!RunCompShaders(output, shaderWidth, shaderHeight))
            {
                // The pass did not finish, so its partial result is dropped: the frame keeps the
                // pre-comp picture and the grid shrinks for the next frame.
                _shaderPixelTarget = Math.Max(ShaderPixelFloor, _shaderPixelTarget / 4);
                _shaderGridReduced = true;
                return;
            }
        }
        catch (Exception exception)
        {
            // A preset shader may call something the engine does not implement, and an unexpected
            // failure must not take the frame with it. Disabling the shaders is the only safe answer.
            ShaderError = "comp: " + exception.GetType().Name + ": " + exception.Message;
            _compShadersFailed = true;
            _compShaders.Clear();
            return;
        }

        if (!scaled)
            return;

        // Bilinear: for many presets the comp shader is the picture rather than a soft
        // post-process, and a nearest-neighbour scale then shows the grid's blocks directly.
        ScaleGridIntoFresh(output, width, height);
    }

    /// <summary>Scales a shader grid back over the frame with bilinear sampling.</summary>
    /// <param name="grid">Grid the shader wrote.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void ScaleGridIntoFresh(PixelBuffer grid, int width, int height)
    {
        var pixelsOut = _fresh.Pixels;
        Span<float> sample = stackalloc float[4];
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) / height;
            var targetRow = y * width;
            for (var x = 0; x < width; x++)
            {
                grid.SampleBilinear((x + 0.5f) / width, v, sample);
                var offset = (targetRow + x) * 4;
                pixelsOut[offset] = sample[0];
                pixelsOut[offset + 1] = sample[1];
                pixelsOut[offset + 2] = sample[2];
            }
        }
    }

    /// <summary>
    /// Runs the single comp shader as a Skia runtime effect when it translates, which is what moves
    /// the comp pass off the CPU interpreter. Its own per-pixel block is emitted into the same
    /// effect. It returns <see langword="false"/> so the caller keeps the interpreter whenever
    /// anything is unsupported.
    /// </summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns><see langword="true"/> when the pass ran on the Skia path.</returns>
    private bool TryApplySkiaCompShader(int width, int height)
    {
        if (!UseSkiaPasses)
            return false;
        if (_compShaders.Count != 1)
            return false;

        if (!_skiaCompTried)
        {
            _skiaCompTried = true;
            var shader = _compShaders[0].Shader;
            var perPixel = shader.PerPixel.IsEmpty ? null : shader.PerPixel;
            _skiaComp = SkiaShaderRunner.CompPass.TryCreate(shader.Program, perPixel, _textures, out var error);
            if (_skiaComp is not null)
                _skiaComp.GpuBlur = true;
            else
                ShaderError = "comp (skia): " + error;
        }

        if (_skiaComp is null)
            return false;

        try
        {
            var sources = new Dictionary<string, SkiaShaderRunner.SamplerSource>(StringComparer.Ordinal)
            {
                ["sampler_main"] = new(_previous.RawPixels, width, height),
                ["sampler_fc_main"] = new(_previous.RawPixels, width, height),
                ["sampler_pc_main"] = new(_previous.RawPixels, width, height)
            };

            // Skia rasterises a runtime effect on the CPU, and a comp shader that samples the blur
            // levels can cost seconds over a full frame. It therefore runs on the same adaptive grid
            // as the interpreter and is scaled back up, and a pass that overruns its budget is
            // abandoned for the interpreter, which owns the grid adaptation.
            var (shaderWidth, shaderHeight) = ShaderGrid(width, height);
            var scaled = shaderWidth != width || shaderHeight != height;
            if (scaled &&
                (_shaderOutput is null || _shaderOutput.Width != shaderWidth || _shaderOutput.Height != shaderHeight))
            {
                _shaderOutput = new PixelBuffer(shaderWidth, shaderHeight);
            }

            // The blur levels are built from sampler_main, so the renderer only has to supply the
            // frame copies.
            var target = scaled ? _shaderOutput! : _fresh;
            _skiaCompClock.Restart();
            _skiaComp.Render(target, shaderWidth, shaderHeight, sources, BuildSkiaScalars(_skiaComp.PerPixelUniforms), BuildSkiaVectors());
            _skiaCompClock.Stop();
            if (_skiaCompClock.Elapsed.TotalMilliseconds > ShaderPassBudgetMilliseconds)
            {
                // The pass finished but is too expensive: hand the preset back to the interpreter,
                // which shrinks its grid until the frame fits the budget.
                ShaderError = "comp (skia): over the pass budget";
                _skiaComp.Dispose();
                _skiaComp = null;
                return false;
            }

            if (scaled)
                ScaleGridIntoFresh(target, width, height);
            return true;
        }
        catch (Exception exception)
        {
            ShaderError = "comp (skia): " + exception.GetType().Name + ": " + exception.Message;
            _skiaComp?.Dispose();
            _skiaComp = null;
            return false;
        }
    }

    /// <summary>Collects the scalar uniforms the shader reads from the seeded slots.</summary>
    /// <param name="perPixelVariables">Per-pixel variables to seed under their emitted names, or <see langword="null"/>.</param>
    /// <returns>The scalar uniforms.</returns>
    private Dictionary<string, float> BuildSkiaScalars(IReadOnlyList<string>? perPixelVariables = null)
    {
        var scalars = new Dictionary<string, float>(StringComparer.Ordinal);
        var layout = Preset.Layout;
        foreach (var (name, count) in ShaderTranspiler.UniformComponents)
        {
            if (count != 1)
                continue;

            var slot = layout.IndexOf(name);
            if (slot >= 0 && slot < _slots.Length)
                scalars[name] = (float)_slots[slot];
        }

        if (perPixelVariables is not null)
        {
            foreach (var name in perPixelVariables)
            {
                var slot = layout.IndexOf(name);
                scalars[PresetExpressionTranspiler.UniformName(name)] =
                    slot >= 0 && slot < _slots.Length ? (float)_slots[slot] : 0f;
            }
        }

        return scalars;
    }

    /// <summary>Collects the vector uniforms the shader reads.</summary>
    /// <returns>The vector uniforms.</returns>
    private IReadOnlyDictionary<string, float[]> BuildSkiaVectors()
    {
        var inverseAspectX = Read("aspectx", 1f);
        var inverseAspectY = Read("aspecty", 1f);
        return new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            // Milkdrop's aspect is a float4 whose xy is the aspect and whose zw is the inverse pair
            // presets read as aspect.zw. The preset's aspectx/aspecty variables are that inverse.
            ["aspect"] =
            [
                1f / Math.Max(0.0001f, inverseAspectX),
                1f / Math.Max(0.0001f, inverseAspectY),
                inverseAspectX,
                inverseAspectY
            ],
            ["rand_frame"] = _randFrame
        };
    }

    /// <summary>
    /// Fills every uniform a translated shader prelude declares from the current frame, so the OpenGL
    /// pipeline seeds the same values the interpreter binds. The caller owns the dictionary, because a
    /// frame must not allocate one.
    /// </summary>
    /// <param name="destination">Dictionary to fill; existing entries are overwritten.</param>
    /// <param name="perPixelVariables">
    /// Preset variables the shader's per-pixel block reads, as reported by the emitter, or
    /// <see langword="null"/> when the block reads none.
    /// </param>
    public void WriteShaderUniforms(
        IDictionary<string, ShaderValue> destination,
        IReadOnlyList<string>? perPixelVariables = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var width = _previous.Width;
        var height = _previous.Height;
        destination["texsize"] = ShaderValue.Vector(width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height), 4);
        destination["time"] = ShaderValue.Scalar(Read("time", 0f));
        destination["frame"] = ShaderValue.Scalar(Read("frame", 0f));
        destination["fps"] = ShaderValue.Scalar(Read("fps", 0f));
        destination["bass"] = ShaderValue.Scalar(BassRelative);
        destination["mid"] = ShaderValue.Scalar(MidRelative);
        destination["treb"] = ShaderValue.Scalar(TrebleRelative);
        destination["vol"] = ShaderValue.Scalar(Volume);
        var previousMin = 0f; var previousMax = 1f;
        for (var level = 1; level <= 3; level++)
        {
            var minimum = Math.Max(previousMin, Read("blur" + level + "_min", 0f));
            var maximum = Math.Min(previousMax, Read("blur" + level + "_max", 1f));
            if (maximum - minimum < 0.1f)
            {
                var middle = (minimum + maximum) * 0.5f;
                minimum = middle - 0.05f; maximum = middle + 0.05f;
            }
            destination["blur" + level + "_min"] = ShaderValue.Scalar(minimum);
            destination["blur" + level + "_max"] = ShaderValue.Scalar(maximum);
            previousMin = minimum; previousMax = maximum;
        }
        destination["blur1_edge_darken"] = ShaderValue.Scalar(Read("blur1_edge_darken", 0f));
        destination["bass_att"] = ShaderValue.Scalar(Read("bass_att", 0f));
        destination["mid_att"] = ShaderValue.Scalar(Read("mid_att", 0f));
        destination["treb_att"] = ShaderValue.Scalar(Read("treb_att", 0f));
        for (var index = 1; index <= 32; index++)
            destination["q" + index] = ShaderValue.Scalar(Read("q" + index, 0f));
        for (var index = 1; index <= 8; index++)
            destination["t" + index] = ShaderValue.Scalar(Read("t" + index, 0f));

        // The reference's hue shade, which every shader may read as hue_shader. A single uniform cannot
        // carry the per-pixel interpolation, so the four corners travel and the emitted shader mixes
        // them by the fragment's own position, exactly as the reference's vertex interpolation does.
        for (var corner = 0; corner < 4; corner++)
        {
            destination["hue_shader_r" + corner] = ShaderValue.Scalar(HueShadeCorner(0, corner));
            destination["hue_shader_g" + corner] = ShaderValue.Scalar(HueShadeCorner(1, corner));
            destination["hue_shader_b" + corner] = ShaderValue.Scalar(HueShadeCorner(2, corner));
        }

        var inverseAspectX = Read("aspectx", 1f);
        var inverseAspectY = Read("aspecty", 1f);
        destination["aspectx"] = ShaderValue.Scalar(inverseAspectX);
        destination["aspecty"] = ShaderValue.Scalar(inverseAspectY);
        destination["aspect"] = ShaderValue.Vector(
            1f / Math.Max(0.0001f, inverseAspectX),
            1f / Math.Max(0.0001f, inverseAspectY),
            inverseAspectX,
            inverseAspectY,
            4);
        destination["rand_frame"] = ShaderValue.Vector(_randFrame[0], _randFrame[1], _randFrame[2], _randFrame[3], 4);
        destination["roam_cos"] = _roam[0];
        destination["roam_sin"] = _roam[1];
        destination["slow_roam_cos"] = _roam[2];
        destination["slow_roam_sin"] = _roam[3];

        // The motion uniforms of the warp entry point. They are the per-frame values the CPU warp
        // reads, so the GPU warp reproduces the same sampling position.
        destination["_orynivo_size"] = ShaderValue.Vector(width, height, 0f, 0f, 2);
        destination["_orynivo_zoom"] = ShaderValue.Scalar(Math.Max(0.01f, Read("zoom", Preset.Zoom)));
        destination["_orynivo_zoomExp"] = ShaderValue.Scalar(Read("zoomexp", 1f));
        destination["_orynivo_rotation"] = ShaderValue.Scalar(Read("rot", 0f));
        destination["_orynivo_centre"] = ShaderValue.Vector(Read("cx", 0f), Read("cy", 0f), 0f, 0f, 2);
        destination["_orynivo_offset"] = ShaderValue.Vector(Read("dx", 0f), Read("dy", 0f), 0f, 0f, 2);
        destination["_orynivo_stretch"] = ShaderValue.Vector(Read("sx", 1f), Read("sy", 1f), 0f, 0f, 2);
        destination["_orynivo_warp"] = ShaderValue.Scalar(Read("warp", Preset.Warp));
        destination["_orynivo_warpTime"] = ShaderValue.Scalar((float)_elapsed * Read("fWarpAnimSpeed", 1f));
        destination["_orynivo_warpScale"] = ShaderValue.Scalar(Read("fWarpScale", 1f));

        if (perPixelVariables is null)
            return;

        var layout = Preset.Layout;
        foreach (var name in perPixelVariables)
        {
            var slot = layout.IndexOf(name);
            destination[PresetExpressionTranspiler.UniformName(name)] =
                ShaderValue.Scalar(slot >= 0 && slot < _slots.Length ? (float)_slots[slot] : 0f);
        }
    }

    private static readonly float[] RoamFrequencies = [0.329f, 1.293f, 5.070f, 20.051f];
    private static readonly float[] SlowRoamFrequencies = [0.0050f, 0.0085f, 0.0133f, 0.0217f];
    private static readonly float[] RoamPhases = [1.2f, 3.9f, 2.5f, 5.4f];
    private static readonly float[] SlowRoamPhases = [2.7f, 5.3f, 4.5f, 3.8f];

    /// <summary>
    /// Builds one of the reference implementation's roam vectors: a cosine or sine at four frequencies,
    /// mapped into the upper half of the range. A shader that normalises one without it bound gets a
    /// zero vector, and <c>normalize(0)</c> is an infinity that spreads across the frame.
    /// </summary>
    /// <param name="time">Preset time in seconds.</param>
    /// <param name="sine">Whether to use sine instead of cosine.</param>
    /// <param name="slow">Whether to use the slow frequency set.</param>
    /// <returns>The roam vector.</returns>
    private static ShaderValue RoamVector(float time, bool sine, bool slow)
    {
        var frequencies = slow ? SlowRoamFrequencies : RoamFrequencies;
        var phases = slow ? SlowRoamPhases : RoamPhases;
        Span<float> result = stackalloc float[4];
        for (var index = 0; index < 4; index++)
        {
            var angle = (time * frequencies[index]) + phases[index];
            result[index] = 0.5f + (0.5f * (sine ? MathF.Sin(angle) : MathF.Cos(angle)));
        }

        return ShaderValue.Vector(result[0], result[1], result[2], result[3], 4);
    }

    /// <summary>Writes the variables every shader can read for this pixel.</summary>
    /// <param name="interpreter">Shader about to run.</param>
    /// <param name="u">Sampling coordinate in the range zero to one.</param>
    /// <param name="v">Sampling row in the range zero to one.</param>
    /// <param name="originalU">Coordinate of the pixel itself.</param>
    /// <param name="originalV">Row of the pixel itself.</param>
    /// <param name="mathSpacePolar">
    /// Whether <c>rad</c>/<c>ang</c> use the reference's <c>UvToMathSpace</c> convention (aspect
    /// scaled, <c>rad</c> one at the screen corners, <c>ang</c> wrapped to zero through two pi). The
    /// comp shader needs that convention; the warp shader keeps its own per-vertex polar pair.
    /// </param>
    private void BindShaderVariables(ShaderInterpreter interpreter, float u, float v, float originalU, float originalV, bool mathSpacePolar)
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
        // Milkdrop shaders share q1..q32 and t1..t8 with the expression blocks, so the values the
        // per-frame block computed reach the shader. The GPU path seeds the same set from the slots.
        for (var index = 1; index <= 32; index++)
            interpreter.SetVariable("q" + index, Read("q" + index, 0f));
        for (var index = 1; index <= 8; index++)
            interpreter.SetVariable("t" + index, Read("t" + index, 0f));
        var inverseAspectX = Read("aspectx", 1f);
        var inverseAspectY = Read("aspecty", 1f);
        interpreter.SetVariable("aspectx", inverseAspectX);
        interpreter.SetVariable("aspecty", inverseAspectY);
        // Milkdrop's aspect is a float4: xy is the aspect and zw the inverse pair, which presets use
        // as aspect.zw. projectM binds the same four components to its first shader constant.
        interpreter.SetVariable(
            "aspect",
            ShaderValue.Vector(
                1f / Math.Max(0.0001f, inverseAspectX),
                1f / Math.Max(0.0001f, inverseAspectY),
                inverseAspectX,
                inverseAspectY,
                4));
        // The reference passes its animated hue shade to every shader as hue_shader, interpolated from
        // the four quad corners by the pixel's own position.
        var hueShade = HueShadeAt(originalU, originalV);
        interpreter.SetVariable("hue_shader", ShaderValue.Vector(hueShade.Red, hueShade.Green, hueShade.Blue, 0f, 3));
        interpreter.SetVariable("rand_frame", ShaderValue.Vector(_randFrame[0], _randFrame[1], _randFrame[2], _randFrame[3], 4));        interpreter.SetVariable("roam_cos", _roam[0]);
        interpreter.SetVariable("roam_sin", _roam[1]);
        interpreter.SetVariable("slow_roam_cos", _roam[2]);
        interpreter.SetVariable("slow_roam_sin", _roam[3]);
        if (mathSpacePolar)
        {
            // MilkDrop's UvToMathSpace (milkdropfs.cpp): the position is scaled by the aspect, rad is
            // one at the screen corners, and ang runs zero through two pi. The comp shader's
            // GetBlur/frac(rs) presets depend on this exact convention.
            var aspectX = 1f / Math.Max(0.0001f, inverseAspectX);
            var aspectY = 1f / Math.Max(0.0001f, inverseAspectY);
            var px = ((u * 2f) - 1f) * aspectX;
            var py = ((v * 2f) - 1f) * aspectY;
            var corner = MathF.Sqrt((aspectX * aspectX) + (aspectY * aspectY));
            var angle = MathF.Atan2(py, px);
            if (angle < 0f)
                angle += 2f * MathF.PI;
            interpreter.SetVariable("rad", MathF.Sqrt((px * px) + (py * py)) / corner);
            interpreter.SetVariable("ang", angle);
        }
        else
        {
            var x = (u * 2f) - 1f;
            var y = (v * 2f) - 1f;
            interpreter.SetVariable("rad", MathF.Sqrt((x * x) + (y * y)));
            interpreter.SetVariable("ang", MathF.Atan2(y, x));
        }
    }

    /// <summary>
    /// One shader that may have been compiled. The interpreter stays the reference implementation
    /// and runs whenever the compiler reported the body as unsupported.
    /// </summary>
    private sealed class CompiledShader
    {
        /// <summary>The engine variables a shader reads, seeded once per frame.</summary>
        public static readonly string[] FrameVariables =
        [
            "time", "frame", "fps", "bass", "mid", "treb", "vol",
            "bass_att", "mid_att", "treb_att", "aspectx", "aspecty", "texsize", "rand_frame", "aspect"
        ];

        /// <summary>
        /// The shared <c>q1</c>-<c>q32</c> and <c>t1</c>-<c>t8</c> variables a shader reads from the
        /// preset slots. Milkdrop keeps them in one universe for the expression blocks and the
        /// shader, so they are seeded rather than left at zero.
        /// </summary>
        public static readonly string[] PresetVariables = BuildPresetVariables();

        private static string[] BuildPresetVariables()
        {
            var names = new string[40];
            for (var index = 0; index < 32; index++)
                names[index] = "q" + (index + 1);
            for (var index = 0; index < 8; index++)
                names[32 + index] = "t" + (index + 1);
            return names;
        }

        private readonly ShaderProgram? _program;
        private readonly ShaderValue[]? _slots;

        private CompiledShader(ShaderProgram? program)
        {
            _program = program;
            _slots = program is null ? null : new ShaderValue[program.SlotCount];
            FrameIndices = program is null ? [] : Array.ConvertAll(FrameVariables, program.IndexOf);
            PresetIndices = program is null ? [] : Array.ConvertAll(PresetVariables, program.IndexOf);
            UvIndex = program?.IndexOf("uv") ?? -1;
            UvOrigIndex = program?.IndexOf("uv_orig") ?? -1;
            RadIndex = program?.IndexOf("rad") ?? -1;
            AngIndex = program?.IndexOf("ang") ?? -1;
        }

        /// <summary>Gets the resolved slots of the frame variables, aligned with the name list.</summary>
        public int[] FrameIndices { get; }

        /// <summary>Gets the resolved slots of the shared q and t variables, aligned with the name list.</summary>
        public int[] PresetIndices { get; }

        /// <summary>Gets the resolved slot of the sampling coordinate.</summary>
        public int UvIndex { get; }

        /// <summary>Gets the resolved slot of the unmodified sampling coordinate.</summary>
        public int UvOrigIndex { get; }

        /// <summary>Gets the resolved slot of the pixel radius.</summary>
        public int RadIndex { get; }

        /// <summary>Gets the resolved slot of the pixel angle.</summary>
        public int AngIndex { get; }

        /// <summary>Writes one resolved slot.</summary>
        /// <param name="index">Slot index, or a negative value when the shader lacks the variable.</param>
        /// <param name="value">Value to store.</param>
        public void SetAt(int index, ShaderValue value)
        {
            if (_slots is not null && index >= 0)
                _slots[index] = value;
        }

        /// <summary>Gets a value indicating whether this shader runs compiled.</summary>
        public bool IsCompiled => _program is not null;

        /// <summary>Compiles a shader body, or reports that it stays interpreted.</summary>
        /// <param name="program">Parsed shader body.</param>
        /// <returns>The holder.</returns>
        public static CompiledShader Create(ShaderNode program) => new(ShaderCompiler.Compile(program));

        /// <summary>Runs the compiled shader.</summary>
        /// <param name="sampler">Bound sampler.</param>
        /// <returns>The returned value.</returns>
        public ShaderValue Run(IShaderSampler sampler) =>
            _program!.Execute(_slots!, sampler);
    }

    /// <inheritdoc/>
    public ShaderValue Sample(string sampler, float u, float v)
    {
        // The qualifier (fc_/fw_/pc_/pw_) selects the sampling mode; the base name selects the
        // texture. Milkdrop's qualified main samplers are all the same frame, only read differently.
        var parsed = ShaderSamplerName.Parse(sampler);

        // Milkdrop shaders sample the noise and random textures it ships. We generate those, so a
        // referenced sampler is resolved against the bank instead of falling back to the frame.
        if (VisualizerTextureBank.TryResolve("sampler_" + parsed.BaseName, out var texture))
        {
            _textures.Sample(texture, u, v, parsed.Wrap).CopyTo(_sample);
            return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
        }

        // MilkDrop binds VS[0] to main samplers in both stages. VS[1], the current warp and
        // overlay, only becomes the next frame's VS[0] after presentation.
        var source = _previous;
        source.SampleShader(u, v, parsed.Wrap, parsed.Nearest, _sample);
        return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
    }

    /// <inheritdoc/>
    public ShaderValue SampleBlur(int level, float u, float v)
    {
        level = Math.Clamp(level, 1, 3);
        // Every level keeps its own buffer. A shader that reads two levels in one pixel used to
        // thrash a single cache and rebuild a full-frame blur for every sample, which cost seconds
        // per frame for a comp shader that reads GetBlur1 and GetBlur3. The levels are built in
        // order and each continues from the one below it, so asking for level 3 after level 1 costs
        // one more pass rather than three.
        var buffer = _blurLevels[level - 1];
        if (!_blurLevelReady[level - 1])
        {
            // MilkDrop refreshes the chain after warp but binds VS[0], the previous feedback, as
            // its source. Clear the cache between stages to follow that lifecycle.
            for (var build = 1; build <= level; build++)
            {
                if (_blurLevelReady[build - 1])
                    continue;

                _blurLevels[build - 1].ResampleFrom(build == 1 ? _previous : _blurLevels[build - 2]);
                // The reference runs two passes per level: a long horizontal one and a short vertical
                // one, so level N is N such pairs from its downscaled source.
                for (var pass = 0; pass < build; pass++)
                {
                    _blurLevels[build - 1].BlurReference(horizontal: true);
                    _blurLevels[build - 1].BlurReference(horizontal: false);
                }

                _blurLevelReady[build - 1] = true;
            }
        }

        buffer.SampleBilinear(u, v, _sample);
        return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
    }

    /// <inheritdoc/>
    public ShaderValue SampleVolume(string sampler, float x, float y, float z)
    {
        // Milkdrop's tex3D reads one of the generated cubic volumes. An unexpected sampler name
        // falls back to the two dimensional path so the shader keeps a sensible picture.
        if (VisualizerTextureBank.TryResolve(sampler, out var texture) &&
            VisualizerTextureBank.IsVolume(texture))
        {
            _textures.SampleVolume(texture, x, y, z, VisualizerTextureWrap.Repeat).CopyTo(_sample);
            return ShaderValue.Vector(_sample[0], _sample[1], _sample[2], _sample[3], 4);
        }

        return Sample(sampler, x, y);
    }

    /// <inheritdoc/>
    public ShaderValue SamplePixel(int x, int y)
    {
        // GetPixel reads the same VS[0] that sampler_main reads in both shader stages.
        var source = _previous;
        var column = Math.Clamp(x, 0, source.Width - 1);
        var row = Math.Clamp(y, 0, source.Height - 1);
        var offset = (((row * source.Width) + column) * 4);
        return ShaderValue.Vector(
            source.Pixels[offset],
            source.Pixels[offset + 1],
            source.Pixels[offset + 2],
            source.Pixels[offset + 3],
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
        var pixels = _warped.RawPixels;
        // Milkdrop's bDarkenCenter is a small black diamond fan (milkdropfs.cpp, DrawSprites):
        // half size 0.05 in clip space, peak alpha 3/32 at the centre and zero at the rim, blended
        // towards black. It is not a full-frame radial fade.
        WarpSampling.GetAspect(width, height, out _, out var aspectY);
        var halfX = 0.05f * aspectY;
        const float HalfY = 0.05f;
        const float PeakAlpha = 3f / 32f;
        ParallelRows.For(ParallelismEnabled, height, (worker, from, to) =>
        {
            for (var y = from; y < to; y++)
            {
                var clipY = height > 1 ? (y / (float)(height - 1) * 2f) - 1f : 0f;
                for (var x = 0; x < width; x++)
                {
                    var clipX = width > 1 ? (x / (float)(width - 1) * 2f) - 1f : 0f;
                    var diamond = (MathF.Abs(clipX) / halfX) + (MathF.Abs(clipY) / HalfY);
                    if (diamond >= 1f)
                        continue;

                    var factor = 1f - (PeakAlpha * (1f - diamond) * amount);
                    var offset = (((y * width) + x) * 4);
                    pixels[offset] *= factor;
                    pixels[offset + 1] *= factor;
                    pixels[offset + 2] *= factor;
                }
            }
        });
    }


    /// <summary>Applies the preset's gamma adjustment to the warped frame.</summary>
    /// <summary>
    /// Multiplies the frame by the reference's animated hue shade and its gamma gain. The shade is a
    /// four-corner colour — three animated sine channels normalised so their maximum is one — blended
    /// across the frame, and the gamma is a linear brightness gain. Both belong to the legacy final
    /// composite, which a comp shader replaces.
    /// </summary>
    /// <summary>
    /// Computes the reference's animated hue shade for the four quad corners. The reference always
    /// passes these to shaders as <c>hue_shader</c> (<c>_vDiffuse.xyz</c>, "since we don't know if the
    /// shader uses it or not"), so they are computed for every frame, not only for the legacy
    /// composite that also multiplies the finished frame by them.
    /// </summary>
    private void ComputeHueShades()
    {
        var time = (float)_elapsed * 30f;

        // The reference's four quad corners, in its vertex order: top-left, top-right, bottom-left,
        // bottom-right.
        for (var corner = 0; corner < 4; corner++)
        {
            var index = corner;
            var red = 0.6f + (0.3f * MathF.Sin((time * 0.0143f) + 3f + (index * 21f) + _hueOffsets[3]));
            var green = 0.6f + (0.3f * MathF.Sin((time * 0.0107f) + 1f + (index * 13f) + _hueOffsets[1]));
            var blue = 0.6f + (0.3f * MathF.Sin((time * 0.0129f) + 6f + (index * 9f) + _hueOffsets[2]));
            var max = MathF.Max(red, MathF.Max(green, blue));
            if (MathF.Abs(max) < 1e-6f)
                max = 1f;
            _hueShadeR[corner] = 0.5f + (0.5f * (red / max));
            _hueShadeG[corner] = 0.5f + (0.5f * (green / max));
            _hueShadeB[corner] = 0.5f + (0.5f * (blue / max));
        }
    }

    /// <summary>
    /// Gets the reference's hue shade for one corner channel, so a shader uniform can carry it.
    /// </summary>
    /// <param name="channel">Zero for red, one for green, two for blue.</param>
    /// <param name="corner">Corner index in the reference's order.</param>
    /// <returns>The shade value at that corner.</returns>
    private float HueShadeCorner(int channel, int corner) => channel switch
    {
        0 => _hueShadeR[corner],
        1 => _hueShadeG[corner],
        _ => _hueShadeB[corner]
    };

    /// <summary>Gets the interpolated hue shade at a screen position, both in the zero-to-one range.</summary>
    /// <param name="u">Horizontal position, zero at the left edge.</param>
    /// <param name="v">Vertical position, zero at the top edge.</param>
    /// <returns>The shade colour.</returns>
    private (float Red, float Green, float Blue) HueShadeAt(float u, float v) => (
        Lerp(Lerp(_hueShadeR[0], _hueShadeR[1], u), Lerp(_hueShadeR[2], _hueShadeR[3], u), v),
        Lerp(Lerp(_hueShadeG[0], _hueShadeG[1], u), Lerp(_hueShadeG[2], _hueShadeG[3], u), v),
        Lerp(Lerp(_hueShadeB[0], _hueShadeB[1], u), Lerp(_hueShadeB[2], _hueShadeB[3], u), v));

    private void ApplyHueShadeAndGamma()
    {
        var gamma = Math.Clamp(Read("fGammaAdj", 1f), 0.1f, 10f);
        // The legacy composite mixes the animated hue shade with white by the shader amount, so a
        // zero value leaves the frame untinted (milkdropfs.cpp, ShowToUser_NoShaders). The preset key
        // is fShader, which the parser aliases onto the shader variable.
        var shaderAmount = Math.Clamp(Read("shader", 1f), 0f, 1f);
        var width = _warped.Width;
        var height = _warped.Height;
        ComputeHueShades();

        var shadeR = _hueShadeR;
        var shadeG = _hueShadeG;
        var shadeB = _hueShadeB;

        var pixels = _warped.RawPixels;
        var stride = width * 4;
        ParallelRows.For(ParallelismEnabled, height, (worker, from, to) =>
        {
            for (var y = from; y < to; y++)
            {
                var v = height > 1 ? y / (float)(height - 1) : 0f;
                var rowStart = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var u = width > 1 ? x / (float)(width - 1) : 0f;
                    var redShade = Lerp(Lerp(shadeR[0], shadeR[1], u), Lerp(shadeR[2], shadeR[3], u), v);
                    var greenShade = Lerp(Lerp(shadeG[0], shadeG[1], u), Lerp(shadeG[2], shadeG[3], u), v);
                    var blueShade = Lerp(Lerp(shadeB[0], shadeB[1], u), Lerp(shadeB[2], shadeB[3], u), v);
                    redShade = (redShade * shaderAmount) + (1f - shaderAmount);
                    greenShade = (greenShade * shaderAmount) + (1f - shaderAmount);
                    blueShade = (blueShade * shaderAmount) + (1f - shaderAmount);
                    var index = rowStart + (x * 4);
                    pixels[index] = Math.Clamp(pixels[index] * redShade * gamma, 0f, 1f);
                    pixels[index + 1] = Math.Clamp(pixels[index + 1] * greenShade * gamma, 0f, 1f);
                    pixels[index + 2] = Math.Clamp(pixels[index + 2] * blueShade * gamma, 0f, 1f);
                }
            }
        });
    }

    /// <summary>Linear interpolation.</summary>
    /// <param name="from">Value at zero.</param>
    /// <param name="to">Value at one.</param>
    /// <param name="amount">Blend amount.</param>
    /// <returns>The blended value.</returns>
    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);

    /// <summary>Draws motion vectors, shapes, custom waves and the default wave into the overlay.</summary>
    private void DrawOverlay()
    {
        _fresh.Clear();
        DrawMotionVectors();
        DrawShapes();
        DrawWaves();
    }

    /// <summary>
    /// Draws the four custom waveforms, then the default waveform with global <c>wave_*</c> settings. The custom
    /// waveforms require <c>wavecode_N_enabled</c>. The two are separate in the reference: the
    /// default wave uses the global mode and the global <c>per_point</c> block, while each custom
    /// wave has its own state and its own <c>wave_N_per_point</c> block.
    /// </summary>
    private void DrawWaves()
    {
        foreach (var wave in Preset.Waves)
            DrawCustomWave(wave);
        DrawDefaultWave();
    }

    /// <summary>
    /// Draws the default waveform with the global <c>wave_*</c> settings. The geometry comes from
    /// <see cref="MilkdropWaveform"/>'s per-mode math instead of the earlier line/circle
    /// approximation, and the global <c>per_point</c> block may still move every point.
    /// </summary>
    private void DrawDefaultWave()
    {
        var alpha = Math.Clamp(Read("wave_a", Preset.WaveAlpha), 0f, 1f);
        if (alpha <= 0f)
            return;

        // Milkdrop's idle preset uses mode six, the single line, and its numbering is the reference's.
        var mode = (MilkdropWaveform.Mode)(int)Math.Clamp(Read("wave_mode", 6f), 0f, 8f);
        var spectrum = MilkdropWaveform.IsSpectrumMode(mode);
        var left = spectrum ? SpectrumLeft : WaveformLeft;
        var right = spectrum ? SpectrumRight : WaveformRight;
        if (left.Length < 2 || right.Length < 2)
            return;

        var smoothing = Math.Clamp(Read("wave_smoothing", 0f), 0f, 1f);
        MilkdropWaveform.Prepare(left, _wavePcmLeft, Preset.WaveScale, smoothing, out var sampleCount);
        MilkdropWaveform.Prepare(right, _wavePcmRight, Preset.WaveScale, smoothing, out _);

        var width = _fresh.Width;
        var height = _fresh.Height;
        WarpSampling.GetAspect(width, height, out var aspectX, out var aspectY);
        var mystery = Read("wave_mystery", 0f);
        var waveX = (2f * Read("wave_x", 0.5f)) - 1f;
        var waveY = (2f * Read("wave_y", 0.5f)) - 1f;

        MilkdropWaveform.Generate(
            mode,
            _wavePcmLeft,
            _wavePcmRight,
            sampleCount,
            mystery,
            waveX,
            waveY,
            aspectX,
            aspectY,
            (float)_elapsed,
            width,
            _wavePointX,
            _wavePointY,
            _waveSecondX,
            _waveSecondY,
            out var count,
            out var secondCount);

        var red = Math.Clamp(Read("wave_r", 1f), 0f, 1f);
        var green = Math.Clamp(Read("wave_g", 1f), 0f, 1f);
        var blue = Math.Clamp(Read("wave_b", 1f), 0f, 1f);
        var dots = Read("wave_dots", 0f) >= 0.5f;
        var thick = Read("wave_thick", 0f) >= 0.5f;
        var additive = Read("wave_additive", 1f) >= 0.5f;
        var loop = MilkdropWaveform.IsLoop(mode);

        ApplyDefaultWavePerPoint(count, _wavePointX, _wavePointY);
        DrawDefaultWaveVertices(count, _wavePointX, _wavePointY, red, green, blue, alpha, dots, thick, additive, loop);

        if (secondCount > 0)
        {
            // The reference runs no per-point code on the default wave, so only the legacy global
            // block is honoured, and only for the first trace.
            DrawDefaultWaveVertices(secondCount, _waveSecondX, _waveSecondY, red, green, blue, alpha, dots, thick, additive, loop: false);
        }
    }

    /// <summary>Runs the legacy global <c>per_point</c> block over one trace of the default wave.</summary>
    /// <param name="count">Vertex count.</param>
    /// <param name="xs">Vertex x coordinates in minus-one-to-one space.</param>
    /// <param name="ys">Vertex y coordinates in minus-one-to-one space.</param>
    private void ApplyDefaultWavePerPoint(int count, float[] xs, float[] ys)
    {
        var perPoint = Preset.WavePerPoint;
        if (perPoint.IsEmpty)
            return;

        var step = count > 1 ? 1f / (count - 1) : 0f;
        for (var index = 0; index < count; index++)
        {
            Write("t", index * step);
            Write("i", index);
            Write("sample", ys[index]);
            Write("x", xs[index]);
            Write("y", ys[index]);
            perPoint.Execute(_slots);
            xs[index] = Read("x", xs[index]);
            ys[index] = Read("y", ys[index]);
        }
    }

    /// <summary>Draws one default-wave trace as dots or as a strip, optionally closed into a loop.</summary>
    /// <param name="count">Vertex count.</param>
    /// <param name="xs">Vertex x coordinates in minus-one-to-one space.</param>
    /// <param name="ys">Vertex y coordinates in minus-one-to-one space.</param>
    /// <param name="red">Red, zero to one.</param>
    /// <param name="green">Green, zero to one.</param>
    /// <param name="blue">Blue, zero to one.</param>
    /// <param name="alpha">Opacity, zero to one.</param>
    /// <param name="dots">Whether the trace is drawn as dots.</param>
    /// <param name="thick">Whether the trace is drawn thick.</param>
    /// <param name="additive">Whether the colour is added instead of alpha-blended.</param>
    /// <param name="loop">Whether the trace is closed.</param>
    private void DrawDefaultWaveVertices(
        int count, float[] xs, float[] ys, float red, float green, float blue, float alpha,
        bool dots, bool thick, bool additive, bool loop)
    {
        var width = _fresh.Width;
        var height = _fresh.Height;
        if (count < 1 || width < 1 || height < 1)
            return;

        var previousX = 0;
        var previousY = 0;
        for (var index = 0; index < count; index++)
        {
            var x = (int)Math.Clamp((xs[index] * 0.5f + 0.5f) * (width - 1), 0f, width - 1);
            var y = (int)Math.Clamp((0.5f - (ys[index] * 0.5f)) * (height - 1), 0f, height - 1);
            if (dots)
            {
                PaintPixel(x, y, red, green, blue, alpha, additive);
                if (thick)
                    PaintPixel(x, y + 1, red, green, blue, alpha * 0.6f, additive);
                continue;
            }

            if (index > 0)
                DrawWaveSegment(previousX, previousY, x, y, red, green, blue, alpha, red, green, blue, alpha, additive, thick);
            previousX = x;
            previousY = y;
        }

        if (!dots && loop && count > 2)
        {
            var firstX = (int)Math.Clamp((xs[0] * 0.5f + 0.5f) * (width - 1), 0f, width - 1);
            var firstY = (int)Math.Clamp((0.5f - (ys[0] * 0.5f)) * (height - 1), 0f, height - 1);
            DrawWaveSegment(previousX, previousY, firstX, firstY, red, green, blue, alpha, red, green, blue, alpha, additive, thick);
        }
    }

    /// <summary>
    /// Draws one custom waveform with the reference's contract. The sample data is built from the
    /// waveform or the spectrum, smoothed forwards and backwards, and scaled by the wave's own
    /// <c>scaling</c>; the per-point block then sees <c>sample</c> (the normalized index),
    /// <c>value1</c>/<c>value2</c> (the channel samples) and may move and colour the point. Only a
    /// waveform that asks for the spectrum reads it, so a preset without one draws no bars.
    /// </summary>
    /// <param name="wave">Waveform state and programs.</param>
    private void DrawCustomWave(VisualizerWave wave)
    {
        if (!wave.Enabled)
            return;

        var left = wave.Spectrum ? SpectrumLeft : WaveformLeft;
        var right = wave.Spectrum ? SpectrumRight : WaveformRight;
        if (left.Length < 2 || right.Length < 2)
            return;

        if (!_elementFrames.ContainsKey(wave)) InitializeElement(wave, wave.Init);
        var presetSlots = _slots;
        _slots = _elementFrames[wave];
        try
        {
            CopyElementInputs(presetSlots, _slots);
            RestoreElementT(wave);
            SeedWave(wave);
            wave.PerFrame.Execute(_slots);

            var available = Math.Min(left.Length, right.Length);
            var samples = Math.Min(available, (int)Math.Clamp(Read("samples", wave.Samples), 0f, 512f));
            var separation = Math.Clamp(wave.Separation, -(available - samples), available - samples);
            if (wave.UseDots ? samples < 1 : samples < 2)
                return;

            var scaling = Read("scaling", wave.Scaling);
            var smoothing = Math.Clamp(Read("smoothing", wave.Smoothing), 0f, 1f);
            var mult = scaling * Preset.WaveScale * (wave.Spectrum ? 0.15f : 128f * 0.004f);

            // The reference smooths the sample data forwards and then backwards, which removes the
            // asymmetry between the start and the end of the trace.
            var mix1 = MathF.Pow(smoothing * 0.98f, 0.5f);
            var mix2 = 1f - mix1;
            for (var sample = 0; sample < samples; sample++)
            {
                var sourceIndex = wave.Spectrum ? sample * Math.Max(1, available - separation) / samples : sample;
                var offsetLeft = wave.Spectrum ? 0 : (available - samples) / 2 - separation / 2;
                var offsetRight = wave.Spectrum ? 0 : (available - samples) / 2 + separation / 2;
                _waveSamples[sample] = left[Math.Clamp(sourceIndex + offsetLeft, 0, available - 1)];
                _waveSamplesRight[sample] = right[Math.Clamp(sourceIndex + offsetRight, 0, available - 1)];
            }

            SmoothAndScale(_waveSamples, samples, mix1, mix2, mult);
            SmoothAndScale(_waveSamplesRight, samples, mix1, mix2, mult);

            var baseRed = Read("r", wave.Red);
            var baseGreen = Read("g", wave.Green);
            var baseBlue = Read("b", wave.Blue);
            var baseAlpha = Read("a", wave.Alpha);
            var frameSlots = _slots;
            _slots = _elementPoints[wave];
            CopyElementInputs(frameSlots, _slots);
            for (var t = 1; t <= 8; t++)
                Write(TNames[t - 1], (float)frameSlots[Preset.Layout.IndexOf(TNames[t - 1])]);
            var step = samples > 1 ? 1f / (samples - 1) : 0f;
            for (var sample = 0; sample < samples; sample++)
            {
                // Milkdrop keeps the two channels separate: value1 is the left trace, value2 the right.
                var value1 = _waveSamples[sample];
                var value2 = _waveSamplesRight[sample];
                Write("sample", sample * step);
                Write("value1", value1);
                Write("value2", value2);
                Write("x", 0.5f + value1);
                Write("y", 0.5f + value2);
                Write("r", baseRed);
                Write("g", baseGreen);
                Write("b", baseBlue);
                Write("a", baseAlpha);
                wave.PerPoint.Execute(_slots);

                _wavePointX[sample] = Read("x", 0.5f);
                _wavePointY[sample] = Read("y", 0.5f);
                _waveRed[sample] = Math.Clamp(Read("r", baseRed), 0f, 1f);
                _waveGreen[sample] = Math.Clamp(Read("g", baseGreen), 0f, 1f);
                _waveBlue[sample] = Math.Clamp(Read("b", baseBlue), 0f, 1f);
                _waveAlpha[sample] = Math.Clamp(Read("a", baseAlpha), 0f, 1f);
            }

            var count = SmoothWavePoints(samples);
            DrawWavePoints(count, wave);
        }
        finally { _slots = presetSlots; }
    }

    /// <summary>Creates isolated frame and point contexts and captures the init T values.</summary>
    private void InitializeElement(object element, PresetProgram init)
    {
        var parent = _slots;
        var context = new double[parent.Length];
        _elementFrames[element] = context;
        _elementPoints[element] = new double[parent.Length];
        _slots = context;
        try
        {
            CopyElementInputs(parent, context);
            if (element is VisualizerWave wave) SeedWave(wave);
            if (element is VisualizerShape shape) SeedShape(shape);
            init.Execute(context);
            var initialT = new double[8];
            for (var t = 0; t < 8; t++) initialT[t] = Read(TNames[t], 0f);
            _elementInitT[element] = initialT;
        }
        finally { _slots = parent; }
    }

    /// <summary>Transfers read-only frame inputs and Q values, never another context's user variables.</summary>
    private void CopyElementInputs(double[] source, double[] target)
    {
        foreach (var name in ElementInputs)
        {
            var slot = Preset.Layout.IndexOf(name);
            if (slot >= 0) target[slot] = source[slot];
        }
        for (var q = 1; q <= 32; q++)
        {
            var slot = Preset.Layout.IndexOf(QNames[q - 1]);
            target[slot] = source[slot];
        }
    }

    /// <summary>Restores the init T values before each frame or shape instance.</summary>
    private void RestoreElementT(object element)
    {
        var initial = _elementInitT[element];
        for (var t = 0; t < 8; t++) Write(TNames[t], (float)initial[t]);
    }

    /// <summary>Seeds a custom wave's saved parameters before init or per-frame evaluation.</summary>
    private void SeedWave(VisualizerWave wave)
    {
        Write("samples", wave.Samples);
        Write("sep", wave.Separation);
        Write("scaling", wave.Scaling);
        Write("smoothing", wave.Smoothing);
        Write("r", wave.Red); Write("g", wave.Green); Write("b", wave.Blue); Write("a", wave.Alpha);
    }

    /// <summary>
    /// Smooths one channel of the sample data forwards and then backwards, which removes the
    /// asymmetry between the start and the end of the trace, and scales it.
    /// </summary>
    /// <param name="samples">Sample buffer to smooth in place.</param>
    /// <param name="count">Number of valid samples.</param>
    /// <param name="mix1">Weight of the previous sample.</param>
    /// <param name="mix2">Weight of the current sample.</param>
    /// <param name="mult">Scale applied after the smoothing.</param>
    private static void SmoothAndScale(float[] samples, int count, float mix1, float mix2, float mult)
    {
        for (var sample = 1; sample < count; sample++)
            samples[sample] = (samples[sample] * mix2) + (samples[sample - 1] * mix1);
        for (var sample = count - 2; sample >= 0; sample--)
            samples[sample] = (samples[sample] * mix2) + (samples[sample + 1] * mix1);
        for (var sample = 0; sample < count; sample++)
            samples[sample] *= mult;
    }

    /// <summary>
    /// Builds the smoothed polyline from the per-point values. Each pair of points gains one extra
    /// point, exactly like the reference's four-tap smoothing, so a trace that turns sharply does not
    /// look like a staircase.
    /// </summary>
    /// <param name="samples">Number of per-point values.</param>
    /// <returns>Number of points in the smoothed polyline.</returns>
    private int SmoothWavePoints(int samples)
    {
        const float c1 = -0.15f;
        const float c2 = 1.15f;
        const float c3 = 1.15f;
        const float c4 = -0.15f;
        const float inverseSum = 1f / (c1 + c2 + c3 + c4);

        var output = 0;
        var below = 0;
        var above2 = 1;
        for (var input = 0; input < samples - 1; input++)
        {
            var above = above2;
            above2 = Math.Min(samples - 1, input + 2);
            _waveOutX[output] = _wavePointX[input];
            _waveOutY[output] = _wavePointY[input];
            _waveOutRed[output] = _waveRed[input];
            _waveOutGreen[output] = _waveGreen[input];
            _waveOutBlue[output] = _waveBlue[input];
            _waveOutAlpha[output] = _waveAlpha[input];
            output++;

            _waveOutX[output] = ((c1 * _wavePointX[below]) + (c2 * _wavePointX[input]) +
                                 (c3 * _wavePointX[above]) + (c4 * _wavePointX[above2])) * inverseSum;
            _waveOutY[output] = ((c1 * _wavePointY[below]) + (c2 * _wavePointY[input]) +
                                 (c3 * _wavePointY[above]) + (c4 * _wavePointY[above2])) * inverseSum;
            _waveOutRed[output] = _waveRed[input];
            _waveOutGreen[output] = _waveGreen[input];
            _waveOutBlue[output] = _waveBlue[input];
            _waveOutAlpha[output] = _waveAlpha[input];
            output++;

            below = input;
        }

        if (output < _waveOutX.Length)
        {
            _waveOutX[output] = _wavePointX[samples - 1];
            _waveOutY[output] = _wavePointY[samples - 1];
            _waveOutRed[output] = _waveRed[samples - 1];
            _waveOutGreen[output] = _waveGreen[samples - 1];
            _waveOutBlue[output] = _waveBlue[samples - 1];
            _waveOutAlpha[output] = _waveAlpha[samples - 1];
            output++;
        }

        return output;
    }

    /// <summary>Draws the smoothed polyline as dots or as connected segments.</summary>
    /// <param name="count">Number of polyline points.</param>
    /// <param name="wave">Waveform state, for the dot and thickness modes.</param>
    private void DrawWavePoints(int count, VisualizerWave wave)
    {
        var width = _fresh.Width;
        var height = _fresh.Height;
        if (count < 1 || width < 1 || height < 1)
            return;

        var inverseAspectX = MathF.Max(1f, height / (float)width);
        var inverseAspectY = MathF.Max(1f, width / (float)height);

        for (var index = 0; index < count; index++)
        {
            // MilkDrop stores custom-wave points in aspect-corrected clip space.
            // Its inverse aspect expands the short screen axis about the centre.
            var x = (0.5f + (_waveOutX[index] - 0.5f) * inverseAspectX) * (width - 1);
            var y = (0.5f - (_waveOutY[index] - 0.5f) * inverseAspectY) * (height - 1);
            var red = _waveOutRed[index];
            var green = _waveOutGreen[index];
            var blue = _waveOutBlue[index];
            var alpha = _waveOutAlpha[index];

            if (wave.UseDots)
            {
                if (float.IsFinite(x) && float.IsFinite(y) && x >= 0f && x < width && y >= 0f && y < height)
                {
                    // Milkdrop's point size is two at normal texture sizes and one larger with
                    // bDrawThick, so a thick dot covers a two-by-two square (milkdropfs.cpp,
                    // DrawCustomWaves). Dots are not the four-offset line path.
                    var pointSize = (width >= 1024 ? 2 : 1) + (wave.DrawThick ? 1 : 0);
                    var pixelX = (int)x;
                    var pixelY = (int)y;
                    for (var offsetY = 0; offsetY < pointSize; offsetY++)
                        for (var offsetX = 0; offsetX < pointSize; offsetX++)
                            PaintPixel(pixelX + offsetX, pixelY + offsetY, red, green, blue, alpha, wave.Additive);
                }
                continue;
            }

            if (index == 0)
                continue;

            var previousX = (0.5f + (_waveOutX[index - 1] - 0.5f) * inverseAspectX) * (width - 1);
            var previousY = (0.5f - (_waveOutY[index - 1] - 0.5f) * inverseAspectY) * (height - 1);
            if (ClipWaveSegment(ref previousX, ref previousY, ref x, ref y, width, height))
                DrawWaveSegment(
                    (int)previousX, (int)previousY,
                    (int)x, (int)y,
                    _waveOutRed[index - 1], _waveOutGreen[index - 1], _waveOutBlue[index - 1], _waveOutAlpha[index - 1],
                    red, green, blue, alpha,
                    wave.Additive, wave.DrawThick);
        }
    }

    /// <summary>Clips a waveform segment to the frame, as the original graphics API does.</summary>
    /// <param name="x0">First endpoint column, updated to the clipped position.</param>
    /// <param name="y0">First endpoint row, updated to the clipped position.</param>
    /// <param name="x1">Second endpoint column, updated to the clipped position.</param>
    /// <param name="y1">Second endpoint row, updated to the clipped position.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <returns>Whether any part of the segment intersects the frame.</returns>
    private static bool ClipWaveSegment(ref float x0, ref float y0, ref float x1, ref float y1, int width, int height)
    {
        if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1))
            return false;

        var dx = x1 - x0;
        var dy = y1 - y0;
        var enter = 0f;
        var leave = 1f;
        if (!ClipWaveEdge(-dx, x0, ref enter, ref leave) ||
            !ClipWaveEdge(dx, (width - 1) - x0, ref enter, ref leave) ||
            !ClipWaveEdge(-dy, y0, ref enter, ref leave) ||
            !ClipWaveEdge(dy, (height - 1) - y0, ref enter, ref leave))
            return false;

        x1 = x0 + leave * dx;
        y1 = y0 + leave * dy;
        x0 += enter * dx;
        y0 += enter * dy;
        return true;
    }

    /// <summary>Restricts the visible portion of a waveform segment against one frame edge.</summary>
    /// <param name="p">Signed direction perpendicular to the edge.</param>
    /// <param name="q">Distance from the first endpoint to the edge.</param>
    /// <param name="enter">Entering fraction, updated in place.</param>
    /// <param name="leave">Leaving fraction, updated in place.</param>
    /// <returns>Whether a visible segment remains.</returns>
    private static bool ClipWaveEdge(float p, float q, ref float enter, ref float leave)
    {
        if (p == 0f)
            return q >= 0f;
        var fraction = q / p;
        if (p < 0f)
            enter = MathF.Max(enter, fraction);
        else
            leave = MathF.Min(leave, fraction);
        return enter <= leave;
    }

    /// <summary>Draws one anti-aliased-free line segment of a custom waveform.</summary>
    /// <param name="x0">Start column.</param>
    /// <param name="y0">Start row.</param>
    /// <param name="x1">End column.</param>
    /// <param name="y1">End row.</param>
    /// <param name="startRed">Red at the start, zero to one.</param>
    /// <param name="startGreen">Green at the start, zero to one.</param>
    /// <param name="startBlue">Blue at the start, zero to one.</param>
    /// <param name="startAlpha">Opacity at the start, zero to one.</param>
    /// <param name="endRed">Red at the end, zero to one.</param>
    /// <param name="endGreen">Green at the end, zero to one.</param>
    /// <param name="endBlue">Blue at the end, zero to one.</param>
    /// <param name="endAlpha">Opacity at the end, zero to one.</param>
    /// <param name="additive">Whether the colour is added instead of alpha-blended.</param>
    /// <param name="thick">Whether the segment uses MilkDrop's four full-opacity offsets.</param>
    private void DrawWaveSegment(
        int x0, int y0, int x1, int y1,
        float startRed, float startGreen, float startBlue, float startAlpha,
        float endRed, float endGreen, float endBlue, float endAlpha,
        bool additive, bool thick)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0)
        {
            PaintPixel(x0, y0, endRed, endGreen, endBlue, endAlpha, additive);
            if (thick)
            {
                PaintPixel(x0 + 1, y0, endRed, endGreen, endBlue, endAlpha, additive);
                PaintPixel(x0 + 1, y0 - 1, endRed, endGreen, endBlue, endAlpha, additive);
                PaintPixel(x0, y0 - 1, endRed, endGreen, endBlue, endAlpha, additive);
            }
            return;
        }

        for (var step = 0; step <= steps; step++)
        {
            // Milkdrop draws the line strip with per-vertex diffuse, so the colour runs from the
            // start vertex to the end vertex instead of using one colour for the whole segment.
            var fraction = step / (float)steps;
            var red = Lerp(startRed, endRed, fraction);
            var green = Lerp(startGreen, endGreen, fraction);
            var blue = Lerp(startBlue, endBlue, fraction);
            var alpha = Lerp(startAlpha, endAlpha, fraction);
            var x = x0 + (int)MathF.Round(dx * step / (float)steps);
            var y = y0 + (int)MathF.Round(dy * step / (float)steps);
            PaintPixel(x, y, red, green, blue, alpha, additive);
            if (thick)
            {
                PaintPixel(x + 1, y, red, green, blue, alpha, additive);
                PaintPixel(x + 1, y - 1, red, green, blue, alpha, additive);
                PaintPixel(x, y - 1, red, green, blue, alpha, additive);
            }
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
        var length = Math.Clamp(Read("mv_l", 1f), 0f, 1f);
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
        if (UseSkiaFramePasses)
        {
            try
            {
                SkiaShaderRunner.Borders(_warped, ReadBand("ob_", 0f, 0.02f), ReadBand("ib_", 0.06f, 0.02f));
                return;
            }
            catch (Exception exception)
            {
                ShaderError = "borders (skia): " + exception.GetType().Name + ": " + exception.Message;
            }
        }

        DrawBorderFrame(0f, 0.02f);
        DrawBorderFrame(0.06f, 0.02f);
    }

    /// <summary>Reads one border band's colour keys.</summary>
    /// <param name="prefix">Key prefix, <c>ob_</c> or <c>ib_</c>.</param>
    /// <param name="inset">Inset as a fraction of the smaller dimension.</param>
    /// <param name="thickness">Band thickness as a fraction of the smaller dimension.</param>
    /// <returns>The band.</returns>
    private SkiaShaderRunner.BorderBand ReadBand(string prefix, float inset, float thickness) =>
        new(
            inset,
            thickness,
            Math.Clamp(Read(prefix + "r", 1f), 0f, 1f),
            Math.Clamp(Read(prefix + "g", 1f), 0f, 1f),
            Math.Clamp(Read(prefix + "b", 1f), 0f, 1f),
            Math.Clamp(Read(prefix + "a", 0f), 0f, 1f));

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
        if (UseSkiaFramePasses)
        {
            try
            {
                SkiaShaderRunner.VideoEcho(_warped, alpha, zoom, orientation);
                return;
            }
            catch (Exception exception)
            {
                ShaderError = "echo (skia): " + exception.GetType().Name + ": " + exception.Message;
            }
        }

        var width = _warped.Width;
        var height = _warped.Height;
        _fresh.CopyFrom(_warped);
        var target = _warped.RawPixels;
        ParallelRows.For(ParallelismEnabled, height, (worker, from, to) =>
        {
            // Each worker samples into its own scratch, so two pixels never race on one buffer.
            var sample = _workerSample[worker];
            for (var y = from; y < to; y++)
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

                    _fresh.SampleBilinear(sampleU, sampleV, sample);
                    var offset = (((y * width) + x) * 4);
                    target[offset] = Math.Clamp((target[offset] * (1f - alpha)) + (sample[0] * alpha), 0f, 1f);
                    target[offset + 1] = Math.Clamp((target[offset + 1] * (1f - alpha)) + (sample[1] * alpha), 0f, 1f);
                    target[offset + 2] = Math.Clamp((target[offset + 2] * (1f - alpha)) + (sample[2] * alpha), 0f, 1f);
                }
            }
        });
    }

    /// <summary>Draws every custom shape of the preset.</summary>
    private void DrawShapes()
    {
        _shapeFills.Clear();
        foreach (var shape in Preset.Shapes)
        {
            if (!shape.Enabled) continue;
            if (!_elementFrames.ContainsKey(shape)) InitializeElement(shape, shape.Init);
            var parent = _slots;
            _slots = _elementFrames[shape];
            try
            {
                for (var instance = 0; instance < shape.Instances; instance++)
                {
                    CopyElementInputs(parent, _slots);
                    RestoreElementT(shape);
                    SeedShape(shape);
                    Write("instance", instance);
                    shape.PerFrame.Execute(_slots);
                    var centreX = Read("x", shape.X);
                    var centreY = Read("y", shape.Y);
                    var radius = Math.Max(0f, Read("rad", shape.Radius));
                    var angle = Read("ang", shape.Angle);
                    var sides = (int)Math.Clamp(Read("sides", shape.Sides), shape.MilkdropCoordinates ? 3f : 0f, 100f);
                    var red = Math.Clamp(Read("r", shape.Red), 0f, 1f);
                    var green = Math.Clamp(Read("g", shape.Green), 0f, 1f);
                    var blue = Math.Clamp(Read("b", shape.Blue), 0f, 1f);
                    var alpha = Math.Clamp(Read("a", shape.Alpha), 0f, 1f);
                    var additive = Read("additive", shape.Additive ? 1f : 0f) != 0f;
                    var vertices = BuildVertices(shape, sides, centreX, centreY, radius, angle);
                    if (shape.MilkdropCoordinates)
                    {
                        // The fan centre is in Milkdrop's y-up space too, so it is converted the same way
                        // as the vertices before it reaches the rasterizer or the GPU.
                        var centreFanX = centreX * 2f - 1f;
                        var centreFanY = 2f * centreY - 1f;
                        if (CollectShapeFills)
                            CollectShapeFill(vertices, centreFanX, centreFanY, red, green, blue, alpha, additive);
                        else
                            FillShapeFan(vertices, centreFanX, centreFanY, red, green, blue, alpha, additive);
                    }
                    else
                        FillPolygon(vertices, red, green, blue, alpha, additive);
                    DrawPolygonBorder(vertices, shape);
                }
            }
            finally { _slots = parent; }
        }
    }

    /// <summary>
    /// Publishes one Milkdrop shape's fill as a triangle fan instead of rasterizing it. The centre
    /// vertex carries the shape's first colour and the texture centre, and the rim vertices carry its
    /// second colour and the fan's rim coordinates, which is exactly the interpolation
    /// <see cref="FillShapeFan"/> applies.
    /// </summary>
    /// <param name="vertices">Rim vertices in the rasterizer's y-down space.</param>
    /// <param name="centreX">Fan centre in the rasterizer's space.</param>
    /// <param name="centreY">Fan centre in the rasterizer's space.</param>
    /// <param name="red">Rim red, which is the shape's first colour, zero to one.</param>
    /// <param name="green">Rim green, zero to one.</param>
    /// <param name="blue">Rim blue, zero to one.</param>
    /// <param name="alpha">Rim alpha, zero to one.</param>
    /// <param name="additive">Whether the fill adds to the frame instead of blending over it.</param>
    private void CollectShapeFill(
        (float X, float Y)[] vertices,
        float centreX,
        float centreY,
        float red,
        float green,
        float blue,
        float alpha,
        bool additive)
    {
        var red2 = Read("r2", 1f); var green2 = Read("g2", 1f);
        var blue2 = Read("b2", 1f); var alpha2 = Read("a2", 0f);
        var textured = Read("textured", 0f) != 0f;
        if (textured)
            BuildShapeTextureCoordinates(vertices.Length);

        // The fan closes on itself, so the first rim vertex is repeated: GL_TRIANGLE_FAN does not wrap
        // and the CPU's rasterizer does, and the missing wedge is a visible notch.
        var fan = new ShapeFillVertex[vertices.Length + 2];
        fan[0] = new ShapeFillVertex(centreX, centreY, red, green, blue, alpha, 0.5f, 0.5f);
        for (var index = 0; index < vertices.Length; index++)
        {
            fan[index + 1] = new ShapeFillVertex(
                vertices[index].X,
                vertices[index].Y,
                red2,
                green2,
                blue2,
                alpha2,
                textured ? _shapeUvX[index] : 0.5f,
                textured ? _shapeUvY[index] : 0.5f);
        }

        fan[^1] = fan[1];
        _shapeFills.Add(new ShapeFill(fan, textured, additive));
    }

    /// <summary>Rasterizes the Milkdrop triangle fan with interpolated centre and edge colours.</summary>
    private void FillShapeFan((float X, float Y)[] vertices, float cx, float cy,
        float red, float green, float blue, float alpha, bool additive)
    {
        var red2 = Read("r2", 1f); var green2 = Read("g2", 1f);
        var blue2 = Read("b2", 1f); var alpha2 = Read("a2", 0f);
        // A textured shape samples the frame instead of using the gradient colours.
        var textured = Read("textured", 0f) != 0f;
        var width = _fresh.Width; var height = _fresh.Height;
        if (textured)
            BuildShapeTextureCoordinates(vertices.Length);

        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i]; var b = vertices[(i + 1) % vertices.Length];
            var next = (i + 1) % vertices.Length;
            var det = (a.Y - b.Y) * (cx - b.X) + (b.X - a.X) * (cy - b.Y);
            if (MathF.Abs(det) < 1e-9f) continue;
            var minX = Math.Max(0, (int)MathF.Floor((Math.Min(cx, Math.Min(a.X, b.X)) + 1f) * width * 0.5f));
            var maxX = Math.Min(width - 1, (int)MathF.Ceiling((Math.Max(cx, Math.Max(a.X, b.X)) + 1f) * width * 0.5f));
            var minY = Math.Max(0, (int)MathF.Floor((Math.Min(cy, Math.Min(a.Y, b.Y)) + 1f) * height * 0.5f));
            var maxY = Math.Min(height - 1, (int)MathF.Ceiling((Math.Max(cy, Math.Max(a.Y, b.Y)) + 1f) * height * 0.5f));
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var px = (x + 0.5f) * 2f / width - 1f;
                var py = (y + 0.5f) * 2f / height - 1f;
                var w0 = ((a.Y - b.Y) * (px - b.X) + (b.X - a.X) * (py - b.Y)) / det;
                var w1 = ((b.Y - cy) * (px - b.X) + (cx - b.X) * (py - b.Y)) / det;
                var w2 = 1f - w0 - w1;
                // Half-open radial edge avoids blending shared fan edges twice.
                if (w0 < 0f || w1 < 0f || w2 <= 0f) continue;
                var pixelAlpha = Math.Clamp(alpha2 + (alpha - alpha2) * w0, 0f, 1f);
                if (textured)
                {
                    // The coordinate is interpolated across the fan like the reference's textured
                    // shape: the centre maps to the texture centre and the rim to a circle whose
                    // radius is 0.5 / tex_zoom.
                    var u = (0.5f * w0) + (_shapeUvX[i] * w1) + (_shapeUvX[next] * w2);
                    var v = (0.5f * w0) + (_shapeUvY[i] * w1) + (_shapeUvY[next] * w2);
                    // MilkDrop binds VS[0], the previous feedback, as the textured shape's
                    // source while drawing the shape into the newly warped VS[1].
                    _previous.SampleShader(u, v, VisualizerTextureWrap.Repeat, nearest: false, _shapeSample);
                    var pixelRed = red2 + (red - red2) * w0;
                    var pixelGreen = green2 + (green - green2) * w0;
                    var pixelBlue = blue2 + (blue - blue2) * w0;
                    PaintPixel(x, y, _shapeSample[0] * pixelRed, _shapeSample[1] * pixelGreen,
                        _shapeSample[2] * pixelBlue, pixelAlpha, additive);
                    continue;
                }

                PaintPixel(x, y, red2 + (red - red2) * w0, green2 + (green - green2) * w0,
                    blue2 + (blue - blue2) * w0, pixelAlpha, additive);
            }
        }
    }

    /// <summary>
    /// Builds the per-vertex texture coordinates of a textured shape. The centre of the fan maps to
    /// the texture centre and each rim vertex to a circle whose radius is <c>0.5 / tex_zoom</c>,
    /// rotated by <c>tex_ang</c>; the engine's frames are top-down, so the vertical axis is mirrored.
    /// </summary>
    /// <param name="count">Rim vertex count.</param>
    private void BuildShapeTextureCoordinates(int count)
    {
        var texZoom = Math.Max(0.01f, Read("tex_zoom", 1f));
        var texAngle = Read("tex_ang", 0f);
        WarpSampling.GetAspect(_warped.Width, _warped.Height, out _, out var aspectY);
        var limit = Math.Min(count, _shapeUvX.Length);
        for (var i = 0; i < limit; i++)
        {
            var angle = (i / (float)count * 2f * MathF.PI) + texAngle + (MathF.PI * 0.25f);
            _shapeUvX[i] = 0.5f + (0.5f * MathF.Cos(angle) / texZoom * aspectY);
            _shapeUvY[i] = 0.5f - (0.5f * MathF.Sin(angle) / texZoom);
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
            var vertexAngle = angle + (t * 2f * MathF.PI) + (shape.MilkdropCoordinates ? MathF.PI * 0.25f : 0f);
            var x = centreX + (MathF.Cos(vertexAngle) * radius);
            var y = centreY + (MathF.Sin(vertexAngle) * radius);
            if (shape.MilkdropCoordinates)
            {
                var aspect = Math.Min(1f, _fresh.Height / (float)_fresh.Width);
                x = centreX * 2f - 1f + MathF.Cos(vertexAngle) * radius * aspect;
                y = 1f - centreY * 2f + MathF.Sin(vertexAngle) * radius;
            }

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

            // Milkdrop's shape space is Direct3D's y-up space while the overlay rasterizer is y-down, so
            // a Milkdrop shape's y is negated only when it leaves the preset's own expression space.
            // Orynivo's shape_N_* keys keep the raster space they were authored in.
            vertices[index] = shape.MilkdropCoordinates ? (x, -y) : (x, y);
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
        if (vertices.Length < 2 || Read("border_a", shape.BorderAlpha) <= 0f)
            return;

        var width = _fresh.Width;
        var height = _fresh.Height;
        for (var index = 0; index < vertices.Length; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % vertices.Length];
            var steps = Math.Max(2, (int)Math.Max(MathF.Abs(b.X - a.X) * width, MathF.Abs(b.Y - a.Y) * height));
            for (var step = 0; step <= steps; step++)
            {
                var t = step / (float)steps;
                var x = a.X + ((b.X - a.X) * t);
                var y = a.Y + ((b.Y - a.Y) * t);
                var column = (int)((x * 0.5f + 0.5f) * (width - 1));
                var row = (int)((y * 0.5f + 0.5f) * (height - 1));
                var thickness = Read("thick", shape.ThickOutline ? 1f : 0f) != 0f ? 2 : 1;
                for (var offsetY = 0; offsetY < thickness; offsetY++)
                for (var offsetX = 0; offsetX < thickness; offsetX++)
                PaintPixel(
                    column + offsetX,
                    row + offsetY,
                    Read("border_r", shape.BorderRed),
                    Read("border_g", shape.BorderGreen),
                    Read("border_b", shape.BorderBlue),
                    Read("border_a", shape.BorderAlpha),
                    Read("additive", shape.Additive ? 1f : 0f) != 0f);
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

        if (!additive)
            _fresh.Pixels[offset + 3] += (1f - _fresh.Pixels[offset + 3]) * alpha;
    }

    /// <summary>Seeds a shape's default values into the shared slots.</summary>
    /// <param name="shape">Shape about to be drawn.</param>
    private void SeedShape(VisualizerShape shape)
    {
        Write("sides", shape.Sides);
        Write("r2", shape.Red2); Write("g2", shape.Green2); Write("b2", shape.Blue2); Write("a2", shape.Alpha2);
        Write("border_r", shape.BorderRed); Write("border_g", shape.BorderGreen);
        Write("border_b", shape.BorderBlue); Write("border_a", shape.BorderAlpha);
        Write("additive", shape.Additive ? 1f : 0f);
        Write("textured", shape.Textured ? 1f : 0f);
        Write("tex_zoom", shape.TextureZoom); Write("tex_ang", shape.TextureAngle);
        Write("num_inst", shape.Instances);
        Write("thick", shape.ThickOutline ? 1f : 0f);
        Write("x", shape.X);
        Write("y", shape.Y);
        Write("rad", shape.Radius);
        Write("ang", shape.Angle);
        Write("r", shape.Red);
        Write("g", shape.Green);
        Write("b", shape.Blue);
        Write("a", shape.Alpha);
    }

    /// <summary>
    /// Composites premultiplied overlay RGB with separately accumulated coverage onto the feedback.
    /// Adds the overlay frame (<see cref="_fresh"/>) onto the warped frame (<see cref="_warped"/>). It
    /// runs before the centre darkening and the border, so those later passes cover the overlay the way
    /// the reference draws its shapes and waves first; <see cref="Publish"/> copies the finished frame
    /// back into the display buffer afterwards.
    /// </summary>
    private void Composite()
    {
        var overlay = _fresh.RawPixels;
        var warped = _warped.RawPixels;
        var stride = _warped.Width * 4;
        ParallelRows.For(ParallelismEnabled, _warped.Height, (worker, from, to) =>
        {
            var end = to * stride;
            for (var index = from * stride; index < end; index += 4)
            {
                warped[index] = Math.Clamp(warped[index] * (1f - overlay[index + 3]) + overlay[index], 0f, 1f);
                warped[index + 1] = Math.Clamp(warped[index + 1] * (1f - overlay[index + 3]) + overlay[index + 1], 0f, 1f);
                warped[index + 2] = Math.Clamp(warped[index + 2] * (1f - overlay[index + 3]) + overlay[index + 2], 0f, 1f);
                warped[index + 3] = 1f;
            }
        });
    }

    /// <summary>Copies the finished frame into the display buffer, which the presenter shows.</summary>
    private void Publish() => _fresh.CopyFrom(_warped);

    /// <summary>Reads a variable of the shared slot layout, for diagnostics and tests.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The current value, or zero when the layout does not contain the name.</returns>
    public float ReadVariable(string name) => Read(name, 0f);

    private float Read(string name, float fallback)
    {
        var slot = Preset.Layout.IndexOf(name);
        return slot < 0 ? fallback : (float)_slots[slot];
    }

    private void Write(string name, float value)
    {
        var slot = Preset.Layout.IndexOf(name);
        if (slot >= 0)
            _slots[slot] = value;
    }

    /// <summary>Writes a slot that was resolved once, instead of looking the name up per pixel.</summary>
    /// <param name="slot">Slot index, or a negative value when the layout lacks the variable.</param>
    /// <param name="value">Value to store.</param>
    private void Write(int slot, float value) => Write(_slots, slot, value);

    /// <summary>Writes a slot of a specific slot array, used by the parallel workers.</summary>
    /// <param name="slots">Slot array to write to.</param>
    /// <param name="slot">Slot index, or a negative value when the layout lacks the variable.</param>
    /// <param name="value">Value to store.</param>
    private static void Write(double[] slots, int slot, float value)
    {
        if (slot >= 0)
            slots[slot] = value;
    }

    /// <summary>Reads a slot that was resolved once, instead of looking the name up per pixel.</summary>
    /// <param name="slot">Slot index, or a negative value when the layout lacks the variable.</param>
    /// <param name="fallback">Value used when the layout lacks the variable.</param>
    /// <returns>The stored value.</returns>
    private float Read(int slot, float fallback) => Read(_slots, slot, fallback);

    /// <summary>Reads a slot of a specific slot array, used by the parallel workers.</summary>
    /// <param name="slots">Slot array to read from.</param>
    /// <param name="slot">Slot index, or a negative value when the layout lacks the variable.</param>
    /// <param name="fallback">Value used when the layout lacks the variable.</param>
    /// <returns>The stored value.</returns>
    private static float Read(double[] slots, int slot, float fallback) => slot < 0 ? fallback : (float)slots[slot];

    /// <summary>Reports whether the engine re-seeds a variable for every pixel.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns><see langword="true"/> when the warp stage writes the variable before each pixel.</returns>
    private static bool IsSeededPerPixel(string name) => name is "x" or "y" or "rad" or "ang";

    /// <summary>Adds every variable a program reads to a set.</summary>
    /// <param name="target">Set to add to.</param>
    /// <param name="program">Program to inspect; a missing program contributes nothing.</param>
    private static void AddReferencedVariables(HashSet<string> target, PresetProgram? program)
    {
        if (program is null)
            return;
        foreach (var name in program.ReferencedVariables)
            target.Add(name);
    }
}




