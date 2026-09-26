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
    private const int GlHalfFloat = 0x140B;
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
    private const int GlBlend = 0x0BE2;
    private const int GlOne = 1;
    private const int GlOneMinusSrcAlpha = 0x0303;
    private const int GlTriangleFan = 0x0006;

    /// <summary>
    /// Draws one shape fill's triangle fan. The position arrives in the engine's minus-one-to-one
    /// space with y up, exactly like the CPU fan, so it is flipped here to match the overlay bitmap,
    /// whose rows the presenter uploads reversed.
    /// </summary>
    private const string ShapeVertexSource = """
        #version 300 es
        precision highp float;
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec4 aColor;
        layout(location = 2) in vec2 aUv;
        out vec4 vColor;
        out vec2 vUv;
        void main()
        {
            // The CPU's fan is rasterized in the overlay's top-down space, so the position is flipped
            // into OpenGL's y-up space; the frame readback flips it back.
            gl_Position = vec4(aPosition.x, -aPosition.y, 0.0, 1.0);
            vColor = aColor;
            vUv = aUv;
        }
        """;

    /// <summary>
    /// Colours one shape fill. A textured fill samples the blurred frame the CPU's fan reads, whose
    /// texture is bottom-up while the fan coordinate is top-down; otherwise the interpolated vertex
    /// colour is used. The output is premultiplied, which is what the CPU's <c>PaintPixel</c> writes,
    /// so the blend reproduces its "over" operation exactly.
    /// </summary>
    private const string ShapeFragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec4 vColor;
        in vec2 vUv;
        out vec4 fragColor;
        uniform sampler2D uFrame;
        uniform float uTextured;
        void main()
        {
            vec3 rgb = uTextured != 0.0
                ? texture(uFrame, vec2(vUv.x, 1.0 - vUv.y)).rgb * vColor.rgb
                : vColor.rgb;
            // MilkDrop selects diffuse (shape) alpha even for a textured fan.
            fragColor = vec4(rgb * vColor.a, vColor.a);
        }
        """;

    /// <summary>
    /// Floats per mesh vertex: the position pair, the ten motion values of the incoming preset, and
    /// the ten of the outgoing preset. The outgoing block lets the vertex shader morph the sampling
    /// coordinate between the two presets during a blend; when no blend runs its values are ignored
    /// because <c>uBlend</c> is one.
    /// </summary>
    private const int FloatsPerVertex = 2 + (PresetRenderer.MeshValues * 2);

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
        layout(location = 5) in vec3 aOldMotion0;
        layout(location = 6) in vec3 aOldMotion1;
        layout(location = 7) in vec3 aOldMotion2;
        layout(location = 8) in float aOldWarp;
        out vec2 vUv;
        out vec2 vUvOrig;
        out float vRad;
        out float vAng;
        uniform float uFrameWidth;
        uniform float uFrameHeight;
        uniform float uNeedsRadius;
        uniform float uWarpTime;
        uniform float uWarpScale;
        uniform float uBlend;

        // The reference warp vertex shader's arithmetic for one preset's motion, applied at the
        // vertex so the resulting texture coordinate is interpolated across the quad. aPosition is
        // the vertex position in minus-one-to-one space.
        vec2 orynivoWarpUv(vec2 pos, vec3 m0, vec3 m1, vec3 m2, float warp, float radius, float aspectX, float aspectY)
        {
            float zoom = max(0.01, m0.x);
            float zoomExp = m0.y;
            float rotation = m0.z;
            float cx = m1.x;
            float cy = m1.y;
            float dx = m1.z;
            float dy = m2.x;
            float sx = m2.y;
            float sy = m2.z;
            float radialZoom = (uNeedsRadius > 0.5 && zoomExp != 1.0)
                ? pow(zoom, pow(zoomExp, radius * 2.0 - 1.0))
                : zoom;
            float inverseZoom = 1.0 / max(0.01, radialZoom);

            float u = pos.x * aspectX * 0.5 * inverseZoom + 0.5;
            float v = pos.y * aspectY * 0.5 * inverseZoom + 0.5;
            u = (u - cx) / max(abs(sx), 0.0001) * sign(sx) + cx;
            v = (v - cy) / max(abs(sy), 0.0001) * sign(sy) + cy;

            if (warp != 0.0)
            {
                float scaleInverse = uWarpScale == 0.0 ? 1.0 : 1.0 / uWarpScale;
                float factor0 = 11.68 + 4.0 * cos(uWarpTime * 1.413 + 10.0);
                float factor1 = 8.77 + 3.0 * cos(uWarpTime * 1.113 + 7.0);
                float factor2 = 10.54 + 3.0 * cos(uWarpTime * 1.233 + 3.0);
                float factor3 = 11.49 + 4.0 * cos(uWarpTime * 0.933 + 5.0);
                float amount = warp * 0.0035;
                u += amount * sin(uWarpTime * 0.333 + scaleInverse * (pos.x * factor0 - pos.y * factor3))
                   + amount * cos(uWarpTime * 0.753 - scaleInverse * (pos.x * factor1 - pos.y * factor2));
                v += amount * cos(uWarpTime * 0.375 - scaleInverse * (pos.x * factor2 + pos.y * factor1))
                   + amount * sin(uWarpTime * 0.825 + scaleInverse * (pos.x * factor0 + pos.y * factor3));
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
            return vec2(u, v);
        }

        void main()
        {
            float aspectX = uFrameHeight > uFrameWidth ? uFrameWidth / uFrameHeight : 1.0;
            float aspectY = uFrameWidth > uFrameHeight ? uFrameHeight / uFrameWidth : 1.0;
            float radius = uNeedsRadius > 0.5 ? length(aPosition * vec2(aspectX, aspectY)) : 0.0;

            vec2 newUv = orynivoWarpUv(aPosition, aMotion0, aMotion1, aMotion2, aWarp, radius, aspectX, aspectY);
            vec2 oldUv = orynivoWarpUv(aPosition, aOldMotion0, aOldMotion1, aOldMotion2, aOldWarp, radius, aspectX, aspectY);

            // During a blend the outgoing preset's coordinate is eased into the incoming one, the way
            // the reference morphs the two per-vertex UV sets; uBlend is one when no blend runs, so
            // the result is exactly the incoming coordinate.
            vUv = mix(oldUv, newUv, clamp(uBlend, 0.0, 1.0));
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
        uniform float uShaderAmount;
        uniform float uPostBrighten;
        uniform float uPostDarken;
        uniform float uPostSolarize;
        uniform float uPostInvert;
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
                // MilkDrop draws a tiny six-vertex black fan, with 3/32 opacity at the
                // centre and zero opacity at its diamond-shaped rim.
                vec2 fromCentre = abs((vUv - 0.5) * vec2(uFrameWidth, uFrameHeight));
                float radius = uSmaller * 0.025;
                float coverage = max(0.0, 1.0 - (fromCentre.x + fromCentre.y) / max(radius, 0.0001));
                colour.rgb *= 1.0 - uDarken * (3.0 / 32.0) * coverage;
            }

            // The border bands are clip-space Chebyshev rings, [inset, inset + thickness), symmetric
            // in both axes, so the bottom-up coordinate gives the same ring as the CPU's top-down one.
            vec4 outer = vec4(uOuterR, uOuterG, uOuterB, uOuterA);
            vec4 inner = vec4(uInnerR, uInnerG, uInnerB, uInnerA);
            vec2 clip = abs(vUv * 2.0 - 1.0);
            float chebyshev = max(clip.x, clip.y);
            if (outer.a > 0.0 && uOuterThickness > 0.0 && chebyshev >= uOuterInset && chebyshev <= uOuterInset + uOuterThickness)
                colour = mix(colour, vec4(outer.rgb, colour.a), outer.a);
            if (inner.a > 0.0 && uInnerThickness > 0.0 && chebyshev >= uInnerInset && chebyshev <= uInnerInset + uInnerThickness)
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
                vec3 shade = mix(mix(hueShade(3.0), hueShade(2.0), vUv.x),
                                 mix(hueShade(1.0), hueShade(0.0), vUv.x), 1.0 - vUv.y);
                colour.rgb *= mix(vec3(1.0), shade, uShaderAmount);
                colour.rgb *= uGamma;
                // The legacy display filters, in the reference's order (GenCompPShaderText).
                if (uPostBrighten > 0.5) colour.rgb = sqrt(colour.rgb);
                if (uPostDarken > 0.5) colour.rgb *= colour.rgb;
                if (uPostSolarize > 0.5) colour.rgb = colour.rgb * (1.0 - colour.rgb) * 4.0;
                if (uPostInvert > 0.5) colour.rgb = 1.0 - colour.rgb;
            }

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

    /// <summary>Sets the source and destination blend factors.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BlendFunc(int source, int destination);

    /// <summary>Enables or disables writing each colour channel.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ColorMask(byte red, byte green, byte blue, byte alpha);

    private BlendFunc? _blendFunc;
    private ColorMask? _colorMask;

    /// <summary>Enables or disables a GL capability.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SetCapability(int capability);

    private SetCapability? _enableCapability;
    private SetCapability? _disableCapability;

    /// <summary>
    /// Resolves the blend entry points the shape fills need. <see cref="GlInterface"/> exposes
    /// neither, so they are fetched by name like the sampler objects.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <returns><see langword="true"/> when every entry point is available.</returns>
    private bool InitializeBlend(GlInterface gl)
    {
        try
        {
            _blendFunc = Marshal.GetDelegateForFunctionPointer<BlendFunc>(gl.GetProcAddress("glBlendFunc"));
            _colorMask = Marshal.GetDelegateForFunctionPointer<ColorMask>(gl.GetProcAddress("glColorMask"));
            _enableCapability = Marshal.GetDelegateForFunctionPointer<SetCapability>(gl.GetProcAddress("glEnable"));
            _disableCapability = Marshal.GetDelegateForFunctionPointer<SetCapability>(gl.GetProcAddress("glDisable"));
            return true;
        }
        catch (Exception exception)
        {
            ShapeError = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

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

    /// <summary>The shape-fill program, its uniforms, and its vertex buffer.</summary>
    private int _shapeProgram;
    private Dictionary<string, int> _shapeUniforms = new(StringComparer.Ordinal);
    private int _shapeVertexArray;
    private int _shapeVertexBuffer;
    private float[] _shapeVertices = new float[FloatsPerShapeVertex * 64];

    /// <summary>Floats per shape-fill vertex: position, colour, and texture coordinate.</summary>
    private const int FloatsPerShapeVertex = 8;
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

    /// <summary>The previous feedback, bound to every main sampler in the comp shader.</summary>
    private int _previousTexture;
    private int _previousFramebuffer;

    /// <summary>Blurred copies of previous feedback for <c>sampler_blur1</c>-<c>3</c>.</summary>
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

    /// <summary>Gets why the shape-fill program failed to build, or <see langword="null"/>.</summary>
    public string? ShapeError { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the pipeline can draw overlay shape fills. A caller that sets
    /// <c>PresetRenderer.CollectShapeFills</c> must only do so while this is <see langword="true"/>,
    /// because the renderer then skips its own rasterized fill.
    /// </summary>
    public bool ShapeFillsSupported => _shapeProgram != 0;

    /// <summary>
    /// Gets a value indicating whether the pipeline can draw the custom-wave geometry. A caller that
    /// sets <c>PresetRenderer.CollectWaveGeometry</c> must only do so while this is
    /// <see langword="true"/>, because the renderer then skips its own CPU line rasterization, which
    /// is prohibitively slow at a high render resolution.
    /// </summary>
    public bool WaveGeometrySupported => _shapeProgram != 0;

    /// <summary>
    /// Publishes the emitted GLSL for the current preset. The sources are compiled on the next frame,
    /// because the GL context is only current inside the render callback. A source that fails to build
    /// leaves that stage on the fixed pipeline instead of losing the frame.
    /// </summary>
    /// <param name="warpSource">GLSL for the warp shader, or <see langword="null"/>.</param>
    /// <param name="compSource">GLSL for the comp shader, or <see langword="null"/>.</param>
    /// <param name="perPixelWarp">
    /// Whether <paramref name="warpSource"/> is a per-pixel warp: it computes the coordinate from the
    /// fragment position, so it is drawn over a full-screen quad and the frame motion reaches it as
    /// the <c>_orynivo_*</c> uniforms instead of through the mesh vertices.
    /// </param>
    public void SetShaders(string? warpSource, string? compSource, bool perPixelWarp = false)
    {
        if (!string.Equals(_warpShaderSource, warpSource, StringComparison.Ordinal) ||
            !string.Equals(_compShaderSource, compSource, StringComparison.Ordinal) ||
            _perPixelWarp != perPixelWarp)
        {
            _warpShaderSource = warpSource;
            _compShaderSource = compSource;
            _perPixelWarp = perPixelWarp;
            _warpSamplerNames = SamplerNames(warpSource);
            _compSamplerNames = SamplerNames(compSource);
            _shadersDirty = true;
        }
    }

    /// <summary>Clears feedback from the previous preset before rendering a newly selected one.</summary>
    /// <param name="gl">Current OpenGL context.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    public void ResetFeedback(GlInterface gl, int width, int height)
    {
        if (!_ready || width <= 0 || height <= 0)
            return;

        EnsureSize(gl, width, height);
        gl.ClearColor(0f, 0f, 0f, 1f);
        foreach (var framebuffer in new[] { _feedbackFramebuffer, _previousFramebuffer, _compFramebuffer,
                     _pingFramebuffer[0], _pingFramebuffer[1] })
        {
            gl.BindFramebuffer(GlFramebuffer, framebuffer);
            gl.Viewport(0, 0, width, height);
            gl.Clear(GlColorBufferBit);
        }

        for (var index = 0; index < _shaderBlurFramebuffer.Length; index++)
        {
            var (blurWidth, blurHeight) = ShaderBlurSize(width, height, index);
            gl.BindFramebuffer(GlFramebuffer, _shaderBlurFramebuffer[index]);
            gl.Viewport(0, 0, blurWidth, blurHeight);
            gl.Clear(GlColorBufferBit);
        }

        _outputTexture = 0;
        _frame = 0;
    }

    /// <summary>Whether the published warp shader computes the coordinate per pixel.</summary>
    private bool _perPixelWarp;

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

    /// <summary>Whether the current preset's warp GLSL program was linked and used.</summary>
    internal bool WarpShaderActive => _warpShaderProgram != 0;

    /// <summary>Whether the current preset's composite GLSL program was linked and used.</summary>
    internal bool CompShaderActive => _compShaderProgram != 0;

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
                ["uSource", "uFrameWidth", "uFrameHeight", "uNeedsRadius", "uWarpTime", "uWarpScale", "uDecay", "uBlend"]);
            _blurUniforms = Uniforms(gl, _blurProgram, ["uSource", "uTexelX", "uTexelY"]);
            _postUniforms = Uniforms(
                gl,
                _postProgram,
                [
                    "uSource", "uOverlay", "uDecay", "uDisplayOnly", "uHueTime", "uHue0", "uHue1", "uHue2", "uHue3",
                    "uEchoZoom", "uEchoAlpha", "uEchoOrientation",
                    "uDarken", "uGamma", "uShaderAmount", "uPostBrighten", "uPostDarken", "uPostSolarize", "uPostInvert", "uOuterInset", "uOuterThickness", "uOuterR", "uOuterG",
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

            if (TryBuildShaderProgram(gl, ShapeVertexSource, ShapeFragmentSource, out var shapeProgram, out var shapeError))
            {
                _shapeProgram = shapeProgram;
                _shapeUniforms = Uniforms(gl, shapeProgram, ["uFrame", "uTextured"]);
                if (!InitializeBlend(gl))
                {
                    gl.DeleteProgram(_shapeProgram);
                    _shapeProgram = 0;
                }
            }
            else
            {
                ShapeError = shapeError;
            }

            _shapeVertexArray = gl.GenVertexArray();
            _shapeVertexBuffer = gl.GenBuffer();
            gl.BindVertexArray(_shapeVertexArray);
            gl.BindBuffer(GlArrayBuffer, _shapeVertexBuffer);
            var shapeStride = FloatsPerShapeVertex * sizeof(float);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GlFloat, 0, shapeStride, IntPtr.Zero);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, GlFloat, 0, shapeStride, (IntPtr)(2 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 2, GlFloat, 0, shapeStride, (IntPtr)(6 * sizeof(float)));

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
            // The outgoing preset's motion block drives the blend's morphing coordinate.
            gl.EnableVertexAttribArray(5);
            gl.VertexAttribPointer(5, 3, GlFloat, 0, stride, (IntPtr)(12 * sizeof(float)));
            gl.EnableVertexAttribArray(6);
            gl.VertexAttribPointer(6, 3, GlFloat, 0, stride, (IntPtr)(15 * sizeof(float)));
            gl.EnableVertexAttribArray(7);
            gl.VertexAttribPointer(7, 3, GlFloat, 0, stride, (IntPtr)(18 * sizeof(float)));
            gl.EnableVertexAttribArray(8);
            gl.VertexAttribPointer(8, 1, GlFloat, 0, stride, (IntPtr)(21 * sizeof(float)));
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

        foreach (var program in new[] { _quadProgram, _warpProgram, _blurProgram, _postProgram, _shaderBlurProgram, _shapeProgram })
            gl.DeleteProgram(program);
        gl.DeleteVertexArray(_quadVertexArray);
        gl.DeleteBuffer(_quadVertexBuffer);
        gl.DeleteVertexArray(_shapeVertexArray);
        gl.DeleteBuffer(_shapeVertexBuffer);
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
    /// <param name="blendMotion">
    /// Per-vertex motion of the outgoing preset during a preset blend, or <see langword="null"/> when
    /// no blend runs. It gives the vertex shader the second coordinate it morphs from.
    /// </param>
    /// <param name="blendMix">
    /// Eased blend progress where zero is the outgoing preset and one the incoming. One disables the
    /// morph, so a frame that does not blend is byte-identical to the pre-blend pipeline.
    /// </param>
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
        IReadOnlyDictionary<string, ShaderValue>? uniforms = null,
        IReadOnlyList<ShapeFill>? shapeFills = null,
        IReadOnlyList<WaveGeometry>? waveGeometry = null,
        float[]? blendMotion = null,
        float blendMix = 1f)
    {
        if (!_ready || frameWidth <= 0 || frameHeight <= 0)
            return false;

        try
        {
            EnsureSize(gl, frameWidth, frameHeight);
            EnsureShaderPrograms(gl);
            UploadOverlay(gl, overlayBgra, frameWidth, frameHeight);
            PackVertices(mesh, blendMotion, meshX, meshY);

            // Warp the feedback into ping zero, with the emitted warp shader when the preset has one.
            gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[0]);
            gl.Viewport(0, 0, frameWidth, frameHeight);
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(GlColorBufferBit);
            if (_warpShaderProgram != 0)
            {
                // Warp reads the retained blur chain, which is one generation older than this frame's
                // sampler_main. Draw into the full-resolution target.
                gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[0]);
                gl.Viewport(0, 0, frameWidth, frameHeight);
                gl.UseProgram(_warpShaderProgram);
                BindShaderSamplers(gl, _warpShaderProgram, _warpShaderUniforms, _feedbackTexture);
                SetShaderUniforms(gl, _warpShaderProgram, _warpShaderUniforms, uniforms);
                // The vertex stage owns the transform, so it needs the same motion parameters the
                // fixed warp passes.
                Set(gl, _warpShaderUniforms, "uFrameWidth", frameWidth);
                Set(gl, _warpShaderUniforms, "uFrameHeight", frameHeight);
                Set(gl, _warpShaderUniforms, "uNeedsRadius", needsRadius ? 1f : 0f);
                Set(gl, _warpShaderUniforms, "uWarpTime", parameters.WarpTime);
                Set(gl, _warpShaderUniforms, "uWarpScale", parameters.WarpScale);
                Set(gl, _warpShaderUniforms, "uBlend", blendMix);
                if (_perPixelWarp)
                {
                    // The fragment stage owns the whole warp, so the frame motion is seeded as
                    // uniforms and the quad covers the frame instead of the warped mesh. A blend
                    // eases the motion uniforms between the two presets' mesh positions.
                    SeedPixelWarpMotion(gl, mesh, blendMotion, blendMix, frameWidth, frameHeight, parameters);
                    DrawQuad(gl);
                }
                else
                {
                    DrawMesh(gl);
                }
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
                Set(gl, _warpUniforms, "uBlend", blendMix);
                // The fixed warp fragment shader applies the decay itself, like the reference's
                // frag_COLOR, so the post pass must not apply it again.
                Set(gl, _warpUniforms, "uDecay", parameters.Decay);
                DrawMesh(gl);
            }

            ClearSamplerBindings();

            // MilkDrop's BlurPasses runs after the warp and binds VS[0], the feedback the warp just
            // consumed, then the frame buffers swap. The next warp's sampler_main is therefore one
            // generation newer than its sampler_blur1-3, which the reference documents as intended
            // ("when sampling the blurred textures in the warp shader, they are one frame old").
            // Build the chain here from the feedback before the post pass overwrites it; comp reads
            // the same freshly built chain.
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

            // The overlay's shape fills run on the GPU: the CPU publishes the fans, and they are drawn
            // into the post's source with the same "over" that PaintPixel applies. A textured fill
            // samples the previous feedback (VS[0]), so the draw targets the other ping and the
            // post reads it back.
            if ((shapeFills is { Count: > 0 } || waveGeometry is { Count: > 0 }) && _shapeProgram != 0)
            {
                var overlayTarget = 1 - current;
                Blit(gl, _pingTexture[current], _pingFramebuffer[overlayTarget], frameWidth, frameHeight);
                if (shapeFills is { Count: > 0 })
                    DrawShapeFills(gl, shapeFills, _feedbackTexture, frameWidth, frameHeight, overlayTarget);
                if (waveGeometry is { Count: > 0 })
                    DrawWaveGeometry(gl, waveGeometry, frameWidth, frameHeight, overlayTarget);
                current = overlayTarget;
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
            // including any fade; applying the legacy decay here would attenuate it twice. A
            // per-pixel warp is a geometric warp like the fixed one, so its decay lands here
            // instead, after the blur, which is linear and therefore order-independent.
            Set(gl, _postUniforms, "uDecay", _perPixelWarp ? parameters.Decay : 1f);
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

            // The comp shader is a display pass. MilkDrop binds the old VS[0] to sampler_main;
            // the freshly composited VS[1] becomes feedback only after presentation.
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
                Set(gl, _postUniforms, "uShaderAmount", parameters.ShaderAmount);
                Set(gl, _postUniforms, "uPostBrighten", parameters.Brighten ? 1f : 0f);
                Set(gl, _postUniforms, "uPostDarken", parameters.Darken ? 1f : 0f);
                Set(gl, _postUniforms, "uPostSolarize", parameters.Solarize ? 1f : 0f);
                Set(gl, _postUniforms, "uPostInvert", parameters.Invert ? 1f : 0f);
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

    /// <summary>
    /// Seeds a per-pixel warp's motion uniforms from the frame motion. The mesh carries one value set
    /// per vertex, in <see cref="PresetRenderer.MeshValues"/> order; a per-pixel warp runs on the
    /// uniform mesh the frame motion describes, so the first vertex is the frame's own values.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="mesh">Packed per-vertex motion.</param>
    /// <param name="blendMotion">Outgoing preset's mesh motion during a blend, or <see langword="null"/>.</param>
    /// <param name="blendMix">Eased blend progress, zero for the outgoing preset and one for the incoming.</param>
    /// <param name="frameWidth">Frame width in pixels.</param>
    /// <param name="frameHeight">Frame height in pixels.</param>
    /// <param name="parameters">The frame's pass values.</param>
    private void SeedPixelWarpMotion(
        GlInterface gl,
        float[] mesh,
        float[]? blendMotion,
        float blendMix,
        int frameWidth,
        int frameHeight,
        VisualizerFrameParameters parameters)
    {
        if (mesh.Length >= PresetRenderer.MeshValues)
        {
            // A blend eases the frame motion between the outgoing and incoming presets, so a
            // per-pixel warp morphs its coordinate instead of switching it on the first frame.
            float Blend(int index) => blendMotion is not null && index < blendMotion.Length
                ? blendMotion[index] + ((mesh[index] - blendMotion[index]) * blendMix)
                : mesh[index];

            Set(gl, _warpShaderUniforms, "_orynivo_zoom", Math.Max(0.01f, Blend(0)));
            Set(gl, _warpShaderUniforms, "_orynivo_zoomExp", Blend(1));
            Set(gl, _warpShaderUniforms, "_orynivo_rotation", Blend(2));
            Set(gl, _warpShaderUniforms, "_orynivo_centre_x", Blend(3));
            Set(gl, _warpShaderUniforms, "_orynivo_centre_y", Blend(4));
            Set(gl, _warpShaderUniforms, "_orynivo_offset_x", Blend(5));
            Set(gl, _warpShaderUniforms, "_orynivo_offset_y", Blend(6));
            Set(gl, _warpShaderUniforms, "_orynivo_stretch_x", Blend(7));
            Set(gl, _warpShaderUniforms, "_orynivo_stretch_y", Blend(8));
            Set(gl, _warpShaderUniforms, "_orynivo_warp", Blend(9));
        }

        Set(gl, _warpShaderUniforms, "_orynivo_size_x", frameWidth);
        Set(gl, _warpShaderUniforms, "_orynivo_size_y", frameHeight);
        Set(gl, _warpShaderUniforms, "_orynivo_warpTime", parameters.WarpTime);
        Set(gl, _warpShaderUniforms, "_orynivo_warpScale", parameters.WarpScale);
        // The warp samples the full-resolution feedback, so texsize is the frame, not a shader grid.
        Set(gl, _warpShaderUniforms, "texsize_x", frameWidth);
        Set(gl, _warpShaderUniforms, "texsize_y", frameHeight);
    }

    /// <summary>Draws the static full-screen quad.</summary>
    /// <param name="gl">GL interface.</param>
    private void DrawQuad(GlInterface gl)    {
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

    /// <summary>
    /// Draws the overlay's shape fills with the blending the CPU's <c>PaintPixel</c> uses: a
    /// premultiplied "over" for a normal fill, which accumulates the coverage exactly as the CPU's
    /// does, and a plain add for an additive one, whose alpha channel stays untouched.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="fills">Fills to draw, in order.</param>
    /// <param name="frameTexture">Previous feedback (MilkDrop VS[0]) sampled by a textured fill.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="target">Ping target index to draw into.</param>
    private void DrawShapeFills(
        GlInterface gl,
        IReadOnlyList<ShapeFill> fills,
        int frameTexture,
        int width,
        int height,
        int target)
    {
        gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[target]);
        gl.Viewport(0, 0, width, height);
        gl.UseProgram(_shapeProgram);
        gl.ActiveTexture(GlTexture0);
        gl.BindTexture(GlTexture2D, frameTexture);
        _bindSampler?.Invoke(0, _samplerStates[2]);
        SetSampler(gl, _shapeUniforms, "uFrame", 0);
        gl.BindVertexArray(_shapeVertexArray);
        gl.BindBuffer(GlArrayBuffer, _shapeVertexBuffer);
        _enableCapability?.Invoke(GlBlend);
        foreach (var fill in fills)
        {
            if (fill.Vertices.Count < 3)
                continue;

            PackShapeFill(gl, fill);
            _blendFunc?.Invoke(GlOne, fill.Additive ? GlOne : GlOneMinusSrcAlpha);
            _colorMask?.Invoke(1, 1, 1, fill.Additive ? (byte)0 : (byte)1);
            Set(gl, _shapeUniforms, "uTextured", fill.Textured ? 1f : 0f);
            gl.DrawArrays(GlTriangleFan, 0, fill.Vertices.Count);
        }

        _colorMask?.Invoke(1, 1, 1, 1);
        _disableCapability?.Invoke(GlBlend);
        ClearSamplerBindings();
    }

    /// <summary>Uploads one fill's fan into the shape vertex buffer.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="fill">Fill to upload.</param>
    private void PackShapeFill(GlInterface gl, ShapeFill fill)
    {
        var required = fill.Vertices.Count * FloatsPerShapeVertex;
        if (_shapeVertices.Length < required)
            _shapeVertices = new float[required];

        var target = 0;
        foreach (var vertex in fill.Vertices)
        {
            _shapeVertices[target] = vertex.X;
            _shapeVertices[target + 1] = vertex.Y;
            _shapeVertices[target + 2] = vertex.Red;
            _shapeVertices[target + 3] = vertex.Green;
            _shapeVertices[target + 4] = vertex.Blue;
            _shapeVertices[target + 5] = vertex.Alpha;
            _shapeVertices[target + 6] = vertex.U;
            _shapeVertices[target + 7] = vertex.V;
            target += FloatsPerShapeVertex;
        }

        var handle = GCHandle.Alloc(_shapeVertices, GCHandleType.Pinned);
        try
        {
            gl.BufferData(GlArrayBuffer, (IntPtr)(required * sizeof(float)), handle.AddrOfPinnedObject(), GlDynamicDraw);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Draws the custom-wave triangle lists into the frame. It reuses the shape program untextured,
    /// so the premultiplied colour and the "over"/additive blend match the CPU rasterizer.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="geometry">Wave geometry, in draw order.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="target">Ping target the geometry is drawn into.</param>
    private void DrawWaveGeometry(
        GlInterface gl,
        IReadOnlyList<WaveGeometry> geometry,
        int width,
        int height,
        int target)
    {
        gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[target]);
        gl.Viewport(0, 0, width, height);
        gl.UseProgram(_shapeProgram);
        Set(gl, _shapeUniforms, "uTextured", 0f);
        gl.BindVertexArray(_shapeVertexArray);
        gl.BindBuffer(GlArrayBuffer, _shapeVertexBuffer);
        _enableCapability?.Invoke(GlBlend);
        foreach (var wave in geometry)
        {
            if (wave.Vertices.Count < 3)
                continue;

            PackWaveGeometry(gl, wave);
            _blendFunc?.Invoke(GlOne, wave.Additive ? GlOne : GlOneMinusSrcAlpha);
            _colorMask?.Invoke(1, 1, 1, wave.Additive ? (byte)0 : (byte)1);
            gl.DrawArrays(GlTriangles, 0, wave.Vertices.Count);
        }

        _colorMask?.Invoke(1, 1, 1, 1);
        _disableCapability?.Invoke(GlBlend);
    }

    /// <summary>Uploads one wave's triangles into the shape vertex buffer.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="wave">Wave geometry to upload.</param>
    private void PackWaveGeometry(GlInterface gl, WaveGeometry wave)
    {
        var required = wave.Vertices.Count * FloatsPerShapeVertex;
        if (_shapeVertices.Length < required)
            _shapeVertices = new float[required];

        var target = 0;
        foreach (var vertex in wave.Vertices)
        {
            _shapeVertices[target] = vertex.X;
            _shapeVertices[target + 1] = vertex.Y;
            _shapeVertices[target + 2] = vertex.Red;
            _shapeVertices[target + 3] = vertex.Green;
            _shapeVertices[target + 4] = vertex.Blue;
            _shapeVertices[target + 5] = vertex.Alpha;
            _shapeVertices[target + 6] = 0.5f;
            _shapeVertices[target + 7] = 0.5f;
            target += FloatsPerShapeVertex;
        }

        var handle = GCHandle.Alloc(_shapeVertices, GCHandleType.Pinned);
        try
        {
            gl.BufferData(GlArrayBuffer, (IntPtr)(required * sizeof(float)), handle.AddrOfPinnedObject(), GlDynamicDraw);
        }
        finally
        {
            handle.Free();
        }
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
            // A per-pixel warp computes the coordinate from the fragment position, so it is linked
            // with the pass-through quad vertex shader and drawn over the whole frame; the mesh warp
            // keeps the vertex stage that transforms and interpolates.
            var warpVertexSource = _perPixelWarp ? QuadVertexSource : WarpVertexSource;
            if (TryBuildShaderProgram(gl, warpVertexSource, _warpShaderSource, out var program, out var error))
            {
                _warpShaderProgram = program;
                _warpShaderUniforms = Uniforms(gl, program, _perPixelWarp
                    ?
                    [
                        "uFrameWidth", "uFrameHeight", "uNeedsRadius", "uWarpTime", "uWarpScale",
                        "texsize_x", "texsize_y",
                        "_orynivo_size_x", "_orynivo_size_y", "_orynivo_zoom", "_orynivo_zoomExp",
                        "_orynivo_rotation", "_orynivo_centre_x", "_orynivo_centre_y",
                        "_orynivo_offset_x", "_orynivo_offset_y", "_orynivo_stretch_x", "_orynivo_stretch_y",
                        "_orynivo_warp", "_orynivo_warpTime", "_orynivo_warpScale"
                    ]
                    : ["uFrameWidth", "uFrameHeight", "uNeedsRadius", "uWarpTime", "uWarpScale", "uBlend"]);
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

    /// <summary>Runs the comp shader with the previous feedback as its main sampler.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="uniforms">Shader uniforms to seed.</param>
    /// <returns><see langword="true"/> when the pass was drawn.</returns>
    private bool RunCompShader(GlInterface gl, int width, int height, IReadOnlyDictionary<string, ShaderValue>? uniforms)
    {
        // VS[0] is snapshotted before the post pass overwrites feedback. MilkDrop binds it to
        // every main sampler in the comp shader, including qualified sampler names.

        gl.BindFramebuffer(GlFramebuffer, _compFramebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(GlColorBufferBit);
        gl.UseProgram(_compShaderProgram);
        BindShaderSamplers(gl, _compShaderProgram, _compShaderUniforms, _previousTexture);
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
    /// Binds the frame textures to the emitted shader's samplers. Every qualified main sampler reads
    /// the stage's main texture; blur levels use their own textures, and unknown samplers fall back
    /// to that main texture.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="locations">Uniform locations of the program.</param>
    /// <param name="main">The stage's main frame texture.</param>
    /// <param name="program">The program whose active samplers are bound.</param>
    private void BindShaderSamplers(
        GlInterface gl,
        int program,
        Dictionary<string, int> locations,
        int main)
    {
        var nextUnit = 0;
        foreach (var name in program == _warpShaderProgram ? _warpSamplerNames : _compSamplerNames)
        {
            if (!locations.TryGetValue(name, out var location))
                locations[name] = location = gl.GetUniformLocationString(program, name);
            if (location < 0) continue;
            if (nextUnit >= 16) throw new InvalidOperationException("Shader exceeds the supported texture unit count.");
            var parsed = ShaderSamplerName.Parse(name);
            var unit = nextUnit++;
            // Make this sampler's unit active before resolving the texture. A user texture is
            // uploaded lazily here, and an upload binds its new texture to the active unit: if the
            // active unit were still the previous sampler's, the upload would overwrite that
            // sampler's binding. Unused samplers are skipped above, so the previous unit can be
            // sampler_main's and the user texture then aliased it.
            gl.ActiveTexture(GlTexture0 + unit);
            var texture = parsed.BaseName switch
            {
                "main" => main,
                "blur1" => _shaderBlurTexture[0],
                "blur2" => _shaderBlurTexture[1],
                "blur3" => _shaderBlurTexture[2],
                _ => ResolveGeneratedOrUserSampler(gl, name, parsed.BaseName, parsed, main)
            };
            gl.BindTexture(GlTexture2D, texture);
            var mode = (parsed.Nearest ? 1 : 0) | (parsed.Wrap == VisualizerTextureWrap.Repeat ? 2 : 0);
            if (parsed.BaseName.StartsWith("blur", StringComparison.Ordinal)) mode = 0;
            _bindSampler?.Invoke(unit, _samplerStates[mode]);
            gl.Uniform1i(location, unit);
        }
    }

    /// <summary>
    /// Resolves a shader sampler that is neither a main frame nor a blur level: a generated noise or
    /// volume texture, a preset texture file loaded from the textures folder (as MilkDrop loads
    /// <c>textures/&lt;name&gt;.jpg</c>), or the frame fallback.
    /// </summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="name">Sampler name as the shader declares it.</param>
    /// <param name="baseName">Sampler name without its filtering/wrap qualifier.</param>
    /// <param name="parsed">Parsed sampler qualifier.</param>
    /// <param name="main">The stage's main frame texture.</param>
    /// <returns>The texture handle to bind.</returns>
    private int ResolveGeneratedOrUserSampler(
        GlInterface gl, string name, string baseName, ShaderSamplerName parsed, int main)
    {
        if (_samplerTextures.TryGetValue(name, out var generated) ||
            _samplerTextures.TryGetValue("sampler_" + baseName, out generated))
            return generated;

        if (VisualizerUserTextures.TryGet(baseName, out var user))
        {
            var buffer = user.Buffer;
            var texture = UploadTexture(
                gl, buffer.Pixels, buffer.Width, buffer.Height,
                parsed.Wrap == VisualizerTextureWrap.Clamp, parsed.Nearest);
            _samplerTextures[name] = texture;
            return texture;
        }

        return main;
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
        gl.TexImage2D(GlTexture2D, 0, _halfFloat ? GlRgba16f : GlRgba8, width, height, 0, GlRgba,
            _halfFloat ? GlHalfFloat : GlUnsignedByte, IntPtr.Zero);

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

    /// <summary>Fills the vertex buffer with the grid positions and both presets' mesh motion.</summary>
    /// <param name="motion">Per-vertex motion of the incoming preset.</param>
    /// <param name="oldMotion">
    /// Per-vertex motion of the outgoing preset, or <see langword="null"/> when no blend runs. The
    /// vertex shader ignores it while <c>uBlend</c> is one.
    /// </param>
    /// <param name="meshX">Mesh grid columns.</param>
    /// <param name="meshY">Mesh grid rows.</param>
    private void PackVertices(float[] motion, float[]? oldMotion, int meshX, int meshY)
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
                {
                    _vertices[target + 2 + value] = motion[source + value];
                    _vertices[target + 2 + PresetRenderer.MeshValues + value] =
                        oldMotion is not null && source + value < oldMotion.Length ? oldMotion[source + value] : 0f;
                }

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



