using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Orynivo.Audio;

namespace Orynivo.Library;

/// <summary>Reads chapter boundaries and tags from Matroska Audio containers.</summary>
internal static class MatroskaChapterParser
{
    /// <summary>Reads playable chapter definitions from an MKA file through FFprobe.</summary>
    /// <param name="sourcePath">Absolute path of the Matroska Audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered chapter definitions, or an empty list when the file has no usable chapters.</returns>
    internal static IReadOnlyList<CueTrackDefinition> Parse(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-v", "error",
                "-show_chapters",
                "-show_format",
                "-of", "json",
                fullPath
            }
        });
        if (process is null)
            throw new InvalidOperationException("FFprobe could not be started.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidDataException($"FFprobe could not read Matroska chapters: {error.Trim()}");

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var formatTags = root.TryGetProperty("format", out var format)
            ? ReadTags(format)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("chapters", out var chapters) ||
            chapters.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<CueTrackDefinition>();
        var ordinal = 0;
        foreach (var chapter in chapters.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ordinal++;
            if (!TryReadSeconds(chapter, "start_time", out var start) ||
                !TryReadSeconds(chapter, "end_time", out var end) ||
                end <= start)
            {
                continue;
            }

            var tags = ReadTags(chapter);
            var number = ReadPositiveNumber(tags, "track", "tracknumber") ?? ordinal;
            var title = ReadTag(tags, "title");
            var artist = ReadTag(tags, "artist", "performer") ?? ReadTag(formatTags, "artist", "performer");
            var album = ReadTag(tags, "album") ?? ReadTag(formatTags, "album", "title");
            var albumArtist = ReadTag(tags, "album_artist", "albumartist") ??
                              ReadTag(formatTags, "album_artist", "albumartist") ?? artist;
            var genre = ReadTag(tags, "genre") ?? ReadTag(formatTags, "genre");
            var year = ReadYear(tags) ?? ReadYear(formatTags);

            parsed.Add(new CueTrackDefinition(
                CreateVirtualPath(fullPath, ordinal),
                fullPath,
                fullPath,
                number,
                title,
                artist,
                album,
                albumArtist,
                genre,
                year,
                start,
                end));
        }

        return parsed;
    }

    /// <summary>Determines whether a path identifies a virtual MKA chapter.</summary>
    /// <param name="path">Path to inspect.</param>
    /// <returns><see langword="true"/> for an MKA chapter URI.</returns>
    internal static bool IsVirtualPath(string path) =>
        path.StartsWith("mka://", StringComparison.OrdinalIgnoreCase);

    private static string CreateVirtualPath(string sourcePath, int ordinal) =>
        $"mka://chapter/{ordinal.ToString("D3", CultureInfo.InvariantCulture)}?source={Uri.EscapeDataString(sourcePath.Replace('\\', '/'))}";

    private static Dictionary<string, string> ReadTags(JsonElement owner)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!owner.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var property in tags.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } value)
                result[property.Name] = value.Trim();
        }
        return result;
    }

    private static string? ReadTag(IReadOnlyDictionary<string, string> tags, params string[] names)
    {
        foreach (var name in names)
        {
            if (tags.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static int? ReadPositiveNumber(IReadOnlyDictionary<string, string> tags, params string[] names)
    {
        var value = ReadTag(tags, names)?.Split('/', 2)[0].Trim();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;
    }

    private static int? ReadYear(IReadOnlyDictionary<string, string> tags)
    {
        var value = ReadTag(tags, "date", "year");
        return value is { Length: >= 4 } &&
               int.TryParse(value[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private static bool TryReadSeconds(JsonElement owner, string propertyName, out double seconds)
    {
        seconds = 0;
        return owner.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
               double.IsFinite(seconds) && seconds >= 0;
    }
}
