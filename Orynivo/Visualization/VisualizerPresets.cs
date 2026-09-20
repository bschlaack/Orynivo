using Orynivo.Visualization;

namespace Orynivo.Visualization;

/// <summary>
/// The presets the visualizer ships with. They are written here rather than loaded from disk
/// so a first run has something to show, and they deliberately use only the supported
/// expression subset, which also makes them usable as documentation for preset authors.
/// </summary>
internal static class VisualizerPresets
{
    /// <summary>Gets the built-in presets in display order.</summary>
    public static IReadOnlyList<VisualizerPreset> BuiltIn { get; } =
    [
        VisualizerPreset.Parse("""
            name=Plasma
            decay=0.94
            blur_level=1
            wave_alpha=0.7
            per_frame_1=q1 = 0.4 + bass * 1.6;
            per_pixel_1=a2 = ang + q1 * 0.6;
            per_pixel_2=x = x + cos(a2) * 0.03 * (1 + bass);
            per_pixel_3=y = y + sin(a2) * 0.03 * (1 + mid);
            """),
        VisualizerPreset.Parse("""
            name=Tunnel
            decay=0.93
            blur_level=2
            wave_alpha=0.6
            per_frame_1=q1 = 1 + bass * 0.08;
            per_pixel_1=s = 1 + rad * 0.06 * q1;
            per_pixel_2=x = x * s;
            per_pixel_3=y = y * s;
            """),
        VisualizerPreset.Parse("""
            name=Zoom Pulse
            decay=0.9
            wave_alpha=0.8
            per_frame_1=zoom = 1 + bass * 0.05;
            per_frame_2=warp = 0.5 + mid;
            per_pixel_1=x = x + cos(ang) * 0.02 * warp;
            per_pixel_2=y = y + sin(ang) * 0.02 * warp;
            """),
        VisualizerPreset.Parse("""
            name=Spectrum
            decay=0.85
            blur_level=1
            wave_alpha=0.9
            wave_scale=0.35
            """),
        VisualizerPreset.Parse("""
            name=Kaleidoscope
            decay=0.92
            blur_level=1
            wave_alpha=0.5
            per_frame_1=q1 = 3 + floor(bass * 4);
            per_pixel_1=a2 = ang * q1;
            per_pixel_2=x = cos(a2) * rad;
            per_pixel_3=y = sin(a2) * rad;
            """)
    ];

    /// <summary>Returns the built-in preset at an index, wrapping around.</summary>
    /// <param name="index">Preset index.</param>
    /// <returns>The selected preset.</returns>
    public static VisualizerPreset At(int index)
    {
        var count = BuiltIn.Count;
        var wrapped = ((index % count) + count) % count;
        return BuiltIn[wrapped];
    }
}
