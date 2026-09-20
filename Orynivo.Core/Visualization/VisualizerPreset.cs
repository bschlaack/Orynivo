using System.Globalization;

namespace Orynivo.Visualization;

/// <summary>
/// One parsed visualization preset. Presets are INI-style text in the spirit of Milkdrop:
/// the expression blocks <c>per_frame_init_1</c>, <c>per_frame_1</c>, and
/// <c>per_pixel_1</c> (with optional numbered continuations) describe the movement, and
/// <c>decay</c>, <c>zoom</c>, and <c>warp</c> provide the defaults their code can override.
/// Unknown keys are ignored, so a third-party preset degrades instead of failing to load.
/// </summary>
public sealed class VisualizerPreset
{
    private VisualizerPreset(
        string name,
        PresetVariableLayout layout,
        PresetProgram perFrameInit,
        PresetProgram perFrame,
        PresetProgram perPixel,
        float decay,
        float zoom,
        float warp,
        int blurLevel,
        float waveAlpha,
        float waveScale)
    {
        Name = name;
        Layout = layout;
        PerFrameInit = perFrameInit;
        PerFrame = perFrame;
        PerPixel = perPixel;
        Decay = decay;
        Zoom = zoom;
        Warp = warp;
        BlurLevel = blurLevel;
        WaveAlpha = waveAlpha;
        WaveScale = waveScale;
    }

    /// <summary>Gets the preset name.</summary>
    public string Name { get; }

    /// <summary>Gets the slot layout shared by every expression block.</summary>
    public PresetVariableLayout Layout { get; }

    /// <summary>Gets the one-time initialisation block.</summary>
    public PresetProgram PerFrameInit { get; }

    /// <summary>Gets the per-frame block that runs before the warp.</summary>
    public PresetProgram PerFrame { get; }

    /// <summary>Gets the per-pixel block that chooses the sampling position.</summary>
    public PresetProgram PerPixel { get; }

    /// <summary>Gets the default feedback decay, where one keeps the image unchanged.</summary>
    public float Decay { get; }

    /// <summary>Gets the default sampling zoom, where one leaves the image unscaled.</summary>
    public float Zoom { get; }

    /// <summary>Gets the default displacement amount offered to the preset code as <c>warp</c>.</summary>
    public float Warp { get; }

    /// <summary>Gets how many box-blur passes are applied to the warped frame.</summary>
    public int BlurLevel { get; }

    /// <summary>Gets the waveform opacity.</summary>
    public float WaveAlpha { get; }

    /// <summary>Gets the waveform height as a fraction of the frame.</summary>
    public float WaveScale { get; }

    /// <summary>Parses preset text.</summary>
    /// <param name="text">INI-style preset text.</param>
    /// <param name="fallbackName">Name used when the text carries none.</param>
    /// <returns>The parsed preset.</returns>
    /// <exception cref="PresetExpressionException">An expression block is invalid.</exception>
    public static VisualizerPreset Parse(string? text, string? fallbackName = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = fallbackName ?? "Preset";
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            values[key] = value;
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                name = value;
        }

        var layout = new PresetVariableLayout();
        // Register the parameters up front so the renderer can always write them, even when a
        // preset never mentions them in its own code.
        layout.GetOrAdd("decay");
        layout.GetOrAdd("zoom");
        layout.GetOrAdd("warp");

        return new VisualizerPreset(
            name,
            layout,
            PresetCompiler.Compile(Join(values, "per_frame_init"), layout),
            PresetCompiler.Compile(Join(values, "per_frame"), layout),
            PresetCompiler.Compile(Join(values, "per_pixel"), layout),
            Math.Clamp(ReadFloat(values, "decay", 0.96f), 0f, 1f),
            Math.Max(0.05f, ReadFloat(values, "zoom", 1f)),
            ReadFloat(values, "warp", 1f),
            (int)Math.Clamp(ReadFloat(values, "blur_level", 0f), 0f, 4f),
            Math.Clamp(ReadFloat(values, "wave_alpha", 0.8f), 0f, 1f),
            Math.Clamp(ReadFloat(values, "wave_scale", 0.25f), 0f, 1f));
    }

    /// <summary>Creates a preset from expression text without an INI wrapper, for tests and defaults.</summary>
    /// <param name="name">Preset name.</param>
    /// <param name="perFrame">Per-frame expression, or <see langword="null"/>.</param>
    /// <param name="perPixel">Per-pixel expression, or <see langword="null"/>.</param>
    /// <param name="decay">Feedback decay.</param>
    /// <returns>The preset.</returns>
    public static VisualizerPreset Create(string name, string? perFrame, string? perPixel, float decay = 0.96f)
    {
        var layout = new PresetVariableLayout();
        layout.GetOrAdd("decay");
        layout.GetOrAdd("zoom");
        layout.GetOrAdd("warp");
        return new VisualizerPreset(
            name,
            layout,
            PresetProgram.Empty,
            PresetCompiler.Compile(perFrame, layout),
            PresetCompiler.Compile(perPixel, layout),
            Math.Clamp(decay, 0f, 1f),
            1f,
            1f,
            0,
            0.8f,
            0.25f);
    }

    /// <summary>Joins the numbered continuations of one expression block.</summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="prefix">Block prefix, for example <c>per_frame</c>.</param>
    /// <returns>The joined expression, or <see langword="null"/> when the block is absent.</returns>
    private static string? Join(Dictionary<string, string> values, string prefix)
    {
        var parts = new List<string>();
        for (var index = 1; index <= 64; index++)
        {
            if (values.TryGetValue($"{prefix}_{index}", out var part) && !string.IsNullOrWhiteSpace(part))
                parts.Add(part);
        }

        // The unnumbered key is accepted as well, which keeps hand-written presets short.
        if (values.TryGetValue(prefix, out var single) && !string.IsNullOrWhiteSpace(single))
            parts.Add(single);

        return parts.Count == 0 ? null : string.Join(";", parts);
    }

    private static float ReadFloat(Dictionary<string, string> values, string key, float fallback) =>
        values.TryGetValue(key, out var text) &&
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
