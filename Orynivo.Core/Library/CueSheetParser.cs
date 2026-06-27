using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orynivo.Library;

internal sealed record CueTrackDefinition(
    string VirtualPath,
    string CuePath,
    string SourcePath,
    int Number,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    int? Year,
    double StartSeconds,
    double? EndSeconds);

internal static partial class CueSheetParser
{
    internal static IReadOnlyList<CueTrackDefinition> Parse(string cuePath)
    {
        var cueFullPath = Path.GetFullPath(cuePath);
        var directory = Path.GetDirectoryName(cueFullPath) ?? string.Empty;
        var lines = ReadAllLines(cueFullPath);
        var albumTitle = (string?)null;
        var albumArtist = (string?)null;
        var genre = (string?)null;
        var year = (int?)null;
        var currentFile = (string?)null;
        var currentTrack = (MutableTrack?)null;
        var tracks = new List<MutableTrack>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var (command, value) = SplitCommand(line);
            switch (command)
            {
                case "FILE":
                    currentFile = ParseQuotedValue(value);
                    break;
                case "TRACK":
                    var trackParts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (currentFile is not null &&
                        trackParts.Length >= 2 &&
                        int.TryParse(trackParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                        string.Equals(trackParts[1], "AUDIO", StringComparison.OrdinalIgnoreCase))
                    {
                        currentTrack = new MutableTrack(number, currentFile);
                        tracks.Add(currentTrack);
                    }
                    else
                    {
                        currentTrack = null;
                    }
                    break;
                case "TITLE":
                    if (currentTrack is null)
                        albumTitle = ParseQuotedValue(value);
                    else
                        currentTrack.Title = ParseQuotedValue(value);
                    break;
                case "PERFORMER":
                    if (currentTrack is null)
                        albumArtist = ParseQuotedValue(value);
                    else
                        currentTrack.Artist = ParseQuotedValue(value);
                    break;
                case "INDEX" when currentTrack is not null:
                    var indexParts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (indexParts.Length == 2 &&
                        indexParts[0] == "01" &&
                        TryParseTimestamp(indexParts[1], out var seconds))
                    {
                        currentTrack.StartSeconds = seconds;
                    }
                    break;
                case "REM":
                    var (remCommand, remValue) = SplitCommand(value);
                    if (remCommand == "GENRE")
                        genre = ParseQuotedValue(remValue);
                    else if ((remCommand == "DATE" || remCommand == "YEAR") &&
                             int.TryParse(ParseQuotedValue(remValue), out var parsedYear))
                        year = parsedYear;
                    break;
            }
        }

        var result = new List<CueTrackDefinition>();
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (track.StartSeconds is not double start)
                continue;

            var sourcePath = Path.GetFullPath(Path.Combine(directory, track.FileName));
            double? end = null;
            if (index + 1 < tracks.Count)
            {
                var next = tracks[index + 1];
                var nextSourcePath = Path.GetFullPath(Path.Combine(directory, next.FileName));
                if (string.Equals(sourcePath, nextSourcePath, StringComparison.OrdinalIgnoreCase))
                    end = next.StartSeconds;
            }

            result.Add(new CueTrackDefinition(
                CreateVirtualPath(cueFullPath, track.Number),
                cueFullPath,
                sourcePath,
                track.Number,
                track.Title,
                track.Artist ?? albumArtist,
                albumTitle,
                albumArtist,
                genre,
                year,
                start,
                end));
        }

        return result;
    }

    internal static bool IsVirtualPath(string path) =>
        path.StartsWith("cue://", StringComparison.OrdinalIgnoreCase);

    private static string CreateVirtualPath(string cuePath, int trackNumber) =>
        $"cue://track/{trackNumber.ToString("D3", CultureInfo.InvariantCulture)}?sheet={Uri.EscapeDataString(cuePath.Replace('\\', '/'))}";

    private static string[] ReadAllLines(string path)
    {
        try
        {
            return File.ReadAllLines(path, new UTF8Encoding(false, true));
        }
        catch (DecoderFallbackException)
        {
            return File.ReadAllLines(path, Encoding.Default);
        }
    }

    private static (string Command, string Value) SplitCommand(string line)
    {
        var separator = line.IndexOfAny([' ', '\t']);
        return separator < 0
            ? (line.ToUpperInvariant(), string.Empty)
            : (line[..separator].ToUpperInvariant(), line[(separator + 1)..].Trim());
    }

    private static string ParseQuotedValue(string value)
    {
        var match = QuotedValueRegex().Match(value);
        return match.Success ? match.Groups[1].Value : value.Trim();
    }

    private static bool TryParseTimestamp(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Split(':');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wholeSeconds) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
            return false;

        seconds = minutes * 60d + wholeSeconds + frames / 75d;
        return seconds >= 0;
    }

    [GeneratedRegex("^\"(.*)\"(?:\\s+\\S+)?$")]
    private static partial Regex QuotedValueRegex();

    private sealed class MutableTrack(int number, string fileName)
    {
        internal int Number { get; } = number;
        internal string FileName { get; } = fileName;
        internal string? Title { get; set; }
        internal string? Artist { get; set; }
        internal double? StartSeconds { get; set; }
    }
}
