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
                AddSection(sections, current);
                continue;
            }

            current.Append(line).Append('\n');
        }

        AddSection(sections, current);
        return sections;
    }

    /// <summary>
    /// Adds the buffered section when it carries preset content. A Milkdrop file usually opens with
    /// a version preamble before its first <c>[presetNN]</c> header; that preamble parses into a
    /// preset with nothing but defaults, which renders only the shared overlay and makes a user
    /// stepping through a collection see an empty picture every few presses.
    /// </summary>
    /// <param name="sections">Sections collected so far.</param>
    /// <param name="current">Buffered section text, cleared afterwards.</param>
    private static void AddSection(List<string> sections, System.Text.StringBuilder current)
    {
        var text = current.ToString();
        if (text.Trim().Length > 0 && CarriesPresetKeys(text))
            sections.Add(text);

        current.Clear();
    }

    /// <summary>Reports whether a section declares anything besides the version preamble.</summary>
    /// <param name="section">Section text.</param>
    /// <returns><see langword="true"/> when the section holds preset content.</returns>
    private static bool CarriesPresetKeys(string section)
    {
        foreach (var line in section.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!PreambleKeys.Contains(trimmed[..separator].Trim()))
                return true;
        }

        return false;
    }

    /// <summary>The keys a Milkdrop file carries before its first preset.</summary>
    private static readonly HashSet<string> PreambleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MILKDROP_PRESET_VERSION", "MILKDROP_VERSION", "PSVERSION", "PSVERSION_WARP", "PSVERSION_COMP"
    };

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
            Math.Clamp(ReadFloat(values, "wave_a", 0.8f), 0f, 1f),
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

        // Milkdrop 2 stores a shader as one numbered key per source line, each line carrying a
        // backtick marker, while Milkdrop 1 and hand-written presets store a whole shader in one
        // key. The marker tells the two apart, and such a preset has exactly one shader of that
        // kind, so its lines are joined instead of being read as separate shaders.
        if (IsLineBasedShader(values, prefix))
        {
            var source = JoinShaderLines(values, prefix);
            if (source.Length > 0 && ReadShape(values, prefix + "_1_", "enabled", 1f) >= 0.5f)
            {
                try
                {
                    shaders.Add(new VisualizerShader(
                        1,
                        ShaderParser.Parse(TranslateShaderDialect(source)),
                        PresetCompiler.Compile(Join(values, prefix + "_1_per_frame"), layout),
                        PresetCompiler.Compile(Join(values, prefix + "_1_per_pixel"), layout)));
                }
                catch (PresetExpressionException exception)
                {
                    failed.Add($"{prefix}_1: {exception.Message}");
                }
            }

            return shaders;
        }

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
                program = ShaderParser.Parse(TranslateShaderDialect(source));
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
    /// The math constants Milkdrop provides to shaders. Presets use them without defining them, so
    /// they have to be substituted before the source can be parsed.
    /// </summary>
    private static readonly Dictionary<string, string> ShaderConstants = new(StringComparer.Ordinal)
    {
        ["M_PI"] = "3.14159265",
        ["M_PI_2"] = "1.57079633",
        ["M_2PI"] = "6.28318531",
        ["M_INV_PI"] = "0.31830989",
        ["M_INV_PI_2"] = "0.63661977",
        ["M_E"] = "2.71828183"
    };

    /// <summary>
    /// Translates the parts of Milkdrop's shader source that are not HLSL: its preprocessor
    /// conditionals (which it writes with trailing comments) and its built-in math constants. The
    /// result is plain HLSL that <see cref="ShaderParser"/> can read.
    /// </summary>
    /// <param name="source">Shader source as stored in the preset.</param>
    /// <returns>The translated source.</returns>
    private static string TranslateShaderDialect(string source)
    {
        var builder = new System.Text.StringBuilder();
        var constants = new Dictionary<string, string>(ShaderConstants, StringComparer.Ordinal);
        var enclosing = new Stack<bool>();
        var active = true;
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var directive = StripLineComment(trimmed[1..].TrimStart());
                if (directive.StartsWith("define", StringComparison.Ordinal))
                {
                    var rest = directive[6..].Trim();
                    var separator = rest.IndexOfAny([' ', '\t']);
                    if (separator > 0)
                        constants[rest[..separator]] = rest[separator..].Trim();
                    continue;
                }

                if (directive.StartsWith("ifdef", StringComparison.Ordinal))
                {
                    enclosing.Push(active);
                    active = active && constants.ContainsKey(FirstWord(directive[5..]));
                    continue;
                }

                if (directive.StartsWith("ifndef", StringComparison.Ordinal))
                {
                    enclosing.Push(active);
                    active = active && !constants.ContainsKey(FirstWord(directive[6..]));
                    continue;
                }

                if (directive.StartsWith("if", StringComparison.Ordinal))
                {
                    enclosing.Push(active);
                    active = active && FirstWord(directive[2..]) != "0";
                    continue;
                }

                if (directive.StartsWith("else", StringComparison.Ordinal))
                {
                    if (enclosing.Count > 0)
                        active = enclosing.Peek() && !active;
                    continue;
                }

                if (directive.StartsWith("endif", StringComparison.Ordinal))
                {
                    if (enclosing.Count > 0)
                        active = enclosing.Pop();
                    continue;
                }

                continue;
            }

            if (!active)
                continue;

            foreach (var (name, value) in constants)
            {
                if (line.Contains(name, StringComparison.Ordinal))
                {
                    line = System.Text.RegularExpressions.Regex.Replace(
                        line,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(name)}\b",
                        value);
                }
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Removes a trailing <c>//</c> comment from a preprocessor line. A macro definition ends at its
    /// line comment, and presets write them, for example
    /// <c>#define MyGet GetPixel //GetBlur1</c>. Keeping the comment would expand it into the middle
    /// of a call, where it swallows the rest of that line and turns the following line into a
    /// syntax error.
    /// </summary>
    /// <param name="line">Preprocessor line without its leading hash.</param>
    /// <returns>The line without a trailing line comment.</returns>
    private static string StripLineComment(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"')
                quoted = !quoted;
            else if (!quoted && line[index] == '/' && line[index + 1] == '/')
                return line[..index].TrimEnd();
        }

        return line;
    }

    /// <summary>Returns the first word of a preprocessor expression.</summary>
    /// <param name="text">Text after the directive name.</param>
    /// <returns>The first word, or an empty string.</returns>
    private static string FirstWord(string text)
    {
        var trimmed = text.TrimStart();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            end++;
        return end == 0 ? string.Empty : trimmed[..end];
    }

    /// <summary>Reports whether a shader is stored one source line per numbered key.</summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="prefix">Either <c>warp</c> or <c>comp</c>.</param>
    /// <returns><see langword="true"/> when the first line carries the Milkdrop 2 marker.</returns>
    private static bool IsLineBasedShader(Dictionary<string, string> values, string prefix) =>
        values.TryGetValue(prefix + "_1", out var first) && first.TrimStart().StartsWith('`');

    /// <summary>
    /// Joins the line-per-key shader source of a Milkdrop 2 preset, removing the backtick marker
    /// from every line and the body marker that starts it.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="prefix">Either <c>warp</c> or <c>comp</c>.</param>
    /// <returns>The shader source, or an empty string when the preset declares none.</returns>
    private static string JoinShaderLines(Dictionary<string, string> values, string prefix)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 1; index <= 4096; index++)
        {
            if (!values.TryGetValue($"{prefix}_{index}", out var line))
                break;

            var text = line.Trim();
            if (text.StartsWith('`'))
                text = text[1..];

            // The body marker only says where the shader starts, and it shares its line with the
            // opening brace in many presets, so it is removed wherever it appears.
            var marker = text.IndexOf("shader_body", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
                text = text.Remove(marker, "shader_body".Length);

            builder.Append(text).Append('\n');
        }

        return builder.ToString();
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
        // Milkdrop 2 spells the scalar parameters with an f-prefixed key. Without these the engine
        // read the built-in default for every preset that only carries the Milkdrop 2 spelling, so a
        // preset that set fWaveAlpha to 0.001 still drew a full overlay and one that set fDecay to
        // 0.925 fed back at 0.96.
        ["fDecay"] = "decay",
        ["wave_alpha"] = "wave_a",
        ["fWaveAlpha"] = "wave_a",
        ["fWaveScale"] = "wave_scale",
        ["fWaveSmoothing"] = "wave_smoothing",
        ["fWaveParam"] = "wave_mystery",
        ["fWaveR"] = "wave_r",
        ["fWaveG"] = "wave_g",
        ["fWaveB"] = "wave_b",
        ["fWaveX"] = "wave_x",
        ["fWaveY"] = "wave_y",
        // Milkdrop keeps the enable flag and the length apart: bMotionVectors turns the vectors on
        // and defaults to off, while mv_l is only their length and defaults to one. Writing the flag
        // into the length drew a grid of stray lines on every preset that set a length but never
        // asked for vectors.
        ["bMotionVectors"] = "mv_enabled",
        ["nMotionVectorsX"] = "mv_x",
        ["nMotionVectorsY"] = "mv_y",
        // Milkdrop 2 writes the blur and edge chain with short keys; they name the same parameters
        // Milkdrop 1 spells out as blurN_min, blurN_max, and blurN_edge_darken.
        ["b1n"] = "blur1_min",
        ["b1x"] = "blur1_max",
        ["b1ed"] = "blur1_edge_darken",
        ["b2n"] = "blur2_min",
        ["b2x"] = "blur2_max",
        ["b2ed"] = "blur2_edge_darken",
        ["b3n"] = "blur3_min",
        ["b3x"] = "blur3_max",
        ["b3ed"] = "blur3_edge_darken"
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
    /// Parses the four Milkdrop custom waveforms. Every waveform always exists so the renderer can
    /// keep the slot list stable; a waveform without <c>wavecode_N_enabled</c> is simply not drawn.
    /// </summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="layout">Shared slot layout.</param>
    /// <param name="failed">Collects the names of blocks that did not compile.</param>
    /// <returns>The four custom waveforms.</returns>
    private static IReadOnlyList<VisualizerWave> ParseWaves(
        Dictionary<string, string> values,
        PresetVariableLayout layout,
        List<string> failed)
    {
        var waves = new List<VisualizerWave>(4);
        for (var index = 0; index < 4; index++)
        {
            var codePrefix = $"wavecode_{index}_";
            var wavePrefix = $"wave_{index}_";
            waves.Add(new VisualizerWave(
                ReadFloat(values, codePrefix + "enabled", 0f) >= 0.5f,
                (int)Math.Clamp(ReadFloat(values, codePrefix + "samples", 512f), 0f, 512f),
                (int)Math.Clamp(ReadFloat(values, codePrefix + "sep", 0f), 0f, 512f),
                ReadFloat(values, codePrefix + "bSpectrum", 0f) >= 0.5f,
                ReadFloat(values, codePrefix + "bUseDots", 0f) >= 0.5f,
                ReadFloat(values, codePrefix + "bDrawThick", 0f) >= 0.5f,
                ReadFloat(values, codePrefix + "bAdditive", 0f) >= 0.5f,
                ReadFloat(values, codePrefix + "scaling", 1f),
                Math.Clamp(ReadFloat(values, codePrefix + "smoothing", 0.5f), 0f, 1f),
                Math.Clamp(ReadFloat(values, codePrefix + "r", 1f), 0f, 1f),
                Math.Clamp(ReadFloat(values, codePrefix + "g", 1f), 0f, 1f),
                Math.Clamp(ReadFloat(values, codePrefix + "b", 1f), 0f, 1f),
                Math.Clamp(ReadFloat(values, codePrefix + "a", 1f), 0f, 1f),
                CompileBlock(values, layout, wavePrefix + "init", failed),
                CompileBlock(values, layout, wavePrefix + "per_frame", failed),
                CompileBlock(values, layout, wavePrefix + "per_point", failed)));
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
        // Presets number the parts far beyond a handful: a real per-frame block reaches the
        // hundredth part, so the bound matches the shader-line bound instead of cutting a block off
        // in the middle of a nested loop and losing it to a parse error.
        for (var index = 1; index <= 4096; index++)
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
            // A line comment is stripped: the parts are concatenated without a newline, so a
            // trailing "// ..." would otherwise swallow every part after it, which lost a whole
            // per-frame block that ends a loop line with a comment.
            var text = StripExpressionComment(part.Trim());
            if (text.Length == 0)
                continue;

            if (builder.Length > 0 && text[0] != ';' && !StartsWithContinuation(text[0]) &&
                !EndsWithContinuation(builder[^1]) && !ContinuesCall(text, builder))
            {
                builder.Append(';');
            }

            builder.Append(text);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// Reports whether a part starting with an opening parenthesis continues a call whose function
    /// name ended the previous part, as in a split <c>above(x, .95)</c>.
    /// </summary>
    /// <param name="text">Current part.</param>
    /// <param name="builder">Text accumulated so far.</param>
    /// <returns><see langword="true"/> when the two parts form one call.</returns>
    private static bool ContinuesCall(string text, System.Text.StringBuilder builder)
    {
        if (text[0] != '(')
            return false;
        var previous = builder[^1];
        return char.IsLetterOrDigit(previous) || previous == '_';
    }

    /// <summary>Removes a trailing line comment from one expression part.</summary>
    /// <param name="text">Part text.</param>
    /// <returns>The part without its line comment.</returns>
    private static string StripExpressionComment(string text)
    {
        var index = text.IndexOf("//", StringComparison.Ordinal);
        return index < 0 ? text : text[..index].TrimEnd();
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

    private static float ReadFloat(Dictionary<string, string> values, string key, float fallback)
    {
        if (TryReadFloat(values, key, out var value))
            return value;

        // Milkdrop 2 writes some parameters with a short key (fDecay, fWaveAlpha, ...); resolve the
        // raw key that aliases onto the requested variable so a preset that only carries the
        // Milkdrop 2 spelling is not read as its default.
        foreach (var (raw, alias) in KeyAliases)
        {
            if (string.Equals(alias, key, StringComparison.OrdinalIgnoreCase) && TryReadFloat(values, raw, out value))
                return value;
        }

        return fallback;
    }

    /// <summary>Reads one numeric key, reporting whether it was present and parseable.</summary>
    /// <param name="values">Parsed preset values.</param>
    /// <param name="key">Key to read.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when the key was read.</returns>
    private static bool TryReadFloat(Dictionary<string, string> values, string key, out float value)
    {
        value = 0f;
        return values.TryGetValue(key, out var text) &&
               float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}


