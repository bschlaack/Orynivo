namespace Orynivo.Visualization;

/// <summary>
/// One warp or comp shader of a preset: the parsed HLSL program plus the optional expression
/// blocks that run around it. Milkdrop numbers its shaders, so the index identifies which
/// <c>warp_N</c> or <c>comp_N</c> key group produced it.
/// </summary>
/// <param name="Index">One-based shader number from the preset keys.</param>
/// <param name="Program">Parsed shader body.</param>
/// <param name="PerFrame">Per-frame expression block that runs before the shader, or empty.</param>
/// <param name="PerPixel">Per-pixel expression block that runs alongside the shader, or empty.</param>
public sealed record VisualizerShader(
    int Index,
    ShaderNode Program,
    PresetProgram PerFrame,
    PresetProgram PerPixel);
