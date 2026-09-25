namespace Orynivo.Visualization;

/// <summary>
/// The shared slot layout of one preset. Milkdrop presets pass user variables such as
/// <c>q1</c> from the per-frame stage into the per-pixel stage, so every expression block
/// of a preset compiles against the same layout. Custom elements use isolated slot arrays.
/// </summary>
public sealed class PresetVariableLayout
{
    private readonly List<string> _names = [];
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    /// <summary>Gets the variable names in slot order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Gets the number of allocated slots.</summary>
    public int Count => _names.Count;

    /// <summary>Returns the slot of a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index, or <c>-1</c> when the layout does not contain it.</returns>
    public int IndexOf(string name) =>
        name is not null && _slots.TryGetValue(name, out var slot) ? slot : -1;

    /// <summary>The fixed part of the standard Milkdrop variable set.</summary>
    private static readonly string[] StandardNames =
    [
        "time", "fps", "frame", "monitor",
        "r", "g", "b", "a", "r2", "g2", "b2", "a2", "sides", "additive",
        "border_r", "border_g", "border_b", "border_a", "thickoutline", "thick",
        "textured", "tex_zoom", "tex_ang", "instance", "num_inst",
        "samples", "sep", "scaling", "smoothing", "sample", "value1", "value2",
        "fWarpAnimSpeed", "fWarpScale", "bTexWrap",
        "bass", "mid", "treb", "vol", "bass_att", "mid_att", "treb_att",
        "aspectx", "aspecty", "pixelsx", "pixelsy",
        "decay", "fDecay", "fGammaAdj", "shader", "fWarpAmount", "fWaveAlpha", "fWaveScale",
        "zoom", "zoomexp", "rot", "cx", "cy", "dx", "dy", "warp", "sx", "sy",
        "blur1", "blur2", "blur3", "darken_center",
        "blur1_min", "blur1_max", "blur1_edge_darken",
        "blur2_min", "blur2_max", "blur2_edge_darken",
        "blur3_min", "blur3_max", "blur3_edge_darken",
        "wave_mode", "wave_r", "wave_g", "wave_b", "wave_a", "wave_x", "wave_y",
        "wave_mystery", "wave_dots", "wave_thick", "wave_additive", "wave_brighten",
        "ob_size", "ob_r", "ob_g", "ob_b", "ob_a", "ib_size", "ib_r", "ib_g", "ib_b", "ib_a",
        "mv_x", "mv_y", "mv_dx", "mv_dy", "mv_l", "mv_enabled",
        "echo_zoom", "echo_alpha", "echo_orient",
        "fVideoEchoZoom", "fVideoEchoAlpha", "nVideoEchoOrientation",
        "x", "y", "rad", "ang", "progress", "meshx", "meshy", "rand_frame"
    ];

    /// <summary>
    /// Registers the standard Milkdrop variable set up front, so the renderer can always write
    /// every variable a preset may read and user variables such as <c>q1</c> keep their value
    /// between stages. Presets do not have to mention any of them.
    /// </summary>
    /// <param name="layout">Layout to extend.</param>
    /// <returns>The same layout for chaining.</returns>
    public static PresetVariableLayout RegisterStandardVariables(PresetVariableLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var name in StandardNames)
            layout.GetOrAdd(name);

        for (var index = 1; index <= 32; index++)
            layout.GetOrAdd("q" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 1; index <= 8; index++)
            layout.GetOrAdd("b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Milkdrop provides eight general-purpose "T" variables next to the 32 Q ones.
        for (var index = 1; index <= 8; index++)
            layout.GetOrAdd("t" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return layout;
    }

    /// <summary>
    /// Gets the names the engine provides, so a stage can tell a preset's own variable from one
    /// another stage or the renderer itself reads. A per-pixel block may only treat a written name
    /// as its own local value when it is not one of these.
    /// </summary>
    public static IReadOnlySet<string> Standard { get; } = BuildStandardSet();

    /// <summary>Builds the standard-name set, including the numbered general-purpose variables.</summary>
    /// <returns>The standard variable names.</returns>
    private static HashSet<string> BuildStandardSet()
    {
        var names = new HashSet<string>(StandardNames, StringComparer.Ordinal);
        for (var index = 1; index <= 32; index++)
            names.Add("q" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 1; index <= 8; index++)
            names.Add("b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 1; index <= 8; index++)
            names.Add("t" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return names;
    }

    /// <summary>Returns the slot of a variable, allocating one on first use.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index.</returns>
    public int GetOrAdd(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_slots.TryGetValue(name, out var existing))
            return existing;

        var slot = _names.Count;
        _names.Add(name);
        _slots[name] = slot;
        return slot;
    }
}
