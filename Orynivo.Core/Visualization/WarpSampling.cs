namespace Orynivo.Visualization;

/// <summary>
/// The sampling position of the warp stage, from the motion values the preset produced for a mesh
/// vertex. It is the single definition of that arithmetic: the CPU warp calls it per pixel and the
/// GPU warp's fragment shader is its translation, so the two paths cannot drift apart.
/// </summary>
public static class WarpSampling
{
    /// <summary>The lowest zoom a preset may ask for, matching the reference's clamp.</summary>
    public const float MinimumZoom = 0.01f;

    /// <summary>
    /// Computes where the warp reads the frame for one position. The position is in
    /// minus-one-to-one space, which is the engine's own convention for both the frame position and
    /// the result.
    /// </summary>
    /// <param name="normalizedX">Frame position x, minus one to one.</param>
    /// <param name="normalizedY">Frame position y, minus one to one.</param>
    /// <param name="zoom">Zoom factor.</param>
    /// <param name="zoomExp">Zoom exponent; one means no radial zoom.</param>
    /// <param name="rotation">Rotation in radians.</param>
    /// <param name="centreX">Rotation and zoom centre x.</param>
    /// <param name="centreY">Rotation and zoom centre y.</param>
    /// <param name="offsetX">Translation x.</param>
    /// <param name="offsetY">Translation y.</param>
    /// <param name="stretchX">Horizontal stretch.</param>
    /// <param name="stretchY">Vertical stretch.</param>
    /// <param name="needsRadius">Whether the radial zoom term applies at all.</param>
    /// <param name="sampleX">Receives the sampling position x.</param>
    /// <param name="sampleY">Receives the sampling position y.</param>
    public static void SamplePosition(
        float normalizedX,
        float normalizedY,
        float zoom,
        float zoomExp,
        float rotation,
        float centreX,
        float centreY,
        float offsetX,
        float offsetY,
        float stretchX,
        float stretchY,
        bool needsRadius,
        out float sampleX,
        out float sampleY)
    {
        var warpedX = (normalizedX - centreX) * stretchX;
        var warpedY = (normalizedY - centreY) * stretchY;
        var cosine = MathF.Cos(rotation);
        var sine = MathF.Sin(rotation);
        var rotatedX = (warpedX * cosine) - (warpedY * sine);
        var rotatedY = (warpedX * sine) + (warpedY * cosine);

        var pixelZoom = zoom;
        if (needsRadius && zoomExp != 1f)
        {
            var radius = MathF.Sqrt((rotatedX * rotatedX) + (rotatedY * rotatedY));
            pixelZoom = MathF.Pow(zoom, 1f + (zoomExp * radius * 2f));
        }

        sampleX = (rotatedX * pixelZoom) + centreX + offsetX;
        sampleY = (rotatedY * pixelZoom) + centreY + offsetY;
    }
}

