using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Orynivo.Visualization;

namespace Orynivo.Controls;

/// <summary>
/// Presents a rendered frame through OpenGL inside the visual tree instead of uploading it to a
/// <c>WriteableBitmap</c>. The preset pipeline still renders on the CPU; this is the first step of
/// roadmap 40f, and it proves the context, the shader compilation, the vertex buffer, and the
/// texture upload that the later steps build on.
/// </summary>
/// <remarks>
/// It is shown only when <c>ORYNIVO_VISUALIZER_OPENGL</c> is set, because whether Avalonia hands out
/// a GL context depends on the platform and its graphics backend; the visualizer keeps its bitmap
/// presentation until the GL path has been confirmed on every supported platform. The context is
/// OpenGL ES 3.0 through ANGLE on Windows.
/// </remarks>
public sealed class VisualizerGlPresenter : OpenGlControlBase
{
    private const int GlColorBufferBit = 0x4000;
    private const int GlFramebuffer = 0x8D40;
    private const int GlTexture2D = 0x0DE1;
    private const int GlTexture0 = 0x84C0;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlLinear = 0x2601;
    private const int GlClampToEdge = 0x812F;
    private const int GlRgba = 0x1908;
    private const int GlRgba8 = 0x8058;
    private const int GlUnsignedByte = 0x1401;
    private const int GlVertexShader = 0x8B31;
    private const int GlFragmentShader = 0x8B30;
    private const int GlArrayBuffer = 0x8892;
    private const int GlStaticDraw = 0x88E4;
    private const int GlFloat = 0x1406;
    private const int GlTriangleStrip = 0x0005;

