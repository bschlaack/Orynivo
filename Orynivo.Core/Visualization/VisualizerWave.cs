namespace Orynivo.Visualization;

/// <summary>
/// One of the four Milkdrop custom waveforms. Its state comes from the <c>wavecode_N_*</c> keys and
/// its expression blocks from the <c>wave_N_*</c> keys; the renderer builds the sample data and runs
/// the per-point block with the reference's <c>sample</c>, <c>value1</c>, and <c>value2</c> contract.
/// </summary>
public sealed class VisualizerWave
{
    /// <summary>Creates a custom waveform from its parsed state and programs.</summary>
    /// <param name="enabled">Whether the waveform is drawn.</param>
    /// <param name="samples">Number of samples the waveform is built from.</param>
    /// <param name="separation">Separation between the two channel traces.</param>
    /// <param name="spectrum">Whether the waveform reads the spectrum instead of the PCM waveform.</param>
    /// <param name="useDots">Whether the waveform is drawn as dots instead of a line.</param>
    /// <param name="drawThick">Whether the line is drawn thick.</param>
    /// <param name="additive">Whether the colour is added instead of alpha-blended.</param>
    /// <param name="scaling">Vertical scale of the sample data.</param>
    /// <param name="smoothing">Smoothing applied to the sample data, zero to one.</param>
    /// <param name="red">Default red, zero to one.</param>
    /// <param name="green">Default green, zero to one.</param>
    /// <param name="blue">Default blue, zero to one.</param>
    /// <param name="alpha">Default opacity, zero to one.</param>
    /// <param name="init">One-time initialisation block.</param>
    /// <param name="perFrame">Block that runs once per frame before the waveform is drawn.</param>
    /// <param name="perPoint">Block that may move and colour every point.</param>
    public VisualizerWave(
        bool enabled,
        int samples,
        int separation,
        bool spectrum,
        bool useDots,
        bool drawThick,
        bool additive,
        float scaling,
        float smoothing,
        float red,
        float green,
        float blue,
        float alpha,
        PresetProgram init,
        PresetProgram perFrame,
        PresetProgram perPoint)
    {
        Enabled = enabled;
        Samples = samples;
        Separation = separation;
        Spectrum = spectrum;
        UseDots = useDots;
        DrawThick = drawThick;
        Additive = additive;
        Scaling = scaling;
        Smoothing = smoothing;
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
        Init = init;
        PerFrame = perFrame;
        PerPoint = perPoint;
    }

    /// <summary>Gets whether the waveform is drawn.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the number of samples the waveform is built from.</summary>
    public int Samples { get; }

    /// <summary>Gets the separation between the two channel traces.</summary>
    public int Separation { get; }

    /// <summary>Gets whether the waveform reads the spectrum instead of the PCM waveform.</summary>
    public bool Spectrum { get; }

    /// <summary>Gets whether the waveform is drawn as dots instead of a line.</summary>
    public bool UseDots { get; }

    /// <summary>Gets whether the line is drawn thick.</summary>
    public bool DrawThick { get; }

    /// <summary>Gets whether the colour is added instead of alpha-blended.</summary>
    public bool Additive { get; }

    /// <summary>Gets the vertical scale of the sample data.</summary>
    public float Scaling { get; }

    /// <summary>Gets the smoothing applied to the sample data, zero to one.</summary>
    public float Smoothing { get; }

    /// <summary>Gets the default red, zero to one.</summary>
    public float Red { get; }

    /// <summary>Gets the default green, zero to one.</summary>
    public float Green { get; }

    /// <summary>Gets the default blue, zero to one.</summary>
    public float Blue { get; }

    /// <summary>Gets the default opacity, zero to one.</summary>
    public float Alpha { get; }

    /// <summary>Gets the one-time initialisation block.</summary>
    public PresetProgram Init { get; }

    /// <summary>Gets the block that runs once per frame before the waveform is drawn.</summary>
    public PresetProgram PerFrame { get; }

    /// <summary>Gets the block that may move and colour every point.</summary>
    public PresetProgram PerPoint { get; }

    /// <summary>Gets whether the waveform declares no code at all.</summary>
    public bool IsEmpty =>
        Init.IsEmpty && PerFrame.IsEmpty && PerPoint.IsEmpty;
}
