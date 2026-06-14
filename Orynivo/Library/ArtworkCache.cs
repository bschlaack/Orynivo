using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Orynivo.Library;

public static class ArtworkCache
{
    public sealed record StoredArtwork(string OriginalPath, string Thumb96Path, string Thumb320Path);

    private static readonly string Root = AppPaths.GetDataPath("artworks");

    public static StoredArtwork EnsureFiles(string hash, byte[] data, string? mimeType)
    {
        var ext = MimeToExtension(mimeType);
        var original = BuildPath("original", hash, ext);
        var thumb96 = BuildPath("thumb_96", hash, ".jpg");
        var thumb320 = BuildPath("thumb_320", hash, ".jpg");

        if (!File.Exists(original))
            File.WriteAllBytes(original, data);
        if (!File.Exists(thumb96))
            TryWriteThumbnail(data, thumb96, 96);
        if (!File.Exists(thumb320))
            TryWriteThumbnail(data, thumb320, 320);

        return new StoredArtwork(original, thumb96, thumb320);
    }

    private static string BuildPath(string bucket, string hash, string ext)
    {
        var dir = Path.Combine(Root, bucket);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, hash + ext);
    }

    private static void TryWriteThumbnail(byte[] data, string path, int size)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];
            var scale = Math.Min(size / (double)source.PixelWidth, size / (double)source.PixelHeight);
            if (scale > 1) scale = 1;
            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(transformed));
            using var output = File.Create(path);
            encoder.Save(output);
        }
        catch
        {
            // Manche eingebetteten Cover enthalten ungültige oder exotische Bitmap-Metadaten.
            // Das Original bleibt erhalten; die UI fällt für dieses Bild ohne Thumbnail zurück.
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string MimeToExtension(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
    };
}
