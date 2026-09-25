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
            decay=0.96
            blur_level=2
            nWaveMode=8
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=0.55
            wave_scale=0.30
            per_frame_1=q1 = time * 0.4;
            per_frame_2=q2 = 0.4 + bass * 0.8;
            per_frame_3=wave_r = 0.5 + 0.5 * sin(q1);
            per_frame_4=wave_g = 0.5 + 0.5 * sin(q1 + 2.1);
            per_frame_5=wave_b = 0.5 + 0.5 * sin(q1 + 4.2);
            per_pixel_1=spin = ang + q1 + rad * q2 * 4;
            per_pixel_2=x = x + cos(spin) * 0.02 * (1 + bass);
            per_pixel_3=y = y + sin(spin) * 0.02 * (1 + mid);
            """),
        VisualizerPreset.Parse("""
            name=Nebula
            decay=0.97
            blur_level=3
            nWaveMode=8
            wave_alpha=0.25
            wave_scale=0.18
            per_frame_1=q1 = time * 0.25;
            per_frame_2=zoom = 1.008 + bass * 0.012;
            per_frame_3=wave_r = 0.4 + 0.6 * sin(q1);
            per_frame_4=wave_g = 0.4 + 0.6 * sin(q1 + 2.1);
            per_frame_5=wave_b = 0.6 + 0.4 * sin(q1 + 4.2);
            per_pixel_1=spin = ang + rad * (1.5 + bass * 2.0);
            per_pixel_2=x = x + cos(spin) * 0.012;
            per_pixel_3=y = y + sin(spin) * 0.012;
            """),
        VisualizerPreset.Parse("""
            name=Tunnel
            decay=0.95
            blur_level=2
            nWaveMode=6
            wave_alpha=0.45
            wave_scale=0.14
            per_frame_1=q1 = 1 + bass * 0.15;
            per_frame_2=rot = rot + 0.003 + mid * 0.015;
            per_frame_3=wave_r = 0.95;
            per_frame_4=wave_g = 0.6;
            per_frame_5=wave_b = 0.15;
            per_pixel_1=s = 1 + rad * 0.10 * q1;
            per_pixel_2=x = x * s;
            per_pixel_3=y = y * s;
            """),
        VisualizerPreset.Parse("""
            name=Spectrum Bars
            decay=0.80
            blur_level=1
            nWaveMode=8
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.60
            per_frame_1=wave_x = 0.5;
            per_frame_2=wave_y = 0.93;
            per_frame_3=wave_r = 0.2 + 0.8 * bass;
            per_frame_4=wave_g = 0.7 + 0.3 * mid;
            per_frame_5=wave_b = 0.4 + 0.6 * treb;
            per_frame_6=wave_a = 1.0;
            """),
        VisualizerPreset.Parse("""
            name=Bloom
            decay=0.92
            blur_level=3
            nWaveMode=8
            wave_alpha=0.40
            wave_scale=0.30
            per_frame_1=q1 = 0.5 + bass * 0.5;
            per_frame_2=zoom = 1.012 + q1 * 0.012;
            per_frame_3=wave_r = 0.6 + 0.4 * sin(time * 0.5);
            per_frame_4=wave_g = 0.6 + 0.4 * sin(time * 0.5 + 2.0);
            per_frame_5=wave_b = 0.9;
            per_pixel_1=x = x + cos(ang) * 0.010 * q1;
            per_pixel_2=y = y + sin(ang) * 0.010 * q1;
            """),
        VisualizerPreset.Parse("""
            name=Orbit
            decay=0.93
            blur_level=1
            nWaveMode=8
            wave_alpha=0.40
            wave_scale=0.18
            per_frame_1=q1 = time * 0.6;
            shape_0_sides=6
            shape_0_rad=0.16
            shape_0_r=0.15
            shape_0_g=0.9
            shape_0_b=1
            shape_0_a=0.30
            shape_0_border_r=1
            shape_0_border_g=1
            shape_0_border_b=1
            shape_0_border_a=0.9
            shape_0_per_frame_1=x = cos(q1) * 0.45;
            shape_0_per_frame_2=y = sin(q1) * 0.45;
            shape_0_per_frame_3=rad = 0.10 + bass * 0.20;
            shape_1_sides=3
            shape_1_rad=0.05
            shape_1_r=1
            shape_1_g=0.4
            shape_1_b=0.1
            shape_1_a=0.8
            shape_1_border_a=0
            shape_1_per_frame_1=x = cos(q1 + 3.14159) * 0.45;
            shape_1_per_frame_2=y = sin(q1 + 3.14159) * 0.45;
            """),
        VisualizerPreset.Parse("""
            name=Mandala
            decay=0.94
            blur_level=1
            nWaveMode=8
            wave_alpha=0.35
            wave_scale=0.22
            per_frame_1=q1 = 4 + floor(bass * 4);
            per_frame_2=q2 = time * 0.15;
            per_pixel_1=wedge = ang * q1 + q2;
            per_pixel_2=x = cos(wedge) * rad;
            per_pixel_3=y = sin(wedge) * rad;
            """),
        VisualizerPreset.Parse("""
            name=Starfield
            decay=0.90
            blur_level=0
            nWaveMode=8
            bWaveDots=1
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=0.9
            wave_scale=0.25
            per_frame_1=zoom = 1.02 + bass * 0.02;
            per_frame_2=dx = dx + sin(time * 0.31) * 0.010;
            per_frame_3=dy = dy + cos(time * 0.27) * 0.010;
            per_frame_4=wave_r = 0.8 + 0.2 * sin(time * 0.7);
            per_frame_5=wave_g = 0.8 + 0.2 * sin(time * 0.9 + 2.0);
            per_frame_6=wave_b = 0.6 + 0.4 * sin(time * 1.1 + 4.0);
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
