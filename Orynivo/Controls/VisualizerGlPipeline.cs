using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Orynivo.Visualization;

namespace Orynivo.Controls;

/// <summary>
/// The GPU frame pipeline for the visualizer: the warp as a mesh draw, the blur chain, the decay,
/// video echo, centre darkening, borders, gamma, and the overlay composite, all on the GPU. The CPU
/// supplies the per-frame block's results (the mesh and <see cref="VisualizerFrameParameters"/>) and
/// the overlay, which stays a vector drawing.
/// </summary>
/// <remarks>
/// This is roadmap 40f step 3. Every texture holds the frame bottom-up, which is OpenGL's natural
/// orientation, so both the overlay upload and the mesh warp convert between that and the engine's
/// top-down convention. <see cref="GlInterface"/> exposes only scalar uniforms, so a vector uniform
/// is set component by component. The pipeline runs on the control's thread, because the GL context
/// belongs to the control.
/// </remarks>
internal sealed class VisualizerGlPipeline
{
    private const int GlArrayBuffer = 0x8892;
    private const int GlElementArrayBuffer = 0x8893;
    private const int GlStaticDraw = 0x88E4;
    private const int GlDynamicDraw = 0x88E8;
    private const int GlFloat = 0x1406;
    private const int GlUnsignedShort = 0x1403;
    private const int GlUnsignedByte = 0x1401;
    private const int GlTriangles = 0x0004;
    private const int GlTriangleStrip = 0x0005;
    private const int GlTexture2D = 0x0DE1;
    private const int GlTexture0 = 0x84C0;
    private const int GlTexture1 = 0x84C1;
    private const int GlTexture2 = 0x84C2;
    private const int GlTexture3 = 0x84C3;
    private const int GlTexture4 = 0x84C4;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlLinear = 0x2601;
    private const int GlNearest = 0x2600;
    private const int GlClampToEdge = 0x812F;
    private const int GlRepeat = 0x2901;
    private const int GlRgba = 0x1908;
    private const int GlRgba8 = 0x8058;
    private const int GlRgba16f = 0x881A;
    private const int GlVertexShader = 0x8B31;
    private const int GlFragmentShader = 0x8B30;
    private const int GlFramebuffer = 0x8D40;
    private const int GlColorAttachment0 = 0x8CE0;
    private const int GlFramebufferComplete = 0x8CD5;
    private const int GlColorBufferBit = 0x4000;

    /// <summary>Floats per mesh vertex: the position pair and the nine motion values.</summary>
    private const int FloatsPerVertex = 2 + PresetRenderer.MeshValues;

