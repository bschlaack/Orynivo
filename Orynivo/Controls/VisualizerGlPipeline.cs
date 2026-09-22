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
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlLinear = 0x2601;
    private const int GlClampToEdge = 0x812F;
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
        out vec3 vMotion0;
        out vec3 vMotion1;
        out vec3 vMotion2;
        void main()
        {
            vMotion0 = aMotion0;
            vMotion1 = aMotion1;
            vMotion2 = aMotion2;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    /// <summary>
    /// The warp sampling position, translated from <see cref="WarpSampling.SamplePosition"/>. The
    /// engine's coordinates are top-down, so the row is measured from the top and the source texture,
    /// which is bottom-up, is sampled with a flipped v.
    /// </summary>
    private const string WarpFragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec3 vMotion0;
        in vec3 vMotion1;
        in vec3 vMotion2;
        out vec4 fragColor;
        uniform sampler2D uSource;
        uniform float uFrameWidth;
        uniform float uFrameHeight;
        uniform float uNeedsRadius;
        void main()
        {
            float zoom = max(0.01, vMotion0.x);
            float zoomExp = vMotion0.y;
            float rotation = vMotion0.z;
            float cx = vMotion1.x;
            float cy = vMotion1.y;
            float dx = vMotion1.z;
            float dy = vMotion2.x;
            float sx = vMotion2.y;
            float sy = vMotion2.z;

            float column = floor(gl_FragCoord.x);
            float row = uFrameHeight - 1.0 - floor(gl_FragCoord.y);
            vec2 normalized = vec2(
                (column / max(uFrameWidth - 1.0, 1.0)) * 2.0 - 1.0,
                (row / max(uFrameHeight - 1.0, 1.0)) * 2.0 - 1.0);

            vec2 warped = (normalized - vec2(cx, cy)) * vec2(sx, sy);
            float cosine = cos(rotation);
            float sine = sin(rotation);
            vec2 rotated = vec2(
                (warped.x * cosine) - (warped.y * sine),
                (warped.x * sine) + (warped.y * cosine));

            float pixelZoom = zoom;
            if (uNeedsRadius > 0.5 && zoomExp != 1.0)
            {
                pixelZoom = pow(zoom, 1.0 + (zoomExp * length(rotated) * 2.0));
            }

            vec2 samplePosition = (rotated * pixelZoom) + vec2(cx, cy) + vec2(dx, dy);
            vec2 uv = (samplePosition * 0.5) + 0.5;

            // Outside the frame the warp is transparent black, like the CPU sampler.
            vec4 colour = texture(uSource, vec2(uv.x, 1.0 - uv.y));
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            fragColor = colour * inside;
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

            fragColor = sum / 9.0;
        }
        """;

    /// <summary>
    /// The post-processing chain in the CPU order: decay, video echo, centre darkening, borders,
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

        void main()
        {
            vec4 colour = texture(uSource, vUv);
            colour *= uDecay;

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

            if (uGamma != 1.0)
                colour.rgb = pow(max(colour.rgb, 0.0), vec3(uGamma));

            colour += texture(uOverlay, vUv);
            fragColor = clamp(colour, 0.0, 1.0);
        }
        """;

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
    public int OutputTexture => _feedbackTexture;

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

            _warpUniforms = Uniforms(gl, _warpProgram, ["uSource", "uFrameWidth", "uFrameHeight", "uNeedsRadius"]);
            _blurUniforms = Uniforms(gl, _blurProgram, ["uSource", "uTexelX", "uTexelY"]);
            _postUniforms = Uniforms(
                gl,
                _postProgram,
                [
                    "uSource", "uOverlay", "uDecay", "uEchoZoom", "uEchoAlpha", "uEchoOrientation",
                    "uDarken", "uGamma", "uOuterInset", "uOuterThickness", "uOuterR", "uOuterG",
                    "uOuterB", "uOuterA", "uInnerInset", "uInnerThickness", "uInnerR", "uInnerG",
                    "uInnerB", "uInnerA", "uFrameWidth", "uFrameHeight", "uSmaller"
                ]);
            _quadTextureUniform = gl.GetUniformLocationString(_quadProgram, "uSource");

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
        if (!_ready)
            return;

        foreach (var program in new[] { _quadProgram, _warpProgram, _blurProgram, _postProgram })
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
        VisualizerFrameParameters parameters)
    {
        if (!_ready || frameWidth <= 0 || frameHeight <= 0)
            return false;

        try
        {
            EnsureSize(gl, frameWidth, frameHeight);
            UploadOverlay(gl, overlayBgra, frameWidth, frameHeight);
            PackVertices(mesh, meshX, meshY);

            // Warp the feedback into ping zero.
            gl.BindFramebuffer(GlFramebuffer, _pingFramebuffer[0]);
            gl.Viewport(0, 0, frameWidth, frameHeight);
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(GlColorBufferBit);
            gl.UseProgram(_warpProgram);
            gl.ActiveTexture(GlTexture0);
            gl.BindTexture(GlTexture2D, _feedbackTexture);
            SetSampler(gl, _warpUniforms, "uSource", 0);
            Set(gl, _warpUniforms, "uFrameWidth", frameWidth);
            Set(gl, _warpUniforms, "uFrameHeight", frameHeight);
            Set(gl, _warpUniforms, "uNeedsRadius", needsRadius ? 1f : 0f);
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
            Set(gl, _postUniforms, "uDecay", parameters.Decay);
            Set(gl, _postUniforms, "uEchoZoom", parameters.EchoZoom);
            Set(gl, _postUniforms, "uEchoAlpha", parameters.EchoAlpha);
            Set(gl, _postUniforms, "uEchoOrientation", parameters.EchoOrientation);
            Set(gl, _postUniforms, "uDarken", parameters.DarkenCenter);
            Set(gl, _postUniforms, "uGamma", parameters.Gamma);
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

            // Present the finished frame, which is also the next frame's feedback.
            gl.BindFramebuffer(GlFramebuffer, framebuffer);
            gl.Viewport(0, 0, Math.Max(1, viewportWidth), Math.Max(1, viewportHeight));
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(GlColorBufferBit);
            gl.UseProgram(_quadProgram);
            gl.ActiveTexture(GlTexture0);
            gl.BindTexture(GlTexture2D, _feedbackTexture);
            if (_quadTextureUniform >= 0)
                gl.Uniform1i(_quadTextureUniform, 0);
            DrawQuad(gl);

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
    }

    /// <summary>Draws the static full-screen quad.</summary>
    /// <param name="gl">GL interface.</param>
    private void DrawQuad(GlInterface gl)
    {
        gl.BindVertexArray(_quadVertexArray);
        gl.DrawArrays(GlTriangleStrip, 0, 4);
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



