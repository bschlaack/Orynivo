using System.Globalization;
using System.Text;

namespace Orynivo.Visualization;

/// <summary>
/// Translates the parsed HLSL subset of a Milkdrop shader into SkSL, the shading language of Skia's
/// runtime effects. It works on the tree <see cref="ShaderParser"/> already produces, so the GPU
/// path shares the front end with the CPU interpreter and only the back end differs; anything the
/// subset does not cover is reported instead of being guessed, and the caller keeps that shader on
/// the interpreter.
/// </summary>
public static class ShaderTranspiler
{
    /// <summary>
    /// Upper bound on the iterations a translated loop may run. Skia unrolls the loops of a
    /// runtime effect, so a large bound makes the program too large to compile; a shader with a
    /// longer loop therefore stays on the CPU interpreter.
    /// </summary>
    private const int MaxTranslatedIterations = 32;

    /// <summary>
    /// Every uniform the prelude declares, with its component count. The runner seeds all of them,
    /// because Skia requires each declared uniform to be set, and the shader vocabulary of Milkdrop
    /// includes variables the engine has to supply: the 32 <c>q</c> and 8 <c>t</c> slots, and the
    /// texture size of each sampler, which shaders read as <c>texsize_noise_lq.zw</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, int> UniformComponents { get; } = BuildUniformComponents();

    /// <summary>The uniforms the literal prelude already spells out.</summary>
    private static readonly HashSet<string> LiteralUniforms = new(StringComparer.Ordinal)
    {
        "texsize", "time", "frame", "fps", "bass", "mid", "treb", "vol",
        "bass_att", "mid_att", "treb_att", "aspect", "rand_frame", "rand_preset"
    };

    /// <summary>Builds the uniform table.</summary>
    /// <returns>The uniform names with their component counts.</returns>
    private static Dictionary<string, int> BuildUniformComponents()
    {
        var uniforms = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["texsize"] = 4,
            ["time"] = 1,
            ["frame"] = 1,
            ["fps"] = 1,
            ["bass"] = 1,
            ["mid"] = 1,
            ["treb"] = 1,
            ["vol"] = 1,
            ["bass_att"] = 1,
            ["mid_att"] = 1,
            ["treb_att"] = 1,
            ["aspect"] = 2,
            ["aspectx"] = 1,
            ["aspecty"] = 1,
            ["rand_frame"] = 4,
            ["rand_preset"] = 4
        };
        foreach (var name in new[]
        {
            "texsize_main", "texsize_fc_main", "texsize_pc_main",
            "texsize_noise_lq", "texsize_noise_mq", "texsize_noise_hq",
            "texsize_noisevol_lq", "texsize_noisevol_hq"
        })
        {
            uniforms[name] = 4;
        }