    private const string QuadVertexSource = """
        #version 300 es
        precision highp float;
        layout(location = 0) in vec2 aPosition;
        out vec2 vUv;
        void main()
        {
            vUv = aPosition * 0.5 + 0.5;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string WarpVertexSource = """
        #version 300 es
        precision highp float;
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec3 aMotion0;
        layout(location = 2) in vec3 aMotion1;
        layout(location = 3) in vec3 aMotion2;
        layout(location = 4) in float aWarp;
        out vec2 vUv;
        out vec2 vUvOrig;
        out float vRad;
        out float vAng;
        uniform float uFrameWidth;
        uniform float uFrameHeight;
        uniform float uNeedsRadius;
        uniform float uWarpTime;
        uniform float uWarpScale;
        void main()
        {
            float zoom = max(0.01, aMotion0.x);
            float zoomExp = aMotion0.y;
            float rotation = aMotion0.z;
            float cx = aMotion1.x;
            float cy = aMotion1.y;
            float dx = aMotion1.z;
            float dy = aMotion2.x;
            float sx = aMotion2.y;
            float sy = aMotion2.z;

            // The reference warp vertex shader's arithmetic, applied at the vertex so the resulting
            // texture coordinate is interpolated across the quad. aPosition is the vertex position in
            // minus-one-to-one space.
            float aspectX = uFrameHeight > uFrameWidth ? uFrameWidth / uFrameHeight : 1.0;
            float aspectY = uFrameWidth > uFrameHeight ? uFrameHeight / uFrameWidth : 1.0;
            float radius = uNeedsRadius > 0.5 ? length(aPosition * vec2(aspectX, aspectY)) : 0.0;
            float radialZoom = (uNeedsRadius > 0.5 && zoomExp != 1.0)
                ? pow(zoom, pow(zoomExp, radius * 2.0 - 1.0))
                : zoom;
            float inverseZoom = 1.0 / max(0.01, radialZoom);

            float u = aPosition.x * aspectX * 0.5 * inverseZoom + 0.5;
            float v = aPosition.y * aspectY * 0.5 * inverseZoom + 0.5;
            u = (u - cx) / max(abs(sx), 0.0001) * sign(sx) + cx;
            v = (v - cy) / max(abs(sy), 0.0001) * sign(sy) + cy;

            if (aWarp != 0.0)
            {
                float scaleInverse = uWarpScale == 0.0 ? 1.0 : 1.0 / uWarpScale;
                float factor0 = 11.68 + 4.0 * cos(uWarpTime * 1.413 + 10.0);
                float factor1 = 8.77 + 3.0 * cos(uWarpTime * 1.113 + 7.0);
                float factor2 = 10.54 + 3.0 * cos(uWarpTime * 1.233 + 3.0);
                float factor3 = 11.49 + 4.0 * cos(uWarpTime * 0.933 + 5.0);
                float amount = aWarp * 0.0035;
                u += amount * sin(uWarpTime * 0.333 + scaleInverse * (aPosition.x * factor0 - aPosition.y * factor3))
                   + amount * cos(uWarpTime * 0.753 - scaleInverse * (aPosition.x * factor1 - aPosition.y * factor2));
                v += amount * cos(uWarpTime * 0.375 - scaleInverse * (aPosition.x * factor2 + aPosition.y * factor1))
                   + amount * sin(uWarpTime * 0.825 + scaleInverse * (aPosition.x * factor0 + aPosition.y * factor3));
            }

            float cosine = cos(rotation);
            float sine = sin(rotation);
            float rotatedU = u - cx;
            float rotatedV = v - cy;
            u = rotatedU * cosine - rotatedV * sine + cx;
            v = rotatedU * sine + rotatedV * cosine + cy;

            u -= dx;
            v -= dy;
            u = (u - 0.5) / aspectX + 0.5;
            v = (v - 0.5) / aspectY + 0.5;
            vUv = vec2(u, v);
            vUvOrig = aPosition * 0.5 + 0.5;
            vRad = radius;
            vAng = -atan(aPosition.y * aspectY, aPosition.x * aspectX);
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    /// <summary>
    /// Samples the bottom-up feedback texture at the coordinate interpolated by the mesh vertex
    /// shader. Both the mesh position and texture use the GL orientation; flipping v here would
    /// vertically reflect the feedback on every frame, even for an identity transform.
    /// </summary>
    private const string WarpFragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec2 vUv;
        out vec4 fragColor;
        uniform sampler2D uSource;
        uniform float uDecay;
        void main()
        {
            vec2 uv = vUv;

            // The bound sampler implements the preset's repeat/clamp mode.
            // The decay multiplies the sampled colour, exactly like the reference warp fragment
            // shader's frag_COLOR, so the blur passes that follow see the faded frame.
            vec4 colour = texture(uSource, uv) * uDecay;
            fragColor = clamp(colour, 0.0, 1.0);
        }
        """;

    /// <summary>The nine-tap clamped box blur, translated from <c>PixelBuffer.Blur</c>.</summary>
    private const string BlurFragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec2 vUv;
        out vec4 fragColor;
        uniform sampler2D uSource;
        uniform float uTexelX;
        uniform float uTexelY;
        void main()
        {
            vec4 sum = vec4(0.0);
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    sum += texture(uSource, vUv + vec2(float(offsetX) * uTexelX, float(offsetY) * uTexelY));
                }
            }

            fragColor = clamp(sum / 9.0, 0.0, 1.0);

        }
        """;

    /// <summary>
    /// The reference implementation's weighted blur, which the shader blur levels read: a long
    /// horizontal pass with eight weighted taps or a short vertical one with four. The weights are
    /// folded into four distances per axis exactly as the reference computes them.
    /// </summary>
    private const string ShaderBlurFragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec2 vUv;
        out vec4 fragColor;
        uniform sampler2D uSource;
        uniform float uTexelX;
        uniform float uTexelY;
        uniform float uHorizontal;
        uniform float uScale;
        uniform float uBias;
        uniform float uEdge;
        void main()
        {
            if (uHorizontal > 0.5)
            {
                float w0 = 7.8;
                float w1 = 6.4;
                float w2 = 3.1;
                float w3 = 1.0;
                float d0 = 0.974359;
                float d1 = 2.90625;
                float d2 = 4.77419;
                float d3 = 6.6;
                float divisor = 0.5 / 18.3;
                vec2 axis = vec2(uTexelX, 0.0);
                vec3 sum = (texture(uSource, vUv + axis * d0).xyz + texture(uSource, vUv - axis * d0).xyz) * w0
                    + (texture(uSource, vUv + axis * d1).xyz + texture(uSource, vUv - axis * d1).xyz) * w1
                    + (texture(uSource, vUv + axis * d2).xyz + texture(uSource, vUv - axis * d2).xyz) * w2
                    + (texture(uSource, vUv + axis * d3).xyz + texture(uSource, vUv - axis * d3).xyz) * w3;
                fragColor = clamp(vec4(sum * divisor * uScale + uBias, 1.0), 0.0, 1.0);
            }
            else
            {
                float v0 = 14.2;
                float v1 = 4.1;
                float e0 = 0.901408;
                float e1 = 2.487805;
                float divisor = 1.0 / 36.6;
                vec2 axis = vec2(0.0, uTexelY);
                vec3 sum = (texture(uSource, vUv + axis * e0).xyz + texture(uSource, vUv - axis * e0).xyz) * v0
                    + (texture(uSource, vUv + axis * e1).xyz + texture(uSource, vUv - axis * e1).xyz) * v1;
                float edge = min(min(vUv.x, vUv.y), 1.0 - max(vUv.x, vUv.y));
                float attenuation = 1.0 - uEdge + uEdge * clamp(sqrt(max(0.0, edge)) * 5.0, 0.0, 1.0);
                fragColor = clamp(vec4(sum * divisor * attenuation, 1.0), 0.0, 1.0);
            }
        }
        """;

    /// <summary>The post-processing chain in the CPU order: decay, video echo, centre darkening, borders,
    /// gamma, then the additive overlay composite. Every step is a function of the same input frame,
    /// so they share one pass.
    /// </summary>
    private const string PostFragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec2 vUv;
        out vec4 fragColor;
        uniform sampler2D uSource;
        uniform sampler2D uOverlay;
        uniform float uDecay;
        uniform float uDisplayOnly;
        uniform float uHueTime;
        uniform float uHue0;
        uniform float uHue1;
        uniform float uHue2;
        uniform float uHue3;
        uniform float uEchoZoom;
        uniform float uEchoAlpha;
        uniform float uEchoOrientation;
        uniform float uDarken;
        uniform float uGamma;
        uniform float uOuterInset;
        uniform float uOuterThickness;
        uniform float uOuterR;
        uniform float uOuterG;
        uniform float uOuterB;
        uniform float uOuterA;
        uniform float uInnerInset;
        uniform float uInnerThickness;
        uniform float uInnerR;
        uniform float uInnerG;
        uniform float uInnerB;
        uniform float uInnerA;
        uniform float uFrameWidth;
        uniform float uFrameHeight;
        uniform float uSmaller;

        // The reference's animated hue shade: three sine channels normalised so their maximum is one.
        vec3 hueShade(float corner)
        {
            float r = 0.6 + 0.3 * sin(uHueTime * 0.0143 + 3.0 + corner * 21.0 + uHue3);
            float g = 0.6 + 0.3 * sin(uHueTime * 0.0107 + 1.0 + corner * 13.0 + uHue1);
            float b = 0.6 + 0.3 * sin(uHueTime * 0.0129 + 6.0 + corner * 9.0 + uHue2);
            float m = max(r, max(g, b));
            m = abs(m) < 1e-6 ? 1.0 : m;
            return vec3(0.5 + 0.5 * r / m, 0.5 + 0.5 * g / m, 0.5 + 0.5 * b / m);
        }

        void main()
        {
            vec4 colour = texture(uSource, vUv);
            colour *= uDecay;

            // The overlay is composited before the centre darkening and the border, so those later
            // passes cover it, exactly as the reference draws the shapes and waves before them.
            if (uDisplayOnly < 0.5) {
            vec4 overlay = texture(uOverlay, vUv);
            colour.rgb = colour.rgb * (1.0 - overlay.a) + overlay.rgb;

            if (uDarken > 0.0)
            {
                vec2 normalized = (vUv * 2.0) - 1.0;
                float distance = length(normalized);
                colour.rgb *= 1.0 - (uDarken * clamp(1.0 - distance, 0.0, 1.0));
            }

            // The border bands are rings measured from the frame's edge; the geometry is symmetric in
            // both axes, so the bottom-up coordinate gives the same ring as the CPU's top-down one.
            vec2 pixels = vUv * vec2(uFrameWidth, uFrameHeight);
            vec4 outer = vec4(uOuterR, uOuterG, uOuterB, uOuterA);
            vec4 inner = vec4(uInnerR, uInnerG, uInnerB, uInnerA);
            float edge = min(min(pixels.x, pixels.y), min(uFrameWidth - pixels.x, uFrameHeight - pixels.y));
            if (outer.a > 0.0 && edge < uSmaller * (uOuterInset + uOuterThickness) && edge >= uSmaller * uOuterInset)
                colour = mix(colour, vec4(outer.rgb, colour.a), outer.a);
            if (inner.a > 0.0 && edge < uSmaller * (uInnerInset + uInnerThickness) && edge >= uSmaller * uInnerInset)
                colour = mix(colour, vec4(inner.rgb, colour.a), inner.a);

            }
            // The legacy video echo and gamma adjustment are the reference's final composite when the
            // preset has no comp shader, so they run after the border.
            if (uEchoAlpha > 0.0)
            {
                vec2 echoUv = ((vUv - 0.5) / uEchoZoom) + 0.5;
                if (uEchoOrientation > 0.5 && uEchoOrientation < 2.5)
                    echoUv.x = 1.0 - echoUv.x;
                if (uEchoOrientation > 1.5)
                    echoUv.y = 1.0 - echoUv.y;

                if (echoUv.x >= 0.0 && echoUv.x <= 1.0 && echoUv.y >= 0.0 && echoUv.y <= 1.0)
                {
                    vec4 echoed = texture(uSource, echoUv);
                    colour = mix(colour, echoed, uEchoAlpha);
                }
            }

            if (uDisplayOnly > 0.5) {
                // The legacy final composite tints the finished frame with its animated hue shade.
                vec3 shade = mix(mix(hueShade(0.0), hueShade(1.0), vUv.x),
                                 mix(hueShade(2.0), hueShade(3.0), vUv.x), 1.0 - vUv.y);
                colour.rgb *= shade;
            }

            if (uGamma != 1.0)
                colour.rgb *= uGamma;

            fragColor = clamp(colour, 0.0, 1.0);
        }
        """;

    /// <summary>Allocates or deletes native sampler objects.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SamplerObjects(int count, ref int sampler);
    /// <summary>Binds a sampler to a texture unit.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BindSampler(int unit, int sampler);
    /// <summary>Sets a native sampler parameter.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SamplerParameter(int sampler, int parameter, int value);
    private readonly int[] _samplerStates = new int[4];
    private BindSampler? _bindSampler;
    private SamplerObjects? _deleteSamplers;

    /// <summary>Creates independent filter/wrap objects available in GL 3.3 and ES 3.0.</summary>
    private void InitializeSamplers(GlInterface gl)
    {
        var generate = Marshal.GetDelegateForFunctionPointer<SamplerObjects>(gl.GetProcAddress("glGenSamplers"));
        _deleteSamplers = Marshal.GetDelegateForFunctionPointer<SamplerObjects>(gl.GetProcAddress("glDeleteSamplers"));
        _bindSampler = Marshal.GetDelegateForFunctionPointer<BindSampler>(gl.GetProcAddress("glBindSampler"));
        var parameter = Marshal.GetDelegateForFunctionPointer<SamplerParameter>(gl.GetProcAddress("glSamplerParameteri"));
        for (var i = 0; i < 4; i++)
        {
            generate(1, ref _samplerStates[i]);
            parameter(_samplerStates[i], GlTextureMinFilter, (i & 1) != 0 ? GlNearest : GlLinear);
            parameter(_samplerStates[i], GlTextureMagFilter, (i & 1) != 0 ? GlNearest : GlLinear);
            parameter(_samplerStates[i], GlTextureWrapS, (i & 2) != 0 ? GlRepeat : GlClampToEdge);
            parameter(_samplerStates[i], GlTextureWrapT, (i & 2) != 0 ? GlRepeat : GlClampToEdge);
        }
    }

    /// <summary>Releases custom sampler bindings before fixed pipeline passes or returning to Avalonia.</summary>
    private void ClearSamplerBindings()
    {
        for (var i = 0; i < 16; i++) _bindSampler?.Invoke(i, 0);
    }

    private readonly float[] _vertices = new float[(PresetRenderer.MeshGridX + 1) * (PresetRenderer.MeshGridY + 1) * FloatsPerVertex];
    private readonly ushort[] _indices = BuildIndices();
    private byte[] _overlayRgba = [];

    /// <summary>The full-screen quad, as a triangle strip.</summary>
    private static readonly float[] QuadVertices = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];