    /// <summary>
    /// OpenGL ES 3.0, which is what ANGLE negotiates on Windows and what a desktop GL 3.3 context
    /// accepts as well; the version is chosen from the negotiated context so other platforms can
    /// pick a different preamble.
    /// </summary>
    private const string VertexSource = """
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

    private const string FragmentSource = """
        #version 300 es
        precision highp float;
        precision highp sampler2D;
        in vec2 vUv;
        out vec4 fragColor;
        uniform sampler2D uTexture;
        void main()
        {
            fragColor = texture(uTexture, vUv);
        }
        """;

    private static readonly float[] QuadVertices = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];

    private readonly object _frameLock = new();
    private byte[] _rgba = [];
    private int _frameWidth;
    private int _frameHeight;
    private bool _hasFrame;

    private int _texture;
    private int _program;
    private int _vertexArray;
    private int _vertexBuffer;
    private int _textureUniform = -1;
    /// <summary>The texture last drawn, re-presented on a refresh that published no frame.</summary>
    private int _lastPresented;
    private bool _glReady;

    /// <summary>Gets the negotiated GL version, once the context is up.</summary>
    public string? GlInfo { get; private set; }

    /// <summary>Gets why the GL path could not be used, or <see langword="null"/> when it worked.</summary>
    public string? GlError { get; private set; }

    /// <summary>Gets the number of frames the context has drawn.</summary>
    public int Frames { get; private set; }

    /// <summary>Publishes the next frame. The buffer is tightly packed BGRA, top row first.</summary>
    /// <param name="bgra">Frame pixels.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    public void SetFrame(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            return;

        lock (_frameLock)
        {
            var required = width * height * 4;
            if (_rgba.Length != required)
                _rgba = new byte[required];

            // GLES has no core BGRA texture format, so the swap happens here. The rows are also
            // reversed: a texture's first row is its bottom in OpenGL, and the present shader maps v
            // zero to the bottom of the screen, so an unreversed upload would mirror the picture.
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                var source = y * stride;
                var target = (height - 1 - y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var from = source + (x * 4);
                    var to = target + (x * 4);
                    _rgba[to] = bgra[from + 2];
                    _rgba[to + 1] = bgra[from + 1];
                    _rgba[to + 2] = bgra[from];
                    _rgba[to + 3] = bgra[from + 3];
                }
            }

            _frameWidth = width;
            _frameHeight = height;
            _hasFrame = true;
        }
    }

    private byte[]? _pipelineOverlay;
    private float[]? _pipelineMesh;
    private int _pipelineMeshX;
    private int _pipelineMeshY;
    private VisualizerFrameParameters _pipelineParameters;
    private int _pipelineFrameWidth;
    private int _pipelineFrameHeight;
    private string? _pipelineWarpShader;
    private string? _pipelineCompShader;
    private IReadOnlyDictionary<string, ShaderValue>? _pipelineUniforms;
    private bool _pipelinePending;
    private bool _pipelineFailed;
    private readonly VisualizerGlPipeline _pipeline = new();

    /// <summary>
    /// Gets a value indicating whether the GPU pipeline failed and the caller should fall back to the
    /// CPU frame path.
    /// </summary>
    public bool PipelineFailed => _pipelineFailed;

    /// <summary>Gets the pipeline's one-shot first-frame description, or <see langword="null"/>.</summary>
    public string? PipelineDiagnostics => _pipeline.Diagnostics;

    /// <summary>Gets why an emitted shader failed to build, or <see langword="null"/>.</summary>
    public string? ShaderError => _pipeline.ShaderError;

    /// <summary>
    /// Publishes the CPU half of a GPU frame: the overlay, the per-vertex mesh, the frame's pass
    /// values, and, when the preset has shaders, the emitted GLSL and the uniforms it reads. The GPU
    /// then owns the warp, the passes, and the composite.
    /// </summary>
    /// <param name="overlayBgra">Overlay frame, tightly packed BGRA.</param>
    /// <param name="frameWidth">Render width.</param>
    /// <param name="frameHeight">Render height.</param>
    /// <param name="mesh">Per-vertex mesh motion.</param>
    /// <param name="meshX">Mesh grid columns.</param>
    /// <param name="meshY">Mesh grid rows.</param>
    /// <param name="parameters">The frame's pass values.</param>
    /// <param name="warpShader">Emitted GLSL for the warp shader, or <see langword="null"/>.</param>
    /// <param name="compShader">Emitted GLSL for the comp shader, or <see langword="null"/>.</param>
    /// <param name="uniforms">Shader uniforms to seed, or <see langword="null"/>.</param>
    public void SetPipeline(
        byte[] overlayBgra,
        int frameWidth,
        int frameHeight,
        float[] mesh,
        int meshX,
        int meshY,
        VisualizerFrameParameters parameters,
        string? warpShader = null,
        string? compShader = null,
        IReadOnlyDictionary<string, ShaderValue>? uniforms = null)
    {
        lock (_frameLock)
        {
            _pipelineOverlay = overlayBgra;
            _pipelineFrameWidth = frameWidth;
            _pipelineFrameHeight = frameHeight;
            _pipelineMesh = mesh;
            _pipelineMeshX = meshX;
            _pipelineMeshY = meshY;
            _pipelineParameters = parameters;
            _pipelineWarpShader = warpShader;
            _pipelineCompShader = compShader;
            _pipelineUniforms = uniforms;
            _pipelinePending = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            GlInfo = $"GL {GlVersion}";
            _program = BuildProgram(gl);
            _textureUniform = gl.GetUniformLocationString(_program, "uTexture");
            _texture = gl.GenTexture();
            _vertexArray = gl.GenVertexArray();
            _vertexBuffer = gl.GenBuffer();

            gl.BindVertexArray(_vertexArray);
            gl.BindBuffer(GlArrayBuffer, _vertexBuffer);
            var handle = GCHandle.Alloc(QuadVertices, GCHandleType.Pinned);
            try
            {
                gl.BufferData(
                    GlArrayBuffer,
                    (IntPtr)(QuadVertices.Length * sizeof(float)),
                    handle.AddrOfPinnedObject(),
                    GlStaticDraw);
            }
            finally
            {
                handle.Free();
            }

            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GlFloat, 0, 2 * sizeof(float), IntPtr.Zero);
            gl.BindVertexArray(0);
            if (!_pipeline.Init(gl) && _pipeline.Error is { Length: > 0 } pipelineError)
                GlError = pipelineError;
            _glReady = true;
        }
        catch (Exception exception)
        {
            GlError = $"{exception.GetType().Name}: {exception.Message}";
            _glReady = false;
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (!_glReady)
            return;

        _pipeline.Dispose(gl);
        gl.DeleteProgram(_program);
        gl.DeleteTexture(_texture);
        gl.DeleteVertexArray(_vertexArray);
        gl.DeleteBuffer(_vertexBuffer);
        _glReady = false;
    }

    /// <summary>Gets the framebuffer size in physical pixels, which is not the control's bounds.</summary>
    /// <returns>The framebuffer width and height.</returns>
    private (int Width, int Height) FramebufferSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        return (
            Math.Max(1, (int)Math.Round(Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Round(Bounds.Height * scaling)));
    }

    /// <summary>Draws one texture over the control's framebuffer.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="framebuffer">Destination framebuffer, or zero for the window.</param>
    /// <param name="texture">Texture to draw.</param>
    private void PresentTexture(GlInterface gl, int framebuffer, int texture)
    {
        var viewport = FramebufferSize();
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, viewport.Width, viewport.Height);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(GlColorBufferBit);
        gl.UseProgram(_program);
        gl.ActiveTexture(GlTexture0);
        gl.BindTexture(GlTexture2D, texture);
        if (_textureUniform >= 0)
            gl.Uniform1i(_textureUniform, 0);
        gl.BindVertexArray(_vertexArray);
        gl.DrawArrays(GlTriangleStrip, 0, 4);
        gl.BindVertexArray(0);
        gl.Flush();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_glReady)
            return;

        try
        {
            // A pending pipeline frame is the whole picture: the GPU warps, post-processes, and
            // composites, and the CPU only supplied the overlay, the mesh, and the pass values.
            byte[]? pipelineOverlay = null;
            float[]? pipelineMesh = null;
            int pipelineMeshX = 0, pipelineMeshY = 0, pipelineWidth = 0, pipelineHeight = 0;
            VisualizerFrameParameters pipelineParameters = default;
            string? pipelineWarpShader = null, pipelineCompShader = null;
            IReadOnlyDictionary<string, ShaderValue>? pipelineUniforms = null;
            byte[]? frame = null;
            int frameWidth = 0, frameHeight = 0;
            lock (_frameLock)
            {
                if (_pipelinePending)
                {
                    pipelineOverlay = _pipelineOverlay;
                    pipelineMesh = _pipelineMesh;
                    pipelineMeshX = _pipelineMeshX;
                    pipelineMeshY = _pipelineMeshY;
                    pipelineWidth = _pipelineFrameWidth;
                    pipelineHeight = _pipelineFrameHeight;
                    pipelineParameters = _pipelineParameters;
                    pipelineWarpShader = _pipelineWarpShader;
                    pipelineCompShader = _pipelineCompShader;
                    pipelineUniforms = _pipelineUniforms;
                    _pipelinePending = false;
                }
                else if (_hasFrame)
                {
                    frame = _rgba;
                    frameWidth = _frameWidth;
                    frameHeight = _frameHeight;
                    _hasFrame = false;
                }
            }

            if (pipelineOverlay is not null && pipelineMesh is not null && pipelineWidth > 0 && pipelineHeight > 0)
            {
                var pipelineViewport = FramebufferSize();
                _pipeline.SetShaders(pipelineWarpShader, pipelineCompShader);
                if (_pipeline.Render(
                    gl,
                    fb,
                    pipelineViewport.Width,
                    pipelineViewport.Height,
                    pipelineWidth,
                    pipelineHeight,
                    pipelineOverlay,
                    pipelineMesh,
                    pipelineMeshX,
                    pipelineMeshY,
                    true,
                    pipelineParameters,
                    pipelineUniforms))
                {
                    _lastPresented = _pipeline.OutputTexture;
                    Frames++;
                }
                else
                {
                    // A failed pipeline is fatal for the GL path: the caller falls back to the CPU
                    // frame, because only the overlay was published for this frame.
                    _pipelineFailed = true;
                    GlError = _pipeline.Error ?? "the GL pipeline failed";
                }
            }
            else if (frame is not null && frameWidth > 0 && frameHeight > 0)
            {
                UploadFrame(gl, frame, frameWidth, frameHeight);
                _lastPresented = _texture;
                Frames++;
            }

            // A frame is drawn on every refresh, even when nothing new was published. The control's
            // surface is double buffered, so leaving a refresh undrawn swaps to a buffer that is two
            // presentations old, which shows as the picture jumping backwards.
            if (_lastPresented != 0)
                PresentTexture(gl, fb, _lastPresented);
        }
        catch (Exception exception)
        {
            GlError = $"{exception.GetType().Name}: {exception.Message}";
        }

        // The render thread publishes frames independently, so the context keeps asking for one.
        RequestNextFrameRendering();
    }

    /// <summary>Uploads a finished frame as the presentation texture.</summary>
    /// <param name="gl">GL interface.</param>
    /// <param name="frame">RGBA frame, bottom row first.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private void UploadFrame(GlInterface gl, byte[] frame, int width, int height)
    {
        gl.ActiveTexture(GlTexture0);
        gl.BindTexture(GlTexture2D, _texture);
        gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
        gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
        gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
        var handle = GCHandle.Alloc(frame, GCHandleType.Pinned);
        try
        {
            // GlInterface exposes no TexSubImage2D, so the whole texture is re-specified.
            gl.TexImage2D(
                GlTexture2D,
                0,
                GlRgba8,
                width,
                height,
                0,
                GlRgba,
                GlUnsignedByte,
                handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
    /// <summary>Compiles and links the presentation shaders.</summary>
    /// <param name="gl">GL interface.</param>
    /// <returns>The linked program.</returns>
    /// <exception cref="InvalidOperationException">A shader or the program did not build.</exception>
    private static int BuildProgram(GlInterface gl)
    {
        var vertex = gl.CreateShader(GlVertexShader);
        var vertexError = gl.CompileShaderAndGetError(vertex, VertexSource);
        if (!string.IsNullOrWhiteSpace(vertexError))
            throw new InvalidOperationException("vertex shader: " + vertexError);

        var fragment = gl.CreateShader(GlFragmentShader);
        var fragmentError = gl.CompileShaderAndGetError(fragment, FragmentSource);
        if (!string.IsNullOrWhiteSpace(fragmentError))
            throw new InvalidOperationException("fragment shader: " + fragmentError);

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        var linkError = gl.LinkProgramAndGetError(program);
        if (!string.IsNullOrWhiteSpace(linkError))
            throw new InvalidOperationException("program: " + linkError);

        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        return program;
    }
}








