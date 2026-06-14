using System.Diagnostics;
using System.Text.Json;

namespace Orynivo.Library;

public sealed record RadioStreamMetadata(
    string? StreamTitle,
    string? StationName,
    string? Description,
    string? Genre,
    string? Homepage)
{
    public string? Artist => SplitStreamTitle().Artist;
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

public sealed class RadioStreamMetadataService
{
    public async Task<RadioStreamMetadata?> ProbeAsync(
        string streamUrl,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
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