    private int _quadProgram;
    private int _warpProgram;
    private int _blurProgram;
    private int _postProgram;
    private int _quadVertexArray;
    private int _quadVertexBuffer;
    private int _meshVertexArray;
    private int _meshVertexBuffer;
    private int _meshIndexBuffer;
    private int _meshIndexCount;
    private int _feedbackTexture;
    private int _feedbackFramebuffer;
    private readonly int[] _pingTexture = new int[2];
    private readonly int[] _pingFramebuffer = new int[2];
    private int _overlayTexture;
    private int _width;
    private int _height;
    private bool _ready;
    private int _frame;
    /// <summary>
    /// Forces the sixteen-bit frame format even when the context does not advertise it. The headless
    /// harness sets this so the float path can be exercised on a desktop context; the player never does.
    /// </summary>
    internal static bool ForceHalfFloat { get; set; }

    /// <summary>Whether the frame textures carry sixteen-bit floats instead of eight-bit colour.</summary>
    private bool _halfFloat;


    private Dictionary<string, int> _warpUniforms = new(StringComparer.Ordinal);
    private Dictionary<string, int> _blurUniforms = new(StringComparer.Ordinal);
    private Dictionary<string, int> _postUniforms = new(StringComparer.Ordinal);
    private int _quadTextureUniform = -1;

    /// <summary>The emitted warp shader program, or zero when the preset has none or it failed.</summary>
    private int _warpShaderProgram;

    /// <summary>The emitted comp shader program, or zero when the preset has none or it failed.</summary>
    private int _compShaderProgram;

    /// <summary>The GLSL source each shader program was built from, so a preset switch recompiles.</summary>
    private string? _warpShaderSource;
    private string? _compShaderSource;
    private string[] _warpSamplerNames = [];
    private string[] _compSamplerNames = [];

    /// <summary>Finds every declared GLSL sampler, including qualifier aliases outside the built-in set.</summary>
    private static string[] SamplerNames(string? source) => source is null ? [] :
        System.Text.RegularExpressions.Regex.Matches(source, @"uniform\s+sampler2D\s+(\w+)")
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The uniform locations of the emitted shader programs.</summary>
    private Dictionary<string, int> _warpShaderUniforms = new(StringComparer.Ordinal);
    private Dictionary<string, int> _compShaderUniforms = new(StringComparer.Ordinal);

    /// <summary>The comp pass output, which is presented while the feedback stays the pre-comp frame.</summary>
    private int _compTexture;
    private int _compFramebuffer;

    /// <summary>The previous frame's pre-comp composite, for the comp shader's <c>sampler_pc_main</c>.</summary>
    private int _previousTexture;
    private int _previousFramebuffer;

    /// <summary>Blurred copies of the comp input, for the comp shader's <c>sampler_blur1</c>-<c>3</c>.</summary>
    private readonly int[] _shaderBlurTexture = new int[3];
    private readonly int[] _shaderBlurFramebuffer = new int[3];

    /// <summary>The reference weighted blur program the shader blur levels are built with.</summary>
    private int _shaderBlurProgram;
    private Dictionary<string, int> _shaderBlurUniforms = new(StringComparer.Ordinal);

    /// <summary>The generated noise and volume textures a shader samples, by sampler name.</summary>
    private readonly VisualizerTextureBank _textureBank = new();
    private readonly Dictionary<string, int> _samplerTextures = new(StringComparer.Ordinal);

    /// <summary>Scratch target for the two passes of one shader blur level, one per level size.</summary>
    private readonly int[] _shaderBlurScratchTexture = new int[3];
    private readonly int[] _shaderBlurScratchFramebuffer = new int[3];

    /// <summary>
    /// The size of each shader blur level, which the reference implementation halves per level: blur1
    /// is a quarter of the frame, blur2 an eighth, and blur3 a sixteenth.
    /// </summary>
    /// <param name="frameWidth">Frame width.</param>
    /// <param name="frameHeight">Frame height.</param>
    /// <param name="level">Level index, zero to two.</param>
    /// <returns>The level's width and height.</returns>
    private static (int Width, int Height) ShaderBlurSize(int frameWidth, int frameHeight, int level)
    {
        var divisor = 4 << level;
        return (Math.Max(16, frameWidth / divisor), Math.Max(16, frameHeight / divisor));
    }

    /// <summary>Gets why the last emitted shader failed to build, or <see langword="null"/>.</summary>
    public string? ShaderError { get; private set; }

    /// <summary>
    /// Publishes the emitted GLSL for the current preset. The sources are compiled on the next frame,
    /// because the GL context is only current inside the render callback. A source that fails to build
    /// leaves that stage on the fixed pipeline instead of losing the frame.
    /// </summary>
    /// <param name="warpSource">GLSL for the warp shader, or <see langword="null"/>.</param>
    /// <param name="compSource">GLSL for the comp shader, or <see langword="null"/>.</param>
    public void SetShaders(string? warpSource, string? compSource)
    {
        if (!string.Equals(_warpShaderSource, warpSource, StringComparison.Ordinal) ||
            !string.Equals(_compShaderSource, compSource, StringComparison.Ordinal))
        {
            _warpShaderSource = warpSource;
            _compShaderSource = compSource;
            _warpSamplerNames = SamplerNames(warpSource);
            _compSamplerNames = SamplerNames(compSource);
            _shadersDirty = true;
        }
    }

    private bool _shadersDirty;

    /// <summary>Gets why the pipeline could not be built or last failed, or <see langword="null"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the number of frames the pipeline has drawn.</summary>
    public int Frames { get; private set; }

    /// <summary>
    /// Gets a one-shot description of the first drawn frame, so a black picture can be told apart
    /// from a broken overlay, mesh, or GL state.
    /// </summary>
    public string? Diagnostics { get; private set; }

    /// <summary>Gets the texture holding the last finished frame, for presentation.</summary>
    public int OutputTexture => _outputTexture != 0 ? _outputTexture : _feedbackTexture;

    /// <summary>The texture last presented: the comp output when a comp shader ran, else the feedback.</summary>
    private int _outputTexture;

