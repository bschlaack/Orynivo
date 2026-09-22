using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;

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

            // GLES has no core BGRA texture format, so the swap happens here. The frame is the render
            // resolution, not the window, so this stays small.
            for (var index = 0; index + 3 < required; index += 4)
            {
                _rgba[index] = bgra[index + 2];
                _rgba[index + 1] = bgra[index + 1];
                _rgba[index + 2] = bgra[index];
                _rgba[index + 3] = bgra[index + 3];
            }

            _frameWidth = width;
            _frameHeight = height;
            _hasFrame = true;
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

        gl.DeleteProgram(_program);
        gl.DeleteTexture(_texture);
        gl.DeleteVertexArray(_vertexArray);
        gl.DeleteBuffer(_vertexBuffer);
        _glReady = false;
    }

    /// <inheritdoc/>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_glReady)
            return;

        try
        {
            byte[]? frame = null;
            int frameWidth, frameHeight;
            lock (_frameLock)
            {
                if (_hasFrame)
                {
                    frame = _rgba;
                    frameWidth = _frameWidth;
                    frameHeight = _frameHeight;
                    _hasFrame = false;
                }
                else
                {
                    frameWidth = 0;
                    frameHeight = 0;
                }
            }

            if (frame is not null && frameWidth > 0 && frameHeight > 0)
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
                        frameWidth,
                        frameHeight,
                        0,
                        GlRgba,
                        GlUnsignedByte,
                        handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }

                gl.BindFramebuffer(0x8D40, fb);
                gl.Viewport(0, 0, Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height));
                gl.ClearColor(0f, 0f, 0f, 1f);
                gl.Clear(GlColorBufferBit);
                gl.UseProgram(_program);
                if (_textureUniform >= 0)
                    gl.Uniform1i(_textureUniform, 0);
                gl.BindVertexArray(_vertexArray);
                gl.DrawArrays(GlTriangleStrip, 0, 4);
                gl.BindVertexArray(0);
                gl.Flush();
                Frames++;
            }
        }
        catch (Exception exception)
        {
            GlError = $"{exception.GetType().Name}: {exception.Message}";
        }

        // The render thread publishes frames independently, so the context keeps asking for one.
        RequestNextFrameRendering();
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
