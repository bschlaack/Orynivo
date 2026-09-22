using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;

namespace Orynivo.Controls;

/// <summary>
/// Proves that the desktop can obtain an OpenGL context inside the visual tree, which is the
/// prerequisite for moving the preset pipeline off the CPU. It draws nothing but a colour ramp that
/// advances with the frame, so it exercises the context without depending on the renderer.
/// </summary>
/// <remarks>
/// This is a capability probe, not a renderer. It is shown only when
/// <c>ORYNIVO_VISUALIZER_OPENGL_PROBE</c> is set, because whether Avalonia hands out a GL context
/// depends on the platform and its graphics backend; the visualizer keeps its normal bitmap
/// presentation until the probe has been confirmed on every supported platform.
/// </remarks>
public sealed class VisualizerGlProbe : OpenGlControlBase
{
    /// <summary>Reports the negotiated GL version once the context is up.</summary>
    public string? GlInfo { get; private set; }

    /// <summary>Reports why the context could not be used, or <see langword="null"/> when it worked.</summary>
    public string? GlError { get; private set; }

    /// <summary>Gets the number of frames the context has drawn.</summary>
    public int Frames { get; private set; }

    /// <summary>Called when the control draws one frame.</summary>
    public event Action? FrameRendered;

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            GlInfo = $"GL {GlVersion}";
        }
        catch (Exception exception)
        {
            GlError = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    /// <inheritdoc/>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
    }

    /// <inheritdoc/>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        try
        {
            var width = Math.Max(1, (int)Bounds.Width);
            var height = Math.Max(1, (int)Bounds.Height);
            gl.Viewport(0, 0, width, height);
            // A ramp that changes every frame: a still picture would not prove the loop runs.
            var phase = (Frames % 120) / 120f;
            gl.ClearColor(phase, 1f - phase, 0.25f + (phase * 0.5f), 1f);
            gl.Clear(GlConsts.ColorBufferBit);
            gl.Flush();
            Frames++;
            FrameRendered?.Invoke();
        }
        catch (Exception exception)
        {
            GlError = $"{exception.GetType().Name}: {exception.Message}";
        }

        // Keep the probe animating so the frame counter advances.
        RequestNextFrameRendering();
    }

    /// <summary>The handful of GL constants the probe needs.</summary>
    private static class GlConsts
    {
        /// <summary><c>GL_COLOR_BUFFER_BIT</c>.</summary>
        public const int ColorBufferBit = 0x4000;
    }
}