    /// <summary>Creates the GL objects.</summary>
    /// <param name="gl">GL interface.</param>
    /// <returns><see langword="true"/> when the pipeline is usable.</returns>
    public bool Init(GlInterface gl)
    {
        try
        {
            _quadProgram = BuildProgram(gl, QuadVertexSource, BlurFragmentSource, out _);
            _warpProgram = BuildProgram(gl, WarpVertexSource, WarpFragmentSource, out _);
            _blurProgram = BuildProgram(gl, QuadVertexSource, BlurFragmentSource, out _);
            _postProgram = BuildProgram(gl, QuadVertexSource, PostFragmentSource, out _);
            _shaderBlurProgram = BuildProgram(gl, QuadVertexSource, ShaderBlurFragmentSource, out _);

            _warpUniforms = Uniforms(gl, _warpProgram,
                ["uSource", "uFrameWidth", "uFrameHeight", "uNeedsRadius", "uWarpTime", "uWarpScale", "uDecay"]);
            _blurUniforms = Uniforms(gl, _blurProgram, ["uSource", "uTexelX", "uTexelY"]);
            _postUniforms = Uniforms(
                gl,
                _postProgram,
                [
                    "uSource", "uOverlay", "uDecay", "uDisplayOnly", "uHueTime", "uHue0", "uHue1", "uHue2", "uHue3",
                    "uEchoZoom", "uEchoAlpha", "uEchoOrientation",
                    "uDarken", "uGamma", "uOuterInset", "uOuterThickness", "uOuterR", "uOuterG",
                    "uOuterB", "uOuterA", "uInnerInset", "uInnerThickness", "uInnerR", "uInnerG",
                    "uInnerB", "uInnerA", "uFrameWidth", "uFrameHeight", "uSmaller"
                ]);
            _quadTextureUniform = gl.GetUniformLocationString(_quadProgram, "uSource");
            _shaderBlurUniforms = Uniforms(gl, _shaderBlurProgram, ["uSource", "uTexelX", "uTexelY", "uHorizontal", "uScale", "uBias", "uEdge"]);
            InitializeSamplers(gl);
            UploadSamplerTextures(gl);

            _quadVertexArray = gl.GenVertexArray();
            _quadVertexBuffer = gl.GenBuffer();
            gl.BindVertexArray(_quadVertexArray);
            gl.BindBuffer(GlArrayBuffer, _quadVertexBuffer);
            var quad = GCHandle.Alloc(QuadVertices, GCHandleType.Pinned);
            try
            {
                gl.BufferData(GlArrayBuffer, (IntPtr)(QuadVertices.Length * sizeof(float)), quad.AddrOfPinnedObject(), GlStaticDraw);
            }
            finally
            {
                quad.Free();
            }

            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GlFloat, 0, 2 * sizeof(float), IntPtr.Zero);

            _meshVertexArray = gl.GenVertexArray();
            _meshVertexBuffer = gl.GenBuffer();
            _meshIndexBuffer = gl.GenBuffer();
            gl.BindVertexArray(_meshVertexArray);
            gl.BindBuffer(GlArrayBuffer, _meshVertexBuffer);
            gl.BindBuffer(GlElementArrayBuffer, _meshIndexBuffer);
            var stride = FloatsPerVertex * sizeof(float);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GlFloat, 0, stride, IntPtr.Zero);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, GlFloat, 0, stride, (IntPtr)(2 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 3, GlFloat, 0, stride, (IntPtr)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 3, GlFloat, 0, stride, (IntPtr)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(4, 1, GlFloat, 0, stride, (IntPtr)(11 * sizeof(float)));
            var indices = GCHandle.Alloc(_indices, GCHandleType.Pinned);
            try
            {
                gl.BufferData(GlElementArrayBuffer, (IntPtr)(_indices.Length * sizeof(ushort)), indices.AddrOfPinnedObject(), GlStaticDraw);
            }
            finally
            {
                indices.Free();
            }

            _meshIndexCount = _indices.Length;
            _feedbackTexture = gl.GenTexture();
            _feedbackFramebuffer = gl.GenFramebuffer();
            _pingTexture[0] = gl.GenTexture();
            _pingTexture[1] = gl.GenTexture();
            _pingFramebuffer[0] = gl.GenFramebuffer();
            _pingFramebuffer[1] = gl.GenFramebuffer();
            _overlayTexture = gl.GenTexture();
            _compTexture = gl.GenTexture();
            _compFramebuffer = gl.GenFramebuffer();
            _previousTexture = gl.GenTexture();
            _previousFramebuffer = gl.GenFramebuffer();
            for (var level = 0; level < 3; level++)
            {
                _shaderBlurScratchTexture[level] = gl.GenTexture();
                _shaderBlurScratchFramebuffer[level] = gl.GenFramebuffer();
            }
            for (var index = 0; index < 3; index++)
            {
                _shaderBlurTexture[index] = gl.GenTexture();
                _shaderBlurFramebuffer[index] = gl.GenFramebuffer();
            }

            gl.BindVertexArray(0);
            // A sixteen-bit feedback keeps the frame from being rounded to eight bits on every pass,
            // which is what drifts the GPU picture away from the CPU's float buffers.
            var extensions = gl.GetExtensions() ?? [];
            _halfFloat = ForceHalfFloat ||
                         extensions.Contains("GL_EXT_color_buffer_float") ||
                         extensions.Contains("GL_EXT_color_buffer_half_float");
            _ready = true;
            return true;

        }
        catch (Exception exception)
        {
            Error = $"{exception.GetType().Name}: {exception.Message}";
            _ready = false;
            return false;
        }
    }

    /// <summary>Releases the GL objects.</summary>
    /// <param name="gl">GL interface.</param>
    public void Dispose(GlInterface gl)
    {
        ClearSamplerBindings();
        for (var i = 0; i < _samplerStates.Length; i++)
            if (_samplerStates[i] != 0) { _deleteSamplers?.Invoke(1, ref _samplerStates[i]); _samplerStates[i] = 0; }
        if (!_ready)
            return;

        foreach (var program in new[] { _quadProgram, _warpProgram, _blurProgram, _postProgram, _shaderBlurProgram })
            gl.DeleteProgram(program);
        gl.DeleteVertexArray(_quadVertexArray);
        gl.DeleteBuffer(_quadVertexBuffer);
        gl.DeleteVertexArray(_meshVertexArray);
        gl.DeleteBuffer(_meshVertexBuffer);
        gl.DeleteBuffer(_meshIndexBuffer);
        gl.DeleteTexture(_feedbackTexture);
        gl.DeleteFramebuffer(_feedbackFramebuffer);
        gl.DeleteTexture(_pingTexture[0]);
        gl.DeleteTexture(_pingTexture[1]);
        gl.DeleteFramebuffer(_pingFramebuffer[0]);
        gl.DeleteFramebuffer(_pingFramebuffer[1]);
        gl.DeleteTexture(_overlayTexture);
        gl.DeleteTexture(_compTexture);
        gl.DeleteFramebuffer(_compFramebuffer);
        gl.DeleteTexture(_previousTexture);
        gl.DeleteFramebuffer(_previousFramebuffer);
        for (var index = 0; index < 3; index++)
        {
            gl.DeleteTexture(_shaderBlurTexture[index]);
            gl.DeleteFramebuffer(_shaderBlurFramebuffer[index]);
            gl.DeleteTexture(_shaderBlurScratchTexture[index]);
            gl.DeleteFramebuffer(_shaderBlurScratchFramebuffer[index]);
        }

        foreach (var texture in _samplerTextures.Values)
            gl.DeleteTexture(texture);
        _samplerTextures.Clear();

        if (_warpShaderProgram != 0)
            gl.DeleteProgram(_warpShaderProgram);
        if (_compShaderProgram != 0)
            gl.DeleteProgram(_compShaderProgram);
        _ready = false;
    }

    /// <summary>Runs one frame: warp, blur, post-processing, composite, then presents it.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="framebuffer">Destination framebuffer, or zero for the window.</param>
    /// <param name="viewportWidth">Destination width.</param>
    /// <param name="viewportHeight">Destination height.</param>
    /// <param name="frameWidth">Render width.</param>
    /// <param name="frameHeight">Render height.</param>
    /// <param name="overlayBgra">Overlay frame, tightly packed BGRA, top row first.</param>
    /// <param name="mesh">Per-vertex mesh motion.</param>
    /// <param name="meshX">Mesh grid columns.</param>
    /// <param name="meshY">Mesh grid rows.</param>
    /// <param name="needsRadius">Whether the warp needs the polar radius.</param>
    /// <param name="parameters">The frame's pass values.</param>
    /// <param name="uniforms">The shader uniforms to seed, or <see langword="null"/> for none.</param>
    /// <returns><see langword="true"/> when the frame was drawn.</returns>
    public bool Render(
        GlInterface gl,
        int framebuffer,
        int viewportWidth,
        int viewportHeight,
        int frameWidth,
        int frameHeight,
        byte[] overlayBgra,
        float[] mesh,
        int meshX,
        int meshY,
        bool needsRadius,
        VisualizerFrameParameters parameters,
        IReadOnlyDictionary<string, ShaderValue>? uniforms = null)
    {
        if (!_ready || frameWidth <= 0 || frameHeight <= 0)
            return false;

        try
        {
            EnsureSize(gl, frameWidth, frameHeight);
            EnsureShaderPrograms(gl);
            UploadOverlay(gl, overlayBgra, frameWidth, frameHeight);
            PackVertices(mesh, meshX, meshY);

            // Warp the feedback into ping zero, with the emitted warp shader when the preset has one.
            gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[0]);
            gl.Viewport(0, 0, frameWidth, frameHeight);
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(GlColorBufferBit);
            if (_warpShaderProgram != 0)
            {
                // Warp reads the retained blur chain. Draw into the full-resolution target.
                gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[0]);
                gl.Viewport(0, 0, frameWidth, frameHeight);
                gl.UseProgram(_warpShaderProgram);
                BindShaderSamplers(gl, _warpShaderProgram, _warpShaderUniforms, _feedbackTexture, _previousTexture);
                SetShaderUniforms(gl, _warpShaderProgram, _warpShaderUniforms, uniforms);
                // The vertex stage owns the transform, so it needs the same motion parameters the
                // fixed warp passes.
                Set(gl, _warpShaderUniforms, "uFrameWidth", frameWidth);
                Set(gl, _warpShaderUniforms, "uFrameHeight", frameHeight);
                Set(gl, _warpShaderUniforms, "uNeedsRadius", needsRadius ? 1f : 0f);
                Set(gl, _warpShaderUniforms, "uWarpTime", parameters.WarpTime);
                Set(gl, _warpShaderUniforms, "uWarpScale", parameters.WarpScale);
                DrawMesh(gl);
            }
            else
            {
                gl.UseProgram(_warpProgram);
                gl.ActiveTexture(GlTexture0);
                gl.BindTexture(GlTexture2D, _feedbackTexture);
                _bindSampler?.Invoke(0, _samplerStates[parameters.TextureWrap ? 2 : 0]);
                SetSampler(gl, _warpUniforms, "uSource", 0);
                Set(gl, _warpUniforms, "uFrameWidth", frameWidth);
                Set(gl, _warpUniforms, "uFrameHeight", frameHeight);
                Set(gl, _warpUniforms, "uNeedsRadius", needsRadius ? 1f : 0f);
                Set(gl, _warpUniforms, "uWarpTime", parameters.WarpTime);
                Set(gl, _warpUniforms, "uWarpScale", parameters.WarpScale);
                // The fixed warp fragment shader applies the decay itself, like the reference's
                // frag_COLOR, so the post pass must not apply it again.
                Set(gl, _warpUniforms, "uDecay", parameters.Decay);
                DrawMesh(gl);
            }

            ClearSamplerBindings();
            // Milkdrop updates blur after warp from the previous feedback. The warp reads the
            // retained chain; the comp sees the updated chain, before overlays enter feedback.
            if (_warpShaderProgram != 0 || _compShaderProgram != 0)
                BuildShaderBlurLevels(gl, _feedbackTexture, frameWidth, frameHeight, uniforms);

            // Remember the previous pre-comp frame for the comp shader before the post pass overwrites it.
            if (_compShaderProgram != 0)
                Blit(gl, _feedbackTexture, _previousFramebuffer, frameWidth, frameHeight);

            // Blur, ping-ponging between the two work textures.
            var current = 0;
            gl.UseProgram(_blurProgram);
            for (var pass = 0; pass < parameters.BlurPasses; pass++)
            {
                var next = 1 - current;
                gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[next]);
                gl.Viewport(0, 0, frameWidth, frameHeight);
                gl.ActiveTexture(GlTexture0);
                gl.BindTexture(GlTexture2D, _pingTexture[current]);
                SetSampler(gl, _blurUniforms, "uSource", 0);
                Set(gl, _blurUniforms, "uTexelX", 1f / frameWidth);
                Set(gl, _blurUniforms, "uTexelY", 1f / frameHeight);
                DrawQuad(gl);
                current = next;
            }

            // Post-processing and the overlay composite, into the next feedback.
            gl.BindFramebuffer(GlFramebuffer, _feedbackFramebuffer);
            gl.Viewport(0, 0, frameWidth, frameHeight);
            gl.UseProgram(_postProgram);
            gl.ActiveTexture(GlTexture0);
            gl.BindTexture(GlTexture2D, _pingTexture[current]);
            gl.ActiveTexture(GlTexture1);
            gl.BindTexture(GlTexture2D, _overlayTexture);
            SetSampler(gl, _postUniforms, "uSource", 0);
            SetSampler(gl, _postUniforms, "uOverlay", 1);
            // The fixed warp already applies decay. A custom warp owns its output colour,
            // including any fade; applying the legacy decay here would attenuate it twice.
            Set(gl, _postUniforms, "uDecay", 1f);
            Set(gl, _postUniforms, "uDisplayOnly", 0f);
            Set(gl, _postUniforms, "uEchoZoom", parameters.EchoZoom);
            // The reference's final composite is either the custom comp shader or the legacy video
            // echo and gamma adjustment, never both.
            Set(gl, _postUniforms, "uEchoAlpha", 0f);
            Set(gl, _postUniforms, "uEchoOrientation", parameters.EchoOrientation);
            Set(gl, _postUniforms, "uDarken", parameters.DarkenCenter);
            Set(gl, _postUniforms, "uGamma", 1f);
            Set(gl, _postUniforms, "uOuterInset", parameters.OuterBorder.Inset);
            Set(gl, _postUniforms, "uOuterThickness", parameters.OuterBorder.Thickness);
            Set(gl, _postUniforms, "uOuterR", parameters.OuterBorder.Red);
            Set(gl, _postUniforms, "uOuterG", parameters.OuterBorder.Green);
            Set(gl, _postUniforms, "uOuterB", parameters.OuterBorder.Blue);
            Set(gl, _postUniforms, "uOuterA", parameters.OuterBorder.Alpha);
            Set(gl, _postUniforms, "uInnerInset", parameters.InnerBorder.Inset);
            Set(gl, _postUniforms, "uInnerThickness", parameters.InnerBorder.Thickness);
            Set(gl, _postUniforms, "uInnerR", parameters.InnerBorder.Red);
            Set(gl, _postUniforms, "uInnerG", parameters.InnerBorder.Green);
            Set(gl, _postUniforms, "uInnerB", parameters.InnerBorder.Blue);
            Set(gl, _postUniforms, "uInnerA", parameters.InnerBorder.Alpha);
            Set(gl, _postUniforms, "uFrameWidth", frameWidth);
            Set(gl, _postUniforms, "uFrameHeight", frameHeight);
            Set(gl, _postUniforms, "uSmaller", Math.Min(frameWidth, frameHeight));
            DrawQuad(gl);

            // The comp shader is a display pass: it reads the composited frame and writes the display,
            // while the feedback stays the pre-comp frame the next warp samples.
            var output = _feedbackTexture;
            if (_compShaderProgram != 0 && RunCompShader(gl, frameWidth, frameHeight, uniforms))
                output = _compTexture;

            if (_compShaderProgram == 0)
            {
                gl.BindFramebuffer(GlFramebuffer, _compFramebuffer);
                gl.UseProgram(_postProgram);
                gl.ActiveTexture(GlTexture0);
                gl.BindTexture(GlTexture2D, _feedbackTexture);
                Set(gl, _postUniforms, "uDisplayOnly", 1f);
                Set(gl, _postUniforms, "uEchoAlpha", parameters.EchoAlpha);
                Set(gl, _postUniforms, "uGamma", parameters.Gamma);
                Set(gl, _postUniforms, "uHueTime", parameters.HueTime);
                Set(gl, _postUniforms, "uHue0", parameters.HueOffsets.X);
                Set(gl, _postUniforms, "uHue1", parameters.HueOffsets.Y);
                Set(gl, _postUniforms, "uHue2", parameters.HueOffsets.Z);
                Set(gl, _postUniforms, "uHue3", parameters.HueOffsets.W);
                DrawQuad(gl);
                output = _compTexture;
            }

            // Present the finished frame.
            gl.BindFramebuffer(GlFramebuffer, framebuffer);
            gl.Viewport(0, 0, Math.Max(1, viewportWidth), Math.Max(1, viewportHeight));
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(GlColorBufferBit);
            gl.UseProgram(_quadProgram);
            gl.ActiveTexture(GlTexture0);
            gl.BindTexture(GlTexture2D, output);
            if (_quadTextureUniform >= 0)
                gl.Uniform1i(_quadTextureUniform, 0);
            DrawQuad(gl);

            _outputTexture = output;
            gl.Flush();
            Frames++;
            _frame++;
            if (Diagnostics is null)
            {
                var nonZero = 0;
                for (var index = 0; index + 2 < _overlayRgba.Length; index += 4)
                {
                    if (_overlayRgba[index] != 0 || _overlayRgba[index + 1] != 0 || _overlayRgba[index + 2] != 0)
                        nonZero++;
                }

                Diagnostics =
                    $"GL pipeline first frame: size={frameWidth}x{frameHeight} mesh={meshX}x{meshY} " +
                    $"blur={parameters.BlurPasses} zoom={_vertices[2]:F3} overlayLit={nonZero} " +
                    $"float16={_halfFloat} " +
                    $"glError=0x{gl.GetError():X}";
            }

            return true;

        }
        catch (Exception exception)
        {
            Error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
        finally { ClearSamplerBindings(); }
    }

    /// <summary>Draws the static full-screen quad.</summary>
    /// <param name="gl">GL interface.</param>
    private void DrawQuad(GlInterface gl)
    {
        gl.BindVertexArray(_quadVertexArray);
        gl.DrawArrays(GlTriangleStrip, 0, 4);
    }

    /// <summary>Uploads the packed mesh and draws it, so the vertex stage owns the transform.</summary>
    /// <param name="gl">GL interface.</param>
    private void DrawMesh(GlInterface gl)
    {
        gl.BindVertexArray(_meshVertexArray);
        gl.BindBuffer(GlArrayBuffer, _meshVertexBuffer);
        var vertices = GCHandle.Alloc(_vertices, GCHandleType.Pinned);
        try
        {
            gl.BufferData(GlArrayBuffer, (IntPtr)(_vertices.Length * sizeof(float)), vertices.AddrOfPinnedObject(), GlDynamicDraw);
        }
        finally
        {
            vertices.Free();
        }

        gl.DrawElements(GlTriangles, _meshIndexCount, GlUnsignedShort, IntPtr.Zero);
    }

    /// <summary>Sets a sampler uniform when the program declares it.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="uniforms">Resolved uniform locations.</param>
    /// <param name="name">Uniform name.</param>
    /// <param name="value">Texture unit.</param>
    private static void SetSampler(GlInterface gl, Dictionary<string, int> uniforms, string name, int value)
    {
        if (uniforms.TryGetValue(name, out var location) && location >= 0)
            gl.Uniform1i(location, value);
    }

    /// <summary>Sets a scalar uniform when the program declares it.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="uniforms">Resolved uniform locations.</param>
    /// <param name="name">Uniform name.</param>
    /// <param name="value">Value.</param>
    private static void Set(GlInterface gl, Dictionary<string, int> uniforms, string name, float value)
    {
        if (uniforms.TryGetValue(name, out var location) && location >= 0)
            gl.Uniform1f(location, value);
    }

    /// <summary>Resolves the named uniform locations of a program.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="program">Program.</param>
    /// <param name="names">Uniform names.</param>
    /// <returns>The locations by name.</returns>
    private static Dictionary<string, int> Uniforms(GlInterface gl, int program, string[] names)
    {
        var uniforms = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        foreach (var name in names)
            uniforms[name] = gl.GetUniformLocationString(program, name);
        return uniforms;
    }

    /// <summary>The vertex shader the emitted fragment shaders are paired with: a plain full-screen quad.</summary>
    private const string ShaderVertexSource = """
        #version 300 es
        precision highp float;
        layout(location = 0) in vec2 aPosition;
        void main()
        {
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    /// <summary>Builds the emitted shader programs when the preset published new sources.</summary>
    /// <param name="gl">GL interface.</param>
    private void EnsureShaderPrograms(GlInterface gl)
    {
        if (!_shadersDirty)
            return;

        _shadersDirty = false;
        if (_warpShaderProgram != 0)
        {
            gl.DeleteProgram(_warpShaderProgram);
            _warpShaderProgram = 0;
        }

        if (_compShaderProgram != 0)
        {
            gl.DeleteProgram(_compShaderProgram);
            _compShaderProgram = 0;
        }

        _warpShaderUniforms.Clear();
        _compShaderUniforms.Clear();
        ShaderError = null;

        if (_warpShaderSource is { Length: > 0 })
        {
            // The warp shader is a fragment stage over the mesh: the vertex shader transforms the
            // per-vertex coordinate, exactly as the reference warp vertex shader does.
            if (TryBuildShaderProgram(gl, WarpVertexSource, _warpShaderSource, out var program, out var error))
            {
                _warpShaderProgram = program;
                _warpShaderUniforms = Uniforms(gl, program,
                    ["uFrameWidth", "uFrameHeight", "uNeedsRadius", "uWarpTime", "uWarpScale"]);
            }
            else
                ShaderError = "warp: " + error;
        }

        if (_compShaderSource is { Length: > 0 })
        {
            if (TryBuildShaderProgram(gl, ShaderVertexSource, _compShaderSource, out var program, out var error))
                _compShaderProgram = program;
            else
                ShaderError = (ShaderError is null ? string.Empty : ShaderError + " ") + "comp: " + error;
        }
    }

    /// <summary>Compiles and links one emitted fragment shader with a vertex shader.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="vertexSource">Vertex shader the fragment stage is linked with.</param>
    /// <param name="fragmentSource">Emitted GLSL fragment shader.</param>
    /// <param name="program">Receives the linked program.</param>
    /// <param name="error">Receives the compile or link log.</param>
    /// <returns><see langword="true"/> when the program linked.</returns>
    private static bool TryBuildShaderProgram(GlInterface gl, string vertexSource, string fragmentSource, out int program, out string? error)
    {
        program = 0;
        error = null;
        try
        {
            program = BuildProgram(gl, vertexSource, fragmentSource, out error);
            return true;
        }
        catch (InvalidOperationException)
        {
            program = 0;
            return false;
        }
    }

    /// <summary>Runs the comp shader over the composited frame into its own display target.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="uniforms">Shader uniforms to seed.</param>
    /// <returns><see langword="true"/> when the pass was drawn.</returns>
    private bool RunCompShader(GlInterface gl, int width, int height, IReadOnlyDictionary<string, ShaderValue>? uniforms)
    {
        // The blur chain was updated from the previous feedback before the overlay composite.

        gl.BindFramebuffer(GlFramebuffer, _compFramebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(GlColorBufferBit);
        gl.UseProgram(_compShaderProgram);
        BindShaderSamplers(gl, _compShaderProgram, _compShaderUniforms, _feedbackTexture, _previousTexture);
        SetShaderUniforms(gl, _compShaderProgram, _compShaderUniforms, uniforms);
        DrawQuad(gl);
        ClearSamplerBindings();
        return true;
    }

    /// <summary>
    /// Builds the three blur levels of a frame into the shader blur textures, each level continuing
    /// from the one below it, so a shader's <c>GetBlur1</c>-<c>GetBlur3</c> read the same chain the CPU
    /// builds.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="source">Frame to blur.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void BuildShaderBlurLevels(GlInterface gl, int source, int width, int height, IReadOnlyDictionary<string, ShaderValue>? uniforms)
    {
        gl.UseProgram(_shaderBlurProgram);
        var current = source;
        var sourceWidth = width;
        var sourceHeight = height;
        for (var level = 0; level < 3; level++)
        {
            // The reference halves the blur texture per level, so each level is built from a
            // downscaled copy of the one before it.
            var (blurWidth, blurHeight) = ShaderBlurSize(width, height, level);
            var minimum = uniforms is not null && uniforms.TryGetValue("blur" + (level + 1) + "_min", out var minValue) ? minValue.Get(0) : 0f;
            var maximum = uniforms is not null && uniforms.TryGetValue("blur" + (level + 1) + "_max", out var maxValue) ? maxValue.Get(0) : 1f;
            var previousMin = level > 0 && uniforms is not null && uniforms.TryGetValue("blur" + level + "_min", out var pmin) ? pmin.Get(0) : 0f;
            var previousMax = level > 0 && uniforms is not null && uniforms.TryGetValue("blur" + level + "_max", out var pmax) ? pmax.Get(0) : 1f;
            var range = Math.Max(0.1f, maximum - minimum);
            Set(gl, _shaderBlurUniforms, "uScale", (previousMax - previousMin) / range);
            Set(gl, _shaderBlurUniforms, "uBias", (previousMin - minimum) / range);
            Set(gl, _shaderBlurUniforms, "uEdge", level == 0 && uniforms is not null && uniforms.TryGetValue("blur1_edge_darken", out var edge) ? edge.Get(0) : 0f);
            DrawShaderBlurPass(gl, current, _shaderBlurScratchFramebuffer[level], sourceWidth, sourceHeight, blurWidth, blurHeight, horizontal: true);
            DrawShaderBlurPass(gl, _shaderBlurScratchTexture[level], _shaderBlurFramebuffer[level], blurWidth, blurHeight, blurWidth, blurHeight, horizontal: false);
            current = _shaderBlurTexture[level];
            sourceWidth = blurWidth;
            sourceHeight = blurHeight;
        }
    }

    /// <summary>Draws one pass of the reference weighted blur.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="source">Texture to blur.</param>
    /// <param name="framebuffer">Destination framebuffer.</param>
    /// <param name="sourceWidth">Source width, for the tap offsets.</param>
    /// <param name="sourceHeight">Source height, for the tap offsets.</param>
    /// <param name="targetWidth">Destination width.</param>
    /// <param name="targetHeight">Destination height.</param>
    /// <param name="horizontal">Whether this is the long horizontal pass.</param>
    private void DrawShaderBlurPass(
        GlInterface gl,
        int source,
        int framebuffer,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        bool horizontal)
    {
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, targetWidth, targetHeight);
        gl.ActiveTexture(GlTexture0);
        gl.BindTexture(GlTexture2D, source);
        SetSampler(gl, _shaderBlurUniforms, "uSource", 0);
        Set(gl, _shaderBlurUniforms, "uTexelX", 1f / Math.Max(1, sourceWidth));
        Set(gl, _shaderBlurUniforms, "uTexelY", 1f / Math.Max(1, sourceHeight));
        Set(gl, _shaderBlurUniforms, "uHorizontal", horizontal ? 1f : 0f);
        DrawQuad(gl);
    }

    /// <summary>Copies one texture into a framebuffer.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="texture">Source texture.</param>
    /// <param name="framebuffer">Destination framebuffer.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void Blit(GlInterface gl, int texture, int framebuffer, int width, int height)
    {
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.UseProgram(_quadProgram);
        gl.ActiveTexture(GlTexture0);
        gl.BindTexture(GlTexture2D, texture);
        if (_quadTextureUniform >= 0)
            gl.Uniform1i(_quadTextureUniform, 0);
        DrawQuad(gl);
    }

    /// <summary>
    /// Binds the frame textures to the emitted shader's samplers. The main and filtered samplers read
    /// the composited frame, the previous-composite sampler reads the stored previous frame, the blur
    /// levels read their own textures, and every other sampler falls back to the main frame so it is
    /// never left unbound.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="locations">Uniform locations of the program.</param>
    /// <param name="main">The composited frame.</param>
    /// <param name="previous">Retained previous-frame handle, reserved for legacy callers.</param>
    /// <param name="program">The program whose active samplers are bound.</param>
    private void BindShaderSamplers(
        GlInterface gl,
        int program,
        Dictionary<string, int> locations,
        int main,
        int previous)
    {
        var nextUnit = 0;
        foreach (var name in program == _warpShaderProgram ? _warpSamplerNames : _compSamplerNames)
        {
            if (!locations.TryGetValue(name, out var location))
                locations[name] = location = gl.GetUniformLocationString(program, name);
            if (location < 0) continue;
            if (nextUnit >= 16) throw new InvalidOperationException("Shader exceeds the supported texture unit count.");
            var parsed = ShaderSamplerName.Parse(name);
            var texture = parsed.BaseName switch
            {
                "main" => main,
                "blur1" => _shaderBlurTexture[0],
                "blur2" => _shaderBlurTexture[1],
                "blur3" => _shaderBlurTexture[2],
                _ => _samplerTextures.TryGetValue(name, out var generated) || _samplerTextures.TryGetValue("sampler_" + parsed.BaseName, out generated) ? generated : main
            };
            var unit = nextUnit++;
            gl.ActiveTexture(GlTexture0 + unit);
            gl.BindTexture(GlTexture2D, texture);
            var mode = (parsed.Nearest ? 1 : 0) | (parsed.Wrap == VisualizerTextureWrap.Repeat ? 2 : 0);
            if (parsed.BaseName.StartsWith("blur", StringComparison.Ordinal)) mode = 0;
            _bindSampler?.Invoke(unit, _samplerStates[mode]);
            gl.Uniform1i(location, unit);
        }
    }

    /// <summary>Sets the scalar and vector uniforms an emitted shader declares.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="program">Program the locations belong to.</param>
    /// <param name="locations">Resolved locations, extended lazily.</param>
    /// <param name="uniforms">Values to set, or <see langword="null"/>.</param>
    private static void SetShaderUniforms(
        GlInterface gl,
        int program,
        Dictionary<string, int> locations,
        IReadOnlyDictionary<string, ShaderValue>? uniforms)
    {
        if (uniforms is null)
            return;

        foreach (var (name, value) in uniforms)
        {
            if (name.StartsWith("sampler_", StringComparison.Ordinal))
                continue;

            // GL exposes only scalar uniform setters, so a vector uniform is declared as its
            // components in the emitted GLSL and set one scalar at a time.
            var count = Math.Clamp(value.Count, 1, 4);
            for (var component = 0; component < count; component++)
            {
                var componentName = count == 1 ? name : name + ComponentSuffix[component];
                if (!locations.TryGetValue(componentName, out var location))
                {
                    location = gl.GetUniformLocationString(program, componentName);
                    locations[componentName] = location;
                }

                if (location >= 0)
                    gl.Uniform1f(location, value.Get(component));
            }
        }
    }

    /// <summary>The component suffixes the emitted GLSL appends to a vector uniform.</summary>
    private static readonly string[] ComponentSuffix = ["_x", "_y", "_z", "_w"];

    /// <summary>Creates or resizes the frame-sized textures.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void EnsureSize(GlInterface gl, int width, int height)
    {
        if (_width == width && _height == height)
            return;

        _width = width;
        _height = height;
        if (!TryAllocate(gl, width, height) && _halfFloat)
        {
            // The context advertised a renderable float format but refused it, so the frame falls
            // back to eight-bit colour instead of losing the whole pipeline.
            _halfFloat = false;
            TryAllocate(gl, width, height);
        }


        // A freshly specified texture holds undefined content, so the feedback starts black.
        gl.BindFramebuffer(GlFramebuffer, _feedbackFramebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(GlColorBufferBit);
        gl.BindFramebuffer(GlFramebuffer, 0);
        _overlayRgba = new byte[width * height * 4];
    }

    /// <summary>Allocates the frame-sized textures and reports whether the context accepted them.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns><see langword="true"/> when every framebuffer is complete.</returns>
    private bool TryAllocate(GlInterface gl, int width, int height)
    {
        try
        {
            Allocate(gl, _feedbackTexture, _feedbackFramebuffer, width, height);
            Allocate(gl, _pingTexture[0], _pingFramebuffer[0], width, height);
            Allocate(gl, _pingTexture[1], _pingFramebuffer[1], width, height);
            Allocate(gl, _compTexture, _compFramebuffer, width, height);
            Allocate(gl, _previousTexture, _previousFramebuffer, width, height);
            for (var index = 0; index < 3; index++)
            {
                var (blurWidth, blurHeight) = ShaderBlurSize(width, height, index);
                Allocate(gl, _shaderBlurTexture[index], _shaderBlurFramebuffer[index], blurWidth, blurHeight);
                Allocate(gl, _shaderBlurScratchTexture[index], _shaderBlurScratchFramebuffer[index], blurWidth, blurHeight);
                gl.BindFramebuffer(GlFramebuffer, _shaderBlurFramebuffer[index]);
                gl.ClearColor(0f, 0f, 0f, 1f);
                gl.Clear(GlColorBufferBit);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Allocates one texture and binds it to one framebuffer.</summary>
    /// <param name="gl">GL interface.</param>

    /// <param name="texture">Texture.</param>
    /// <param name="framebuffer">Framebuffer.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    private void Allocate(GlInterface gl, int texture, int framebuffer, int width, int height)

    {
        gl.BindTexture(GlTexture2D, texture);
        gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
        gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
        gl.TexImage2D(GlTexture2D, 0, _halfFloat ? GlRgba16f : GlRgba8, width, height, 0, GlRgba, GlUnsignedByte, IntPtr.Zero);

        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.FramebufferTexture2D(GlFramebuffer, GlColorAttachment0, GlTexture2D, texture, 0);
        if (gl.CheckFramebufferStatus(GlFramebuffer) != GlFramebufferComplete)
            throw new InvalidOperationException("a pipeline framebuffer is incomplete");
        gl.BindFramebuffer(GlFramebuffer, 0);
    }

    /// <summary>Uploads the overlay, converting BGRA to RGBA and flipping to bottom-up.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="bgra">Overlay frame, top row first.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void UploadOverlay(GlInterface gl, byte[] bgra, int width, int height)
    {
        var required = width * height * 4;
        if (bgra.Length < required || _overlayRgba.Length < required)
            return;

        var rgba = _overlayRgba;

        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            // GL's v axis points up, so the rows are written in reverse.
            var source = y * stride;
            var target = (height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var from = source + (x * 4);
                var to = target + (x * 4);
                rgba[to] = bgra[from + 2];
                rgba[to + 1] = bgra[from + 1];
                rgba[to + 2] = bgra[from];
                rgba[to + 3] = bgra[from + 3];
            }
        }

        gl.ActiveTexture(GlTexture0);
        gl.BindTexture(GlTexture2D, _overlayTexture);
        gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
        gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            gl.TexImage2D(GlTexture2D, 0, GlRgba8, width, height, 0, GlRgba, GlUnsignedByte, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Fills the vertex buffer with the grid positions and the mesh motion.</summary>
    /// <param name="motion">Per-vertex motion.</param>
    /// <param name="meshX">Mesh grid columns.</param>
    /// <param name="meshY">Mesh grid rows.</param>
    private void PackVertices(float[] motion, int meshX, int meshY)
    {
        var vertex = 0;
        var target = 0;
        for (var gridY = 0; gridY <= meshY; gridY++)
        {
            var positionY = (gridY / (float)meshY * 2f) - 1f;
            for (var gridX = 0; gridX <= meshX; gridX++)
            {
                var positionX = (gridX / (float)meshX * 2f) - 1f;
                _vertices[target] = positionX;
                _vertices[target + 1] = positionY;
                var source = vertex * PresetRenderer.MeshValues;
                for (var value = 0; value < PresetRenderer.MeshValues; value++)
                    _vertices[target + 2 + value] = motion[source + value];

                target += FloatsPerVertex;
                vertex++;
            }
        }
    }

    /// <summary>Builds the triangle indices for the grid.</summary>
    /// <returns>Two triangles per quad.</returns>
    private static ushort[] BuildIndices()
    {
        var indices = new ushort[PresetRenderer.MeshGridX * PresetRenderer.MeshGridY * 6];
        var target = 0;
        var columns = PresetRenderer.MeshGridX + 1;
        for (var y = 0; y < PresetRenderer.MeshGridY; y++)
        {
            for (var x = 0; x < PresetRenderer.MeshGridX; x++)
            {
                var topLeft = (y * columns) + x;
                var topRight = topLeft + 1;
                var bottomLeft = topLeft + columns;
                var bottomRight = bottomLeft + 1;
                indices[target] = (ushort)topLeft;
                indices[target + 1] = (ushort)bottomLeft;
                indices[target + 2] = (ushort)topRight;
                indices[target + 3] = (ushort)topRight;
                indices[target + 4] = (ushort)bottomLeft;
                indices[target + 5] = (ushort)bottomRight;
                target += 6;
            }
        }

        return indices;
    }

    /// <summary>
    /// Uploads the generated noise and volume textures a shader may sample. Without them a shader that
    /// reads one (a <c>tex3D</c> cloud, or a noise texture) samples whatever the fallback unit holds,
    /// which is the frame.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    private void UploadSamplerTextures(GlInterface gl)
    {
        foreach (var name in ShaderTranspiler.Samplers)
        {
            // The qualifier (fc_/fw_/pc_/pw_) picks the sampling mode; the base name picks the texture.
            var parsed = ShaderSamplerName.Parse(name);
            if (!VisualizerTextureBank.TryResolve("sampler_" + parsed.BaseName, out var texture))
                continue;

            var volume = VisualizerTextureBank.IsVolume(texture);
            var pixels = volume ? _textureBank.GetVolumeAtlasPixels(texture) : _textureBank.GetPixels(texture);
            var width = volume ? VisualizerTextureBank.VolumeAtlasWidth : VisualizerTextureBank.GetSize(texture);
            var height = volume ? VisualizerTextureBank.VolumeAtlasHeight : VisualizerTextureBank.GetSize(texture);
            _samplerTextures[name] = UploadTexture(
                gl, pixels, width, height, volume || parsed.Wrap == VisualizerTextureWrap.Clamp, parsed.Nearest);
        }
    }

    /// <summary>Uploads one generated texture as RGBA8.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="pixels">RGBA floats in the range zero to one.</param>
    /// <param name="width">Texture width.</param>
    /// <param name="height">Texture height.</param>
    /// <param name="clamp">Whether to clamp instead of repeat outside the texture.</param>
    /// <param name="nearest">Whether to read the nearest texel instead of filtering.</param>
    /// <returns>The texture handle.</returns>
    private static int UploadTexture(GlInterface gl, ReadOnlySpan<float> pixels, int width, int height, bool clamp, bool nearest)
    {
        var handle = gl.GenTexture();
        gl.BindTexture(GlTexture2D, handle);
        gl.TexParameteri(GlTexture2D, GlTextureMinFilter, nearest ? GlNearest : GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureMagFilter, nearest ? GlNearest : GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureWrapS, clamp ? GlClampToEdge : GlRepeat);
        gl.TexParameteri(GlTexture2D, GlTextureWrapT, clamp ? GlClampToEdge : GlRepeat);
        var bytes = new byte[width * height * 4];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = index < pixels.Length ? pixels[index] : 0f;
            bytes[index] = (byte)Math.Clamp((int)((value * 255f) + 0.5f), 0, 255);
        }

        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            gl.TexImage2D(GlTexture2D, 0, GlRgba8, width, height, 0, GlRgba, GlUnsignedByte, pinned.AddrOfPinnedObject());
        }
        finally
        {
            pinned.Free();
        }

        return handle;
    }

    /// <summary>Compiles and links a program.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="vertexSource">Vertex shader source.</param>
    /// <param name="fragmentSource">Fragment shader source.</param>
    /// <param name="error">Receives the compile or link log.</param>
    /// <returns>The linked program.</returns>
    /// <exception cref="InvalidOperationException">A shader or the program did not build.</exception>
    private static int BuildProgram(GlInterface gl, string vertexSource, string fragmentSource, out string? error)
    {
        error = null;
        var vertex = gl.CreateShader(GlVertexShader);
        var vertexError = gl.CompileShaderAndGetError(vertex, vertexSource);
        if (!string.IsNullOrWhiteSpace(vertexError))
        {
            error = "vertex shader: " + vertexError;
            throw new InvalidOperationException(error);
        }

        var fragment = gl.CreateShader(GlFragmentShader);
        var fragmentError = gl.CompileShaderAndGetError(fragment, fragmentSource);
        if (!string.IsNullOrWhiteSpace(fragmentError))
        {
            error = "fragment shader: " + fragmentError;
            throw new InvalidOperationException(error);
        }

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        var linkError = gl.LinkProgramAndGetError(program);
        if (!string.IsNullOrWhiteSpace(linkError))
        {
            error = "program: " + linkError;
            throw new InvalidOperationException(error);
        }

        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        return program;
    }
}



