using Orynivo.Visualization;

namespace Orynivo.Visualization;

/// <summary>
/// The presets the visualizer ships with. They are written here rather than loaded from disk so a
/// first run has something to show, and they deliberately use only the supported expression and
/// shader subset, which also makes them usable as documentation for preset authors. Each effect is a
/// small warp shader that reads the previous feedback, so the picture is structured instead of a flat
/// full-screen smear.
/// </summary>
internal static class VisualizerPresets
{
    /// <summary>Gets the built-in presets in display order.</summary>
    public static IReadOnlyList<VisualizerPreset> BuiltIn { get; } =
    [
        VisualizerPreset.Parse("""
            name=Spiral
            decay=0.97
            blur_level=1
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.45
            per_frame_1=wave_r = 0.5 + 0.5 * sin(time);
            per_frame_2=wave_g = 0.5 + 0.5 * sin(time + 2.1);
            per_frame_3=wave_b = 0.5 + 0.5 * sin(time + 4.2);
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig * 2 - 1;
            warp_4=`  float r = length(p) + 0.001;
            warp_5=`  float a = atan(p.y, p.x) + (0.15 / r) + time * 0.05;
            warp_6=`  uv = float2(cos(a), sin(a)) * r * 0.985 * 0.5 + 0.5;
            warp_7=`  ret = tex2D(sampler_main, uv).rgb * 0.99;
            warp_8=`}
            """),
        VisualizerPreset.Parse("""
            name=Kaleidoscope
            decay=0.96
            blur_level=1
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.5
            per_frame_1=wave_r = 0.5 + 0.5 * sin(time * 1.1);
            per_frame_2=wave_g = 0.5 + 0.5 * sin(time * 1.1 + 2.1);
            per_frame_3=wave_b = 0.5 + 0.5 * sin(time * 1.1 + 4.2);
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = abs(uv_orig * 2 - 1);
            warp_4=`  float a = atan(p.y, p.x);
            warp_5=`  a = abs(frac(a / 1.5708 + time * 0.03) - 0.5) * 3.14159;
            warp_6=`  float r = length(p) * 0.97;
            warp_7=`  uv = float2(cos(a), sin(a)) * r * 0.5 + 0.5;
            warp_8=`  ret = tex2D(sampler_main, uv).rgb * 0.98;
            warp_9=`}
            """),
        VisualizerPreset.Parse("""
            name=Fractal
            decay=0.97
            blur_level=2
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.5
            per_frame_1=wave_r = 0.6 + 0.4 * sin(time * 0.7);
            per_frame_2=wave_g = 0.4 + 0.6 * sin(time * 0.7 + 2.0);
            per_frame_3=wave_b = 0.9;
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig * 2 - 1;
            warp_4=`  float r = length(p) + 0.001;
            warp_5=`  float a = atan(p.y, p.x) + sin(r * 6 - time * 1.5) * 0.5 + time * 0.02;
            warp_6=`  uv = float2(cos(a), sin(a)) * r * 1.015 * 0.5 + 0.5;
            warp_7=`  ret = tex2D(sampler_main, uv).rgb * 0.985;
            warp_8=`}
            """),
        VisualizerPreset.Parse("""
            name=Ripple
            decay=0.96
            blur_level=2
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.55
            per_frame_1=wave_r = 0.3 + 0.7 * sin(time * 0.5);
            per_frame_2=wave_g = 0.7;
            per_frame_3=wave_b = 0.4 + 0.6 * sin(time * 0.5 + 3.0);
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig * 2 - 1;
            warp_4=`  float r = length(p);
            warp_5=`  float ring = sin(r * 10 - time * 2.5);
            warp_6=`  uv = (p * (1 + ring * 0.035)) * 0.99 * 0.5 + 0.5;
            warp_7=`  float3 bg = float3(0.5 + 0.5 * sin(r * 8 - time * 2.5), 0.5 + 0.5 * sin(r * 8 - time * 2.5 + 2.0), 0.5 + 0.5 * sin(r * 8 - time * 2.5 + 4.0)) * (0.30 * max(0.0, 1.0 - r));
            warp_8=`  ret = max(tex2D(sampler_main, uv).rgb * 0.95, bg);
            warp_9=`}
            """),
        VisualizerPreset.Parse("""
            name=Vortex
            decay=0.98
            blur_level=2
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.5
            per_frame_1=wave_r = 0.5 + 0.5 * sin(time * 0.9);
            per_frame_2=wave_g = 0.5 + 0.5 * sin(time * 0.9 + 2.1);
            per_frame_3=wave_b = 0.5 + 0.5 * sin(time * 0.9 + 4.2);
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig * 2 - 1;
            warp_4=`  float r = length(p) + 0.05;
            warp_5=`  float a = atan(p.y, p.x) + 0.12 / (r * r) + time * 0.03;
            warp_6=`  uv = float2(cos(a), sin(a)) * r * 0.985 * 0.5 + 0.5;
            warp_7=`  ret = tex2D(sampler_main, uv).rgb * 0.99;
            warp_8=`}
            """),
        VisualizerPreset.Parse("""
            name=Bloom
            decay=0.96
            blur_level=3
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=0.9
            wave_scale=0.5
            per_frame_1=wave_r = 0.6 + 0.4 * sin(time * 0.5);
            per_frame_2=wave_g = 0.6 + 0.4 * sin(time * 0.5 + 2.0);
            per_frame_3=wave_b = 0.9;
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig * 2 - 1;
            warp_4=`  float r = length(p);
            warp_5=`  uv = p * (1.008 - 0.01 * bass) * 0.5 + 0.5;
            warp_6=`  float3 glow = float3(1.0, 0.6, 0.9) * (0.55 * exp(-r * 2.5));
            warp_7=`  ret = max(tex2D(sampler_main, uv).rgb * 0.98, glow);
            warp_8=`}
            """),
        VisualizerPreset.Parse("""
            name=Spectrum Bars
            decay=0.85
            blur_level=1
            nWaveMode=8
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=1.0
            wave_scale=0.55
            per_frame_1=wave_x = 0.5;
            per_frame_2=wave_y = 0.88;
            per_frame_3=wave_r = 0.2 + 0.8 * bass;
            per_frame_4=wave_g = 0.7 + 0.3 * mid;
            per_frame_5=wave_b = 0.4 + 0.6 * treb;
            per_frame_6=wave_a = 1.0;
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig;
            warp_4=`  float g = 0.5 + 0.5 * sin(p.x * 26 + time) * sin(p.y * 26 - time * 0.7);
            warp_5=`  ret = tex2D(sampler_main, p).rgb * 0.90 + g * 0.06 * float3(0.15, 0.55, 0.95);
            warp_6=`}
            """),
        VisualizerPreset.Parse("""
            name=Starfield
            decay=0.95
            blur_level=1
            bWaveThick=1
            bAdditiveWaves=1
            wave_alpha=0.9
            wave_scale=0.4
            per_frame_1=wave_r = 0.8 + 0.2 * sin(time * 0.7);
            per_frame_2=wave_g = 0.8 + 0.2 * sin(time * 0.9 + 2.0);
            per_frame_3=wave_b = 0.6 + 0.4 * sin(time * 1.1 + 4.0);
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  float2 p = uv_orig * 2 - 1;
            warp_4=`  float2 sp = uv_orig * 48;
            warp_5=`  float2 id = floor(sp);
            warp_6=`  float2 f = frac(sp) - 0.5;
            warp_7=`  float h = frac(sin(dot(id, float2(12.9898, 78.233))) * 43758.5453);
            warp_8=`  float star = max(0.0, 1.0 - length(f) * 14.0) * step(0.86, h);
            warp_9=`  uv = (p * 1.02 + float2(sin(time * 0.31), cos(time * 0.27)) * 0.02) * 0.5 + 0.5;
            warp_10=`  ret = max(tex2D(sampler_main, uv).rgb * 0.97, star * float3(0.8, 0.9, 1.0));
            warp_11=`}
            """),
        VisualizerPreset.Parse("""
            name=Orbit
            decay=0.94
            blur_level=1
            bWaveThick=1
            wave_alpha=0.6
            wave_scale=0.2
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
