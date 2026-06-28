using System.Diagnostics;
using System.Text.Json;
using Orynivo.Audio;

namespace Orynivo.Library;

/// <summary>
/// Live ICY metadata snapshot for an internet radio stream.
/// </summary>
/// <param name="StreamTitle">Raw <c>StreamTitle</c> ICY tag, typically in "Artist - Title" format.</param>
/// <param name="StationName">Station name from the <c>icy-name</c> header.</param>
/// <param name="Description">Station description from <c>icy-description</c>.</param>
/// <param name="Genre">Genre from the <c>icy-genre</c> header.</param>
/// <param name="Homepage">Station homepage URL from <c>icy-url</c>.</param>
public sealed record RadioStreamMetadata(
    string? StreamTitle,
    string? StationName,
    string? Description,
    string? Genre,
    string? Homepage)
{
    /// <summary>Artist portion parsed from <see cref="StreamTitle"/>, or <see langword="null"/>.</summary>
    public string? Artist => SplitStreamTitle().Artist;

    /// <summary>Track title portion parsed from <see cref="StreamTitle"/>, or the full value if no separator was found.</summary>
    public string? Title => SplitStreamTitle().Title;

    private (string? Artist, string? Title) SplitStreamTitle()
    {
        if (string.IsNullOrWhiteSpace(StreamTitle))
            return (null, null);

        foreach (var separator in new[] { " - ", " – ", " — " })
        {
            var index = StreamTitle.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0 && index + separator.Length < StreamTitle.Length)
                return (
                    StreamTitle[..index].Trim(),
                    StreamTitle[(index + separator.Length)..].Trim());
        }

        var slashIndex = StreamTitle.IndexOf(" / ", StringComparison.Ordinal);
        if (slashIndex > 0 && slashIndex + 3 < StreamTitle.Length)
            return (
                StreamTitle[(slashIndex + 3)..].Trim(),
                StreamTitle[..slashIndex].Trim());

        return (null, StreamTitle.Trim());
    }
}

/// <summary>
/// Probes a live radio stream URL with <c>ffprobe</c> to extract ICY metadata tags.
/// </summary>
public sealed class RadioStreamMetadataService
{
    /// <summary>
    /// Runs <c>ffprobe</c> against <paramref name="streamUrl"/> and returns the extracted ICY tags,
    /// or <see langword="null"/> when the probe fails or returns no tags.
    /// </summary>
    /// <param name="streamUrl">Direct stream URL to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RadioStreamMetadata?> ProbeAsync(
        string streamUrl,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-icy");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-rw_timeout");
        startInfo.ArgumentList.Add("10000000");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format_tags");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(streamUrl);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffprobe could not be started.");
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("format", out var format) ||
                !format.TryGetProperty("tags", out var tags))
                return null;

            return new RadioStreamMetadata(
                GetString(tags, "StreamTitle"),
                GetString(tags, "icy-name"),
                GetString(tags, "icy-description"),
                GetString(tags, "icy-genre"),
                GetString(tags, "icy-url"));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static string? GetString(JsonElement tags, string propertyName)
    {
        foreach (var property in tags.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = property.Value.ToString().Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }
}
