namespace Orynivo.Visualization;

/// <summary>
/// The sampling position of the warp stage, from the motion values the preset produced for a mesh
/// vertex. It is the single definition of that arithmetic: the CPU warp calls it per pixel and the
/// GPU warp's fragment shader is its translation, so the two paths cannot drift apart.
/// <para>
/// The arithmetic is a translation of the reference implementation's warp vertex shader
/// (<c>PresetWarpVertexShaderGlsl330.vert</c>): the position is scaled by the aspect, divided by the
/// radial zoom, stretched, rotated, translated, and scaled back by the inverse aspect. The result is
/// in the engine's minus-one-to-one space, which is what the frame sampler expects.
/// </para>
/// </summary>
public static class WarpSampling
{
    /// <summary>The lowest zoom a preset may ask for, matching the reference's clamp.</summary>
    public const float MinimumZoom = 0.01f;

    /// <summary>
    /// Computes the warp's aspect factors for a frame size. The reference keeps both factors at or
    /// below one, so a landscape frame is <c>(1, height/width)</c> and a portrait frame is
    /// <c>(width/height, 1)</c>; the smaller ratio is always the one that is scaled.
    /// </summary>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="aspectX">Receives the horizontal aspect factor.</param>
    /// <param name="aspectY">Receives the vertical aspect factor.</param>
    public static void GetAspect(float width, float height, out float aspectX, out float aspectY)
    {
        aspectX = height > width ? width / height : 1f;
        aspectY = width > height ? height / width : 1f;
    }

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
    /// <param name="centreX">Rotation and zoom centre x, in the aspect-scaled space.</param>
    /// <param name="centreY">Rotation and zoom centre y, in the aspect-scaled space.</param>
    /// <param name="offsetX">Translation x, in the aspect-scaled space.</param>
    /// <param name="offsetY">Translation y, in the aspect-scaled space.</param>
    /// <param name="stretchX">Horizontal stretch; the position is divided by it.</param>
    /// <param name="stretchY">Vertical stretch; the position is divided by it.</param>
    /// <param name="aspectX">Horizontal aspect factor.</param>
    /// <param name="aspectY">Vertical aspect factor.</param>
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
        float aspectX,
        float aspectY,
        bool needsRadius,
        out float sampleX,
        out float sampleY)
    {
        // The reference divides by the radial zoom and the stretch, and subtracts the translation,
        // because it transforms the sampling coordinate the other way around.
        var radius = needsRadius
            ? MathF.Sqrt(((normalizedX * aspectX) * (normalizedX * aspectX)) + ((normalizedY * aspectY) * (normalizedY * aspectY)))
            : 0f;
        var radialZoom = needsRadius && zoomExp != 1f
            ? MathF.Pow(zoom, MathF.Pow(zoomExp, (radius * 2f) - 1f))
            : zoom;
        var inverseZoom = 1f / MathF.Max(MinimumZoom, radialZoom);

        var u = (normalizedX * aspectX * 0.5f * inverseZoom) + 0.5f;
        var v = (normalizedY * aspectY * 0.5f * inverseZoom) + 0.5f;
        u = ((u - centreX) / stretchX) + centreX;
        v = ((v - centreY) / stretchY) + centreY;

        var cosine = MathF.Cos(rotation);
        var sine = MathF.Sin(rotation);
        var rotatedU = u - centreX;
        var rotatedV = v - centreY;
        u = (rotatedU * cosine) - (rotatedV * sine) + centreX;
        v = (rotatedU * sine) + (rotatedV * cosine) + centreY;

        u -= offsetX;
        v -= offsetY;
        u = ((u - 0.5f) / aspectX) + 0.5f;
        v = ((v - 0.5f) / aspectY) + 0.5f;

        sampleX = (u * 2f) - 1f;
        sampleY = (v * 2f) - 1f;
    }
}
