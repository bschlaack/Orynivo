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
        "bass_att", "mid_att", "treb_att", "vol_att", "aspect", "rand_frame", "rand_preset",
        "roam_cos", "roam_sin", "slow_roam_cos", "slow_roam_sin"
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
            ["aspect"] = 4,
            ["aspectx"] = 1,
            ["aspecty"] = 1,
            ["rand_frame"] = 4,
            ["rand_preset"] = 4,
            ["roam_cos"] = 4,
            ["roam_sin"] = 4,
            ["slow_roam_cos"] = 4,
            ["slow_roam_sin"] = 4
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

        for (var index = 1; index <= 3; index++)
        {
            uniforms["blur" + index + "_min"] = 1;
            uniforms["blur" + index + "_max"] = 1;
        }
        for (var index = 1; index <= 32; index++)
            uniforms["q" + index.ToString(CultureInfo.InvariantCulture)] = 1;
        for (var index = 1; index <= 8; index++)
            uniforms["t" + index.ToString(CultureInfo.InvariantCulture)] = 1;
        for (var channel = 0; channel < 3; channel++)
        {
            for (var corner = 0; corner < 4; corner++)
                uniforms[HueShaderUniform(channel, corner)] = 1;
        }

        // Milkdrop's rot_* matrices are float4x3, which neither the interpreter's square-matrix pool
        // nor SkSL models, so each arrives as three float4 columns and the source rewrite reads them.
        foreach (var matrix in ShaderRotationMatrices.Names)
        {
            for (var column = 0; column < 3; column++)
                uniforms[matrix + "_c" + column.ToString(CultureInfo.InvariantCulture)] = 4;
        }

        return uniforms;
    }

    /// <summary>The uniform name that carries one corner channel of the reference's hue shade.</summary>
    /// <param name="channel">Zero for red, one for green, two for blue.</param>
    /// <param name="corner">Corner index in the reference's order.</param>
    /// <returns>The uniform name.</returns>
    private static string HueShaderUniform(int channel, int corner) =>
        "hue_shader_" + "rgb"[channel] + corner.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Emits the local <c>hue_shader</c> the reference passes to every shader: its animated four-corner
    /// shade, mixed by the fragment's own position because a single uniform cannot carry the
    /// interpolation the reference gets from the quad's vertex colours.
    /// </summary>
    /// <param name="builder">Output.</param>
    private static void EmitHueShader(StringBuilder builder)
    {
        builder.Append("    float2 hue_uv = fragCoord / texsize.xy;\n");
        for (var channel = 0; channel < 3; channel++)
        {
            builder.Append("    float hue_").Append("rgb"[channel]).Append(" = mix(mix(")
                .Append(HueShaderUniform(channel, 3)).Append(", ").Append(HueShaderUniform(channel, 2))
                .Append(", hue_uv.x), mix(")
                .Append(HueShaderUniform(channel, 1)).Append(", ").Append(HueShaderUniform(channel, 0))
                .Append(", hue_uv.x), hue_uv.y);\n");
        }

        builder.Append("    float3 hue_shader = float3(hue_r, hue_g, hue_b);\n");
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

        // The product of a row vector and Milkdrop's non-square rot_* matrix, emitted by the source
        // rewrite as one call. GLSL and SkSL both accept the dot-product expansion, and the value is
        // exactly the row-vector mul the presets were written against.
        builder.Append("float3 orynivo_mul4x3(float4 v, float4 c0, float4 c1, float4 c2) "
            + "{ return float3(dot(v, c0), dot(v, c1), dot(v, c2)); }\n");

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
        var pixel = "float2((mod(" + z + ", " + columns + ".0) * " + size + ".0) + " + x
            + " + 0.5, (floor(" + z + " / " + columns + ".0) * " + size + ".0) + " + y + " + 0.5)";
        // Skia samples the atlas in pixels; GLSL samples it normalised.
        var coordinate = _glsl
            ? $"({pixel} / float2({VisualizerTextureBank.VolumeAtlasWidth}.0, {VisualizerTextureBank.VolumeAtlasHeight}.0))"
            : pixel;
        builder.Append("    float4 ").Append(variable).Append(" = ").Append(SampleExpr(sampler, coordinate)).Append(";\n");
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
        uniform float vol_att;
        uniform float bass_att;
        uniform float mid_att;
        uniform float treb_att;
        uniform float4 aspect;
        uniform float4 rand_frame;
        uniform float4 rand_preset;
        uniform float4 roam_cos;
        uniform float4 roam_sin;
        uniform float4 slow_roam_cos;
        uniform float4 slow_roam_sin;
        uniform float4 _qa;
        uniform float4 _qb;
        uniform float4 _qc;
        uniform float4 _qd;
        uniform float4 _qe;
        uniform float4 _qf;
        uniform float4 _qg;
        uniform float4 _qh;
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
        float orynivoSafeDiv(float a, float b) { return b == 0.0 ? 0.0 : a / b; }
        float2 orynivoSafeDiv(float2 a, float2 b) { return float2(b.x == 0.0 ? 0.0 : a.x / b.x, b.y == 0.0 ? 0.0 : a.y / b.y); }
        float3 orynivoSafeDiv(float3 a, float3 b) { return float3(b.x == 0.0 ? 0.0 : a.x / b.x, b.y == 0.0 ? 0.0 : a.y / b.y, b.z == 0.0 ? 0.0 : a.z / b.z); }
        float4 orynivoSafeDiv(float4 a, float4 b) { return float4(b.x == 0.0 ? 0.0 : a.x / b.x, b.y == 0.0 ? 0.0 : a.y / b.y, b.z == 0.0 ? 0.0 : a.z / b.z, b.w == 0.0 ? 0.0 : a.w / b.w); }
        """;

    /// <summary>
    /// Whether the emitter is producing GLSL instead of SkSL. It is set for the duration of one
    /// translation, which is single-threaded, and it only changes the prelude, the sampler access, and
    /// the entry point: the body emission is shared so the two dialects cannot drift apart.
    /// </summary>
    private static bool _glsl;
    private static bool _glslComp;

    /// <summary>
    /// The GLSL prelude. It is the SkSL prelude with GLSL ES 3.0 spelling: the samplers are
    /// <c>sampler2D</c> sampled with <c>texture()</c>, and the vector types are aliased so the shared
    /// body emission can keep writing <c>float2</c>/<c>float3</c>/<c>float4</c>.
    /// </summary>
    private const string GlslPrelude = """
        #version 300 es
        #define float2 vec2
        #define float3 vec3
        #define float4 vec4
        #define half2 vec2
        #define half3 vec3
        #define half4 vec4
        #define half float
        precision highp float;
        precision highp sampler2D;
        uniform float texsize_x;
        uniform float texsize_y;
        uniform float texsize_z;
        uniform float texsize_w;
        #define texsize float4(texsize_x, texsize_y, texsize_z, texsize_w)
        uniform float time;
        uniform float frame;
        uniform float fps;
        uniform float bass;
        uniform float mid;
        uniform float treb;
        uniform float vol;
        uniform float vol_att;
        uniform float bass_att;
        uniform float mid_att;
        uniform float treb_att;
        uniform float aspect_x;
        uniform float aspect_y;
        uniform float aspect_z;
        uniform float aspect_w;
        #define aspect float4(aspect_x, aspect_y, aspect_z, aspect_w)
        uniform float rand_frame_x;
        uniform float rand_frame_y;
        uniform float rand_frame_z;
        uniform float rand_frame_w;
        #define rand_frame float4(rand_frame_x, rand_frame_y, rand_frame_z, rand_frame_w)
        uniform float rand_preset_x;
        uniform float rand_preset_y;
        uniform float rand_preset_z;
        uniform float rand_preset_w;
        #define rand_preset float4(rand_preset_x, rand_preset_y, rand_preset_z, rand_preset_w)
        uniform float roam_cos_x;
        uniform float roam_cos_y;
        uniform float roam_cos_z;
        uniform float roam_cos_w;
        #define roam_cos float4(roam_cos_x, roam_cos_y, roam_cos_z, roam_cos_w)
        uniform float roam_sin_x;
        uniform float roam_sin_y;
        uniform float roam_sin_z;
        uniform float roam_sin_w;
        #define roam_sin float4(roam_sin_x, roam_sin_y, roam_sin_z, roam_sin_w)
        uniform float slow_roam_cos_x;
        uniform float slow_roam_cos_y;
        uniform float slow_roam_cos_z;
        uniform float slow_roam_cos_w;
        #define slow_roam_cos float4(slow_roam_cos_x, slow_roam_cos_y, slow_roam_cos_z, slow_roam_cos_w)
        uniform float slow_roam_sin_x;
        uniform float slow_roam_sin_y;
        uniform float slow_roam_sin_z;
        uniform float slow_roam_sin_w;
        #define slow_roam_sin float4(slow_roam_sin_x, slow_roam_sin_y, slow_roam_sin_z, slow_roam_sin_w)
        #define _qa float4(q1, q2, q3, q4)
        #define _qb float4(q5, q6, q7, q8)
        #define _qc float4(q9, q10, q11, q12)
        #define _qd float4(q13, q14, q15, q16)
        #define _qe float4(q17, q18, q19, q20)
        #define _qf float4(q21, q22, q23, q24)
        #define _qg float4(q25, q26, q27, q28)
        #define _qh float4(q29, q30, q31, q32)
        uniform sampler2D sampler_main;
        uniform sampler2D sampler_blur1;
        uniform sampler2D sampler_blur2;
        uniform sampler2D sampler_blur3;
        uniform sampler2D sampler_noise_lq;
        uniform sampler2D sampler_noise_mq;
        uniform sampler2D sampler_noise_hq;
        uniform sampler2D sampler_fc_main;
        uniform sampler2D sampler_pc_main;
        uniform sampler2D sampler_noisevol_lq;
        uniform sampler2D sampler_noisevol_hq;
        uniform sampler2D sampler_pw_main;
        uniform sampler2D sampler_pw_noise_lq;
        uniform sampler2D sampler_worms;
        float4 toColour(float3 c) { return float4(c, 1.0); }
        float4 toColour(float4 c) { return c; }
        float4 toColour(float c) { return float4(c, c, c, 1.0); }
        float orynivoSafeDiv(float a, float b) { return b == 0.0 ? 0.0 : a / b; }
        float2 orynivoSafeDiv(float2 a, float2 b) { return float2(b.x == 0.0 ? 0.0 : a.x / b.x, b.y == 0.0 ? 0.0 : a.y / b.y); }
        float3 orynivoSafeDiv(float3 a, float3 b) { return float3(b.x == 0.0 ? 0.0 : a.x / b.x, b.y == 0.0 ? 0.0 : a.y / b.y, b.z == 0.0 ? 0.0 : a.z / b.z); }
        float4 orynivoSafeDiv(float4 a, float4 b) { return float4(b.x == 0.0 ? 0.0 : a.x / b.x, b.y == 0.0 ? 0.0 : a.y / b.y, b.z == 0.0 ? 0.0 : a.z / b.z, b.w == 0.0 ? 0.0 : a.w / b.w); }
        """;

    /// <summary>Reads a sampler, spelled for the active dialect.</summary>
    /// <param name="sampler">Sampler expression.</param>
    /// <param name="coordinate">Normalised or texel coordinate expression.</param>
    /// <returns>The sampling expression.</returns>
    private static string SampleExpr(string sampler, string coordinate)
    {
        if (_glslComp && ShaderSamplerName.Parse(sampler).BaseName is "main" or "blur1" or "blur2" or "blur3")
            coordinate = $"vec2(({coordinate}).x, 1.0 - ({coordinate}).y)";
        return _glsl ? $"texture({sampler}, {coordinate})" : $"{sampler}.eval({coordinate})";
    }

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
        uniform float _orynivo_warp;
        uniform float _orynivo_warpTime;
        uniform float _orynivo_warpScale;
        """;

    /// <summary>
    /// The GLSL spelling of the warp uniforms. GL exposes only scalar uniform setters, so a vector is
    /// declared as its components and rebuilt with a macro.
    /// </summary>
    private const string GlslWarpUniforms = """
        uniform float _orynivo_size_x;
        uniform float _orynivo_size_y;
        #define _orynivo_size float2(_orynivo_size_x, _orynivo_size_y)
        uniform float _orynivo_zoom;
        uniform float _orynivo_zoomExp;
        uniform float _orynivo_rotation;
        uniform float _orynivo_centre_x;
        uniform float _orynivo_centre_y;
        #define _orynivo_centre float2(_orynivo_centre_x, _orynivo_centre_y)
        uniform float _orynivo_offset_x;
        uniform float _orynivo_offset_y;
        #define _orynivo_offset float2(_orynivo_offset_x, _orynivo_offset_y)
        uniform float _orynivo_stretch_x;
        uniform float _orynivo_stretch_y;
        #define _orynivo_stretch float2(_orynivo_stretch_x, _orynivo_stretch_y)
        uniform float _orynivo_warp;
        uniform float _orynivo_warpTime;
        uniform float _orynivo_warpScale;
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
        return TranspileCore(program, null, warpedUv: false, glsl: false, out samplers, out _);
    }

    /// <summary>
    /// Translates a comp shader together with its own per-pixel expression block into SkSL. The
    /// block runs before the shader body with the same frame vocabulary; the engine re-seeds
    /// <c>x</c>, <c>y</c>, <c>rad</c>, and <c>ang</c> from the pixel position.
    /// </summary>
    /// <param name="program">Parsed comp shader body.</param>
    /// <param name="perPixel">Per-pixel expression block that runs alongside the shader, or <see langword="null"/>.</param>
    /// <param name="samplers">The samplers the generated SkSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the per-pixel block reads, which the caller has to seed.</param>
    /// <returns>SkSL source for a <c>half4 main(float2 fragCoord)</c> runtime effect.</returns>
    /// <exception cref="PresetExpressionException">The body or block uses something SkSL cannot express here.</exception>
    public static string TranspileComp(
        ShaderNode program,
        PresetProgram? perPixel,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms) =>
        TranspileCore(program, perPixel, warpedUv: false, glsl: false, out samplers, out perPixelUniforms);

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
        TranspileCore(program, perPixel, warpedUv: true, glsl: false, out samplers, out perPixelUniforms);

    /// <summary>
    /// Translates a parsed shader body into GLSL ES 3.0 for the OpenGL pipeline, which is the same
    /// translation as <see cref="Transpile"/> with a different prelude, sampler access, and entry
    /// point. A shader the GLSL dialect cannot express throws, so the caller keeps it on the SkSL or
    /// interpreter path.
    /// </summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <param name="samplers">The samplers the generated GLSL declares.</param>
    /// <returns>GLSL source for a <c>void main()</c> fragment shader writing <c>orynivoColor</c>.</returns>
    /// <exception cref="PresetExpressionException">The body uses something GLSL cannot express here.</exception>
    public static string TranspileGlsl(ShaderNode program, out IReadOnlyList<string> samplers)
    {
        ArgumentNullException.ThrowIfNull(program);
        return TranspileCore(program, null, warpedUv: false, glsl: true, out samplers, out _);
    }

    /// <summary>
    /// Translates a comp shader and its per-pixel block into GLSL ES 3.0 for the OpenGL pipeline.
    /// </summary>
    /// <param name="program">Parsed comp shader body.</param>
    /// <param name="perPixel">Per-pixel expression block that runs alongside the shader, or <see langword="null"/>.</param>
    /// <param name="samplers">The samplers the generated GLSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the per-pixel block reads.</param>
    /// <returns>GLSL source for a <c>void main()</c> fragment shader writing <c>orynivoColor</c>.</returns>
    /// <exception cref="PresetExpressionException">The body or block uses something GLSL cannot express here.</exception>
    public static string TranspileGlslComp(
        ShaderNode program,
        PresetProgram? perPixel,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms) =>
        TranspileCore(program, perPixel, warpedUv: false, glsl: true, out samplers, out perPixelUniforms);

    /// <summary>
    /// Translates a warp shader and the preset's per-pixel block into GLSL ES 3.0 for the OpenGL
    /// pipeline.
    /// </summary>
    /// <param name="program">Parsed warp shader body, or <see langword="null"/> for a warp without a shader.</param>
    /// <param name="perPixel">Per-pixel expression block that chooses the sampling position, or <see langword="null"/>.</param>
    /// <param name="samplers">The samplers the generated GLSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the per-pixel block reads.</param>
    /// <returns>GLSL source for a <c>void main()</c> fragment shader writing <c>orynivoColor</c>.</returns>
    /// <exception cref="PresetExpressionException">The body or block uses something GLSL cannot express here.</exception>
    public static string TranspileGlslWarp(
        ShaderNode? program,
        PresetProgram? perPixel,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms) =>
        TranspileCore(program, perPixel, warpedUv: true, glsl: true, out samplers, out perPixelUniforms);

    /// <summary>
    /// Translates a warp shader into the GLSL fragment stage of the mesh warp. The coordinate, the
    /// original position, and the polar pair arrive interpolated from the vertex shader, so this stage
    /// only runs the shader body and samples; the per-pixel block belongs to the mesh and is not
    /// emitted here. That is the reference's split: the per-vertex program produces the mesh, the
    /// vertex shader transforms, and the warp shader is a fragment stage over the interpolated
    /// coordinate.
    /// </summary>
    /// <param name="program">Parsed warp shader body, or <see langword="null"/> for a direct sample.</param>
    /// <param name="perPixel">
    /// Per-pixel block, used only to collect and declare the variables the shader reads; the mesh
    /// already carries its result, so the body itself is not emitted.
    /// </param>
    /// <param name="samplers">The samplers the generated GLSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the shader reads.</param>
    /// <returns>The generated GLSL fragment shader.</returns>
    public static string TranspileGlslWarpMesh(
        ShaderNode? program,
        PresetProgram? perPixel,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms) =>
        TranspileCore(program, perPixel, warpedUv: true, glsl: true, out samplers, out perPixelUniforms, meshUv: true);

    /// <summary>The shared core of both translations.</summary>
    /// <param name="program">Parsed shader body, or <see langword="null"/> for a warp without a shader.</param>
    /// <param name="perPixel">Per-pixel expression block, or <see langword="null"/>.</param>
    /// <param name="warpedUv">Whether the entry point builds <c>uv</c> from the motion transform.</param>
    /// <param name="samplers">The samplers the generated SkSL declares.</param>
    /// <param name="perPixelUniforms">Preset variables the per-pixel block reads.</param>
    /// <returns>The generated SkSL.</returns>
    /// <summary>
    /// Serializes the emitter's shared state. The translation keeps its type table, its mutable
    /// uniform set, and the dialect flag in static fields for the recursive emitters to read, so two
    /// translations that run at the same time would otherwise interleave and emit a broken shader.
    /// </summary>
    private static readonly object TranspileLock = new();

    private static string TranspileCore(
        ShaderNode? program,
        PresetProgram? perPixel,
        bool warpedUv,
        bool glsl,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms,
        bool meshUv = false)
    {
        lock (TranspileLock)
        {
            return TranspileCoreLocked(
                program, perPixel, warpedUv, glsl, out samplers, out perPixelUniforms, meshUv);
        }
    }

    private static string TranspileCoreLocked(
        ShaderNode? program,
        PresetProgram? perPixel,
        bool warpedUv,
        bool glsl,
        out IReadOnlyList<string> samplers,
        out IReadOnlyList<string> perPixelUniforms,
        bool meshUv = false)
    {
        _glsl = glsl;
        // A GL frame texture is bottom-up while the comp stage and a per-pixel warp derive their
        // coordinate from the fragment position in the engine's top-down convention, so those reads
        // flip. The mesh warp does not: its coordinate arrives from the vertex stage already in the
        // texture's orientation.
        _glslComp = glsl && (!warpedUv || !meshUv);
        var builder = new StringBuilder();

        // Every sampler the shader names is declared, so an unknown sampler does not become a zero
        // constant with a broken ".eval". The base set keeps the prelude's literal declarations.
        var samplerNames = new SortedSet<string>(Samplers, StringComparer.Ordinal);
        if (program is not null)
            CollectSamplers(program, samplerNames);
        samplers = [.. samplerNames];

        builder.Append(glsl ? GlslPrelude : Prelude).Append(GeneratedUniforms()).Append(GeneratedVolumeHelpers());
        if (warpedUv)
            builder.Append(glsl ? GlslWarpUniforms : WarpUniforms);
        var extras = samplerNames.Where(name => !SamplerSet.Contains(name)).ToList();
        foreach (var name in extras)
        {
            // A preset may name its own texture (for example "sampler sampler_cells;"). SkSL spells a
            // sampler type "shader" while GLSL needs "sampler2D", and the wrong one is a compile error
            // on the other backend.
            builder.Append(glsl ? "uniform sampler2D " : "uniform shader ").Append(name).Append(";\n");
        }

        // The type table has to stand before the helpers are emitted, because their parameter types
        // are recorded as they are written out.
        _types = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ret"] = "float3",
            // The per-pixel variables main declares itself; the body may use them before the
            // declaration line is reached in source order, so their types are known up front.
            ["uv"] = "float2",
            ["uv_orig"] = "float2",
            ["hue_shader"] = "float3",
            ["rad"] = "float",
            ["ang"] = "float"
        };
        foreach (var (uniform, count) in UniformComponents)
            _types[uniform] = count switch { 1 => "float", 2 => "float2", 4 => "float4", _ => "float" };
        // Milkdrop's include.fx packs q1..q32 into the float4 banks _qa.._qh; a shader uses them as a
        // vector (for example float2x2(_qb)), so their type must be known for a matrix constructor.
        for (var bank = 0; bank < 8; bank++)
            _types["_q" + (char)('a' + bank)] = "float4";

        _mutableUniforms = new HashSet<string>(StringComparer.Ordinal);
        _helperReturns = new Dictionary<string, string>(StringComparer.Ordinal);
        _helperParameters = new Dictionary<string, List<string>>(StringComparer.Ordinal);

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
                    _helperParameters[statement.Text] =
                        [.. statement.ParameterList.Select(parameter =>
                            parameter.Items.Count > 0 ? SkSL.MapType(parameter.Items[0].Text) : "float")];
                }
            }

            // A file-scope variable is a local of main, which a helper emitted before main cannot see.
            // SkSL runtime effects have no mutable globals, so each helper that reads one takes it as a
            // parameter; the set is closed transitively over the helpers it calls.
            _globals = new HashSet<string>(StringComparer.Ordinal);
            foreach (var statement in program.Items)
            {
                if (statement.Kind != ShaderNodeKind.Declaration)
                    continue;

                foreach (var item in statement.Items)
                {
                    if (item.Text.Length > 0)
                        _globals.Add(item.Text);
                }
            }

            _helperGlobals = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var helper in helpers)
                _helperGlobals[helper.Text] = [];

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var helper in helpers)
                {
                    var needed = new SortedSet<string>(_helperGlobals[helper.Text], StringComparer.Ordinal);
                    foreach (var statement in helper.Items)
                        CollectHelperGlobals(statement, needed);
                    if (needed.Count != _helperGlobals[helper.Text].Count)
                    {
                        _helperGlobals[helper.Text] = [.. needed];
                        changed = true;
                    }
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
            {
                // In mesh mode the per-pixel block already runs on the mesh, so its statements are
                // never emitted here: a block the dialect cannot express - one that uses the shared
                // memory buffer, for example - must not cost the preset its shader. The mesh carries
                // the motion the block wrote, and anything else the shader reads keeps its frame value.
                if (!meshUv)
                    throw new PresetExpressionException(error ?? "The per-pixel block has no SkSL translation.", 0);

                perPixelBody = string.Empty;
            }
            else
            {
                perPixelNames = [.. names];
            }

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
            EmitCompMain(builder, program, perPixelBody);
            return builder.ToString();
        }

        // In mesh mode the per-pixel block already ran on the mesh, so its statements are not emitted
        // again; its variables stay declared and seeded because the shader body may read them.
        EmitWarpMain(builder, program, meshUv ? string.Empty : perPixelBody, meshUv);
        return builder.ToString();
    }

    /// <summary>Emits the comp entry point, which samples <c>uv</c> straight from the fragment position.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="program">Parsed shader body.</param>
    /// <param name="perPixelBody">Emitted per-pixel statements.</param>
    private static void EmitCompMain(StringBuilder builder, ShaderNode? program, string perPixelBody)
    {
        EmitEntryHeader(builder);
        EmitHueShader(builder);
        builder.Append("    float2 uv_orig = fragCoord / texsize.xy;\n");
        builder.Append("    float2 uv = uv_orig;\n");
        builder.Append("    float2 centred = (uv * 2.0) - 1.0;\n");
        builder.Append("    float2 mathPos = centred * aspect.xy;\n");
        builder.Append("    float rad = length(mathPos) / length(aspect.xy);\n");
        builder.Append("    float ang = atan(mathPos.y, mathPos.x);\n");
        builder.Append("    if (ang < 0.0) ang += 6.283185307179586;\n");
        builder.Append("    float3 ret = float3(0.0);\n");
        if (perPixelBody.Length > 0)
        {
            // The block's engine locals are seeded from the pixel position, the way the CPU stage
            // gives it the current mesh point; its writes stay inside the block.
            builder.Append("    float ").Append(PresetExpressionTranspiler.LocalX).Append(" = centred.x;\n");
            builder.Append("    float ").Append(PresetExpressionTranspiler.LocalY).Append(" = centred.y;\n");
            builder.Append("    float ").Append(PresetExpressionTranspiler.LocalRadius).Append(" = rad;\n");
            builder.Append("    float ").Append(PresetExpressionTranspiler.LocalAngle).Append(" = ang;\n");
            builder.Append(perPixelBody);
        }

        EmitMutableUniforms(builder);
        EmitEntryStatements(builder, program);
        EmitEntryReturn(builder, "ret");
        builder.Append("}\n");
    }

    /// <summary>
    /// Emits the entry point header. SkSL takes the fragment coordinate as a parameter and returns the
    /// colour; GLSL has one <c>void main()</c> that writes an output variable and reads
    /// <c>gl_FragCoord</c>, whose origin is bottom-left, so the row is flipped to the engine's top-down
    /// convention.
    /// </summary>
    /// <param name="builder">Output.</param>
    private static void EmitEntryHeader(StringBuilder builder)
    {
        if (_glsl)
        {
            builder.Append("out vec4 orynivoColor;\n");
            builder.Append("void main() {\n");
            builder.Append("    vec2 fragCoord = vec2(gl_FragCoord.x, texsize.y - gl_FragCoord.y);\n");
            return;
        }

        builder.Append("half4 main(float2 fragCoord) {\n");
    }

    /// <summary>Emits the entry point's final return of the shader colour.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="expression">Colour expression.</param>
    private static void EmitEntryReturn(StringBuilder builder, string expression)
    {
        builder.Append("    ")
            .Append(_glsl ? "orynivoColor = " : "return ")
            .Append("half4(toColour(").Append(expression).Append("));\n");
    }

    /// <summary>
    /// Emits the warp entry point. It reproduces the CPU warp stage: the geometric motion transform
    /// builds the sampling position in the range minus one to one, the per-pixel block may move it,
    /// and the shader (or a direct frame sample) produces the colour.
    /// </summary>
    /// <param name="builder">Output.</param>
    /// <param name="program">Parsed warp shader body, or <see langword="null"/>.</param>
    /// <param name="perPixelBody">Emitted per-pixel statements.</param>
    /// <param name="meshUv">
    /// Whether the coordinate, original position, and polar pair arrive interpolated from a vertex
    /// stage. Then the body is a pure fragment stage: no transform is computed and the per-pixel block
    /// is not emitted, because the mesh already carries its result.
    /// </param>
    private static void EmitWarpMain(StringBuilder builder, ShaderNode? program, string perPixelBody, bool meshUv = false)
    {
        if (meshUv)
        {
            builder.Append("in vec2 vUv;\n");
            builder.Append("in vec2 vUvOrig;\n");
            builder.Append("in float vRad;\n");
            builder.Append("in float vAng;\n");
        }

        EmitEntryHeader(builder);
        EmitHueShader(builder);

        if (meshUv)
        {
            builder.Append("    float2 uv = vUv;\n");
            builder.Append("    float2 uv_orig = vUvOrig;\n");
            builder.Append("    float rad = vRad;\n");
            builder.Append("    float ang = vAng;\n");
        }
        else
        {
            builder.Append("    float2 _orynivo_uv_orig = (fragCoord - 0.5) / (_orynivo_size - 1.0);\n");
            builder.Append("    float2 _orynivo_normalized = (_orynivo_uv_orig * 2.0) - 1.0;\n");
            builder.Append("    float _orynivo_aspectX = texsize.y > texsize.x ? texsize.x / texsize.y : 1.0;\n");
            builder.Append("    float _orynivo_aspectY = texsize.x > texsize.y ? texsize.y / texsize.x : 1.0;\n");
            builder.Append("    float _orynivo_radius = length(_orynivo_normalized * float2(_orynivo_aspectX, _orynivo_aspectY));\n");
            builder.Append("    float _orynivo_radialZoom = (_orynivo_zoomExp != 1.0) ? pow(_orynivo_zoom, pow(_orynivo_zoomExp, _orynivo_radius * 2.0 - 1.0)) : _orynivo_zoom;\n");
            builder.Append("    float _orynivo_inverseZoom = 1.0 / max(0.01, _orynivo_radialZoom);\n");
            builder.Append("    float2 _orynivo_uv = float2(_orynivo_normalized.x * _orynivo_aspectX * 0.5 * _orynivo_inverseZoom + 0.5, _orynivo_normalized.y * _orynivo_aspectY * 0.5 * _orynivo_inverseZoom + 0.5);\n");
            builder.Append("    _orynivo_uv = (_orynivo_uv - _orynivo_centre) / _orynivo_stretch + _orynivo_centre;\n");
            builder.Append("    if (_orynivo_warp != 0.0) {\n");
            builder.Append("        float _orynivo_scaleInv = _orynivo_warpScale == 0.0 ? 1.0 : 1.0 / _orynivo_warpScale;\n");
            builder.Append("        float _orynivo_wf0 = 11.68 + 4.0 * cos(_orynivo_warpTime * 1.413 + 10.0);\n");
            builder.Append("        float _orynivo_wf1 = 8.77 + 3.0 * cos(_orynivo_warpTime * 1.113 + 7.0);\n");
            builder.Append("        float _orynivo_wf2 = 10.54 + 3.0 * cos(_orynivo_warpTime * 1.233 + 3.0);\n");
            builder.Append("        float _orynivo_wf3 = 11.49 + 4.0 * cos(_orynivo_warpTime * 0.933 + 5.0);\n");
            builder.Append("        float _orynivo_wa = _orynivo_warp * 0.0035;\n");
            builder.Append("        _orynivo_uv.x += _orynivo_wa * sin(_orynivo_warpTime * 0.333 + _orynivo_scaleInv * (_orynivo_normalized.x * _orynivo_wf0 - _orynivo_normalized.y * _orynivo_wf3)) + _orynivo_wa * cos(_orynivo_warpTime * 0.753 - _orynivo_scaleInv * (_orynivo_normalized.x * _orynivo_wf1 - _orynivo_normalized.y * _orynivo_wf2));\n");
            builder.Append("        _orynivo_uv.y += _orynivo_wa * cos(_orynivo_warpTime * 0.375 - _orynivo_scaleInv * (_orynivo_normalized.x * _orynivo_wf2 + _orynivo_normalized.y * _orynivo_wf1)) + _orynivo_wa * sin(_orynivo_warpTime * 0.825 + _orynivo_scaleInv * (_orynivo_normalized.x * _orynivo_wf0 + _orynivo_normalized.y * _orynivo_wf3));\n");
            builder.Append("    }\n");
            builder.Append("    float _orynivo_cos = cos(_orynivo_rotation);\n");
            builder.Append("    float _orynivo_sin = sin(_orynivo_rotation);\n");
            builder.Append("    float2 _orynivo_rotated = _orynivo_uv - _orynivo_centre;\n");
            builder.Append("    _orynivo_uv = float2((_orynivo_rotated.x * _orynivo_cos) - (_orynivo_rotated.y * _orynivo_sin), (_orynivo_rotated.x * _orynivo_sin) + (_orynivo_rotated.y * _orynivo_cos)) + _orynivo_centre;\n");
            builder.Append("    _orynivo_uv -= _orynivo_offset;\n");
            builder.Append("    _orynivo_uv = (_orynivo_uv - 0.5) / float2(_orynivo_aspectX, _orynivo_aspectY) + 0.5;\n");
            builder.Append("    float2 _orynivo_sample = (_orynivo_uv * 2.0) - 1.0;\n");
            builder.Append("    float _orynivo_x = _orynivo_sample.x;\n");
            builder.Append("    float _orynivo_y = _orynivo_sample.y;\n");
            builder.Append("    float _orynivo_rad = _orynivo_radius;\n");
            builder.Append("    float _orynivo_ang = atan(_orynivo_rotated.y, _orynivo_rotated.x);\n");
            builder.Append(perPixelBody);
        }

        if (program is null)
        {
            // A warp without a shader is the geometric warp: sample the previous frame, black outside.
            if (!meshUv)
                builder.Append("    float2 uv = (float2(_orynivo_x, _orynivo_y) * 0.5) + 0.5;\n");
            builder.Append("    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) { ")
                .Append(_glsl ? "orynivoColor = half4(0.0); return; " : "return half4(0.0); ")
                .Append("}\n");
            var sample = $"half4({SampleExpr("sampler_main", "(uv * (_orynivo_size - 1.0)) + 0.5")})";
            builder.Append("    ")
                .Append(_glsl ? $"orynivoColor = {sample}; return;" : $"return {sample};")
                .Append("\n");
            builder.Append("}\n");
            return;
        }

        if (!meshUv)
        {
            // The shader sees the sampling position in uv and the pixel position in uv_orig, and its
            // polar pair is derived from uv, exactly as the CPU binding does.
            builder.Append("    float2 uv = (float2(_orynivo_x, _orynivo_y) * 0.5) + 0.5;\n");
            builder.Append("    float2 uv_orig = _orynivo_uv_orig;\n");
            builder.Append("    float2 centred = (uv * 2.0) - 1.0;\n");
            builder.Append("    float rad = length(centred);\n");
            builder.Append("    float ang = atan(centred.y, centred.x);\n");
        }

        builder.Append("    float3 ret = float3(0.0);\n");
        EmitMutableUniforms(builder);
        EmitEntryStatements(builder, program);
        EmitEntryReturn(builder, "ret");
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

                builder.Append(indent)
                    .Append(_glsl ? "orynivoColor = " : "return ")
                    .Append("half4(toColour(")
                    .Append(statement.Left is null ? "ret" : EmitExpression(statement.Left))
                    .Append(_glsl ? ")); return;\n" : "));\n");
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
    private static string EmitCondition(ShaderNode expression)
    {
        // A comparison of vectors is component-wise, but the engine's truth test reads the first
        // component (ShaderValue.IsTrue), and SkSL rejects a bool vector as a condition. The
        // condition therefore compares the first components.
        if (expression.Kind == ShaderNodeKind.Binary && IsComparisonOperator(expression.Text))
        {
            var left = SkSL.Convert(EmitExpression(expression.Left!), TypeOf(expression.Left!), "float");
            var right = SkSL.Convert(EmitExpression(expression.Right!), TypeOf(expression.Right!), "float");
            return $"(({left}) {expression.Text} ({right}))";
        }

        var text = EmitExpression(expression);
        var type = TypeOf(expression);
        return type is not null && SkSL.ComponentCount(type) > 1
            ? $"(({text}).x != 0.0)"
            : $"({text} != 0.0)";
    }

    /// <summary>Reports whether an operator is a comparison.</summary>
    /// <param name="text">Operator text.</param>
    /// <returns><see langword="true"/> when the operator compares two values.</returns>
    private static bool IsComparisonOperator(string text) =>
        text is "<" or ">" or "<=" or ">=" or "==" or "!=";

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

    /// <summary>The declared parameter types of each helper, so a call coerces its arguments like HLSL.</summary>
    private static Dictionary<string, List<string>>? _helperParameters;

    /// <summary>
    /// The file-scope variables of the shader being translated. SkSL runtime effects have no mutable
    /// globals, so a helper that reads one takes it as a parameter instead.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _globals;

    /// <summary>The file-scope variables each helper reads, transitively through the helpers it calls.</summary>
    [ThreadStatic]
    private static Dictionary<string, List<string>>? _helperGlobals;

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
            case "distance":
            case "lum":
                return "float";
        }

        if (_helperReturns is not null && _helperReturns.TryGetValue(call.Text, out var helperReturn))
            return helperReturn;

        if (SkSL.IsVectorConstructor(call.Text))
            return SkSL.MapType(call.Text);

        // mul with a matrix yields a matrix for matrix-by-matrix and the vector type otherwise, so
        // the assignment coercion below does not collapse a matrix result to a float2.
        if (call.Text.Equals("mul", StringComparison.OrdinalIgnoreCase))
        {
            var left = call.Items.Count > 0 ? TypeOf(call.Items[0]) : null;
            var right = call.Items.Count > 1 ? TypeOf(call.Items[1]) : null;
            var leftMatrix = left is not null && SkSL.IsMatrixType(left);
            var rightMatrix = right is not null && SkSL.IsMatrixType(right);
            if (leftMatrix && rightMatrix)
                return left;
            if (leftMatrix)
                return right ?? "float2";
            if (rightMatrix)
                return left ?? "float2";
        }

        // An intrinsic that takes matching component counts is emitted with every argument narrowed to
        // the smallest vector count among them, so the result keeps that count. Reporting the widest
        // count here instead made a later operation skip the conversion it needed, which SkSL then
        // rejected as "float3 * float4".
        if (SkSL.DirectFunctions.Contains(call.Text) || call.Text is "saturate" or "lerp" or "atan2" or "mul")
        {
            var smallest = int.MaxValue;
            foreach (var argument in call.Items)
            {
                if (TypeOf(argument) is { } argumentType && SkSL.ComponentCount(argumentType) > 1)
                    smallest = Math.Min(smallest, SkSL.ComponentCount(argumentType));
            }

            return smallest == int.MaxValue ? "float" : SkSL.ComponentType(smallest);
        }

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
        // A comparison is component-wise in the engine, so its result keeps the operands' wider type;
        // the logical operators yield a scalar.
        "<" or ">" or "<=" or ">=" or "==" or "!=" => SkSL.Wider(TypeOf(expression.Left!), TypeOf(expression.Right!)),
        "&&" or "||" => "float",
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

    /// <summary>
    /// Returns whether a shader samples a blur level (<c>sampler_blur1</c>-<c>sampler_blur3</c> or the
    /// <c>GetBlur1</c>-<c>GetBlur3</c> macros). MilkDrop keeps the warp's blur chain one generation
    /// older than <c>sampler_main</c>, which only the interpreter and the OpenGL pipeline reproduce,
    /// so the renderer builds the retained chain and keeps the Skia warp pass away from such a shader.
    /// </summary>
    /// <param name="node">Shader body to inspect.</param>
    /// <returns><see langword="true"/> when the shader reads a blur sampler.</returns>
    internal static bool UsesBlur(ShaderNode node)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        CollectSamplers(node, names);
        foreach (var name in names)
        {
            if (ShaderSamplerName.Parse(name).BaseName.StartsWith("blur", StringComparison.Ordinal))
                return true;
        }

        return HasBlurCall(node);
    }

    /// <summary>Reports whether a shader calls one of the <c>GetBlur1</c>-<c>GetBlur3</c> helpers.</summary>
    /// <param name="node">Node to walk.</param>
    /// <returns><see langword="true"/> when a blur helper is called.</returns>
    private static bool HasBlurCall(ShaderNode node)
    {
        if (node.Kind == ShaderNodeKind.Call && node.Text.ToLowerInvariant() is "getblur1" or "getblur2" or "getblur3")
            return true;

        foreach (var child in node.Items)
        {
            if (HasBlurCall(child))
                return true;
        }

        foreach (var parameter in node.ParameterList)
        {
            if (HasBlurCall(parameter))
                return true;
        }

        if (node.Kind != ShaderNodeKind.Call && node.Left is not null && HasBlurCall(node.Left))
            return true;
        if (node.Right is not null && HasBlurCall(node.Right))
            return true;
        return node.Third is not null && HasBlurCall(node.Third);
    }

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
    /// Adds the file-scope variables a helper reads, and the ones its callees read, so the helper can
    /// take them as parameters.
    /// </summary>
    /// <param name="node">Node to walk.</param>
    /// <param name="needed">Set to fill.</param>
    private static void CollectHelperGlobals(ShaderNode node, SortedSet<string> needed)
    {
        if (node.Kind == ShaderNodeKind.Identifier && _globals is not null && _globals.Contains(node.Text))
            needed.Add(node.Text);
        if (node.Kind == ShaderNodeKind.Call &&
            _helperGlobals is not null &&
            _helperGlobals.TryGetValue(node.Text, out var callee))
        {
            needed.UnionWith(callee);
        }

        foreach (var child in node.Items)
            CollectHelperGlobals(child, needed);
        foreach (var parameter in node.ParameterList)
            CollectHelperGlobals(parameter, needed);

        if (node.Kind != ShaderNodeKind.Call && node.Left is not null)
            CollectHelperGlobals(node.Left, needed);
        if (node.Right is not null)
            CollectHelperGlobals(node.Right, needed);
        if (node.Third is not null)
            CollectHelperGlobals(node.Third, needed);
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

        // A file-scope variable the helper reads is passed in, because SkSL runtime effects have no
        // mutable globals and the helper is emitted before main declares them.
        if (_helperGlobals is not null && _helperGlobals.TryGetValue(function.Text, out var globals))
        {
            foreach (var name in globals)
            {
                var type = _types is not null && _types.TryGetValue(name, out var globalType) ? globalType : "float";
                parameters.Add($"{type} {SkSL.SafeName(name)}");
            }
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
        var common = leftType is not null && rightType is not null ? SkSL.Wider(leftType, rightType) : null;
        if (!isAssignment &&
            leftType is not null && rightType is not null &&
            SkSL.ComponentCount(leftType) != SkSL.ComponentCount(rightType) &&
            common is not null)
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
            "/" => $"orynivoSafeDiv({left}, {right})",
            "%" => $"mod({left}, {right})",
            "=" => EmitAssignment(left, right, expression),
            "+=" => $"{left} += {right}",
            "-=" => $"{left} -= {right}",
            "*=" => $"{left} *= {right}",
            "/=" => $"{left} = orynivoSafeDiv({left}, {right})",
            "==" => ComparisonResult("==", left, right, common),
            "!=" => ComparisonResult("!=", left, right, common),
            "<" => ComparisonResult("<", left, right, common),
            ">" => ComparisonResult(">", left, right, common),
            "<=" => ComparisonResult("<=", left, right, common),
            ">=" => ComparisonResult(">=", left, right, common),
            "&&" => $"((({left} != 0.0) && ({right} != 0.0)) ? 1.0 : 0.0)",
            "||" => $"((({left} != 0.0) || ({right} != 0.0)) ? 1.0 : 0.0)",
            "," => $"({left}, {right})",
            _ => throw new PresetExpressionException(
                $"The binary operator '{expression.Text}' has no SkSL translation.",
                expression.Position)
        };
    }

    /// <summary>
    /// Emits a comparison that yields one or zero. SkSL rejects a bool vector as a ternary condition
    /// and as a constructor argument, so a comparison of vectors is emitted component-wise with
    /// <c>step</c> and <c>sign</c>, which is the zero-or-one vector
    /// <see cref="ShaderRuntime.Compare"/> produces on the CPU.
    /// </summary>
    /// <param name="op">Comparison operator.</param>
    /// <param name="left">Left operand text.</param>
    /// <param name="right">Right operand text.</param>
    /// <param name="common">The operands' widened type, or nothing.</param>
    /// <returns>The comparison text.</returns>
    private static string ComparisonResult(string op, string left, string right, string? common)
    {
        if (common is null || SkSL.ComponentCount(common) <= 1)
            return $"(({left} {op} {right}) ? 1.0 : 0.0)";

        return op switch
        {
            ">=" => $"step({right}, {left})",
            "<=" => $"step({left}, {right})",
            ">" => $"(1.0 - step({left}, {right}))",
            "<" => $"(1.0 - step({right}, {left}))",
            "==" => $"(1.0 - abs(sign({left} - {right})))",
            _ => $"abs(sign({left} - {right}))"
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
        _glsl
            // GLSL samples with normalised coordinates, so the shader's own coordinate is used as is.
            ? coordinate
            : IsFrameSampler(sampler)
                ? $"({coordinate} * ({TexSizeUniform(sampler)}.xy - 1.0) + 0.5)"
                : $"({coordinate} * {TexSizeUniform(sampler)}.xy)";

    /// <summary>Reports whether a sampler reads a frame rather than a generated texture.</summary>
    /// <param name="sampler">Emitted sampler name.</param>
    /// <returns><see langword="true"/> when the sampler is not one of the generated textures.</returns>
    private static bool IsFrameSampler(string sampler) => !VisualizerTextureBank.TryResolve(sampler, out _);

    /// <summary>
    /// Converts a pixel-centre coordinate into the form the active dialect samples with: Skia's
    /// <c>eval</c> takes pixel coordinates, GLSL's <c>texture</c> takes normalised ones.
    /// </summary>
    /// <param name="pixel">Pixel-centre coordinate.</param>
    /// <returns>The coordinate text.</returns>
    private static string PixelCoordinate(string pixel) =>
        _glsl ? $"(({pixel} + 0.5) / texsize.xy)" : $"({pixel} + 0.5)";

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
        // smallest vector count among them; widening a scalar is harmless because it is uniform. A
        // matrix argument is left alone: SkSL spells it mat2 and a component count cannot describe it.
        var hasMatrix = false;
        foreach (var argument in call.Items)
        {
            if (TypeOf(argument) is { } matrixType && SkSL.IsMatrixType(matrixType))
            {
                hasMatrix = true;
                break;
            }
        }

        if (!hasMatrix &&
            (SkSL.DirectFunctions.Contains(name) || name is "saturate" or "lerp" or "atan2" or "mul" or "lum"))
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
                return $"float4({SampleExpr(arguments[0], SamplerCoordinate(arguments[0], Coordinate(call, arguments[1], 1)))})";
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
                {
                    // Milkdrop's GetPixel(uv) macro is tex2D(sampler_main, uv). Only the legacy
                    // two-scalar extension uses integer texel coordinates.
                    if (arguments.Count == 1)
                        return $"float4({SampleExpr(MainSampler, SamplerCoordinate(MainSampler, Coordinate(call, arguments[0], 0)))}).rgb";

                    if (arguments.Count < 2)
                        throw new PresetExpressionException("GetPixel needs a coordinate.", call.Position);
                    var pixel = $"float2(float(int({arguments[0]})), float(int({arguments[1]})))";
                    return $"float4({SampleExpr(MainSampler, PixelCoordinate(pixel))}).rgb";
                }
            case "getblur1":
                return $"(float4({SampleExpr("sampler_blur1", SamplerCoordinate("sampler_blur1", Coordinate(call, arguments[0], 0)))}).rgb{(_glsl ? " * (blur1_max - blur1_min) + blur1_min" : "")})";
            case "getblur2":
                return $"(float4({SampleExpr("sampler_blur2", SamplerCoordinate("sampler_blur2", Coordinate(call, arguments[0], 0)))}).rgb{(_glsl ? " * (blur2_max - blur2_min) + blur2_min" : "")})";
            case "getblur3":
                return $"(float4({SampleExpr("sampler_blur3", SamplerCoordinate("sampler_blur3", Coordinate(call, arguments[0], 0)))}).rgb{(_glsl ? " * (blur3_max - blur3_min) + blur3_min" : "")})";
            case "saturate":
                return $"clamp({arguments[0]}, 0.0, 1.0)";
            case "atan2":
                return $"atan({arguments[0]}, {arguments[1]})";
            case "lerp":
                return $"mix({arguments[0]}, {arguments[1]}, {arguments[2]})";
            case "float2x2":
            case "half2x2":
            case "double2x2":
                return EmitMatrix(call, arguments, 2);
            case "float3x3":
            case "half3x3":
            case "double3x3":
                return EmitMatrix(call, arguments, 3);
            case "float4x4":
            case "half4x4":
            case "double4x4":
                return EmitMatrix(call, arguments, 4);
            case "mul":
                return arguments.Count >= 2 ? $"({arguments[0]} * {arguments[1]})" : arguments[0];
            case "lum":
                // Milkdrop's luminance helper is `dot(x, float3(0.32, 0.49, 0.29))` (include.fx), not the
                // conventional Rec. 601 weights. The implicit float3 weight vector is a second operand,
                // so the value is converted to float3 here rather than by the argument loop above.
                return $"dot({SkSL.Convert(arguments[0], call.Items.Count > 0 ? TypeOf(call.Items[0]) : null, "float3")}, float3(0.32, 0.49, 0.29))";
        }

        // A call to a function the shader defines itself keeps its name. The file-scope variables the
        // helper needs are appended, matching the parameters EmitFunction added.
        if (_helperReturns is not null && _helperReturns.ContainsKey(name))
        {
            // HLSL coerces each argument to the parameter's declared type; SkSL is strict, so a helper
            // that takes a float must not be handed the float3 a preset passes it.
            if (_helperParameters is not null && _helperParameters.TryGetValue(name, out var parameterTypes))
            {
                for (var index = 0; index < arguments.Count && index < parameterTypes.Count; index++)
                    arguments[index] = SkSL.Convert(arguments[index], TypeOf(call.Items[index]), parameterTypes[index]);
            }

            if (_helperGlobals is not null && _helperGlobals.TryGetValue(name, out var globals))
                arguments.AddRange(globals.Select(SkSL.SafeName));
            return $"{name}({string.Join(", ", arguments)})";
        }

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

    /// <summary>
    /// Emits a <c>floatNxN</c> constructor as a SkSL matrix. HLSL fills a matrix row-major from a
    /// single vector, while SkSL's matrix constructors have no four-component form, so the components
    /// of a vector argument are spread explicitly.
    /// </summary>
    /// <param name="call">Constructor call node.</param>
    /// <param name="arguments">Already emitted arguments.</param>
    /// <param name="dimension">Matrix dimension.</param>
    /// <returns>The constructor text.</returns>
    private static string EmitMatrix(ShaderNode call, List<string> arguments, int dimension)
    {
        var size = dimension * dimension;
        if (arguments.Count == 1)
        {
            var type = call.Items.Count > 0 ? TypeOf(call.Items[0]) : null;
            var count = type is null ? 1 : SkSL.ComponentCount(type);
            if (count is >= 2 and <= 4)
            {
                var components = new List<string>(size);
                for (var index = 0; index < size; index++)
                    components.Add(index < count ? $"{arguments[0]}.{"xyzw"[index]}" : "0.0");
                return $"mat{dimension}({string.Join(", ", components)})";
            }
        }

        return $"mat{dimension}({string.Join(", ", arguments)})";
    }
}
