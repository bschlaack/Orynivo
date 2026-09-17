using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Orynivo.Controls;

/// <summary>
/// Derives a vibrant transport accent colour from album artwork and selects a
/// contrast-safe foreground colour for it.
/// </summary>
internal static class ArtworkAccentColor
{
    /// <summary>
    /// Extracts a vibrant accent color from a bitmap by sampling a small scaled
    /// copy, binning qualifying pixels by hue (weighted by saturation × value),
    /// and normalising the dominant hue into a punchy, readable accent.
    /// </summary>
    /// <param name="bitmap">Source artwork bitmap.</param>
    /// <returns>The extracted accent color, or <see langword="null"/> on failure or when the image is colourless.</returns>
    internal static Color? ExtractAccentColor(Bitmap bitmap)
    {
        try
        {
            const int dim = 24;
            using var small = bitmap.CreateScaledBitmap(new PixelSize(dim, dim), BitmapInterpolationMode.MediumQuality);
            var stride = dim * 4;
            var buffer = new byte[stride * dim];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                small.CopyPixels(new PixelRect(0, 0, dim, dim), handle.AddrOfPinnedObject(), buffer.Length, stride);
            }
            finally
            {
                handle.Free();
            }

            const int bins = 12;
            var weight = new double[bins];
            var rSum = new double[bins];
            var gSum = new double[bins];
            var bSum = new double[bins];

            for (var i = 0; i < buffer.Length; i += 4)
            {
                double b = buffer[i], g = buffer[i + 1], r = buffer[i + 2], a = buffer[i + 3];
                if (a < 32)
                    continue;

                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var value = max / 255.0;
                var sat = max <= 0 ? 0 : (max - min) / max;

                // Skip near-gray, near-black, and blown-out near-white pixels.
                if (sat < 0.18 || value < 0.18 || (value > 0.96 && sat < 0.25))
                    continue;

                var bin = (int)(RgbToHue(r, g, b) / 360.0 * bins) % bins;
                var w = sat * value;
                weight[bin] += w;
                rSum[bin] += r * w;
                gSum[bin] += g * w;
                bSum[bin] += b * w;
            }

            var best = -1;
            for (var i = 0; i < bins; i++)
                if (weight[i] > 0 && (best < 0 || weight[i] > weight[best]))
                    best = i;
            if (best < 0)
                return null;

            return AdjustAccent(rSum[best] / weight[best], gSum[best] / weight[best], bSum[best] / weight[best]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the hue (0–360) of an 8-bit RGB triple.</summary>
    /// <param name="r">Red component (0–255).</param>
    /// <param name="g">Green component (0–255).</param>
    /// <param name="b">Blue component (0–255).</param>
    /// <returns>The hue in degrees.</returns>
    internal static double RgbToHue(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta <= 0)
            return 0;

        double hue;
        if (max == r) hue = 60 * (((g - b) / delta) % 6);
        else if (max == g) hue = 60 * (((b - r) / delta) + 2);
        else hue = 60 * (((r - g) / delta) + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    /// <summary>Normalises an averaged RGB accent into a saturated, mid-bright colour.</summary>
    /// <param name="r">Red component (0–255).</param>
    /// <param name="g">Green component (0–255).</param>
    /// <param name="b">Blue component (0–255).</param>
    /// <returns>The adjusted accent color.</returns>
    internal static Color AdjustAccent(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var value = max / 255.0;
        var sat = max <= 0 ? 0 : (max - min) / max;
        var hue = RgbToHue(r, g, b);

        sat = Math.Clamp(sat * 1.15 + 0.1, 0.45, 1.0);
        value = Math.Clamp(value, 0.6, 0.86);
        return HsvToColor(hue, sat, value);
    }

    /// <summary>Converts an HSV triple to an opaque <see cref="Color"/>.</summary>
    /// <param name="h">Hue in degrees (0–360).</param>
    /// <param name="s">Saturation (0–1).</param>
    /// <param name="v">Value/brightness (0–1).</param>
    /// <returns>The resulting color.</returns>
    internal static Color HsvToColor(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb(
            (byte)Math.Clamp((r + m) * 255, 0, 255),
            (byte)Math.Clamp((g + m) * 255, 0, 255),
            (byte)Math.Clamp((b + m) * 255, 0, 255));
    }

    /// <summary>Returns a dark or light text colour with readable contrast over a background colour.</summary>
    /// <param name="background">The background colour behind the text.</param>
    /// <returns>A text colour suitable for the supplied background.</returns>
    internal static Color GetReadableTextColor(Color background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Linear(background.R) +
                        0.7152 * Linear(background.G) +
                        0.0722 * Linear(background.B);
        return luminance > 0.42
            ? Color.Parse("#102033")
            : Colors.White;
    }
}
