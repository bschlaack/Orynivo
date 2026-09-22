namespace Orynivo.Visualization;

/// <summary>
/// The shared slot layout of one preset. Milkdrop presets pass user variables such as
/// <c>q1</c> from the per-frame stage into the per-pixel stage, so every expression block
/// of a preset compiles against the same layout and runs over the same slot array.
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
        string[] names =
        [
            "time", "fps", "frame", "monitor",
            "bass", "mid", "treb", "vol", "bass_att", "mid_att", "treb_att",
            "aspectx", "aspecty", "pixelsx", "pixelsy",
            "decay", "fDecay", "fGammaAdj", "fWarpAmount", "fWaveAlpha", "fWaveScale",
            "zoom", "zoomexp", "rot", "cx", "cy", "dx", "dy", "warp", "sx", "sy",
            "blur1", "blur2", "blur3", "darken_center",
            "blur1_min", "blur1_max", "blur1_edge_darken",
            "blur2_min", "blur2_max", "blur2_edge_darken",
            "blur3_min", "blur3_max", "blur3_edge_darken",
            "wave_mode", "wave_r", "wave_g", "wave_b", "wave_a", "wave_x", "wave_y",
            "wave_mystery", "wave_dots", "wave_thick", "wave_additive", "wave_brighten",
            "ob_r", "ob_g", "ob_b", "ob_a", "ib_r", "ib_g", "ib_b", "ib_a",
            "mv_x", "mv_y", "mv_dx", "mv_dy", "mv_l", "mv_enabled",
            "echo_zoom", "echo_alpha", "echo_orient",
            "fVideoEchoZoom", "fVideoEchoAlpha", "nVideoEchoOrientation",
            "x", "y", "rad", "ang", "progress", "meshx", "meshy", "rand_frame"
        ];
        foreach (var name in names)
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
