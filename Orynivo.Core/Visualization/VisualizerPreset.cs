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
        PresetProgram perPixelInit,
        PresetProgram perPixel,
        float decay,
        float zoom,
        float warp,
        int blurLevel,
        float waveAlpha,
        float waveScale,
        PresetProgram wavePerPoint,
        IReadOnlyList<VisualizerShape> shapes,
        IReadOnlyList<VisualizerWave> waves,
        IReadOnlyDictionary<string, float> defaults,
        IReadOnlyList<VisualizerShader> warpShaders,
        IReadOnlyList<VisualizerShader> compShaders)
    {
        Name = name;
        Layout = layout;
        PerFrameInit = perFrameInit;
        PerFrame = perFrame;
        PerPixelInit = perPixelInit;
        PerPixel = perPixel;
        Decay = decay;
        Zoom = zoom;
        Warp = warp;
        BlurLevel = blurLevel;
        WaveAlpha = waveAlpha;
        WaveScale = waveScale;
        WavePerPoint = wavePerPoint;
        Shapes = shapes;
        Waves = waves;
        Defaults = defaults;
        WarpShaders = warpShaders;
        CompShaders = compShaders;
    }

    /// <summary>Gets the preset name.</summary>
    public string Name { get; }

    /// <summary>Gets the slot layout shared by every expression block.</summary>
    public PresetVariableLayout Layout { get; }

    /// <summary>Gets the one-time initialisation block.</summary>
    public PresetProgram PerFrameInit { get; }

    /// <summary>Gets the per-frame block that runs before the warp.</summary>
    public PresetProgram PerFrame { get; }

    /// <summary>Gets the one-time per-pixel initialisation block.</summary>
    public PresetProgram PerPixelInit { get; }

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

    /// <summary>
    /// Gets the per-point program of the waveform. It may move every point by writing
    /// <c>x</c> and <c>y</c>, which default to the plain waveform line.
    /// </summary>
    public PresetProgram WavePerPoint { get; }

    /// <summary>Gets the custom shapes drawn over the warped frame, in draw order.</summary>
    public IReadOnlyList<VisualizerShape> Shapes { get; }

    /// <summary>
    /// Gets the four Milkdrop waveforms. The renderer draws them in order and honours each
    /// waveform own initialisation, per-frame, and per-point blocks.
    /// </summary>
    public IReadOnlyList<VisualizerWave> Waves { get; }

    /// <summary>
    /// Gets the numeric preset keys as per-frame defaults, keyed by variable name. Milkdrop
    /// presets carry most of their settings as keys such as <c>ob_r</c>, <c>nWaveMode</c>, or
    /// <c>fVideoEchoAlpha</c>, so every key whose value is a number becomes the starting value
    /// of the matching variable and the expression blocks can still override it per frame.
    /// </summary>
    public IReadOnlyDictionary<string, float> Defaults { get; }

    /// <summary>Gets the enabled <c>warp_N</c> shaders, in preset order.</summary>
    public IReadOnlyList<VisualizerShader> WarpShaders { get; }

    /// <summary>Gets the enabled <c>comp_N</c> shaders, in preset order.</summary>
    public IReadOnlyList<VisualizerShader> CompShaders { get; }

    /// <summary>
    /// Gets the preset format version the file declares, or zero when it declares none. Milkdrop
    /// versions its presets through <c>MILKDROP_PRESET_VERSION</c> or <c>PSVERSION</c>; every
    /// version is accepted, and the value is only reported for diagnostics.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets the names of the expression blocks this preset declares but could not compile. A block
    /// that uses a construct the engine does not support is skipped so the rest of the preset still
    /// renders; real Milkdrop presets rely on that, because a single unsupported expression must
    /// not replace the whole preset with the fallback.
    /// </summary>
    public IReadOnlyList<string> FailedBlocks { get; init; } = [];

    /// <summary>
    /// Splits preset text into its sections. A Milkdrop <c>.milk</c> file usually holds several
    /// presets, one per <c>[presetNN]</c> header; text before the first header forms a section of
    /// its own, so a single-preset file yields exactly one entry.
    /// </summary>
    /// <param name="text">Preset file text.</param>
    /// <returns>The sections in file order, each without its header.</returns>
    public static IReadOnlyList<string> ParseSections(string? text)
    {
        var sections = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.StartsWith('[') && trimmed.TrimStart().StartsWith('['))
            {
                if (current.ToString().Trim().Length > 0)
                    sections.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(line).Append('\n');
        }

        if (current.ToString().Trim().Length > 0)
            sections.Add(current.ToString());

        return sections;
    }

    /// <summary>Parses preset text.</summary>
    /// <param name="text">INI-style preset text.</param>
    /// <param name="fallbackName">Name used when the text carries none.</param>
    /// <returns>The parsed preset.</returns>
    /// <exception cref="PresetExpressionException">An expression block is invalid.</exception>
    public static VisualizerPreset Parse(string? text, string? fallbackName = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = fallbackName ?? "Preset";
        var lines = (text ?? string.Empty).Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var trimmed = lines[lineIndex].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = new System.Text.StringBuilder(trimmed[(separator + 1)..].Trim());
            // A value continues over the following lines until one looks like a new key. Milkdrop
            // stores shader source this way, so the newlines have to survive.
            while (lineIndex + 1 < lines.Length && !LooksLikeKey(lines[lineIndex + 1]))
                value.Append('\n').Append(lines[++lineIndex].TrimEnd());

            // Blank lines belong to a shader's source, so they are kept above, but the trailing
            // newline of a section must not become part of a scalar value like the name.
            var valueText = value.ToString().TrimEnd();
            values[key] = valueText;
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && valueText.Length > 0)
                name = valueText;
        }

        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());

        var failed = new List<string>();
        return new VisualizerPreset(
            name,
            layout,
            CompileBlock(values, layout, "per_frame_init", failed),
            CompileBlock(values, layout, "per_frame", failed),
            CompileBlock(values, layout, "per_pixel_init", failed),
            CompileBlock(values, layout, "per_pixel", failed),
            Math.Clamp(ReadFloat(values, "decay", 0.96f), 0f, 1f),
            Math.Max(0.05f, ReadFloat(values, "zoom", 1f)),
            ReadFloat(values, "warp", 1f),
            (int)Math.Clamp(ReadFloat(values, "blur_level", 0f), 0f, 4f),
            Math.Clamp(ReadFloat(values, "wave_alpha", 0.8f), 0f, 1f),
            Math.Clamp(ReadFloat(values, "wave_scale", 0.25f), 0f, 1f),
            CompileBlock(values, layout, "per_point", failed),
            ParseShapes(values, layout, failed),
            ParseWaves(values, layout, failed),
            ParseDefaults(values),
            ParseShaders(values, layout, "warp", failed),
            ParseShaders(values, layout, "comp", failed))
        {
            Version = ReadVersion(values),
            FailedBlocks = failed
        };
    }

    /// <summary>Creates a preset from expression text without an INI wrapper, for tests and defaults.</summary>
    /// <param name="name">Preset name.</param>
    /// <param name="perFrame">Per-frame expression, or <see langword="null"/>.</param>
    /// <param name="perPixel">Per-pixel expression, or <see langword="null"/>.</param>
    /// <param name="decay">Feedback decay.</param>
    /// <returns>The preset.</returns>
    public static VisualizerPreset Create(string name, string? perFrame, string? perPixel, float decay = 0.96f)
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        return new VisualizerPreset(
            name,
            layout,
            PresetProgram.Empty,
            PresetCompiler.Compile(perFrame, layout),
            PresetProgram.Empty,
            PresetCompiler.Compile(perPixel, layout),
            Math.Clamp(decay, 0f, 1f),
            1f,
            1f,
            0,
            0.8f,
            0.25f,
            PresetProgram.Empty,
            [],
            ParseWaves(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), layout, []),
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase),
            [],
            []);
    }

    /// <summary>
    /// Parses the numbered warp and comp shaders. A shader whose source does not parse is
    /// skipped, so one broken shader degrades a preset instead of rejecting it.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="layout">Shared slot layout.</param>
    /// <param name="prefix">Either <c>warp</c> or <c>comp</c>.</param>
    /// <param name="failed">Collects the names of the shaders that could not be parsed.</param>
    /// <returns>The enabled shaders, in preset order.</returns>
    private static IReadOnlyList<VisualizerShader> ParseShaders(
        Dictionary<string, string> values,
        PresetVariableLayout layout,
        string prefix,
        List<string> failed)
    {
        var shaders = new List<VisualizerShader>();
        for (var index = 1; index <= 16; index++)
        {
            var key = $"{prefix}_{index}";
            if (!values.TryGetValue(key, out var source) || string.IsNullOrWhiteSpace(source))
                continue;

            if (ReadShape(values, key + "_", "enabled", 1f) < 0.5f)
                continue;

            ShaderNode program;
            try
            {
                program = ShaderParser.Parse(source);
            }
            catch (PresetExpressionException exception)
            {
                // Recording the reason matters: a skipped shader used to be invisible, which made
                // a preset that renders only its overlay look like a rendering bug.
                failed.Add($"{key}: {exception.Message}");
                continue;
            }

            shaders.Add(new VisualizerShader(
                index,
                program,
                PresetCompiler.Compile(Join(values, key + "_per_frame"), layout),
                PresetCompiler.Compile(Join(values, key + "_per_pixel"), layout)));
        }

        return shaders;
    }

    /// <summary>
    /// Parses the <c>shape_N_*</c> keys. Numbered shapes are read in order until the first
    /// gap, which is how Milkdrop presets declare them.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="layout">Shared slot layout.</param>
    /// <returns>The declared shapes.</returns>
    private static IReadOnlyList<VisualizerShape> ParseShapes(
        Dictionary<string, string> values,
        PresetVariableLayout layout,
        List<string> failed)
    {
        var shapes = new List<VisualizerShape>();
        for (var index = 0; index < 32; index++)
        {
            var prefix = $"shape_{index}_";
            var hasAny = values.Keys.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!hasAny)
            {
                if (index == 0)
                    continue;
                break;
            }

            shapes.Add(new VisualizerShape(
                (int)Math.Clamp(ReadShape(values, prefix, "sides", 4f), 0f, 64f),
                ReadShape(values, prefix, "x", 0f),
                ReadShape(values, prefix, "y", 0f),
                Math.Max(0f, ReadShape(values, prefix, "rad", 0.2f)),
                ReadShape(values, prefix, "ang", 0f),
                Math.Clamp(ReadShape(values, prefix, "r", 1f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "g", 1f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "b", 1f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "a", 0.5f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "border_r", 1f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "border_g", 1f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "border_b", 1f), 0f, 1f),
                Math.Clamp(ReadShape(values, prefix, "border_a", 1f), 0f, 1f),
                ReadShape(values, prefix, "additive", 0f) >= 0.5f,
                CompileBlock(values, layout, prefix + "init", failed),
                CompileBlock(values, layout, prefix + "per_frame", failed),
                CompileBlock(values, layout, prefix + "per_point", failed)));
        }

        return shapes;
    }

    /// <summary>
    /// Compiles one expression block, recording its name and skipping it when it cannot compile.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="layout">Shared slot layout.</param>
    /// <param name="prefix">Block prefix, for example <c>per_frame</c>.</param>
    /// <param name="failed">Collects the names of the blocks that could not compile.</param>
    /// <returns>The compiled program, or an empty program when the block was skipped.</returns>
    private static PresetProgram CompileBlock(
        Dictionary<string, string> values,
        PresetVariableLayout layout,
        string prefix,
        List<string> failed)
    {
        try
        {
            return PresetCompiler.Compile(Join(values, prefix), layout);
        }
        catch (PresetExpressionException exception)
        {
            failed.Add($"{prefix}: {exception.Message}");
            return PresetProgram.Empty;
        }
    }

    /// <summary>Maps the Milkdrop key spellings onto the variable names the engine uses.</summary>
    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nWaveMode"] = "wave_mode",
        ["bWaveDots"] = "wave_dots",
        ["bWaveThick"] = "wave_thick",
        ["bAdditiveWaves"] = "wave_additive",
        ["bWaveBrighten"] = "wave_brighten",
        ["bDarkenCenter"] = "darken_center",
        ["bMotionVectors"] = "mv_l",
        ["nMotionVectorsX"] = "mv_x",
        ["nMotionVectorsY"] = "mv_y"
    };

    /// <summary>
    /// Reads every numeric key as a per-frame default. Expression blocks never parse as a
    /// number, so they are skipped, and a key without a matching variable is simply unused.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <returns>The defaults keyed by variable name.</returns>
    private static IReadOnlyDictionary<string, float> ParseDefaults(Dictionary<string, string> values)
    {
        var defaults = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, text) in values)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;

            defaults[KeyAliases.TryGetValue(key, out var alias) ? alias : key] = value;
        }

        return defaults;
    }

    /// <summary>
    /// Parses the four Milkdrop waveforms. Every waveform always exists so the renderer can
    /// draw the default wave without a preset declaring one.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="layout">Shared slot layout.</param>
    /// <returns>The four waveform programs.</returns>
    private static IReadOnlyList<VisualizerWave> ParseWaves(
        Dictionary<string, string> values,
        PresetVariableLayout layout,
        List<string> failed)
    {
        var waves = new List<VisualizerWave>(4);
        for (var index = 0; index < 4; index++)
        {
            var prefix = $"wave_{index}_";
            waves.Add(new VisualizerWave(
                CompileBlock(values, layout, prefix + "init", failed),
                CompileBlock(values, layout, prefix + "per_frame", failed),
                CompileBlock(values, layout, prefix + "per_point", failed)));
        }

        return waves;
    }

    /// <summary>Reads the declared preset format version, or zero when the file carries none.</summary>
    /// <param name="values">Parsed preset values.</param>
    /// <returns>The declared version.</returns>
    private static int ReadVersion(Dictionary<string, string> values)
    {
        foreach (var key in new[] { "MILKDROP_PRESET_VERSION", "PSVERSION", "preset_version", "version" })
        {
            if (values.TryGetValue(key, out var text) &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
            {
                return version;
            }
        }

        return 0;
    }

    /// <summary>
    /// Reports whether a line starts a new key. A key begins at column zero and is followed by an
    /// equals sign, which is how Milkdrop itself decides where a multi-line value ends.
    /// </summary>
    /// <param name="line">Line to test.</param>
    /// <returns><see langword="true"/> when the line starts a key.</returns>
    private static bool LooksLikeKey(string line)
    {
        if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '[')
            return false;

        var separator = line.IndexOf('=');
        if (separator <= 0)
            return false;

        for (var index = 0; index < separator; index++)
        {
            var character = line[index];
            if (!char.IsLetterOrDigit(character) && character != '_')
                return false;
        }

        return true;
    }

    /// <summary>Reads one shape key, falling back to its default.</summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="prefix">Shape key prefix.</param>
    /// <param name="key">Key name without the prefix.</param>
    /// <param name="fallback">Default value.</param>
    /// <returns>The parsed value.</returns>
    private static float ReadShape(
        Dictionary<string, string> values,
        string prefix,
        string key,
        float fallback) =>
        values.TryGetValue(prefix + key, out var text) &&
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

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

        if (parts.Count == 0)
            return null;

        // Milkdrop presets use both conventions: some split an expression across parts, so a part
        // may end with an operator and the next one continues it, while others rely on a separator
        // between statements. Insert a semicolon only when the previous part is not waiting for
        // more input and the next part does not bring its own.
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            var text = part.Trim();
            if (text.Length == 0)
                continue;

            if (builder.Length > 0 && text[0] != ';' && !StartsWithContinuation(text[0]) &&
                !EndsWithContinuation(builder[^1]))
            {
                builder.Append(';');
            }

            builder.Append(text);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Reports whether a part starts by continuing the previous expression.</summary>
    /// <param name="character">First character of the part.</param>
    /// <returns><see langword="true"/> when the part continues an expression.</returns>
    private static bool StartsWithContinuation(char character) =>
        character is '+' or '-' or '*' or '/' or '%' or ')' or ']' or ',';

    /// <summary>Reports whether a character leaves an expression waiting for more input.</summary>
    /// <param name="character">Last character of the previous part.</param>
    /// <returns><see langword="true"/> when the next part continues the same expression.</returns>
    private static bool EndsWithContinuation(char character) =>
        character is '+' or '-' or '*' or '/' or '%' or ',' or '(' or '[' or '<' or '>' or '='
            or '&' or '|' or '!' or '?' or '^';

    private static float ReadFloat(Dictionary<string, string> values, string key, float fallback) =>
        values.TryGetValue(key, out var text) &&
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
