namespace Orynivo.Visualization;

/// <summary>
/// The pure blending rules MilkDrop uses while one preset replaces another. The reference keeps the
/// outgoing preset running for the blend duration, then eases the incoming preset in: most
/// per-frame variables are interpolated with a cosine curve, integer and boolean switches snap at a
/// threshold, and the motion variables that drive the warp geometry are never blended at all.
/// Keeping the variable contract here lets the renderer and its tests share one definition.
/// </summary>
public static class PresetBlend
{
    /// <summary>
    /// Eases a zero-to-one blend progress with MilkDrop's <c>CosineInterp</c>. The result is zero at
    /// the start, one at the end, and slowest at both ends, so the two presets are easiest to tell
    /// apart exactly when the switch begins and exactly when it finishes.
    /// </summary>
    /// <param name="progress">Blend progress, where zero is the outgoing preset and one the incoming.</param>
    /// <returns>The eased mix in the range zero to one.</returns>
    public static float CosineInterp(float progress)
    {
        var clamped = Math.Clamp(progress, 0f, 1f);
        return 0.5f - (0.5f * MathF.Cos(clamped * MathF.PI));
    }

    /// <summary>
    /// Interpolates one variable between the outgoing and incoming preset.
    /// </summary>
    /// <param name="from">Value of the outgoing preset.</param>
    /// <param name="to">Value of the incoming preset.</param>
    /// <param name="mix">Eased mix where zero returns <paramref name="from"/> and one <paramref name="to"/>.</param>
    /// <returns>The interpolated value.</returns>
    public static float Interpolate(float from, float to, float mix) => from + ((to - from) * mix);

    /// <summary>
    /// The blend threshold at which a snapped variable switches from the outgoing preset to the
    /// incoming one. MilkDrop uses 0.5 for two presets without a comp shader and moves it to an end
    /// value while blending to or from one that has one; the direction is decided by the caller.
    /// </summary>
    public const float DefaultSnapPoint = 0.5f;

    /// <summary>
    /// The per-frame variables MilkDrop interpolates linearly across a preset blend. They cover the
    /// decay, waveform colours and position, the two border bands, the motion-vector display, the
    /// video echo, the gamma, and the blur range keys.
    /// </summary>
    public static IReadOnlyList<string> InterpolatedVariables { get; } =
    [
        "decay",
        "wave_a", "wave_r", "wave_g", "wave_b", "wave_x", "wave_y", "wave_mystery",
        "ob_size", "ob_r", "ob_g", "ob_b", "ob_a",
        "ib_size", "ib_r", "ib_g", "ib_b", "ib_a",
        "mv_x", "mv_y", "mv_dx", "mv_dy", "mv_l", "mv_r", "mv_g", "mv_b", "mv_a",
        "echo_zoom", "echo_alpha",
        "gamma",
        "blur1_min", "blur2_min", "blur3_min",
        "blur1_max", "blur2_max", "blur3_max",
        "blur1_edge_darken"
    ];

    /// <summary>
    /// The per-frame variables MilkDrop snaps instead of interpolating. They are booleans or ordinal
    /// switches, so easing them would be meaningless; each flips once the eased mix passes the snap
    /// point. <c>wave_mode</c> is deliberately absent because it is an integer the reference leaves
    /// to the incoming preset for the whole blend.
    /// </summary>
    public static IReadOnlyList<string> SnappedVariables { get; } =
    [
        "echo_orient",
        "wave_usedots", "wave_thick", "wave_additive", "wave_brighten",
        "darken_center", "wrap",
        "invert", "brighten", "darken", "solarize"
    ];

    /// <summary>
    /// The variables that drive the warp geometry. MilkDrop never interpolates them: the second
    /// blend pass rebuilds the mesh from the outgoing preset's own motion and the two meshes are
    /// blended per vertex, so the incoming preset's motion takes over unchanged.
    /// </summary>
    public static IReadOnlyList<string> MotionVariables { get; } =
        ["zoom", "zoomexp", "rot", "cx", "cy", "dx", "dy", "sx", "sy", "warp"];

    /// <summary>Reports whether a variable is interpolated linearly across a preset blend.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns><see langword="true"/> when the variable is in <see cref="InterpolatedVariables"/>.</returns>
    public static bool IsInterpolated(string name) => Contains(InterpolatedVariables, name);

    /// <summary>Reports whether a variable snaps at the blend's snap point.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns><see langword="true"/> when the variable is in <see cref="SnappedVariables"/>.</returns>
    public static bool IsSnapped(string name) => Contains(SnappedVariables, name);

    /// <summary>Reports whether a variable drives the warp geometry and is therefore never blended.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns><see langword="true"/> when the variable is in <see cref="MotionVariables"/>.</returns>
    public static bool IsMotion(string name) => Contains(MotionVariables, name);

    /// <summary>Case-sensitive membership test that also tolerates a missing name.</summary>
    /// <param name="names">Candidate names.</param>
    /// <param name="name">Name to look up.</param>
    /// <returns><see langword="true"/> when the name is present.</returns>
    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
