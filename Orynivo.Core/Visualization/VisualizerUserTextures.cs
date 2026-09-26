using SkiaSharp;

namespace Orynivo.Visualization;

/// <summary>
/// One image texture a preset asks for by name. A shader declaration such as
/// <c>sampler sampler_seaweed;</c> resolves to <c>seaweed.jpg</c>, exactly as MilkDrop loads
/// <c>textures/&lt;name&gt;.jpg|.png|.tga</c>.
/// </summary>
public sealed class VisualizerUserTexture
{
    /// <summary>Initializes a preset texture.</summary>
    /// <param name="name">Texture name without extension, as the shader names it.</param>
    /// <param name="buffer">Decoded RGBA pixels.</param>
    public VisualizerUserTexture(string name, PixelBuffer buffer)
    {
        Name = name;
        Buffer = buffer;
    }

    /// <summary>Gets the texture name without extension.</summary>
    public string Name { get; }

    /// <summary>Gets the decoded RGBA pixel buffer.</summary>
    public PixelBuffer Buffer { get; }
}

/// <summary>
/// Loads the image files a preset references by sampler name from the configured texture folders,
/// matching MilkDrop's convention of a <c>textures</c> folder beside the presets. Orynivo ships no
/// third-party textures, so a name without a file stays unresolved and keeps the frame fallback.
/// </summary>
public static class VisualizerUserTextures
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, VisualizerUserTexture?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tga"];
    private static string[] _directories = [];

    /// <summary>
    /// Sets the folders a preset texture name is searched in. MilkDrop keeps a <c>textures</c> folder
    /// beside its presets, so callers normally pass the preset folder and its <c>textures</c> child.
    /// </summary>
    /// <param name="directories">Folders to search, or <see langword="null"/> for none.</param>
    public static void SetDirectories(IEnumerable<string>? directories)
    {
        lock (Gate)
        {
            _directories = (directories ?? [])
                .Where(static directory => !string.IsNullOrWhiteSpace(directory))
                .Select(static directory => directory.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Cache.Clear();
        }
    }

    /// <summary>Resolves a preset texture by name, loading and caching it on first use.</summary>
    /// <param name="name">Texture name without extension.</param>
    /// <param name="texture">Receives the loaded texture.</param>
    /// <returns><see langword="true"/> when a texture file was found.</returns>
    public static bool TryGet(string name, out VisualizerUserTexture texture)
    {
        texture = null!;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        VisualizerUserTexture? loaded;
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out loaded))
            {
                if (loaded is null)
                    return false;
                texture = loaded;
                return true;
            }
        }

        loaded = Load(name);
        lock (Gate)
            Cache[name] = loaded;

        if (loaded is null)
            return false;
        texture = loaded;
        return true;
    }

    /// <summary>Loads one texture image from the configured folders.</summary>
    /// <param name="name">Texture name without extension.</param>
    /// <returns>The decoded texture, or <see langword="null"/> when no readable file exists.</returns>
    private static VisualizerUserTexture? Load(string name)
    {
        string[] directories;
        lock (Gate)
            directories = _directories;

        foreach (var directory in directories)
        {
            foreach (var extension in Extensions)
            {
                var path = Path.Combine(directory, name + extension);
                if (!File.Exists(path))
                    continue;

                try
                {
                    using var bitmap = SKBitmap.Decode(path);
                    if (bitmap is null || bitmap.Width < 1 || bitmap.Height < 1)
                        continue;

                    using var rgba = bitmap.Copy(SKColorType.Rgba8888);
                    if (rgba is null)
                        continue;

                    var buffer = new PixelBuffer(rgba.Width, rgba.Height);
                    var destination = buffer.Pixels;
                    for (var y = 0; y < rgba.Height; y++)
                    {
                        for (var x = 0; x < rgba.Width; x++)
                        {
                            var colour = rgba.GetPixel(x, y);
                            var offset = (((y * rgba.Width) + x) * 4);
                            destination[offset] = colour.Red / 255f;
                            destination[offset + 1] = colour.Green / 255f;
                            destination[offset + 2] = colour.Blue / 255f;
                            destination[offset + 3] = colour.Alpha / 255f;
                        }
                    }

                    return new VisualizerUserTexture(name, buffer);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }
        }

        return null;
    }
}