        for (var index = 1; index <= 32; index++)
            uniforms["q" + index.ToString(CultureInfo.InvariantCulture)] = 1;
        for (var index = 1; index <= 8; index++)
            uniforms["t" + index.ToString(CultureInfo.InvariantCulture)] = 1;
        return uniforms;
    }

    /// <summary>Emits the uniform declarations the literal prelude does not carry.</summary>
    /// <returns>The generated declarations.</returns>
    private static string GeneratedUniforms()
    {
        var builder = new StringBuilder();
        foreach (var (name, count) in UniformComponents)
        {
            if (LiteralUniforms.Contains(name))
                continue;

            builder.Append("uniform ")
                .Append(count switch { 2 => "float2", 4 => "float4", _ => "float" })
                .Append(' ').Append(name).Append(";\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Emits the SkSL helpers that sample a cubic volume texture, which is what <c>tex3D</c> reads.
    /// Skia's runtime effects only sample two dimensional shaders, so the volume is carried as a slice
    /// atlas and sampled trilinearly here. The layout and the interpolation match
    /// <see cref="VisualizerTextureBank.SampleVolume"/> exactly, so the GPU and the CPU interpreter
    /// produce the same values from the same volume. SkSL does not allow a <c>shader</c> parameter on a
    /// user function, so each volume sampler gets its own helper that names the global uniform.
    /// </summary>
    /// <returns>The generated helper functions.</returns>
    private static string GeneratedVolumeHelpers()
    {
        var builder = new StringBuilder();
        EmitVolumeHelper(builder, "orynivoTex3DLq", "sampler_noisevol_lq");
        EmitVolumeHelper(builder, "orynivoTex3DHq", "sampler_noisevol_hq");
        return builder.ToString();
    }

    /// <summary>Emits one volume sampling helper bound to a specific sampler uniform.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="name">Helper function name.</param>
    /// <param name="sampler">Sampler uniform the helper reads.</param>
    private static void EmitVolumeHelper(StringBuilder builder, string name, string sampler)
    {
        var size = VisualizerTextureBank.VolumeSize.ToString(CultureInfo.InvariantCulture);
        var columns = VisualizerTextureBank.VolumeAtlasColumns.ToString(CultureInfo.InvariantCulture);
        builder.Append("float4 ").Append(name).Append("(float3 coord) {\n");
        builder.Append("    float3 t = (fract(coord) * ").Append(size).Append(".0) - 0.5;\n");
        builder.Append("    float3 base = floor(t);\n");
        builder.Append("    float3 d = t - base;\n");
        builder.Append("    float3 i0 = mod(base, ").Append(size).Append(".0);\n");
        builder.Append("    float3 i1 = mod(base + 1.0, ").Append(size).Append(".0);\n");
        EmitVolumeCorner(builder, sampler, "c000", columns, size, "i0.x", "i0.y", "i0.z");
        EmitVolumeCorner(builder, sampler, "c100", columns, size, "i1.x", "i0.y", "i0.z");
        EmitVolumeCorner(builder, sampler, "c010", columns, size, "i0.x", "i1.y", "i0.z");
        EmitVolumeCorner(builder, sampler, "c110", columns, size, "i1.x", "i1.y", "i0.z");
        EmitVolumeCorner(builder, sampler, "c001", columns, size, "i0.x", "i0.y", "i1.z");
        EmitVolumeCorner(builder, sampler, "c101", columns, size, "i1.x", "i0.y", "i1.z");
        EmitVolumeCorner(builder, sampler, "c011", columns, size, "i0.x", "i1.y", "i1.z");
        EmitVolumeCorner(builder, sampler, "c111", columns, size, "i1.x", "i1.y", "i1.z");
        builder.Append("    float4 b00 = mix(c000, c100, d.x);\n");
        builder.Append("    float4 b10 = mix(c010, c110, d.x);\n");
        builder.Append("    float4 b01 = mix(c001, c101, d.x);\n");
        builder.Append("    float4 b11 = mix(c011, c111, d.x);\n");
        builder.Append("    float4 b0 = mix(b00, b10, d.y);\n");
        builder.Append("    float4 b1 = mix(b01, b11, d.y);\n");
        builder.Append("    return mix(b0, b1, d.z);\n");
        builder.Append("}\n");
    }

    /// <summary>Emits one trilinear volume corner sample.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="sampler">Sampler uniform the helper reads.</param>
    /// <param name="variable">Corner variable name.</param>
    /// <param name="columns">Atlas columns.</param>
    /// <param name="size">Volume edge length.</param>
    /// <param name="x">X index expression.</param>
    /// <param name="y">Y index expression.</param>
    /// <param name="z">Slice index expression.</param>
    private static void EmitVolumeCorner(
        StringBuilder builder,
        string sampler,
        string variable,
        string columns,
        string size,
        string x,
        string y,
        string z)
    {
        builder.Append("    float4 ").Append(variable).Append(" = ").Append(sampler)
            .Append(".eval(float2((mod(").Append(z).Append(", ").Append(columns).Append(".0) * ")
            .Append(size).Append(".0) + ").Append(x).Append(" + 0.5, (floor(").Append(z)
            .Append(" / ").Append(columns).Append(".0) * ").Append(size).Append(".0) + ").Append(y)
            .Append(" + 0.5));\n");
    }

    /// <summary>The sampler a shader reads when it does not name one.</summary>
    private const string MainSampler = "sampler_main";

    /// <summary>
    /// The samplers the prelude declares. They are never treated as user variables, and the GPU
    /// runner binds one child shader for each.
    /// </summary>
    public static IReadOnlyList<string> Samplers { get; } =
    [
        "sampler_main", "sampler_blur1", "sampler_blur2", "sampler_blur3",
        "sampler_noise_lq", "sampler_noise_mq", "sampler_noise_hq",
        "sampler_fc_main", "sampler_pc_main", "sampler_noisevol_lq", "sampler_noisevol_hq",
        "sampler_pw_main", "sampler_pw_noise_lq", "sampler_worms"
    ];

    /// <summary>The sampler names as a lookup set.</summary>
    private static readonly HashSet<string> SamplerSet = new(Samplers, StringComparer.Ordinal);

    /// <summary>The uniform block every translated shader starts with.</summary>
    private const string Prelude = """
        uniform float4 texsize;
        uniform float time;
        uniform float frame;
        uniform float fps;
        uniform float bass;
        uniform float mid;
        uniform float treb;
        uniform float vol;
        uniform float bass_att;
        uniform float mid_att;
        uniform float treb_att;
        uniform float2 aspect;
        uniform float4 rand_frame;
        uniform float4 rand_preset;
        uniform shader sampler_main;
        uniform shader sampler_blur1;
        uniform shader sampler_blur2;
        uniform shader sampler_blur3;
        uniform shader sampler_noise_lq;
        uniform shader sampler_noise_mq;
        uniform shader sampler_noise_hq;
        uniform shader sampler_fc_main;
        uniform shader sampler_pc_main;
        uniform shader sampler_noisevol_lq;
        uniform shader sampler_noisevol_hq;
        uniform shader sampler_pw_main;
        uniform shader sampler_pw_noise_lq;
        uniform shader sampler_worms;
        float4 toColour(float3 c) { return float4(c, 1.0); }
        float4 toColour(float4 c) { return c; }
        float4 toColour(float c) { return float4(c, c, c, 1.0); }
        """;

    /// <summary>
    /// The motion uniforms of a warp translation. They are prefixed so a shader that declares a
    /// local named <c>zoom</c> or <c>centre</c> cannot collide with them.
    /// </summary>
    private const string WarpUniforms = """
        uniform float2 _orynivo_size;
        uniform float _orynivo_zoom;
        uniform float _orynivo_zoomExp;
        uniform float _orynivo_rotation;
        uniform float2 _orynivo_centre;
        uniform float2 _orynivo_offset;
        uniform float2 _orynivo_stretch;
        """;

    /// <summary>
    /// Translates a parsed shader body into SkSL. Milkdrop shaders write their result into the
    /// <c>ret</c> variable, which becomes the returned colour, so a body that never returns still
    /// produces a picture.
    /// </summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <returns>SkSL source for a <c>half4 main(float2 fragCoord)</c> runtime effect.</returns>
    /// <exception cref="PresetExpressionException">The body uses something SkSL cannot express here.</exception>
    public static string Transpile(ShaderNode program) => Transpile(program, out _);

    /// <summary>
    /// Translates a parsed shader body into SkSL and reports the samplers it declares. Milkdrop names
    /// a long tail of samplers, so the shader's own <c>tex2D</c>/<c>tex3D</c> calls decide which ones
    /// are declared instead of a fixed list; the GPU runner has to bind a child shader for every one.
    /// </summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <param name="samplers">The samplers the generated SkSL declares.</param>
    /// <returns>SkSL source for a <c>half4 main(float2 fragCoord)</c> runtime effect.</returns>
    /// <exception cref="PresetExpressionException">The body uses something SkSL cannot express here.</exception>
    public static string Transpile(ShaderNode program, out IReadOnlyList<string> samplers)
    {
        ArgumentNullException.ThrowIfNull(program);
        return TranspileCore(program, null, warpedUv: false, out samplers, out _);
    }

    /// <summary>
    /// Translates a warp shader together with the preset's per-pixel expression block into SkSL. The
    /// difference from <see cref="Transpile"/> is the entry point: the sampling coordinate comes from
    /// the Milkdrop motion transform (and the per-pixel block) instead of the fragment position, and
    /// <c>uv_orig</c> is the pixel position, exactly what the CPU warp stage computes. A warp without
    /// a shader samples the previous frame directly, so the same method also covers the pure
    /// geometric warp with a per-pixel block.
    /// </summary>
    /// <param name="program">Parsed warp shader body, or <see langword="null"/> for a warp without a shader.</param>
    /// <param name="perPixel">Per-pixel expression block that chooses the sampling position, or <see langword="null"/>.</param>
    /// <param name="samplers">The samplers the generated SkSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the per-pixel block reads, which the caller has to seed.</param>
    /// <returns>SkSL source for a <c>half4 main(float2 fragCoord)</c> runtime effect.</returns>
    /// <exception cref="PresetExpressionException">The body or block uses something SkSL cannot express here.</exception>
    public static string TranspileWarp(
        ShaderNode? program,
        PresetProgram? perPixel,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms) =>
        TranspileCore(program, perPixel, warpedUv: true, out samplers, out perPixelUniforms);

    /// <summary>The shared core of both translations.</summary>
    /// <param name="program">Parsed shader body, or <see langword="null"/> for a warp without a shader.</param>
    /// <param name="perPixel">Per-pixel expression block, or <see langword="null"/>.</param>
    /// <param name="warpedUv">Whether the entry point builds <c>uv</c> from the motion transform.</param>
    /// <param name="samplers">The samplers the generated SkSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the per-pixel block reads.</param>
    /// <returns>The generated SkSL.</returns>
    private static string TranspileCore(
        ShaderNode? program,
        PresetProgram? perPixel,
        bool warpedUv,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms)
    {
        var builder = new StringBuilder();

        // Every sampler the shader names is declared, so an unknown sampler does not become a zero
        // constant with a broken ".eval". The base set keeps the prelude's literal declarations.
        var samplerNames = new SortedSet<string>(Samplers, StringComparer.Ordinal);
        if (program is not null)
            CollectSamplers(program, samplerNames);
        samplers = [.. samplerNames];

        builder.Append(Prelude).Append(GeneratedUniforms()).Append(GeneratedVolumeHelpers());
        if (warpedUv)
            builder.Append(WarpUniforms);
        var extras = samplerNames.Where(name => !SamplerSet.Contains(name)).ToList();
        foreach (var name in extras)
            builder.Append("uniform shader ").Append(name).Append(";\n");

        // The type table has to stand before the helpers are emitted, because their parameter types
        // are recorded as they are written out.
        _types = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ret"] = "float3",
            // The per-pixel variables main declares itself; the body may use them before the
            // declaration line is reached in source order, so their types are known up front.
            ["uv"] = "float2",
            ["uv_orig"] = "float2",
            ["rad"] = "float",
            ["ang"] = "float"
        };
        foreach (var (uniform, count) in UniformComponents)
            _types[uniform] = count switch { 1 => "float", 2 => "float2", 4 => "float4", _ => "float" };

        _mutableUniforms = new HashSet<string>(StringComparer.Ordinal);
        _helperReturns = new Dictionary<string, string>(StringComparer.Ordinal);

        if (program is not null)
        {
            // Every declaration is recorded before anything is emitted. A preset may use a variable
            // before its declaration, and without the type the conversion that keeps the two execution
            // paths identical cannot be chosen, which leaves the expression in a shape SkSL rejects.
            foreach (var statement in program.Items)
                CollectDeclarations(statement);

            // An identifier the shader never declares and the engine never binds reads as zero on the
            // interpreter, so the GPU declares it as a zero constant instead of failing on an unknown
            // name. That keeps the two paths on the same picture rather than losing the shader.
            var unknowns = CollectUnknownIdentifiers(program, samplerNames);
            foreach (var name in unknowns)
                _types[name] = "float";
            if (unknowns.Count > 0)
            {
                builder.Append('\n');
                foreach (var name in unknowns)
                    builder.Append("const float ").Append(name).Append(" = 0.0;\n");
            }

            builder.Append('\n');

            // A uniform the shader writes becomes a writable local in main, because SkSL uniforms are
            // immutable and the interpreter treats the write as per-pixel state.
            _mutableUniforms = CollectAssignedUniforms(program);

            // A helper is emitted as a function before the entry point, so a call resolves to it instead
            // of running its body where it is defined. Their return types are collected first, because a
            // helper may be called from another helper that is emitted before it.
            var helpers = new List<ShaderNode>();
            foreach (var statement in program.Items)
            {
                if (statement.Kind == ShaderNodeKind.Function &&
                    !string.Equals(statement.Text, "main", StringComparison.Ordinal))
                {
                    helpers.Add(statement);
                    _helperReturns[statement.Text] = HelperReturnType(statement);
                }
            }

            foreach (var helper in helpers)
                EmitFunction(builder, helper);
        }

        var perPixelBody = string.Empty;
        var perPixelNames = new List<string>();
        if (perPixel is not null && !perPixel.IsEmpty)
        {
            if (!PresetExpressionTranspiler.TryTranspile(perPixel, out perPixelBody, out var names, out var error))
                throw new PresetExpressionException(error ?? "The per-pixel block has no SkSL translation.", 0);

            perPixelNames = [.. names];
            if (perPixelBody.Length > 0)
                builder.Append('\n').Append(PresetExpressionTranspiler.FmodHelper);
            foreach (var name in perPixelNames)
            {
                builder.Append("uniform float ")
                    .Append(PresetExpressionTranspiler.UniformName(name))
                    .Append(";\n");
            }
        }

        perPixelUniforms = perPixelNames;

        if (!warpedUv)
        {
            EmitCompMain(builder, program);
            return builder.ToString();
        }

        EmitWarpMain(builder, program, perPixelBody);
        return builder.ToString();
    }

    /// <summary>Emits the comp entry point, which samples <c>uv</c> straight from the fragment position.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="program">Parsed shader body.</param>
    private static void EmitCompMain(StringBuilder builder, ShaderNode? program)
    {
        builder.Append("half4 main(float2 fragCoord) {\n");
        builder.Append("    float2 uv_orig = fragCoord / texsize.xy;\n");
        builder.Append("    float2 uv = uv_orig;\n");
        builder.Append("    float2 centred = (uv * 2.0) - 1.0;\n");
        builder.Append("    float rad = length(centred);\n");
        builder.Append("    float ang = atan(centred.y, centred.x);\n");
        builder.Append("    float3 ret = float3(0.0);\n");
        EmitMutableUniforms(builder);
        EmitEntryStatements(builder, program);
        builder.Append("    return half4(toColour(ret));\n");
        builder.Append("}\n");
    }

    /// <summary>
    /// Emits the warp entry point. It reproduces the CPU warp stage: the geometric motion transform
    /// builds the sampling position in the range minus one to one, the per-pixel block may move it,
    /// and the shader (or a direct frame sample) produces the colour.
    /// </summary>
    /// <param name="builder">Output.</param>
    /// <param name="program">Parsed warp shader body, or <see langword="null"/>.</param>
    /// <param name="perPixelBody">Emitted per-pixel statements.</param>
    private static void EmitWarpMain(StringBuilder builder, ShaderNode? program, string perPixelBody)
    {
        builder.Append("half4 main(float2 fragCoord) {\n");
        builder.Append("    float2 _orynivo_uv_orig = (fragCoord - 0.5) / (_orynivo_size - 1.0);\n");
        builder.Append("    float2 _orynivo_normalized = (_orynivo_uv_orig * 2.0) - 1.0;\n");
        builder.Append("    float2 _orynivo_warped = (_orynivo_normalized - _orynivo_centre) * _orynivo_stretch;\n");
        builder.Append("    float _orynivo_cos = cos(_orynivo_rotation);\n");
        builder.Append("    float _orynivo_sin = sin(_orynivo_rotation);\n");
        builder.Append("    float2 _orynivo_rotated = float2((_orynivo_warped.x * _orynivo_cos) - (_orynivo_warped.y * _orynivo_sin), (_orynivo_warped.x * _orynivo_sin) + (_orynivo_warped.y * _orynivo_cos));\n");
        builder.Append("    float _orynivo_radius = length(_orynivo_rotated);\n");
        builder.Append("    float _orynivo_pixelZoom = (_orynivo_zoomExp != 1.0) ? pow(_orynivo_zoom, 1.0 + (_orynivo_zoomExp * _orynivo_radius * 2.0)) : _orynivo_zoom;\n");
        builder.Append("    float2 _orynivo_sample = (_orynivo_rotated * _orynivo_pixelZoom) + _orynivo_centre + _orynivo_offset;\n");
        builder.Append("    float _orynivo_x = _orynivo_sample.x;\n");
        builder.Append("    float _orynivo_y = _orynivo_sample.y;\n");
        builder.Append("    float _orynivo_rad = _orynivo_radius;\n");
        builder.Append("    float _orynivo_ang = atan(_orynivo_rotated.y, _orynivo_rotated.x);\n");
        builder.Append(perPixelBody);

        if (program is null)
        {
            // A warp without a shader is the geometric warp: sample the previous frame, black outside.
            builder.Append("    float2 uv = (float2(_orynivo_x, _orynivo_y) * 0.5) + 0.5;\n");
            builder.Append("    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) { return half4(0.0); }\n");
            builder.Append("    return half4(sampler_main.eval((uv * (_orynivo_size - 1.0)) + 0.5));\n");
            builder.Append("}\n");
            return;
        }

        // The shader sees the sampling position in uv and the pixel position in uv_orig, and its
        // polar pair is derived from uv, exactly as the CPU binding does.
        builder.Append("    float2 uv = (float2(_orynivo_x, _orynivo_y) * 0.5) + 0.5;\n");
        builder.Append("    float2 uv_orig = _orynivo_uv_orig;\n");
        builder.Append("    float2 centred = (uv * 2.0) - 1.0;\n");
        builder.Append("    float rad = length(centred);\n");
        builder.Append("    float ang = atan(centred.y, centred.x);\n");
        builder.Append("    float3 ret = float3(0.0);\n");
        EmitMutableUniforms(builder);
        EmitEntryStatements(builder, program);
        builder.Append("    return half4(toColour(ret));\n");
        builder.Append("}\n");
    }

    /// <summary>Emits the writable copies of the uniforms the shader assigns to.</summary>
    /// <param name="builder">Output.</param>
    private static void EmitMutableUniforms(StringBuilder builder)
    {
        if (_mutableUniforms is null)
            return;

        foreach (var name in _mutableUniforms.OrderBy(name => name, StringComparer.Ordinal))
        {
            builder.Append("    ")
                .Append(_types is not null && _types.TryGetValue(name, out var type) ? type : "float")
                .Append(" _orynivo_").Append(name).Append(" = ").Append(name).Append(";\n");
        }
    }

    /// <summary>Emits the shader body statements of the entry point.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="program">Parsed shader body.</param>
    private static void EmitEntryStatements(StringBuilder builder, ShaderNode? program)
    {
        if (program is null)
            return;

        foreach (var statement in program.Items)
        {
            // A helper was already emitted before main; emitting it here would run its body at
            // the wrong place and reference parameters that do not exist in this scope. The entry
            // point keeps its own name, so it is the one definition that is still emitted here.
            if (statement.Kind != ShaderNodeKind.Function ||
                string.Equals(statement.Text, "main", StringComparison.Ordinal))
            {
                EmitStatement(builder, statement, 1);
            }
        }
    }

    /// <summary>Emits one statement.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="statement">Statement node.</param>
    /// <param name="depth">Indentation depth.</param>
    private static void EmitStatement(StringBuilder builder, ShaderNode statement, int depth)
    {
        var indent = SkSL.Indent(depth);
        switch (statement.Kind)
        {
            case ShaderNodeKind.Block:
            case ShaderNodeKind.Function:
                // A definition is still emitted where it stands until the entry point is marked; that
                // keeps this refactor free of behaviour change.
                builder.Append(indent).Append("{\n");
                foreach (var child in statement.Items)
                    EmitStatement(builder, child, depth + 1);
                builder.Append(indent).Append("}\n");
                return;
            case ShaderNodeKind.Declaration:
                {
                    // A declaration may name several variables; the parser keeps the first name and
                    // the rest are declared alongside it, which SkSL needs spelled out.
                    var name = statement.Items.Count > 0 ? statement.Items[0].Text : null;
                    if (name is null)
                        return;

                    var declared = SkSL.MapType(statement.Text);

                    // The samplers are already declared in the prelude, and SkSL requires a shader
                    // variable to be global, so a sampler declaration inside the body is dropped.
                    if (declared == "shader")
                        return;

                    var names = string.Join(", ", statement.Items.Select(item => SkSL.SafeName(item.Text)));
                    if (_types is not null)
                    {
                        foreach (var item in statement.Items)
                            _types[item.Text] = declared;
                    }

                    builder.Append(indent).Append(declared).Append(' ').Append(names);
                    if (statement.Left is not null)
                        builder.Append(" = ").Append(EmitInitializer(statement.Text, statement.Left));
                    builder.Append(";\n");
                    return;
                }
            case ShaderNodeKind.ExpressionStatement:
                if (statement.Left is not null)
                    builder.Append(indent).Append(EmitExpression(statement.Left)).Append(";\n");
                return;
            case ShaderNodeKind.If:
                builder.Append(indent).Append("if (").Append(EmitCondition(statement.Left!)).Append(") ");
                EmitBody(builder, statement.Right, depth);
                if (statement.Third is not null)
                {
                    builder.Append(indent).Append("else ");
                    EmitBody(builder, statement.Third, depth);
                }

                return;
            case ShaderNodeKind.For:
                EmitFor(builder, statement, depth);
                return;
            case ShaderNodeKind.While:
                // SkSL runtime effects reject while and require a counted for whose index is the
                // left-hand side of the condition, so the loop becomes a bounded counter whose body
                // breaks out as soon as the real condition fails.
                {
                    var counter = $"_orynivoLoop{depth}";
                    var condition = EmitCondition(statement.Left!);
                    builder.Append(indent)
                        .Append("for (int ").Append(counter).Append(" = 0; ")
                        .Append(counter).Append(" < ").Append(MaxTranslatedIterations)
                        .Append("; ").Append(counter).Append("++) {\n");
                    builder.Append(indent).Append("    if (!(").Append(condition).Append(")) { break; }\n");
                    if (statement.Items.Count > 0)
                        EmitStatement(builder, statement.Items[0], depth + 1);
                    builder.Append(indent).Append("}\n");
                    return;
                }
            case ShaderNodeKind.Return:
                // Every return becomes the shader's colour, so the ret convention holds either way.
                // A helper returns its own type; only the entry point becomes the shader colour.
                if (_inHelper)
                {
                    builder.Append(indent).Append("return ")
                        .Append(statement.Left is null ? "ret" : EmitExpression(statement.Left))
                        .Append(";\n");
                    return;
                }

                builder.Append(indent).Append("return half4(toColour(")
                    .Append(statement.Left is null ? "ret" : EmitExpression(statement.Left))
                    .Append("));\n");
                return;
            default:
                throw new PresetExpressionException(
                    $"The shader statement '{statement.Kind}' has no SkSL translation.",
                    statement.Position);
        }
    }

    /// <summary>
    /// Emits a for loop as a bounded counter, the same shape the while translation uses, because SkSL
    /// runtime effects reject a condition that is not a plain comparison against the loop index and the
    /// engine's one-or-zero convention turns every condition into a ternary.
    /// </summary>
    /// <param name="builder">Output.</param>
    /// <param name="loop">Loop node.</param>
    /// <param name="depth">Indentation depth.</param>
    private static void EmitFor(StringBuilder builder, ShaderNode loop, int depth)
    {
        var indent = SkSL.Indent(depth);
        var counter = $"_orynivoLoop{depth}";
        builder.Append(indent).Append("{\n");
        if (loop.Left is not null)
            builder.Append(indent).Append("    ").Append(EmitForPart(loop.Left)).Append(";\n");
        builder.Append(indent).Append("    for (int ").Append(counter).Append(" = 0; ")
            .Append(counter).Append(" < ").Append(MaxTranslatedIterations)
            .Append("; ").Append(counter).Append("++) {\n");
        if (loop.Right is not null)
            builder.Append(indent).Append("        if (!(").Append(EmitCondition(loop.Right)).Append(")) { break; }\n");
        if (loop.Items.Count > 0)
            EmitStatement(builder, loop.Items[0], depth + 2);
        if (loop.Third is not null)
            builder.Append(indent).Append("        ").Append(EmitExpression(loop.Third)).Append(";\n");
        builder.Append(indent).Append("    }\n");
        builder.Append(indent).Append("}\n");
    }

    /// <summary>Emits the body of a control statement, adding braces when it is a single statement.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="body">Body node, or <see langword="null"/> for an empty body.</param>
    /// <param name="depth">Indentation depth.</param>
    private static void EmitBody(StringBuilder builder, ShaderNode? body, int depth)
    {
        if (body is null)
        {
            builder.Append("{}\n");
            return;
        }

        if (body.Kind == ShaderNodeKind.Block)
        {
            EmitStatement(builder, body, depth);
            return;
        }

        builder.Append("{\n");
        EmitStatement(builder, body, depth + 1);
        builder.Append(SkSL.Indent(depth)).Append("}\n");
    }

    /// <summary>Emits a <c>for</c> initializer, which is a declaration or an expression.</summary>
    /// <param name="initializer">Initializer node.</param>
    /// <returns>Text without its trailing semicolon.</returns>
    private static string EmitForPart(ShaderNode initializer)
    {
        var builder = new StringBuilder();
        EmitStatement(builder, initializer, 0);
        return builder.ToString().TrimEnd('\n', ';');
    }

    /// <summary>Emits an expression as a SkSL condition, where a non-zero value is true.</summary>
    /// <param name="expression">Condition node.</param>
    /// <returns>The condition text.</returns>
    private static string EmitCondition(ShaderNode expression) => EmitExpression(expression) + " != 0.0";

    /// <summary>
    /// Emits a declaration initializer. SkSL has no implicit scalar-to-vector or float-to-int
    /// conversion, so <c>float3 sum = 0;</c> has to become <c>float3 sum = float3(0.0);</c> and
    /// <c>int n = 0;</c> needs an integer literal.
    /// </summary>
    /// <param name="type">Declared type name.</param>
    /// <param name="expression">Initializer expression.</param>
    /// <returns>The initializer text.</returns>
    private static string EmitInitializer(string type, ShaderNode expression)
    {
        var mapped = SkSL.MapType(type);
        var text = EmitExpression(expression);
        return SkSL.Convert(text, TypeOf(expression), mapped);
    }

    /// <summary>Reports whether a type name is a vector or matrix with more than one component.</summary>
    /// <param name="type">Mapped type name.</param>
    /// <returns><see langword="true"/> when the type is a vector or matrix.</returns>
    private static bool IsVectorType(string type)
    {
        foreach (var character in type)
        {
            if (character is '2' or '3' or '4')
                return true;
        }

        return false;
    }

    /// <summary>Emits one expression.</summary>
    /// <param name="expression">Expression node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitExpression(ShaderNode expression) => expression.Kind switch
    {
        ShaderNodeKind.Literal => expression.Number.ToString("0.0########", CultureInfo.InvariantCulture),
        ShaderNodeKind.Identifier => IdentifierText(expression.Text),
        ShaderNodeKind.Member => EmitMember(expression),
        ShaderNodeKind.Index => $"{EmitExpression(expression.Left!)}[int({EmitExpression(expression.Right!)})]",
        ShaderNodeKind.Unary => EmitUnary(expression),
        ShaderNodeKind.Binary => EmitBinary(expression),
        ShaderNodeKind.Ternary =>
            $"({EmitCondition(expression.Left!)} ? {EmitExpression(expression.Right!)} : {EmitExpression(expression.Third!)})",
        ShaderNodeKind.Call => EmitCall(expression),
        _ => throw new PresetExpressionException(
            $"The shader expression '{expression.Kind}' has no SkSL translation.",
            expression.Position)
    };

    /// <summary>
    /// Emits an identifier. A uniform the shader writes is read from its writable copy in main, while
    /// a helper keeps the uniform name because SkSL forbids a shader parameter on a user function and
    /// the helper cannot see the copy.
    /// </summary>
    /// <param name="name">Identifier name.</param>
    /// <returns>The name to emit.</returns>
    private static string IdentifierText(string name) =>
        !_inHelper && _mutableUniforms is not null && _mutableUniforms.Contains(name)
            ? "_orynivo_" + name
            : SkSL.SafeName(name);

    /// <summary>
    /// Emits a component access. A scalar broadcasts to every component the way
    /// <see cref="ShaderValue.Scalar"/> does, so swizzling a scalar yields the scalar itself (or a
    /// broadcast vector) instead of a SkSL "invalid swizzle" error.
    /// </summary>
    /// <param name="expression">Member node.</param>
    /// <returns>The access text.</returns>
    private static string EmitMember(ShaderNode expression)
    {
        var operand = EmitExpression(expression.Left!);
        var operandType = TypeOf(expression.Left!);
        if (operandType is not null && SkSL.ComponentCount(operandType) == 1)
            return SkSL.Convert(operand, "float", SkSL.ComponentType(expression.Text.Length));

        return $"{operand}.{expression.Text}";
    }

    /// <summary>
    /// The declared type of every variable of the shader being translated. SkSL is strictly typed
    /// while the engine stores every value as a float, so a preset may write <c>float3 x = 0;</c> or
    /// <c>y = aspect;</c> and SkSL refuses both; knowing the declared types is what lets the emitter
    /// widen or narrow those. The state is per thread because one translation runs on one thread.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, string>? _types;

    /// <summary>The return type of every helper function of the shader being translated.</summary>
    [ThreadStatic]
    private static Dictionary<string, string>? _helperReturns;

    /// <summary>
    /// The uniforms the shader assigns to. SkSL uniforms are immutable, so main keeps a writable copy
    /// of each and the body writes that instead, which is the per-pixel behaviour the interpreter has.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _mutableUniforms;

    /// <summary>Whether the statement being emitted belongs to a helper rather than the entry.</summary>
    [ThreadStatic]
    private static bool _inHelper;

    /// <summary>Infers the SkSL type of an expression, or nothing when it cannot be known.</summary>
    /// <param name="expression">Expression node.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? TypeOf(ShaderNode expression) => expression.Kind switch
    {
        ShaderNodeKind.Literal => "float",
        ShaderNodeKind.Identifier => _types is not null && _types.TryGetValue(expression.Text, out var declared)
            ? declared
            : null,
        ShaderNodeKind.Member => SkSL.ComponentType(expression.Text.Length),
        ShaderNodeKind.Call => CallType(expression),
        ShaderNodeKind.Unary => TypeOf(expression.Left!),
        ShaderNodeKind.Index => "float",
        ShaderNodeKind.Ternary => SkSL.Wider(TypeOf(expression.Right!), TypeOf(expression.Third!)),
        ShaderNodeKind.Binary => BinaryType(expression),
        _ => null
    };

    /// <summary>Infers the type a call produces.</summary>
    /// <param name="call">Call node.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? CallType(ShaderNode call)
    {
        switch (call.Text.ToLowerInvariant())
        {
            case "tex2d":
            case "tex2dlod":
            case "tex2dbias":
            case "tex3d":
                return "float4";
            case "getpixel":
            case "getblur1":
            case "getblur2":
            case "getblur3":
                return "float3";
            case "length":
            case "dot":
            case "lum":
                return "float";
        }

        if (_helperReturns is not null && _helperReturns.TryGetValue(call.Text, out var helperReturn))
            return helperReturn;

        if (SkSL.IsVectorConstructor(call.Text))
            return SkSL.MapType(call.Text);

        // An intrinsic keeps the widest component count of its arguments, which is how pow(float3, …)
        // and max(float3, …) behave. The accumulator must skip an unknown argument instead of folding
        // it in, because Wider reports nothing when either side is unknown and starting from nothing
        // would keep the whole call unknown.
        string? widest = null;
        foreach (var argument in call.Items)
        {
            if (TypeOf(argument) is not { } argumentType)
                continue;

            widest = widest is null ? argumentType : SkSL.Wider(widest, argumentType);
        }

        return widest;
    }

    /// <summary>Infers the type of a binary expression.</summary>
    /// <param name="expression">Binary node.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? BinaryType(ShaderNode expression) => expression.Text switch
    {
        "=" or "+=" or "-=" or "*=" or "/=" => TypeOf(expression.Left!),
        "<" or ">" or "<=" or ">=" or "==" or "!=" or "&&" or "||" => "float",
        _ => SkSL.Wider(TypeOf(expression.Left!), TypeOf(expression.Right!))
    };

    /// <summary>
    /// Records the type of every variable a statement tree declares, so a use that stands before its

    /// declaration still knows what it is.
    /// </summary>
    /// <param name="statement">Statement to walk.</param>
    private static void CollectDeclarations(ShaderNode statement)
    {
        if (statement.Kind == ShaderNodeKind.Declaration && _types is not null)
        {
            var declared = SkSL.MapType(statement.Text);
            foreach (var item in statement.Items)
            {
                if (item.Text.Length > 0)
                    _types[item.Text] = declared;
            }
        }

        // A helper parameter is a declaration too, so a call to the helper knows its argument types.
        if (statement.Kind == ShaderNodeKind.Function && _types is not null)
        {
            foreach (var parameter in statement.ParameterList)
            {
                if (parameter.Text.Length == 0)
                    continue;

                _types[parameter.Text] = parameter.Items.Count > 0 ? SkSL.MapType(parameter.Items[0].Text) : "float";
            }
        }

        foreach (var child in statement.Items)
            CollectDeclarations(child);

        if (statement.Left is not null)
            CollectDeclarations(statement.Left);
        if (statement.Right is not null)
            CollectDeclarations(statement.Right);
        if (statement.Third is not null)
            CollectDeclarations(statement.Third);
    }

    /// <summary>
    /// Finds every identifier the shader uses but never declares, so it can be declared as a zero
    /// constant. The interpreter reads an unbound variable as zero, so this keeps the GPU on the same
    /// picture instead of failing on an unknown name.
    /// </summary>
    /// <param name="program">Root node of the shader.</param>
    /// <param name="samplers">The samplers the shader declares, which are never user variables.</param>
    /// <returns>The unknown names in stable order.</returns>
    private static List<string> CollectUnknownIdentifiers(ShaderNode program, ISet<string> samplers)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var statement in program.Items)
            CollectIdentifiers(statement, names);

        var unknown = new List<string>();
        foreach (var name in names)
        {
            if (_types is not null && _types.ContainsKey(name))
                continue;
            if (samplers.Contains(name))
                continue;
            // A type name is not a variable, and neither are the boolean literals. The parser also
            // keeps a few tokens such as a parameter's ":" as identifiers, which are not names.
            if (IsTypeName(name) || name is "true" or "false" or "null")
                continue;
            if (!IsIdentifier(name))
                continue;

            unknown.Add(name);
        }

        return unknown;
    }

    /// <summary>Reports whether a name is a plain identifier.</summary>
    /// <param name="name">Name to test.</param>
    /// <returns><see langword="true"/> when the name is a valid identifier.</returns>
    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
            return false;

        for (var index = 1; index < name.Length; index++)
        {
            if (!(char.IsLetterOrDigit(name[index]) || name[index] == '_'))
                return false;
        }

        return true;
    }

    /// <summary>Reports whether a name is one of the shader type spellings.</summary>
    /// <param name="name">Name to test.</param>
    /// <returns><see langword="true"/> when the name is a type.</returns>
    private static bool IsTypeName(string name) => ShaderParser.IsType(name);

    /// <summary>Collects the samplers a shader names as the first argument of a texture call.</summary>
    /// <param name="node">Node to walk.</param>
    /// <param name="names">Set to fill.</param>
    private static void CollectSamplers(ShaderNode node, SortedSet<string> names)
    {
        if (node.Kind == ShaderNodeKind.Call &&
            node.Text.ToLowerInvariant() is "tex2d" or "tex2dlod" or "tex2dbias" or "tex3d" &&
            node.Items.Count > 0 &&
            node.Items[0].Kind == ShaderNodeKind.Identifier)
        {
            names.Add(node.Items[0].Text);
        }

        foreach (var child in node.Items)
            CollectSamplers(child, names);
        foreach (var parameter in node.ParameterList)
            CollectSamplers(parameter, names);

        if (node.Kind != ShaderNodeKind.Call && node.Left is not null)
            CollectSamplers(node.Left, names);
        if (node.Right is not null)
            CollectSamplers(node.Right, names);
        if (node.Third is not null)
            CollectSamplers(node.Third, names);
    }

    /// <summary>Adds every identifier of a statement tree to a set.</summary>
    /// <param name="node">Node to walk.</param>
    /// <param name="names">Set to fill.</param>
    private static void CollectIdentifiers(ShaderNode node, SortedSet<string> names)
    {
        if (node.Kind == ShaderNodeKind.Identifier && node.Text.Length > 0)
            names.Add(node.Text);

        foreach (var child in node.Items)
            CollectIdentifiers(child, names);
        foreach (var parameter in node.ParameterList)
            CollectIdentifiers(parameter, names);

        // A call keeps its callee in Left as well as in Text, and a function name is not a variable.
        if (node.Kind != ShaderNodeKind.Call && node.Left is not null)
            CollectIdentifiers(node.Left, names);
        if (node.Right is not null)
            CollectIdentifiers(node.Right, names);
        if (node.Third is not null)
            CollectIdentifiers(node.Third, names);
    }

    /// <summary>
    /// Collects the uniforms a shader assigns to, so main can keep a writable copy of each. Only a
    /// uniform name counts; an assignment to a declared variable or to <c>ret</c> is already writable.
    /// </summary>
    /// <param name="program">Root node of the shader.</param>
    /// <returns>The assigned uniform names.</returns>
    private static HashSet<string> CollectAssignedUniforms(ShaderNode program)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        CollectAssignments(program, assigned);
        assigned.RemoveWhere(name => !UniformComponents.ContainsKey(name));
        return assigned;
    }

    /// <summary>Adds every assignment target of a statement tree to a set.</summary>
    /// <param name="node">Node to walk.</param>
    /// <param name="assigned">Set to fill.</param>
    private static void CollectAssignments(ShaderNode node, HashSet<string> assigned)
    {
        if (node.Kind == ShaderNodeKind.Binary &&
            node.Text is "=" or "+=" or "-=" or "*=" or "/=" &&
            node.Left is { Kind: ShaderNodeKind.Identifier } target)
        {
            assigned.Add(target.Text);
        }

        foreach (var child in node.Items)
            CollectAssignments(child, assigned);
        foreach (var parameter in node.ParameterList)
            CollectAssignments(parameter, assigned);

        if (node.Kind != ShaderNodeKind.Call && node.Left is not null)
            CollectAssignments(node.Left, assigned);
        if (node.Right is not null)
            CollectAssignments(node.Right, assigned);
        if (node.Third is not null)
            CollectAssignments(node.Third, assigned);
    }

    /// <summary>
    /// Emits a helper function as SkSL, with the parameter types the parser kept. Its return type is
    /// read from its own return statement, because the engine does not track declared return types.
    /// </summary>
    /// <param name="builder">Output.</param>
    /// <param name="function">Function definition node.</param>
    private static void EmitFunction(StringBuilder builder, ShaderNode function)
    {
        var parameters = new List<string>(function.ParameterList.Count);
        foreach (var parameter in function.ParameterList)
        {
            var type = parameter.Items.Count > 0 ? SkSL.MapType(parameter.Items[0].Text) : "float";
            if (_types is not null)
                _types[parameter.Text] = type;
            parameters.Add($"{type} {SkSL.SafeName(parameter.Text)}");
        }

        _inHelper = true;
        var returnType = _helperReturns is not null && _helperReturns.TryGetValue(function.Text, out var known) ? known : HelperReturnType(function);
        builder.Append(returnType).Append(' ').Append(function.Text)
            .Append('(').Append(string.Join(", ", parameters)).Append(") {\n");
        foreach (var statement in function.Items)
            EmitStatement(builder, statement, 1);
        builder.Append("}\n");
        _inHelper = false;
    }

    /// <summary>Reads a helper's return type from its own return statement.</summary>
    /// <param name="function">Function definition node.</param>
    /// <returns>The SkSL type name.</returns>
    private static string HelperReturnType(ShaderNode function)
    {
        foreach (var statement in function.Items)
        {
            if (statement.Kind == ShaderNodeKind.Return &&
                statement.Left is not null &&
                TypeOf(statement.Left) is { } type)
            {
                return type;
            }
        }

        return "float3";
    }

    /// <summary>
    /// Emits an assignment, converting between a scalar and a vector where the engine would allow it
    /// and SkSL would not. It only acts when both types are known, so an unknown expression is never
    /// rewritten on a guess.
    /// </summary>
    /// <param name="left">Target text.</param>
    /// <param name="right">Value text.</param>
    /// <param name="expression">Assignment node.</param>
    /// <returns>The assignment text.</returns>
    private static string EmitAssignment(string left, string right, ShaderNode expression)
    {
        right = ConvertToTarget(expression, right, expression.Right is null ? null : TypeOf(expression.Right));
        return $"{left} = {right}";
    }

    /// <summary>
    /// Converts an assignment value to the declared type of its target variable. It only acts when the
    /// target is a plain identifier with a known type, so a swizzle target or an unknown type is left
    /// alone rather than guessed at.
    /// </summary>
    /// <param name="expression">Assignment node.</param>
    /// <param name="right">Value text.</param>
    /// <param name="valueType">The value's inferred type, or nothing.</param>
    /// <returns>The converted value text.</returns>
    private static string ConvertToTarget(ShaderNode expression, string right, string? valueType)
    {
        var targetType = TargetType(expression.Left);
        return targetType is null ? right : SkSL.Convert(right, valueType, targetType);
    }

    /// <summary>Returns the declared type an assignment target has, or nothing when it is unknown.</summary>
    /// <param name="target">Assignment target.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? TargetType(ShaderNode? target) => target switch
    {
        { Kind: ShaderNodeKind.Identifier } identifier when _types is not null &&
            _types.TryGetValue(identifier.Text, out var declared) => declared,
        // A one-letter swizzle selects a scalar component, so the value has to become a scalar too.
        { Kind: ShaderNodeKind.Member } member => SkSL.ComponentType(member.Text.Length),
        // An element read is a scalar in the engine's vector model.
        { Kind: ShaderNodeKind.Index } => "float",
        _ => null
    };

    /// <summary>Emits a unary expression, keeping the preset convention that zero is false.</summary>
    /// <param name="expression">Unary node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitUnary(ShaderNode expression)
    {
        var operand = EmitExpression(expression.Left!);
        return expression.Text switch
        {
            "-" => $"(-{operand})",
            "+" => operand,
            "++" => $"(++{operand})",
            "--" => $"(--{operand})",
            "!" => $"(({operand}) == 0.0 ? 1.0 : 0.0)",
            _ => throw new PresetExpressionException(
                $"The unary operator '{expression.Text}' has no SkSL translation.",
                expression.Position)
        };
    }

    /// <summary>Emits a binary expression, mapping comparisons onto the one-or-zero convention.</summary>
    /// <param name="expression">Binary node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitBinary(ShaderNode expression)
    {
        var leftNode = expression.Left!;
        var rightNode = expression.Right!;
        var leftType = TypeOf(leftNode);
        var rightType = TypeOf(rightNode);
        var left = EmitExpression(leftNode);
        var right = EmitExpression(rightNode);

        // SkSL wants matching component counts, while a preset mixes them freely: "float3 * float2"
        // and "float4 + float3" both occur. Both operands are brought to the wider type, which is
        // what the engine's component-wise arithmetic already does. An assignment is left alone: its
        // target must never be converted, only the value it is given, which EmitAssignment does.
        var isAssignment = expression.Text is "=" or "+=" or "-=" or "*=" or "/=";
        if (!isAssignment &&
            leftType is not null && rightType is not null &&
            SkSL.ComponentCount(leftType) != SkSL.ComponentCount(rightType) &&
            SkSL.Wider(leftType, rightType) is { } common)
        {
            left = SkSL.Convert(left, leftType, common);
            right = SkSL.Convert(right, rightType, common);
        }

        // A compound assignment hands its value back to the target's declared type, so a
        // "float3 *= float4" becomes a swizzle rather than a SkSL type error.
        if (expression.Text is "+=" or "-=" or "*=" or "/=")
            right = ConvertToTarget(expression, right, rightType);

        return expression.Text switch
        {
            "+" => $"({left} + {right})",
            "-" => $"({left} - {right})",
            "*" => $"({left} * {right})",
            "/" => $"({left} / {right})",
            "%" => $"mod({left}, {right})",
            "=" => EmitAssignment(left, right, expression),
            "+=" => $"{left} += {right}",
            "-=" => $"{left} -= {right}",
            "*=" => $"{left} *= {right}",
            "/=" => $"{left} /= {right}",
            "==" => $"(({left} == {right}) ? 1.0 : 0.0)",
            "!=" => $"(({left} != {right}) ? 1.0 : 0.0)",
            "<" => $"(({left} < {right}) ? 1.0 : 0.0)",
            ">" => $"(({left} > {right}) ? 1.0 : 0.0)",
            "<=" => $"(({left} <= {right}) ? 1.0 : 0.0)",
            ">=" => $"(({left} >= {right}) ? 1.0 : 0.0)",
            "&&" => $"((({left} != 0.0) && ({right} != 0.0)) ? 1.0 : 0.0)",
            "||" => $"((({left} != 0.0) || ({right} != 0.0)) ? 1.0 : 0.0)",
            "," => $"({left}, {right})",
            _ => throw new PresetExpressionException(
                $"The binary operator '{expression.Text}' has no SkSL translation.",
                expression.Position)
        };
    }

    /// <summary>
    /// Emits a sampling coordinate as the <c>float2</c> Skia's <c>eval</c> takes. The interpreter
    /// reads only the first two components of a coordinate, so a wider value is narrowed the same way.
    /// </summary>
    /// <param name="call">Call the coordinate belongs to.</param>
    /// <param name="text">Already emitted argument text.</param>
    /// <param name="index">Argument index of the coordinate.</param>
    /// <returns>The coordinate text.</returns>
    private static string Coordinate(ShaderNode call, string text, int index) =>
        index < call.Items.Count ? SkSL.Convert(text, TypeOf(call.Items[index]), "float2") : text;

    /// <summary>Returns the texture-size uniform that belongs to a sampler.</summary>
    /// <param name="sampler">Emitted sampler name.</param>
    /// <returns>The uniform name, or <c>texsize</c> for a sampler without its own size.</returns>
    private static string TexSizeUniform(string sampler) => sampler switch
    {
        "sampler_main" => "texsize_main",
        "sampler_fc_main" => "texsize_fc_main",
        "sampler_pc_main" => "texsize_pc_main",
        "sampler_noise_lq" => "texsize_noise_lq",
        "sampler_noise_mq" => "texsize_noise_mq",
        "sampler_noise_hq" => "texsize_noise_hq",
        "sampler_noisevol_lq" => "texsize_noisevol_lq",
        "sampler_noisevol_hq" => "texsize_noisevol_hq",
        _ => "texsize"
    };

    /// <summary>
    /// Converts a normalised coordinate into the pixel coordinate a sampler's <c>eval</c> takes. A
    /// generated texture is sampled the way <see cref="VisualizerTextureBank.Sample"/> does
    /// (<c>u * size - 0.5</c>), while a frame is sampled the way
    /// <see cref="PixelBuffer.SampleBilinear"/> does (<c>u * (size - 1)</c>), so their scales differ
    /// by one and the frame's texel centre needs the extra half.
    /// </summary>
    /// <param name="sampler">Emitted sampler name.</param>
    /// <param name="coordinate">Emitted normalised coordinate.</param>
    /// <returns>The pixel coordinate text.</returns>
    private static string SamplerCoordinate(string sampler, string coordinate) =>
        IsFrameSampler(sampler)
            ? $"({coordinate} * ({TexSizeUniform(sampler)}.xy - 1.0) + 0.5)"
            : $"({coordinate} * {TexSizeUniform(sampler)}.xy)";

    /// <summary>Reports whether a sampler reads a frame rather than a generated texture.</summary>
    /// <param name="sampler">Emitted sampler name.</param>
    /// <returns><see langword="true"/> when the sampler is not one of the generated textures.</returns>
    private static bool IsFrameSampler(string sampler) => !VisualizerTextureBank.TryResolve(sampler, out _);

    /// <summary>Emits a call, translating the sampler accessors and the renamed intrinsics.</summary>
    /// <param name="call">Call node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitCall(ShaderNode call)
    {
        var name = call.Text;
        var arguments = new List<string>(call.Items.Count);
        foreach (var argument in call.Items)
            arguments.Add(EmitExpression(argument));

        // An intrinsic takes matching component counts, while a preset may hand it a float4 where a
        // float3 is meant, as in "max(ret, tex2D(...) * 0.97)". Every argument is brought to the
        // smallest vector count among them; widening a scalar is harmless because it is uniform.
        if (SkSL.DirectFunctions.Contains(name) || name is "saturate" or "lerp" or "atan2" or "mul" or "lum")
        {
            var smallest = int.MaxValue;
            foreach (var argument in call.Items)
            {
                if (TypeOf(argument) is { } argumentType && SkSL.ComponentCount(argumentType) > 1)
                    smallest = Math.Min(smallest, SkSL.ComponentCount(argumentType));
            }

            if (smallest is > 1 and < int.MaxValue)
            {
                for (var index = 0; index < arguments.Count; index++)
                    arguments[index] = SkSL.Convert(arguments[index], TypeOf(call.Items[index]), SkSL.ComponentType(smallest));
            }
        }


        // Milkdrop samples the frame and its blur levels through these helpers. A Skia runtime
        // effect samples a shader in pixel coordinates, so a normalised coordinate is scaled back.
        switch (name.ToLowerInvariant())
        {
            case "tex2d":
            case "tex2dlod":
            case "tex2dbias":
                if (arguments.Count < 2)
                    throw new PresetExpressionException("tex2D needs a sampler and a coordinate.", call.Position);

                // A Skia shader evaluates to half4, so the result is widened to the float4 the
                // presets expect from tex2D. The coordinate is normalised, so it is scaled back by the
                // sampler's own size; the noise and volume textures are not frame-sized, and using the
                // frame size for them would sample the wrong texels. The frame textures use
                // PixelBuffer.SampleBilinear's convention, which maps a normalised coordinate to
                // zero..size-1, so their scale is one less and their texel centres shift by half.
                return $"float4({arguments[0]}.eval({SamplerCoordinate(arguments[0], Coordinate(call, arguments[1], 1))}))";
            case "tex3d":
                // Milkdrop samples a 3D noise volume. Skia's runtime effects only sample 2D
                // shaders, so the volume travels as a slice atlas and the generated helper does the
                // trilinear filtering; the CPU sampler applies the identical math to the same volume.
                // SkSL forbids a shader parameter on a user function, so each volume sampler has its
                // own helper.
                if (arguments.Count < 2)
                    throw new PresetExpressionException("tex3D needs a sampler and a coordinate.", call.Position);

                var volumeHelper = arguments[0].Contains("hq", StringComparison.Ordinal)
                    ? "orynivoTex3DHq"
                    : "orynivoTex3DLq";
                return $"{volumeHelper}({arguments[1]})";
            case "getpixel":
                // The interpreter reads the texel at the truncated integer coordinate, so the pixel
                // centre is the coordinate plus half.
                return arguments.Count >= 2
                    ? $"float4({MainSampler}.eval(float2(float(int({arguments[0]})), float(int({arguments[1]}))) + 0.5)).rgb"
                    : $"float4({MainSampler}.eval(float2(float(int({arguments[0]}.x)), float(int({arguments[0]}.y))) + 0.5)).rgb";
            case "getblur1":
                return $"float4(sampler_blur1.eval({Coordinate(call, arguments[0], 0)} * (texsize.xy - 1.0) + 0.5)).rgb";
            case "getblur2":
                return $"float4(sampler_blur2.eval({Coordinate(call, arguments[0], 0)} * (texsize.xy - 1.0) + 0.5)).rgb";
            case "getblur3":
                return $"float4(sampler_blur3.eval({Coordinate(call, arguments[0], 0)} * (texsize.xy - 1.0) + 0.5)).rgb";
            case "saturate":
                return $"clamp({arguments[0]}, 0.0, 1.0)";
            case "atan2":
                return $"atan({arguments[0]}, {arguments[1]})";
            case "lerp":
                return $"mix({arguments[0]}, {arguments[1]}, {arguments[2]})";
            case "mul":
                return arguments.Count >= 2 ? $"({arguments[0]} * {arguments[1]})" : arguments[0];
            case "lum":
                // Milkdrop's luminance helper; the weights are the conventional Rec. 601 ones. The
                // implicit float3 weight vector is a second operand, so the value is converted to
                // float3 here rather than by the argument loop above.
                return $"dot({SkSL.Convert(arguments[0], call.Items.Count > 0 ? TypeOf(call.Items[0]) : null, "float3")}, float3(0.299, 0.587, 0.114))";
        }

        // A call to a function the shader defines itself keeps its name.
        if (_helperReturns is not null && _helperReturns.ContainsKey(name))
            return $"{name}({string.Join(", ", arguments)})";

        if (SkSL.RenamedFunctions.TryGetValue(name, out var renamed))
            return $"{renamed}({string.Join(", ", arguments)})";

        if (SkSL.DirectFunctions.Contains(name))
            return $"{name}({string.Join(", ", arguments)})";

        // A vector constructor such as float2(1, 0) keeps its spelling, with the half and double
        // spellings folded onto float because SkSL does not need the precision split here.
        var mapped = SkSL.MapType(name);
        if (mapped != name || SkSL.IsVectorConstructor(name))
            return $"{mapped}({string.Join(", ", arguments)})";

        throw new PresetExpressionException(
            $"The shader function '{name}' has no SkSL translation.",
            call.Position);
    }
}
